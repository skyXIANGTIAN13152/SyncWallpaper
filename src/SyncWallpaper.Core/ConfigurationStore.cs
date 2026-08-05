using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncWallpaper.Core;

public sealed class DataPaths
{
    public string Root { get; }
    public string Wallpapers => Path.Combine(Root, "Wallpapers");
    public string Config => Path.Combine(Root, "Config");
    public string Backups => Path.Combine(Root, "Backups");
    public string Deleted => Path.Combine(Backups, "Deleted");
    public string Logs => Path.Combine(Root, "Logs");
    public string Thumbnails => Path.Combine(Root, "Thumbnails");
    public string Cache => Path.Combine(Root, "Cache");
    public string Rendered => Path.Combine(Cache, "Rendered");
    public DataPaths(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaper");
    public void Ensure() { foreach (var path in new[] { Root, Wallpapers, Config, Backups, Deleted, Logs, Thumbnails, Cache, Rendered }) Directory.CreateDirectory(path); }
}

public sealed class ConfigurationStore
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly object _gate = new();
    private readonly IFaultInjector _faultInjector;
    public DataPaths Paths { get; }
    public ConfigurationStore(DataPaths? paths = null, IFaultInjector? faultInjector = null) { Paths = paths ?? new DataPaths(); Paths.Ensure(); _faultInjector = faultInjector ?? NoFaultInjector.Instance; }

    public T Load<T>(string fileName, T fallback)
    {
        var path = Path.Combine(Paths.Config, fileName); var backup = Path.Combine(Paths.Backups, fileName + ".bak");
        lock (_gate)
        {
            if (_faultInjector.IsRequested(FaultPoint.ConfigurationCorrupt))
            {
                try { _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationCorrupt); } catch (InjectedFaultException) { return fallback; }
            }
            foreach (var candidate in new[] { path, backup })
            {
                try { if (File.Exists(candidate)) { var value = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), _options); if (value is not null) return value; } }
                catch { /* try the last known good copy */ }
            }
            return fallback;
        }
    }

    public void Save<T>(string fileName, T value)
    {
        var path = Path.Combine(Paths.Config, fileName); var backup = Path.Combine(Paths.Backups, fileName + ".bak"); var temp = path + ".tmp";
        lock (_gate)
        {
            _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationWrite);
            Paths.Ensure();
            var json = JsonSerializer.Serialize(value, _options);
            File.WriteAllText(temp, json);
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read)) stream.Flush(true);
            if (File.Exists(path)) File.Copy(path, backup, true);
            File.Move(temp, path, true);
        }
    }
}

public static class WallpaperCacheKey
{
    public static string Create(string sourceHash, int width, int height, WallpaperFitMode mode, string background, string rendererVersion = "1")
        => $"{sourceHash}_{width}x{height}_{mode}_{background.TrimStart('#')}_r{rendererVersion}".Replace("#", string.Empty);
}

public sealed class EventDebouncer : IDisposable
{
    private readonly TimeSpan _delay; private readonly Func<CancellationToken, Task> _action; private readonly object _gate = new(); private Timer? _timer; private CancellationTokenSource _cts = new();
    public EventDebouncer(TimeSpan delay, Func<CancellationToken, Task> action) { _delay = delay; _action = action; }
    public void Signal() { lock (_gate) { _timer?.Dispose(); _timer = new Timer(async _ => await RunAsync(), null, _delay, Timeout.InfiniteTimeSpan); } }
    private async Task RunAsync() { CancellationToken token; lock (_gate) { _cts.Cancel(); _cts.Dispose(); _cts = new(); token = _cts.Token; } try { await _action(token); } catch (OperationCanceledException) { } }
    public void Dispose() { lock (_gate) { _timer?.Dispose(); _cts.Cancel(); _cts.Dispose(); } }
}

public static class FileUtilities
{
    public static string Sha256(string file) { using var sha = System.Security.Cryptography.SHA256.Create(); using var s = File.OpenRead(file); return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant(); }
    public static long DirectorySize(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } }) : 0;
}
