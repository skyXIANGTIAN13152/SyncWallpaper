namespace SyncWallpaper.TaskbarHost;

public sealed record TaskbarTaskGroup(
    string Key,
    string DisplayName,
    string ProcessPath,
    string AppUserModelId,
    IReadOnlyList<TaskbarTaskItem> Tasks)
{
    public int Count => Tasks.Count;
    public bool IsForeground => Tasks.Any(x => x.IsForeground);
    public bool AllMinimized => Tasks.Count > 0 && Tasks.All(x => x.IsMinimized);
    public TaskbarTaskItem PreviewTask => Tasks.FirstOrDefault(x => x.IsForeground)
        ?? Tasks.FirstOrDefault(x => !x.IsMinimized)
        ?? Tasks[0];
}

public static class TaskbarGrouping
{
    public static IReadOnlyList<TaskbarTaskGroup> Build(IEnumerable<TaskbarTaskItem> tasks)
        => tasks
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var windows = group
                    .OrderByDescending(x => x.IsForeground)
                    .ThenBy(x => x.IsMinimized)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var first = windows[0];
                var displayName = !string.IsNullOrWhiteSpace(first.ProcessName)
                    ? first.ProcessName
                    : !string.IsNullOrWhiteSpace(first.AppUserModelId)
                        ? AppName(first.AppUserModelId)
                        : first.Title;
                return new TaskbarTaskGroup(
                    group.Key,
                    displayName,
                    first.ProcessPath,
                    first.AppUserModelId,
                    windows);
            })
            .OrderByDescending(x => x.IsForeground)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string GroupKey(TaskbarTaskItem task)
    {
        if (!string.IsNullOrWhiteSpace(task.AppUserModelId))
            return "aumid:" + task.AppUserModelId.Trim();
        if (!string.IsNullOrWhiteSpace(task.ProcessPath))
            return "path:" + task.ProcessPath.Trim();
        if (!string.IsNullOrWhiteSpace(task.ProcessName))
            return "process:" + task.ProcessName.Trim();
        return "class:" + task.WindowClass.Trim();
    }

    private static string AppName(string appUserModelId)
    {
        var bang = appUserModelId.IndexOf('!');
        var package = bang > 0 ? appUserModelId[..bang] : appUserModelId;
        var underscore = package.IndexOf('_');
        return underscore > 0 ? package[..underscore] : package;
    }
}
