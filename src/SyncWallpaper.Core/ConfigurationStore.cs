using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace SyncWallpaper.Core;

public sealed class DataPaths
{
    public string Root { get; }
    public string Wallpapers => Path.Combine(Root, "Wallpapers");
    public string Config => Path.Combine(Root, "Config");
    public string Logs => Path.Combine(Root, "Logs");
    public string Thumbnails => Path.Combine(Root, "Thumbnails");
    public string Cache => Path.Combine(Root, "Cache");
    public string Rendered => Path.Combine(Cache, "Rendered");
    public DataPaths(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaper");
    public void Ensure() { foreach (var path in new[] { Root, Wallpapers, Config, Logs, Thumbnails, Cache, Rendered }) Directory.CreateDirectory(path); }
}

public sealed class ConfigurationStore
{
    private const long MaxConfigurationBytes = 10 * 1024 * 1024;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 32, Converters = { new JsonStringEnumConverter() } };
    private readonly object _gate = new();
    private readonly IFaultInjector _faultInjector;
    public DataPaths Paths { get; }
    public ConfigurationStore(DataPaths? paths = null, IFaultInjector? faultInjector = null)
    {
        Paths = paths ?? new DataPaths();
        Paths.Ensure();
        _faultInjector = faultInjector ?? NoFaultInjector.Instance;
    }

    public T Load<T>(string fileName, T fallback, Func<T, bool>? validator = null)
    {
        ValidateFileName(fileName);
        var path = Path.Combine(Paths.Config, fileName);
        lock (_gate)
        {
            if (_faultInjector.IsRequested(FaultPoint.ConfigurationCorrupt))
            {
                try { _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationCorrupt); } catch (InjectedFaultException) { return fallback; }
            }
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxConfigurationBytes) return fallback;
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options);
                if (value is not null && (validator is null || validator(value))) return value;
            }
            catch { }
            return fallback;
        }
    }

    public void Save<T>(string fileName, T value)
    {
        ValidateFileName(fileName);
        var path = Path.Combine(Paths.Config, fileName); var temp = path + ".tmp";
        lock (_gate)
        {
            _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationWrite);
            Paths.Ensure();
            var json = JsonSerializer.Serialize(value, _options);
            if (Encoding.UTF8.GetByteCount(json) > MaxConfigurationBytes) throw new InvalidDataException("配置文件超过 10 MiB 安全上限。");
            File.WriteAllText(temp, json);
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read)) stream.Flush(true);
            ReplaceAtomically(temp, path);
        }
    }

    private static void ReplaceAtomically(string temp, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try { File.Replace(temp, path, null, true); }
        catch (PlatformNotSupportedException) { File.Move(temp, path, true); }
        catch (IOException) { File.Move(temp, path, true); }
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) || fileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("配置文件名必须是当前配置目录中的简单文件名。", nameof(fileName));
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
