using System.Collections.Concurrent;
using System.Text.Json;
using SyncWallpaper.Core;
using SyncWallpaper.TaskbarHost;

namespace SyncWallpaper.Host;

/// <summary>
/// Process boundary for optional modules. A TaskbarHost instance owns its UI,
/// hooks and timers here; a failure exits this process without touching the
/// core wallpaper service.
/// </summary>
internal static class Program
{
    private static readonly object WriteGate = new();

    private static async Task Main(string[] args)
    {
        var moduleName = ReadArgument(args, "--module") ?? string.Empty;
        var protocol = ReadArgument(args, "--protocol") ?? string.Empty;
        var instanceId = ReadArgument(args, "--instance-id") ?? string.Empty;
        if (!Enum.TryParse<SyncWallpaperModule>(moduleName, ignoreCase: true, out var module)
            || module is not (SyncWallpaperModule.TaskbarHost or SyncWallpaperModule.ShellHost or SyncWallpaperModule.ScreenSaverHost or SyncWallpaperModule.RemoteHost or SyncWallpaperModule.OnlineWallpaperProviders))
        {
            Console.Error.WriteLine("Unknown or non-process module.");
            Environment.ExitCode = 2;
            return;
        }
        if (!string.Equals(protocol, ModuleIpcProtocol.Version, StringComparison.Ordinal))
        {
            Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "ready", false, "IncompatibleProtocol", "不兼容的 IPC 协议版本"));
            Environment.ExitCode = 3;
            return;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        TaskbarModuleProcess? taskbar = null;
        try
        {
            if (module == SyncWallpaperModule.TaskbarHost)
            {
                taskbar = new TaskbarModuleProcess();
                try { await taskbar.StartAsync(stop.Token).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "ready", false,
                        "TaskbarStartFailure", ex.Message, StatusPayload(taskbar), DateTime.UtcNow));
                    Environment.ExitCode = 4;
                    return;
                }
            }

            var responses = new ConcurrentDictionary<string, ModuleIpcResponse>(StringComparer.OrdinalIgnoreCase);
            Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "ready", true,
                Payload: StatusPayload(taskbar), TimestampUtc: DateTime.UtcNow));

            var reader = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var line = await Console.In.ReadLineAsync(stop.Token).ConfigureAwait(false);
                        if (line is null) { stop.Cancel(); break; }
                        if (!ModuleIpcJson.TryDeserialize(line, out var request) || request is null)
                        {
                            Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "error", false,
                                "MalformedMessage", "无法解析的消息", TimestampUtc: DateTime.UtcNow));
                            continue;
                        }
                        if (!string.Equals(request.ProtocolVersion, ModuleIpcProtocol.Version, StringComparison.Ordinal)
                            || !string.Equals(request.ModuleInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                        {
                            Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "error", false,
                                "IncompatibleProtocol", "协议版本或模块实例不匹配", TimestampUtc: DateTime.UtcNow));
                            continue;
                        }
                        if (responses.TryGetValue(request.RequestId, out var previous)) { Write(previous); continue; }

                        ModuleIpcResponse response;
                        if (request.Type.Equals("stop", StringComparison.OrdinalIgnoreCase))
                            response = new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "stop", true,
                                Payload: StatusPayload(taskbar), TimestampUtc: DateTime.UtcNow);
                        else if (request.Type.Equals("status", StringComparison.OrdinalIgnoreCase))
                            response = new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "status", true,
                                Payload: StatusPayload(taskbar), TimestampUtc: DateTime.UtcNow);
                        else
                            response = new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "error", false,
                                "UnsupportedRequest", "当前宿主不支持该请求", TimestampUtc: DateTime.UtcNow);

                        responses[request.RequestId] = response;
                        while (responses.Count > 64 && responses.Keys.FirstOrDefault() is { } oldest) responses.TryRemove(oldest, out _);
                        Write(response);
                        if (request.Type.Equals("stop", StringComparison.OrdinalIgnoreCase)) { stop.Cancel(); break; }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "error", false,
                        "HostReaderFailure", ex.Message, Payload: StatusPayload(taskbar), TimestampUtc: DateTime.UtcNow));
                    stop.Cancel();
                }
            });

            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop.Token).ConfigureAwait(false);
                    if (taskbar?.Completion.IsCompleted == true)
                    {
                        var message = taskbar.Status.LastError ?? "任务栏运行时意外停止。";
                        Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "error", false,
                            "TaskbarRuntimeFailure", message, StatusPayload(taskbar), DateTime.UtcNow));
                        Environment.ExitCode = 10;
                        stop.Cancel();
                        break;
                    }
                    Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "heartbeat", true,
                        Payload: StatusPayload(taskbar), TimestampUtc: DateTime.UtcNow));
                }
            }
            catch (OperationCanceledException) { }
            try { await reader.ConfigureAwait(false); } catch { }
        }
        finally
        {
            if (taskbar is not null)
            {
                try { await taskbar.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                await taskbar.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static JsonElement? StatusPayload(TaskbarModuleProcess? taskbar)
        => taskbar is null ? null : JsonSerializer.SerializeToElement(taskbar.Status, ModuleIpcJson.Options);

    private static void Write(ModuleIpcResponse response)
    {
        lock (WriteGate)
        {
            Console.WriteLine(ModuleIpcJson.Serialize(response));
            Console.Out.Flush();
        }
    }

    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
