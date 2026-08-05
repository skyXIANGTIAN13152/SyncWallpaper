using System.Collections.Concurrent;
using SyncWallpaper.Core;

namespace SyncWallpaper.Host;

/// <summary>Minimal process boundary. It deliberately performs no Explorer,
/// display, audio, or window mutation; concrete hosts can be added behind this
/// protocol without weakening core isolation.</summary>
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
        var responses = new ConcurrentDictionary<string, ModuleIpcResponse>(StringComparer.OrdinalIgnoreCase);
        Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "ready", true, TimestampUtc: DateTime.UtcNow));

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
                        Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "error", false, "MalformedMessage", "无法解析的消息", TimestampUtc: DateTime.UtcNow));
                        continue;
                    }
                    if (!string.Equals(request.ProtocolVersion, ModuleIpcProtocol.Version, StringComparison.Ordinal)
                        || !string.Equals(request.ModuleInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "error", false, "IncompatibleProtocol", "协议版本或模块实例不匹配", TimestampUtc: DateTime.UtcNow));
                        continue;
                    }
                    if (responses.TryGetValue(request.RequestId, out var previous)) { Write(previous); continue; }
                    var response = request.Type.Equals("stop", StringComparison.OrdinalIgnoreCase)
                        ? new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "stop", true, TimestampUtc: DateTime.UtcNow)
                        : new ModuleIpcResponse(ModuleIpcProtocol.Version, request.RequestId, instanceId, "error", false, "UnsupportedRequest", "当前宿主不支持该请求", TimestampUtc: DateTime.UtcNow);
                    responses[request.RequestId] = response;
                    while (responses.Count > 64 && responses.Keys.FirstOrDefault() is { } oldest) responses.TryRemove(oldest, out _);
                    Write(response);
                    if (request.Type.Equals("stop", StringComparison.OrdinalIgnoreCase)) { stop.Cancel(); break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "error", false, "HostReaderFailure", ex.Message, TimestampUtc: DateTime.UtcNow));
                stop.Cancel();
            }
        });

        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop.Token).ConfigureAwait(false);
                Write(new ModuleIpcResponse(ModuleIpcProtocol.Version, string.Empty, instanceId, "heartbeat", true, TimestampUtc: DateTime.UtcNow));
            }
        }
        catch (OperationCanceledException) { }
        try { await reader.ConfigureAwait(false); } catch { }
    }

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
