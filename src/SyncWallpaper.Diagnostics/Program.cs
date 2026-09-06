using System.Diagnostics;
using System.Text.Json;
using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Diagnostics;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        WindowsDpiAwareness.TryEnablePerMonitorV2();
        var command = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "help";
        try
        {
            return command switch
            {
                "snapshot" => await SnapshotAsync(args),
                "wallpaper-snapshot" => await WallpaperSnapshotAsync(args),
                "monitor-soak" => await MonitorSoakAsync(args),
                _ => Help()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Diagnostics failed: " + ex.Message);
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("SyncWallpaper.Diagnostics (read-only)");
        Console.WriteLine("  snapshot                         Export monitor identity, mode and resource snapshot");
        Console.WriteLine("  wallpaper-snapshot               Export current Explorer wallpaper paths and hashes");
        Console.WriteLine("  monitor-soak --iterations 1000  Repeat read-only discovery and check handle growth");
        return 0;
    }

    private static async Task<int> SnapshotAsync(string[] args)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var discovery = new MonitorDiscoveryService();
        var monitors = discovery.Discover();
        var payload = new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTime.UtcNow,
            operatingSystem = Environment.OSVersion.VersionString,
            displayCount = monitors.Count,
            displays = monitors.Select(MonitorIdentitySanitizer.Sanitize).ToArray(),
            discoveryError = discovery.LastError,
            explorerRunning = Process.GetProcessesByName("explorer").Length > 0,
            process = new
            {
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.HandleCount,
                threadCount = process.Threads.Count,
                cpuSeconds = process.TotalProcessorTime.TotalSeconds
            },
            systemMutation = false
        };
        var path = ResolveOutput(args, "display-snapshot");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine($"Read-only monitor snapshot written to {path}; monitors={monitors.Count}");
        return 0;
    }

    private static async Task<int> WallpaperSnapshotAsync(string[] args)
    {
        var snapshot = new WallpaperSnapshotService().Capture();
        var path = ResolveOutput(args, "wallpaper-snapshot");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        Console.WriteLine($"Read-only wallpaper snapshot written to {path}; active monitors={snapshot.ActiveMonitorCount}; error={snapshot.Error ?? "none"}");
        return snapshot.Error is null ? 0 : 1;
    }

    private static async Task<int> MonitorSoakAsync(string[] args)
    {
        var iterations = Math.Clamp(ReadInt(args, "iterations", 1_000), 1, 100_000);
        var discovery = new MonitorDiscoveryService();
        _ = discovery.Discover();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var beforeHandles = process.HandleCount;
        var beforePrivate = process.PrivateMemorySize64;
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<MonitorIdentity> monitors = Array.Empty<MonitorIdentity>();
        for (var i = 0; i < iterations; i++) monitors = discovery.Discover();
        stopwatch.Stop();
        process.Refresh();
        var payload = new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTime.UtcNow,
            iterations,
            elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            finalDisplayCount = monitors.Count,
            handleDelta = process.HandleCount - beforeHandles,
            privateBytesDelta = process.PrivateMemorySize64 - beforePrivate,
            discoveryError = discovery.LastError,
            systemMutation = false
        };
        var path = ResolveOutput(args, "monitor-soak");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine($"Read-only monitor soak completed: {iterations} iterations, handle delta={payload.handleDelta}, report={path}");
        return payload.handleDelta < 100 ? 0 : 1;
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var prefix = "--" + name;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var value)) return value;
        return fallback;
    }

    private static string ResolveOutput(string[] args, string basis)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        var directory = Path.Combine(FindWorkspace(), "artifacts", "diagnostics");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{basis}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    }

    private static string FindWorkspace()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SyncWallpaper.sln"))) return directory.FullName;
        return Directory.GetCurrentDirectory();
    }
}
