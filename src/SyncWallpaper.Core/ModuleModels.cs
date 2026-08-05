using System.Diagnostics;

namespace SyncWallpaper.Core;

/// <summary>
/// The modules that can be enabled independently.  Wallpaper matching is part of
/// the core host and is intentionally always enabled; it is included here only so
/// the diagnostics page can show it as a first-class runtime component.
/// </summary>
public enum SyncWallpaperModule
{
    Wallpaper,
    DisplayEngine,
    AudioEngine,
    WindowEngine,
    Automation,
    DesktopEngine,
    TaskbarHost,
    ShellHost,
    ScreenSaverHost,
    RemoteHost,
    OnlineWallpaperProviders
}

public enum ModuleLifecycleState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public enum ModuleMode
{
    Lightweight,
    Standard,
    Full,
    Custom
}

public sealed class ModuleConfiguration
{
    public ModuleMode Mode { get; set; } = ModuleMode.Lightweight;
    public Dictionary<string, bool> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(SyncWallpaperModule module)
    {
        Overrides ??= new(StringComparer.OrdinalIgnoreCase);
        if (module == SyncWallpaperModule.Wallpaper) return true;
        if (Mode == ModuleMode.Standard) return StandardModules.Contains(module);
        if (Mode == ModuleMode.Full) return FullModules.Contains(module);
        if (Mode == ModuleMode.Custom && Overrides.TryGetValue(module.ToString(), out var value)) return value;
        return false;
    }

    public void SetEnabled(SyncWallpaperModule module, bool enabled)
    {
        Overrides ??= new(StringComparer.OrdinalIgnoreCase);
        if (Mode != ModuleMode.Custom)
        {
            var previous = Mode;
            foreach (var candidate in Enum.GetValues<SyncWallpaperModule>())
            {
                if (candidate == SyncWallpaperModule.Wallpaper) continue;
                Overrides[candidate.ToString()] = previous switch
                {
                    ModuleMode.Standard => StandardModules.Contains(candidate),
                    ModuleMode.Full => FullModules.Contains(candidate),
                    _ => false
                };
            }
            Mode = ModuleMode.Custom;
        }
        if (module == SyncWallpaperModule.Wallpaper) return;
        Overrides[module.ToString()] = enabled;
    }

    public void ApplyPreset(ModuleMode mode)
    {
        Overrides ??= new(StringComparer.OrdinalIgnoreCase);
        Mode = mode;
        Overrides.Clear();
    }

    public static IReadOnlySet<SyncWallpaperModule> StandardModules { get; } = new HashSet<SyncWallpaperModule>
    {
        SyncWallpaperModule.DisplayEngine,
        SyncWallpaperModule.AudioEngine,
        SyncWallpaperModule.WindowEngine,
        SyncWallpaperModule.Automation,
        SyncWallpaperModule.DesktopEngine
    };

    public static IReadOnlySet<SyncWallpaperModule> FullModules { get; } = new HashSet<SyncWallpaperModule>
    {
        SyncWallpaperModule.DisplayEngine,
        SyncWallpaperModule.AudioEngine,
        SyncWallpaperModule.WindowEngine,
        SyncWallpaperModule.Automation,
        SyncWallpaperModule.DesktopEngine,
        SyncWallpaperModule.TaskbarHost,
        SyncWallpaperModule.ShellHost,
        SyncWallpaperModule.ScreenSaverHost,
        SyncWallpaperModule.RemoteHost,
        SyncWallpaperModule.OnlineWallpaperProviders
    };
}

public sealed record ModuleDefinition(
    SyncWallpaperModule Id,
    string DisplayName,
    bool OutOfProcess,
    IReadOnlyList<SyncWallpaperModule> Dependencies);

public sealed record ModuleResourceSnapshot(
    DateTime CapturedAt,
    long WorkingSetBytes,
    long PrivateBytes,
    long HandleCount,
    double CpuSeconds,
    int? ProcessId,
    int ThreadCount = 0);

public sealed class ModuleRuntimeDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, ModuleRuntimeState> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModuleRuntimeState
{
    public bool Enabled { get; set; }
    public DateTime? LastSuccessfulStartUtc { get; set; }
    public DateTime? LastFaultUtc { get; set; }
    public string? LastFault { get; set; }
    public int CrashCount { get; set; }
    public string? LastRecoveryPoint { get; set; }
    public bool DisabledByCrashLoop { get; set; }
}

public sealed class ModuleLifecycleOptions
{
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan RecoveryBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan CrashWindow { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxAutoRecoveryAttempts { get; init; } = 1;
    public bool EnableAutoRecovery { get; init; } = true;
}

public sealed class ModulePerformanceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<ModulePerformanceRecord> Records { get; set; } = new();
}

public sealed class ModulePerformanceRecord
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public ModuleMode Mode { get; set; }
    public long StartupMilliseconds { get; set; }
    public List<ModulePerformanceSample> Modules { get; set; } = new();
}

public sealed class ModulePerformanceSample
{
    public SyncWallpaperModule Id { get; set; }
    public ModuleLifecycleState State { get; set; }
    public int? ProcessId { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public long HandleCount { get; set; }
    public double CpuSeconds { get; set; }
    public int ThreadCount { get; set; }
}

public sealed class ModuleStatusSnapshot : EventArgs
{
    public SyncWallpaperModule Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ModuleLifecycleState State { get; init; }
    public bool Enabled { get; init; }
    public bool OutOfProcess { get; init; }
    public int? ProcessId { get; init; }
    public IReadOnlyList<SyncWallpaperModule> Dependencies { get; init; } = Array.Empty<SyncWallpaperModule>();
    public string HookStatus { get; init; } = "未注册";
    public string? LastError { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? StoppedAt { get; init; }
    public DateTime? LastTransitionAt { get; init; }
    public string LastTransitionReason { get; init; } = string.Empty;
    public int FailureCount { get; init; }
    public DateTime? NextRetryAt { get; init; }
    public bool FaultDisabled { get; init; }
    public string InstanceId { get; init; } = string.Empty;
    public DateTime? LastHeartbeatAt { get; init; }
    public ModuleResourceSnapshot Resources { get; init; } = new(DateTime.UtcNow, 0, 0, 0, 0, null);
}

public interface IModuleController : IDisposable
{
    event Action<string>? Faulted;
    bool IsRunning { get; }
    int? ProcessId { get; }
    string HookStatus { get; }
    string? LastError { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IModuleHealth
{
    string InstanceId { get; }
    DateTime? LastHeartbeatAt { get; }
}

/// <summary>
/// A small lifecycle manager shared by the app and tests.  It deliberately knows
/// nothing about WPF, hooks, COM, or a particular process host; controllers own
/// those resources and must release them from StopAsync/Dispose.
/// </summary>
public sealed class ModuleManager : IDisposable
{
    private sealed class Registration
    {
        public required ModuleDefinition Definition { get; init; }
        public required IModuleController Controller { get; init; }
        public ModuleLifecycleState State { get; set; } = ModuleLifecycleState.Stopped;
        public DateTime? StartedAt { get; set; }
        public DateTime? StoppedAt { get; set; }
        public DateTime? LastTransitionAt { get; set; }
        public string LastTransitionReason { get; set; } = "初始化";
        public string? LastError { get; set; }
        public int FailureCount { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public bool FaultDisabled { get; set; }
        public int AutoRecoveryAttempts { get; set; }
        public string InstanceId { get; } = Guid.NewGuid().ToString("N");
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Task? RecoveryTask { get; set; }
        public CancellationTokenSource RecoveryCts { get; set; } = new();
    }

    private readonly Dictionary<SyncWallpaperModule, Registration> _registrations = new();
    private readonly object _gate = new();
    private readonly ModuleLifecycleOptions _options;
    private readonly ModuleRuntimeDocument _runtime;
    private readonly Action<ModuleRuntimeDocument>? _persist;
    private bool _disposed;

    public ModuleManager(Action<string>? log = null, ModuleLifecycleOptions? options = null, ModuleRuntimeDocument? runtime = null, Action<ModuleRuntimeDocument>? persist = null)
    {
        Log = log;
        _options = options ?? new ModuleLifecycleOptions();
        _runtime = runtime ?? new ModuleRuntimeDocument();
        _persist = persist;
    }

    public Action<string>? Log { get; }
    public ModuleLifecycleOptions Options => _options;
    public ModuleRuntimeDocument Runtime => _runtime;
    public event EventHandler<ModuleStatusSnapshot>? StateChanged;

    public void Register(ModuleDefinition definition, IModuleController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        lock (_gate)
        {
            if (_registrations.ContainsKey(definition.Id)) throw new InvalidOperationException($"模块已注册：{definition.Id}");
            var registration = new Registration { Definition = definition, Controller = controller };
            if (_runtime.Modules.TryGetValue(definition.Id.ToString(), out var saved) && saved.DisabledByCrashLoop)
            {
                registration.State = ModuleLifecycleState.Faulted;
                registration.FaultDisabled = true;
                registration.FailureCount = saved.CrashCount;
                registration.LastError = saved.LastFault;
                registration.LastTransitionReason = "从持久化故障状态恢复；等待手动重新启用";
            }
            controller.Faulted += error => MarkFaulted(definition.Id, error);
            _registrations.Add(definition.Id, registration);
        }
    }

    public bool IsRegistered(SyncWallpaperModule id) { lock (_gate) return _registrations.ContainsKey(id); }

    public ModuleLifecycleState GetState(SyncWallpaperModule id)
    {
        lock (_gate) return _registrations.TryGetValue(id, out var registration) ? registration.State : ModuleLifecycleState.Stopped;
    }

    public IReadOnlyList<ModuleStatusSnapshot> Snapshot(ModuleConfiguration? configuration = null)
    {
        lock (_gate) return _registrations.Values.Select(x => ToSnapshot(x, configuration)).OrderBy(x => x.Id).ToArray();
    }

    public ModuleStatusSnapshot? Snapshot(SyncWallpaperModule id, ModuleConfiguration? configuration = null)
    {
        lock (_gate) return _registrations.TryGetValue(id, out var registration) ? ToSnapshot(registration, configuration) : null;
    }

    public async Task StartEnabledAsync(ModuleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        foreach (var registration in Registrations())
        {
            var enabled = configuration.IsEnabled(registration.Definition.Id);
            if (enabled)
            {
                await StartCoreAsync(registration.Definition.Id, cancellationToken, new HashSet<SyncWallpaperModule>(), clearFault: false, isRecovery: false).ConfigureAwait(false);
                GetOrCreateRuntime(registration.Definition.Id).Enabled = GetState(registration.Definition.Id) == ModuleLifecycleState.Running;
            }
            else GetOrCreateRuntime(registration.Definition.Id).Enabled = false;
        }
        PersistRuntime();
    }

    public async Task SetEnabledAsync(SyncWallpaperModule id, bool enabled, ModuleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (id == SyncWallpaperModule.Wallpaper && !enabled)
        {
            configuration.SetEnabled(id, true);
            GetOrCreateRuntime(id).Enabled = true;
            PersistRuntime();
            return;
        }
        configuration.SetEnabled(id, enabled);
        var runtime = GetOrCreateRuntime(id);
        runtime.Enabled = enabled;
        if (enabled)
        {
            ClearFault(id, "用户手动重新启用");
            await StartCoreAsync(id, cancellationToken, new HashSet<SyncWallpaperModule>(), clearFault: false, isRecovery: false).ConfigureAwait(false);
        }
        else await StopAsync(id, cancellationToken).ConfigureAwait(false);
        PersistRuntime();
    }

    public Task StartAsync(SyncWallpaperModule id, CancellationToken cancellationToken = default)
        => StartCoreAsync(id, cancellationToken, new HashSet<SyncWallpaperModule>(), clearFault: true, isRecovery: false);

    private async Task StartCoreAsync(SyncWallpaperModule id, CancellationToken cancellationToken, HashSet<SyncWallpaperModule> visiting, bool clearFault, bool isRecovery)
    {
        Registration registration;
        lock (_gate)
        {
            if (_disposed) return;
            if (!_registrations.TryGetValue(id, out registration!)) throw new InvalidOperationException($"未注册模块：{id}");
            if (!visiting.Add(id)) throw new InvalidOperationException($"检测到模块依赖循环：{string.Join(" -> ", visiting)} -> {id}");
            if (clearFault) ClearFaultLocked(registration, "手动启动");
            if (registration.FaultDisabled && !clearFault && !isRecovery) { visiting.Remove(id); return; }
            if (registration.State is ModuleLifecycleState.Running or ModuleLifecycleState.Starting) { visiting.Remove(id); return; }
        }

        await registration.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (registration.State is ModuleLifecycleState.Running or ModuleLifecycleState.Starting) return;
                if (registration.FaultDisabled && !clearFault && !isRecovery) return;
                TransitionLocked(registration, ModuleLifecycleState.Starting, isRecovery ? "故障恢复重试" : "请求启动");
                registration.LastError = null;
                registration.NextRetryAt = null;
            }
            Publish(registration);

            foreach (var dependency in registration.Definition.Dependencies)
            {
                if (!IsRegistered(dependency)) throw new InvalidOperationException($"依赖模块未注册：{dependency}");
                await StartCoreAsync(dependency, cancellationToken, new HashSet<SyncWallpaperModule>(visiting), clearFault: false, isRecovery: false).ConfigureAwait(false);
                if (GetState(dependency) != ModuleLifecycleState.Running) throw new InvalidOperationException($"依赖模块未运行：{dependency}");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.StartTimeout);
            try { await registration.Controller.StartAsync(timeout.Token).WaitAsync(_options.StartTimeout, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested) { throw new TimeoutException("模块启动超时 (timed out)。"); }
            lock (_gate)
            {
                TransitionLocked(registration, ModuleLifecycleState.Running, "启动成功");
                registration.StartedAt = DateTime.UtcNow;
                registration.LastError = null;
                registration.NextRetryAt = null;
                var saved = GetOrCreateRuntimeLocked(id);
                saved.Enabled = true;
                saved.LastSuccessfulStartUtc = DateTime.UtcNow;
                saved.LastRecoveryPoint = "running";
            }
            PersistRuntime();
            Publish(registration);
            Log?.Invoke($"模块启动：{id}");
        }
        catch (Exception ex)
        {
            FaultFromException(registration, ex, "启动失败", scheduleRecovery: isRecovery);
        }
        finally
        {
            registration.Gate.Release();
            lock (_gate) visiting.Remove(id);
        }
    }

    public async Task StopAsync(SyncWallpaperModule id, CancellationToken cancellationToken = default)
    {
        Registration? registration;
        lock (_gate)
        {
            if (!_registrations.TryGetValue(id, out registration)) return;
            if (registration.State is ModuleLifecycleState.Stopped or ModuleLifecycleState.Stopping) return;
        }
        await registration.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (registration.State == ModuleLifecycleState.Stopped) return;
                TransitionLocked(registration, ModuleLifecycleState.Stopping, "请求停止");
                registration.NextRetryAt = null;
                registration.RecoveryCts.Cancel();
                registration.RecoveryTask = null;
            }
            Publish(registration);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.StopTimeout);
            try { await registration.Controller.StopAsync(timeout.Token).WaitAsync(_options.StopTimeout, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested) { throw new TimeoutException("模块停止超时 (timed out)。"); }
            lock (_gate)
            {
                TransitionLocked(registration, ModuleLifecycleState.Stopped, "停止成功");
                registration.StoppedAt = DateTime.UtcNow;
                registration.LastError = null;
                var saved = GetOrCreateRuntimeLocked(id);
                saved.Enabled = false; saved.LastRecoveryPoint = "stopped";
            }
            PersistRuntime();
            Publish(registration);
            Log?.Invoke($"模块停止：{id}；Hook={registration.Controller.HookStatus}");
        }
        catch (Exception ex)
        {
            FaultFromException(registration, ex, "停止失败", scheduleRecovery: false);
        }
        finally { registration.Gate.Release(); }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var id in Registrations().OrderByDescending(x => x.Definition.Dependencies.Count).Select(x => x.Definition.Id).ToArray())
            await StopAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public bool VerifyStopped(SyncWallpaperModule id)
    {
        lock (_gate)
        {
            if (!_registrations.TryGetValue(id, out var registration)) return true;
            return registration.State == ModuleLifecycleState.Stopped && !registration.Controller.IsRunning && registration.Controller.ProcessId is null;
        }
    }

    private IEnumerable<Registration> Registrations() { lock (_gate) return _registrations.Values.ToArray(); }

    private ModuleStatusSnapshot ToSnapshot(Registration registration, ModuleConfiguration? configuration)
    {
        var processId = registration.Controller.ProcessId;
        var resources = CaptureResources(processId);
        return new ModuleStatusSnapshot
        {
            Id = registration.Definition.Id,
            DisplayName = registration.Definition.DisplayName,
            State = registration.State,
            Enabled = !registration.FaultDisabled && (configuration?.IsEnabled(registration.Definition.Id) ?? registration.State != ModuleLifecycleState.Stopped),
            OutOfProcess = registration.Definition.OutOfProcess,
            ProcessId = processId,
            Dependencies = registration.Definition.Dependencies,
            HookStatus = registration.Controller.HookStatus,
            LastError = registration.LastError ?? registration.Controller.LastError,
            StartedAt = registration.StartedAt,
            StoppedAt = registration.StoppedAt,
            LastTransitionAt = registration.LastTransitionAt,
            LastTransitionReason = registration.LastTransitionReason,
            FailureCount = registration.FailureCount,
            NextRetryAt = registration.NextRetryAt,
            FaultDisabled = registration.FaultDisabled,
            InstanceId = (registration.Controller as IModuleHealth)?.InstanceId ?? registration.InstanceId,
            LastHeartbeatAt = (registration.Controller as IModuleHealth)?.LastHeartbeatAt,
            Resources = resources
        };
    }

    private void MarkFaulted(SyncWallpaperModule id, string error)
    {
        Registration? registration;
        lock (_gate)
        {
            if (!_registrations.TryGetValue(id, out registration) || _disposed || registration.State is ModuleLifecycleState.Stopping or ModuleLifecycleState.Stopped) return;
            var now = DateTime.UtcNow;
            if (registration.LastFailureAt is null || now - registration.LastFailureAt > _options.CrashWindow) registration.FailureCount = 0;
            registration.FailureCount++;
            registration.LastFailureAt = now;
            registration.LastError = error;
            registration.StoppedAt = now;
            registration.AutoRecoveryAttempts++;
            registration.FaultDisabled = registration.FailureCount > _options.MaxAutoRecoveryAttempts;
            TransitionLocked(registration, ModuleLifecycleState.Faulted, "控制器报告故障");
            var saved = GetOrCreateRuntimeLocked(id);
            saved.LastFaultUtc = now; saved.LastFault = error; saved.CrashCount++; saved.LastRecoveryPoint = "faulted"; saved.DisabledByCrashLoop = registration.FaultDisabled;
            if (!registration.FaultDisabled && _options.EnableAutoRecovery)
            {
                registration.NextRetryAt = now + _options.RecoveryBackoff;
                if (registration.RecoveryTask is null || registration.RecoveryTask.IsCompleted)
                        registration.RecoveryTask = RecoverLaterAsync(id, registration.NextRetryAt.Value, registration.RecoveryCts.Token);
            }
        }
        PersistRuntime();
        Publish(registration);
        Log?.Invoke($"模块故障：{id}：{error}{(registration.FaultDisabled ? "；已禁用，需手动重新启用" : "；将按退避策略重试")}");
    }

    private async Task RecoverLaterAsync(SyncWallpaperModule id, DateTime retryAt, CancellationToken cancellationToken)
    {
        try
        {
            var delay = retryAt - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await StartCoreAsync(id, cancellationToken, new HashSet<SyncWallpaperModule>(), clearFault: false, isRecovery: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke($"模块恢复调度失败：{id}：{ex.Message}"); }
    }

    private void FaultFromException(Registration registration, Exception ex, string reason, bool scheduleRecovery)
    {
        lock (_gate)
        {
            if (registration.State != ModuleLifecycleState.Faulted) TransitionLocked(registration, ModuleLifecycleState.Faulted, reason);
            registration.LastError = ex.Message;
            var now = DateTime.UtcNow;
            registration.StoppedAt = now;
            registration.NextRetryAt = null;
            if (scheduleRecovery)
            {
                if (registration.LastFailureAt is null || now - registration.LastFailureAt > _options.CrashWindow) registration.FailureCount = 0;
                registration.FailureCount++;
                registration.LastFailureAt = now;
                registration.AutoRecoveryAttempts++;
                registration.FaultDisabled = registration.FailureCount > _options.MaxAutoRecoveryAttempts;
                if (!registration.FaultDisabled && _options.EnableAutoRecovery)
                {
                    registration.NextRetryAt = now + _options.RecoveryBackoff;
                    if (registration.RecoveryTask is null || registration.RecoveryTask.IsCompleted)
                        registration.RecoveryTask = RecoverLaterAsync(registration.Definition.Id, registration.NextRetryAt.Value, registration.RecoveryCts.Token);
                }
            }
            var saved = GetOrCreateRuntimeLocked(registration.Definition.Id);
            saved.LastFaultUtc = now; saved.LastFault = ex.Message; saved.LastRecoveryPoint = reason; saved.CrashCount++; saved.DisabledByCrashLoop = registration.FaultDisabled;
        }
        PersistRuntime();
        Publish(registration);
        Log?.Invoke($"模块故障：{registration.Definition.Id}：{ex.Message}");
    }

    private void ClearFault(SyncWallpaperModule id, string reason)
    {
        lock (_gate)
        {
            if (_registrations.TryGetValue(id, out var registration)) ClearFaultLocked(registration, reason);
        }
        PersistRuntime();
    }

    private static void ClearFaultLocked(Registration registration, string reason)
    {
        try { registration.RecoveryCts.Cancel(); registration.RecoveryCts.Dispose(); } catch { }
        registration.RecoveryCts = new CancellationTokenSource();
        registration.RecoveryTask = null;
        registration.FaultDisabled = false;
        registration.AutoRecoveryAttempts = 0;
        registration.NextRetryAt = null;
        if (registration.State == ModuleLifecycleState.Faulted) TransitionLocked(registration, ModuleLifecycleState.Stopped, reason);
    }

    private static bool IsAllowed(ModuleLifecycleState from, ModuleLifecycleState to) => from switch
    {
        ModuleLifecycleState.Stopped => to == ModuleLifecycleState.Starting,
        ModuleLifecycleState.Starting => to is ModuleLifecycleState.Running or ModuleLifecycleState.Faulted,
        ModuleLifecycleState.Running => to is ModuleLifecycleState.Stopping or ModuleLifecycleState.Faulted,
        ModuleLifecycleState.Stopping => to is ModuleLifecycleState.Stopped or ModuleLifecycleState.Faulted,
        ModuleLifecycleState.Faulted => to is ModuleLifecycleState.Starting or ModuleLifecycleState.Stopping or ModuleLifecycleState.Stopped,
        _ => false
    };

    private static void TransitionLocked(Registration registration, ModuleLifecycleState target, string reason)
    {
        if (registration.State != target && !IsAllowed(registration.State, target))
            throw new InvalidOperationException($"禁止的模块状态转换：{registration.Definition.Id} {registration.State} -> {target}");
        registration.State = target;
        registration.LastTransitionAt = DateTime.UtcNow;
        registration.LastTransitionReason = reason;
    }

    private ModuleRuntimeState GetOrCreateRuntime(SyncWallpaperModule id)
    {
        lock (_gate) return GetOrCreateRuntimeLocked(id);
    }

    private ModuleRuntimeState GetOrCreateRuntimeLocked(SyncWallpaperModule id)
    {
        _runtime.Modules ??= new(StringComparer.OrdinalIgnoreCase);
        if (!_runtime.Modules.TryGetValue(id.ToString(), out var value)) _runtime.Modules[id.ToString()] = value = new ModuleRuntimeState();
        return value;
    }

    private void PersistRuntime()
    {
        try { _persist?.Invoke(_runtime); } catch (Exception ex) { Log?.Invoke($"模块状态持久化失败：{ex.Message}"); }
    }

    private static ModuleResourceSnapshot CaptureResources(int? processId)
    {
        if (processId is null) return new(DateTime.UtcNow, 0, 0, 0, 0, null);
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            process.Refresh();
            return new(DateTime.UtcNow, process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, process.TotalProcessorTime.TotalSeconds, process.Id, process.Threads.Count);
        }
        catch { return new(DateTime.UtcNow, 0, 0, 0, 0, processId); }
    }

    private void Publish(Registration registration)
    {
        try { StateChanged?.Invoke(this, ToSnapshot(registration, null)); }
        catch (Exception ex) { Log?.Invoke($"模块状态订阅方异常：{registration.Definition.Id}：{ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_gate) _disposed = true;
        try { StopAllAsync().GetAwaiter().GetResult(); } catch { }
        foreach (var registration in Registrations()) { try { registration.RecoveryCts.Cancel(); registration.RecoveryCts.Dispose(); } catch { } try { registration.Controller.Dispose(); } catch { } registration.Gate.Dispose(); }
    }
}

/// <summary>Useful for in-process modules whose lifetime is a disposable service.</summary>
public sealed class DelegateModuleController : IModuleController
{
    private readonly Func<CancellationToken, Task> _start;
    private readonly Func<CancellationToken, Task> _stop;
    private readonly Func<bool> _running;
    private readonly Func<int?> _processId;
    private readonly Func<string> _hookStatus;
    private readonly Func<string?> _lastError;

    public DelegateModuleController(
        Func<CancellationToken, Task> start,
        Func<CancellationToken, Task> stop,
        Func<bool> running,
        Func<int?> processId,
        Func<string> hookStatus,
        Func<string?> lastError,
        Action? dispose = null)
    {
        _start = start; _stop = stop; _running = running; _processId = processId; _hookStatus = hookStatus; _lastError = lastError; DisposeAction = dispose;
    }

    private Action? DisposeAction { get; }
    event Action<string>? IModuleController.Faulted { add { } remove { } }
    public bool IsRunning => _running();
    public int? ProcessId => _processId();
    public string HookStatus => _hookStatus();
    public string? LastError => _lastError();
    public Task StartAsync(CancellationToken cancellationToken = default) => _start(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => _stop(cancellationToken);
    public void Dispose() => DisposeAction?.Invoke();
}
