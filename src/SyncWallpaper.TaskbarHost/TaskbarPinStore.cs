using System.Diagnostics;
using System.IO;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public sealed class TaskbarPinDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<TaskbarPinnedItem> Items { get; set; } = new();
}

public sealed record TaskbarPinnedItem
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string AppUserModelId { get; init; } = string.Empty;
    public DateTime PinnedAtUtc { get; init; } = DateTime.UtcNow;
    public int Order { get; init; }
}

public enum TaskbarPinLaunchResult
{
    Missing,
    Started,
    InvalidTarget,
    Failed
}

public interface ITaskbarPinStore : IDisposable
{
    IReadOnlyList<TaskbarPinnedItem> Items { get; }
    bool IsPinned(string groupKey);
    bool CanPin(TaskbarTaskGroup group);
    bool Toggle(TaskbarTaskGroup group);
    bool Remove(string id);
    TaskbarPinLaunchResult Launch(string id);
}

public sealed class JsonTaskbarPinStore : ITaskbarPinStore
{
    public const string FileName = "taskbar-pins.json";
    private readonly ConfigurationStore _store;
    private readonly object _gate = new();
    private TaskbarPinDocument _document;
    private bool _disposed;

    public JsonTaskbarPinStore(string dataRoot)
    {
        _store = new ConfigurationStore(new DataPaths(dataRoot));
        _document = Normalize(_store.Load(FileName, new TaskbarPinDocument(), Validate));
    }

    public IReadOnlyList<TaskbarPinnedItem> Items
    {
        get
        {
            lock (_gate)
                return _document.Items.OrderBy(x => x.Order).ThenBy(x => x.PinnedAtUtc).ToArray();
        }
    }

    public bool IsPinned(string groupKey)
    {
        lock (_gate)
            return _document.Items.Any(x => x.Id.Equals(groupKey, StringComparison.OrdinalIgnoreCase));
    }

    public bool CanPin(TaskbarTaskGroup group)
        => !string.IsNullOrWhiteSpace(group.AppUserModelId)
            || (!string.IsNullOrWhiteSpace(group.ProcessPath) && File.Exists(group.ProcessPath));

    /// <summary>Returns the state after toggling: true means pinned.</summary>
    public bool Toggle(TaskbarTaskGroup group)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var existing = _document.Items.FindIndex(x => x.Id.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                _document.Items.RemoveAt(existing);
                Save();
                return false;
            }
            if (!CanPin(group)) return false;
            _document.Items.Add(new TaskbarPinnedItem
            {
                Id = group.Key,
                DisplayName = group.DisplayName,
                ExecutablePath = group.ProcessPath,
                AppUserModelId = group.AppUserModelId,
                PinnedAtUtc = DateTime.UtcNow,
                Order = _document.Items.Count == 0 ? 0 : _document.Items.Max(x => x.Order) + 1
            });
            Save();
            return true;
        }
    }

    public bool Remove(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var removed = _document.Items.RemoveAll(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public TaskbarPinLaunchResult Launch(string id)
    {
        TaskbarPinnedItem? item;
        lock (_gate) item = _document.Items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null) return TaskbarPinLaunchResult.Missing;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.AppUserModelId))
            {
                if (item.AppUserModelId.Length > 512 || item.AppUserModelId.Any(char.IsControl))
                    return TaskbarPinLaunchResult.InvalidTarget;
                var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add("shell:AppsFolder\\" + item.AppUserModelId);
                return Process.Start(start) is null ? TaskbarPinLaunchResult.Failed : TaskbarPinLaunchResult.Started;
            }

            if (string.IsNullOrWhiteSpace(item.ExecutablePath) || !Path.IsPathFullyQualified(item.ExecutablePath)
                || !File.Exists(item.ExecutablePath))
                return TaskbarPinLaunchResult.InvalidTarget;
            return Process.Start(new ProcessStartInfo
            {
                FileName = item.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(item.ExecutablePath) ?? string.Empty,
                UseShellExecute = true
            }) is null ? TaskbarPinLaunchResult.Failed : TaskbarPinLaunchResult.Started;
        }
        catch
        {
            return TaskbarPinLaunchResult.Failed;
        }
    }

    private void Save() => _store.Save(FileName, _document);

    private static bool Validate(TaskbarPinDocument value)
        => value.SchemaVersion == 1 && value.Items is not null && value.Items.Count <= 256;

    private static TaskbarPinDocument Normalize(TaskbarPinDocument value)
    {
        value.Items = value.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && x.Id.Length <= 1024 && x.DisplayName.Length <= 512)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(256)
            .ToList();
        return value;
    }

    public void Dispose() => _disposed = true;
}

public static class TaskbarDataRootResolver
{
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
            return Path.GetFullPath(configured);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SyncWallpaper.sln"))
                || File.Exists(Path.Combine(directory.FullName, "package-manifest.json"))
                || (Directory.Exists(Path.Combine(directory.FullName, "Config"))
                    && Directory.Exists(Path.Combine(directory.FullName, "App"))))
                return directory.FullName;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaper");
    }
}
