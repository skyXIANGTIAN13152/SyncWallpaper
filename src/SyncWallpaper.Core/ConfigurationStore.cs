using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

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
    private const long MaxConfigurationBytes = 10 * 1024 * 1024;
    private const int MaximumSupportedRecoveryVersions = 5;
    private readonly int _recoveryVersions;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 32, Converters = { new JsonStringEnumConverter() } };
    private readonly object _gate = new();
    private readonly IFaultInjector _faultInjector;
    public DataPaths Paths { get; }
    public ConfigurationStore(DataPaths? paths = null, IFaultInjector? faultInjector = null, int recoveryVersions = 0)
    {
        if (recoveryVersions < 0 || recoveryVersions > MaximumSupportedRecoveryVersions) throw new ArgumentOutOfRangeException(nameof(recoveryVersions));
        Paths = paths ?? new DataPaths();
        Paths.Ensure();
        _faultInjector = faultInjector ?? NoFaultInjector.Instance;
        _recoveryVersions = recoveryVersions;
    }

    public T Load<T>(string fileName, T fallback, Func<T, bool>? validator = null)
    {
        ValidateFileName(fileName);
        var path = Path.Combine(Paths.Config, fileName); var backup = Path.Combine(Paths.Backups, fileName + ".bak");
        lock (_gate)
        {
            if (_faultInjector.IsRequested(FaultPoint.ConfigurationCorrupt))
            {
                try { _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationCorrupt); } catch (InjectedFaultException) { return fallback; }
            }
            foreach (var candidate in RecoveryCandidates(fileName, path, backup))
            {
                try
                {
                    if (!File.Exists(candidate) || new FileInfo(candidate).Length > MaxConfigurationBytes) continue;
                    var value = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), _options);
                    if (value is not null && (validator is null || validator(value))) return value;
                }
                catch { /* try the last known good copy */ }
            }
            return fallback;
        }
    }

    public void Save<T>(string fileName, T value)
    {
        ValidateFileName(fileName);
        var path = Path.Combine(Paths.Config, fileName); var backup = Path.Combine(Paths.Backups, fileName + ".bak"); var temp = path + ".tmp";
        lock (_gate)
        {
            _faultInjector.ThrowIfRequested(FaultPoint.ConfigurationWrite);
            Paths.Ensure();
            var json = JsonSerializer.Serialize(value, _options);
            if (Encoding.UTF8.GetByteCount(json) > MaxConfigurationBytes) throw new InvalidDataException("配置文件超过 10 MiB 安全上限。");
            File.WriteAllText(temp, json);
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read)) stream.Flush(true);
            if (_recoveryVersions > 0) RotateBackups(fileName, path, backup);
            ReplaceAtomically(temp, path);
        }
    }

    public IReadOnlyList<ConfigurationRecoveryPoint> ListRecoveryPoints(string fileName)
    {
        ValidateFileName(fileName);
        lock (_gate)
        {
            var list = new List<ConfigurationRecoveryPoint>();
            var primary = Path.Combine(Paths.Config, fileName);
            if (File.Exists(primary)) list.Add(new(0, primary, File.GetLastWriteTimeUtc(primary), new FileInfo(primary).Length));
            foreach (var candidate in RecoveryCandidates(fileName, primary, Path.Combine(Paths.Backups, fileName + ".bak")).Skip(1))
                if (File.Exists(candidate)) list.Add(new(ExtractVersion(candidate, fileName), candidate, File.GetLastWriteTimeUtc(candidate), new FileInfo(candidate).Length));
            return list.OrderBy(x => x.Version).ToArray();
        }
    }

    public void Restore(string fileName, int version)
    {
        ValidateFileName(fileName);
        if (version < 0 || version > _recoveryVersions) throw new ArgumentOutOfRangeException(nameof(version));
        var source = version == 0 ? Path.Combine(Paths.Config, fileName) : Path.Combine(Paths.Backups, fileName + ".bak" + (version == 1 ? string.Empty : "." + (version - 1)));
        if (!File.Exists(source)) throw new FileNotFoundException("找不到指定的配置恢复点。", source);
        var target = Path.Combine(Paths.Config, fileName);
        lock (_gate)
        {
            if (new FileInfo(source).Length > MaxConfigurationBytes) throw new InvalidDataException("恢复点超过安全大小上限。");
            using var document = JsonDocument.Parse(File.ReadAllText(source), new JsonDocumentOptions { MaxDepth = 32 });
            var temp = target + ".restore.tmp";
            File.Copy(source, temp, true);
            File.Move(temp, target, true);
        }
    }

    private IEnumerable<string> RecoveryCandidates(string fileName, string primary, string backup)
    {
        yield return primary;
        if (_recoveryVersions == 0) yield break;
        yield return backup;
        for (var i = 1; i < _recoveryVersions; i++) yield return Path.Combine(Paths.Backups, fileName + ".bak." + i);
    }

    private void RotateBackups(string fileName, string path, string backup)
    {
        if (!File.Exists(path)) return;
        for (var i = _recoveryVersions - 1; i >= 1; i--)
        {
            var from = Path.Combine(Paths.Backups, fileName + ".bak." + (i - 1 == 0 ? string.Empty : (i - 1).ToString()));
            var to = Path.Combine(Paths.Backups, fileName + ".bak." + i);
            if (i == 1) from = backup;
            if (File.Exists(from)) File.Copy(from, to, true);
        }
        File.Copy(path, backup, true);
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

    private static int ExtractVersion(string path, string fileName)
    {
        var suffix = path[(Path.Combine(string.Empty, fileName + ".bak").Length)..].Trim('.');
        return string.IsNullOrWhiteSpace(suffix) ? 1 : int.TryParse(suffix, out var value) ? value + 1 : 99;
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) || fileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("配置文件名必须是当前配置目录中的简单文件名。", nameof(fileName));
    }
}

public sealed record ConfigurationRecoveryPoint(int Version, string Path, DateTime LastWriteTimeUtc, long SizeBytes);

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
