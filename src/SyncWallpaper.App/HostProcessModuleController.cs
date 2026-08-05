using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

/// <summary>
/// Owns one optional process host. Communication is line-delimited JSON with a
/// protocol version, request id and per-start instance id. A malformed or
/// incompatible message is logged and ignored; it can never take down the core.
/// </summary>
public sealed class HostProcessModuleController : IModuleController, IModuleHealth
{
    private readonly SyncWallpaperModule _module;
    private readonly Action<string>? _log;
    private readonly string? _executableOverride;
    private readonly IFaultInjector _faultInjector;
    private readonly TimeSpan _responseTimeout;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ModuleIpcResponse>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private CancellationTokenSource? _ioCts;
    private Task? _heartbeatTask;
    private TaskCompletionSource<ModuleIpcResponse>? _ready;
    private DateTime _lastHeartbeatUtc;
    private string _instanceId = string.Empty;
    private string? _lastError;
    private bool _stopping;
    private bool _faultNotified;

    public HostProcessModuleController(SyncWallpaperModule module, Action<string>? log = null, string? executableOverride = null,
        IFaultInjector? faultInjector = null, TimeSpan? responseTimeout = null)
    {
        _module = module; _log = log; _executableOverride = executableOverride;
        _faultInjector = faultInjector ?? NoFaultInjector.Instance;
        _responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(5);
    }

    public event Action<string>? Faulted;

    public bool IsRunning { get { lock (_gate) return _process is { HasExited: false }; } }
    public int? ProcessId { get { lock (_gate) return _process is { HasExited: false } process ? process.Id : null; } }
    public string HookStatus => IsRunning ? $"独立进程；IPC 心跳={(_lastHeartbeatUtc == default ? "等待" : "正常")}" : "进程已退出";
    public string? LastError { get { lock (_gate) return _lastError; } }
    public string ModuleInstanceId { get { lock (_gate) return _instanceId; } }
    string IModuleHealth.InstanceId => ModuleInstanceId;
    DateTime? IModuleHealth.LastHeartbeatAt => _lastHeartbeatUtc == default ? null : _lastHeartbeatUtc;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process process;
        TaskCompletionSource<ModuleIpcResponse> ready;
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;
            _faultInjector.ThrowIfRequested(FaultPoint.ProcessStart);
            var executable = _executableOverride ?? FindHostExecutable();
            if (executable is null) throw new FileNotFoundException("找不到独立模块宿主，请先完成发布或构建 SyncWallpaper.Host。", Path.Combine(AppContext.BaseDirectory, "SyncWallpaper.Host.exe"));
            _stopping = false;
            _faultNotified = false;
            _lastError = null;
            _instanceId = Guid.NewGuid().ToString("N");
            _lastHeartbeatUtc = DateTime.UtcNow;
            ready = _ready = new TaskCompletionSource<ModuleIpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ioCts = new CancellationTokenSource();
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"--module {_module} --protocol {ModuleIpcProtocol.Version} --instance-id {_instanceId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppContext.BaseDirectory
                },
                EnableRaisingEvents = true
            };
            process.Exited += Process_Exited;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _log?.Invoke($"{_module} 错误: {e.Data}"); };
            if (!process.Start()) throw new InvalidOperationException($"无法启动 {_module} 宿主进程。");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            _process = process;
            _log?.Invoke($"{_module} 宿主进程已启动 PID={process.Id} Instance={_instanceId}");
            _heartbeatTask = MonitorHeartbeatAsync(_ioCts.Token);
        }

        try
        {
            try { _faultInjector.ThrowIfRequested(FaultPoint.ProcessImmediateExit); }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { process.Dispose(); } catch { }
                lock (_gate) { _ioCts?.Cancel(); _process = null; }
                throw;
            }
            if (_faultInjector.IsRequested(FaultPoint.HostNoResponse) || _faultInjector.IsRequested(FaultPoint.IpcTimeout))
                await ready.Task.WaitAsync(_responseTimeout, cancellationToken).ConfigureAwait(false);
            else
                await ready.Task.WaitAsync(_responseTimeout, cancellationToken).ConfigureAwait(false);
            if (!ready.Task.IsCompletedSuccessfully || !ready.Task.Result.Success)
                throw new InvalidOperationException(ready.Task.IsCompletedSuccessfully ? ready.Task.Result.ErrorMessage ?? "宿主拒绝启动" : "宿主未就绪");
        }
        catch
        {
            try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static string? FindHostExecutable()
    {
            var published = Path.Combine(AppContext.BaseDirectory, "SyncWallpaper.Host.exe");
            if (!File.Exists(published)) published = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Host", "SyncWallpaper.Host.exe"));
            if (File.Exists(published)) return published;
        var development = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe"));
        return File.Exists(development) ? development : null;
    }

    private void Process_OutputDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        if (!ModuleIpcJson.TryDeserializeResponse(e.Data, out var response) || response is null)
        {
            _log?.Invoke($"{_module} 收到无法解析的 IPC 消息，已忽略。");
            return;
        }
        if (!string.Equals(response.ProtocolVersion, ModuleIpcProtocol.Version, StringComparison.Ordinal)
            || !string.Equals(response.ModuleInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase))
        {
            _log?.Invoke($"{_module} IPC 版本或实例不匹配，已拒绝消息。");
            return;
        }
        if (response.Type.Equals("heartbeat", StringComparison.OrdinalIgnoreCase)) _lastHeartbeatUtc = DateTime.UtcNow;
        if (response.Type.Equals("ready", StringComparison.OrdinalIgnoreCase)) _ready?.TrySetResult(response);
        if (!string.IsNullOrWhiteSpace(response.RequestId) && _pending.TryRemove(response.RequestId, out var waiter)) waiter.TrySetResult(response);
        _log?.Invoke($"{_module} IPC: {response.Type} {(response.Success ? "成功" : response.ErrorCode ?? "失败")}");
    }

    private async Task MonitorHeartbeatAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                if (IsRunning && DateTime.UtcNow - _lastHeartbeatUtc > TimeSpan.FromSeconds(10))
                {
                    NotifyFault($"宿主心跳超时：{_module}");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        var process = sender as Process;
        var unexpected = false;
        lock (_gate)
        {
            if (process is null || !ReferenceEquals(process, _process)) return;
            unexpected = !_stopping;
            if (unexpected)
            {
                var code = -1;
                try { code = process?.ExitCode ?? -1; } catch { }
                _lastError = $"宿主进程异常退出，ExitCode={code}";
                _ready?.TrySetException(new InvalidOperationException(_lastError));
            }
        }
        if (unexpected) NotifyFault($"宿主进程意外退出：{_module}");
    }

    private void NotifyFault(string message)
    {
        Process? process;
        lock (_gate)
        {
            if (_stopping || _faultNotified) return;
            _faultNotified = true;
            _lastError = message;
            _stopping = true;
            process = _process;
            _process = null;
        }
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
        try { process?.Dispose(); } catch { }
        lock (_gate) _stopping = false;
        _log?.Invoke(message);
        try { Faulted?.Invoke(message); } catch { }
    }

    public async Task<ModuleIpcResponse> SendRequestAsync(string type, JsonElement? payload = null, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) throw new InvalidOperationException($"{_module} 宿主未运行。");
        _faultInjector.ThrowIfRequested(FaultPoint.IpcFailure);
        if (_faultInjector.IsRequested(FaultPoint.IpcTimeout))
        {
            await Task.Delay(_responseTimeout, cancellationToken).ConfigureAwait(false);
            throw new TimeoutException($"{_module} IPC 请求超时。");
        }
        var requestId = Guid.NewGuid().ToString("N");
        var waiter = new TaskCompletionSource<ModuleIpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = waiter;
        try
        {
            var message = new ModuleIpcMessage(ModuleIpcProtocol.Version, requestId, ModuleInstanceId, type, payload);
            if (_faultInjector.IsRequested(FaultPoint.IpcCorruptMessage))
            {
                _faultInjector.ThrowIfRequested(FaultPoint.IpcCorruptMessage);
            }
            Process process;
            lock (_gate) process = _process ?? throw new InvalidOperationException("宿主进程已退出。");
            await process.StandardInput.WriteLineAsync(ModuleIpcJson.Serialize(message)).WaitAsync(_responseTimeout, cancellationToken).ConfigureAwait(false);
            var response = await waiter.Task.WaitAsync(_responseTimeout, cancellationToken).ConfigureAwait(false);
            return response;
        }
        finally { _pending.TryRemove(requestId, out _); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            if (process is null) return;
            _stopping = true;
        }
        try
        {
            if (!process.HasExited)
            {
                try { await SendRequestAsync("stop", cancellationToken: cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); } catch { }
                try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); } catch { }
            }
            if (!process.HasExited) try { process.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            lock (_gate)
            {
                _ioCts?.Cancel(); _ioCts?.Dispose(); _ioCts = null;
                _process = null; _ready = null; _stopping = false; _faultNotified = false;
                foreach (var waiter in _pending.Values) waiter.TrySetCanceled();
                _pending.Clear();
            }
            try { process.Exited -= Process_Exited; process.OutputDataReceived -= Process_OutputDataReceived; process.Dispose(); } catch { }
        }
    }

    public void Dispose() { try { StopAsync().GetAwaiter().GetResult(); } catch { } }
}
