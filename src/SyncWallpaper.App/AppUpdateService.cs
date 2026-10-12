using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.App;

internal static class AppUpdateService
{
    private const string OwnerMarker = "syncwallpaper-update-owner.txt";
    private const string OwnerText = "SyncWallpaper update workspace v1";
    public static string WorkRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaperUpdates");

    public static async Task<PreparedUpdate> DownloadAsync(UpdatePackage package,
        IProgress<UpdateDownloadProgress> progress, CancellationToken token)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var downloader = new UpdatePackageDownloader(client, ProjectLinks.RepositorySettings);
        var prepared = await downloader.DownloadAndStageAsync(package, WorkRoot, progress, token).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(prepared.WorkDirectory, OwnerMarker), OwnerText, token).ConfigureAwait(false);
            return prepared;
        }
        catch
        {
            // This is the downloader's unique, just-created directory.
            UpdatePackageLayout.EnsureNoReparseAncestors(prepared.WorkDirectory);
            if (!Directory.EnumerateFileSystemEntries(prepared.WorkDirectory, "*", SearchOption.AllDirectories)
                .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                Directory.Delete(prepared.WorkDirectory, true);
            throw;
        }
    }

    public static bool IsInstalledLayout => string.Equals(new DirectoryInfo(AppContext.BaseDirectory).Name, "App", StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "SyncWallpaper.Updater.exe"));

    public static async Task<Process> StartInstallerAsync(PreparedUpdate prepared, string dataRoot, CancellationToken token)
    {
        if (!IsInstalledLayout) throw new InvalidOperationException("当前程序未包含更新助手，请从发布页面安装完整版本后再试。");
        ValidateOwnedWorkspace(prepared.WorkDirectory);
        var appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var installRoot = Directory.GetParent(appDirectory)!.FullName;
        var runnerDirectory = Path.Combine(prepared.WorkDirectory, "runner");
        await Task.Run(() => CopyRunner(appDirectory, runnerDirectory, token), token).ConfigureAwait(false);
        using var current = Process.GetCurrentProcess();
        var plan = new UpdateInstallPlan
        {
            InstallRoot = installRoot, PackageDirectory = prepared.PackageDirectory,
            WorkDirectory = prepared.WorkDirectory, Version = prepared.Version,
            ParentProcessId = current.Id, ParentStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks,
            DataRoot = dataRoot
        };
        var planPath = Path.Combine(prepared.WorkDirectory, "install-plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Path.Combine(runnerDirectory, "SyncWallpaper.Updater.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = runnerDirectory
        };
        start.ArgumentList.Add("--plan");
        start.ArgumentList.Add(planPath);
        var helper = Process.Start(start) ?? throw new IOException("无法启动更新助手。");
        try
        {
            var readyPath = Path.Combine(prepared.WorkDirectory, UpdateInstallPlan.HelperReadyFileName);
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                token.ThrowIfCancellationRequested();
                if (helper.HasExited) throw new IOException("更新助手未能准备完成，旧版本保持不变。");
                if (File.Exists(readyPath))
                {
                    try
                    {
                        using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyPath, token).ConfigureAwait(false));
                        if (ready.RootElement.GetProperty("ProcessId").GetInt32() == helper.Id
                            && ready.RootElement.GetProperty("StartTimeUtcTicks").GetInt64() == helper.StartTime.ToUniversalTime().Ticks)
                        {
                            token.ThrowIfCancellationRequested();
                            return helper;
                        }
                    }
                    catch (JsonException) { } // The helper may still be finishing its marker write.
                }
                await Task.Delay(150, token).ConfigureAwait(false);
            }
            throw new IOException("更新助手准备超时，旧版本保持不变。请稍后重试。");
        }
        catch
        {
            // The current app is still running: this helper has not begun its swap.
            // Stop only the helper launched above, never any existing app instance.
            try { if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync().ConfigureAwait(false); } }
            finally { helper.Dispose(); }
            throw;
        }
    }

    private static void CopyRunner(string source, string target, CancellationToken token)
    {
        UpdatePackageLayout.EnsureNoReparseAncestors(source);
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("程序目录包含链接，无法自动更新。");
            var destination = Path.Combine(target, Path.GetRelativePath(source, entry));
            if ((attributes & FileAttributes.Directory) != 0) Directory.CreateDirectory(destination);
            else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(entry, destination, false); }
        }
    }

    public static void DeleteOwnedWorkspace(string directory)
    {
        if (!Directory.Exists(directory)) return;
        ValidateOwnedWorkspace(directory);
        if (HelperIsRunning(directory)) throw new IOException("更新助手仍在运行，暂时保留更新文件。");
        if (Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("更新目录包含链接，已保留供检查。");
        Directory.Delete(directory, true);
    }

    public static async Task CleanupCompletedAsync(Action<string> log)
    {
        try { await CleanupCompletedCoreAsync(log).ConfigureAwait(false); }
        catch (Exception ex) { log("更新临时目录暂未清理：" + ex.Message); }
    }

    private static async Task CleanupCompletedCoreAsync(Action<string> log)
    {
        // The new application may start before its helper has written the result.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (!Directory.Exists(WorkRoot)) return;
            foreach (var directory in Directory.EnumerateDirectories(WorkRoot))
            {
                try
                {
                    if (!Guid.TryParse(Path.GetFileName(directory), out _) || !File.Exists(Path.Combine(directory, OwnerMarker))) continue;
                    var resultPath = Path.Combine(directory, UpdateInstallPlan.ResultFileName);
                    if (File.Exists(resultPath))
                    {
                        var result = JsonSerializer.Deserialize<UpdateInstallResult>(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false));
                        if (result is { RecoveryDirectory: null } && (result.Success || result.RolledBack)) DeleteOwnedWorkspace(directory);
                    }
                    else if (!File.Exists(Path.Combine(directory, "install-plan.json"))
                        && Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1)) DeleteOwnedWorkspace(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                { log("更新临时文件暂未清理：" + ex.Message); }
            }
        }
    }

    public static void WriteApplicationReady(string? readyFile)
    {
        if (string.IsNullOrWhiteSpace(readyFile)) return;
        var path = Path.GetFullPath(readyFile);
        var directory = Path.GetDirectoryName(path)!;
        ValidateOwnedWorkspace(directory);
        if (Path.GetFileName(path) != UpdateInstallPlan.AppReadyFileName) throw new InvalidDataException("更新启动标记无效。");
        File.WriteAllText(path, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void ValidateOwnedWorkspace(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!string.Equals(Path.GetDirectoryName(full), WorkRoot, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(Path.GetFileName(full), out _)) throw new IOException("更新临时目录不在预期位置。");
        UpdatePackageLayout.EnsureNoReparseAncestors(full);
        if (File.ReadAllText(Path.Combine(full, OwnerMarker)) != OwnerText) throw new IOException("无法确认更新临时目录的归属。");
    }

    private static bool HelperIsRunning(string directory)
    {
        var path = Path.Combine(directory, UpdateInstallPlan.HelperReadyFileName);
        if (!File.Exists(path)) return false;
        using var ready = JsonDocument.Parse(File.ReadAllText(path));
        try
        {
            using var process = Process.GetProcessById(ready.RootElement.GetProperty("ProcessId").GetInt32());
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ready.RootElement.GetProperty("StartTimeUtcTicks").GetInt64();
        }
        catch (ArgumentException) { return false; }
    }
}
