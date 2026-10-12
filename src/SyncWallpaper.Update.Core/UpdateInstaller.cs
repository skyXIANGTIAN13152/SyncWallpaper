using System.Text.Json;

namespace SyncWallpaper.Update.Core;

/// <summary>
/// A portable-install transaction. Candidates and backups live on the installation volume;
/// only App, Diagnostics, and the package manifest are ever replaced.
/// </summary>
public sealed class UpdateInstaller
{
    private readonly UpdateInstallerOptions _options;
    private static readonly string[] ProtectedNames =
        ["Config", "Wallpapers", "Cache", "Thumbnails", "Logs", "Backups", "Deleted", "UserData"];

    public UpdateInstaller(UpdateInstallerOptions? options = null) => _options = options ?? new();

    public void Validate(UpdateInstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var root = AbsoluteDirectory(plan.InstallRoot);
        var work = AbsoluteDirectory(plan.WorkDirectory);
        var package = AbsoluteDirectory(plan.PackageDirectory);
        var data = AbsoluteDirectory(plan.DataRoot);
        if (!Guid.TryParse(Path.GetFileName(work), out _))
            throw new InvalidDataException("更新工作目录不是独立的临时目录。");
        if (!IsWithin(package, work) || IsWithin(root, work) || Same(root, work))
            throw new InvalidDataException("更新包或工作目录的位置不安全。");
        foreach (var target in new[] { Path.Combine(root, "App"), Path.Combine(root, "Diagnostics") })
            if (Same(work, target) || IsWithin(work, target) || Same(data, target) || IsWithin(data, target)
                || Same(package, target) || IsWithin(package, target))
                throw new InvalidDataException("程序目录中包含用户数据或更新工作目录，无法安全自动更新。");
        if (!Directory.Exists(root) || !Directory.Exists(work) || !Directory.Exists(package)
            || !File.Exists(Path.Combine(root, "App", "SyncWallpaper.App.exe")))
            throw new InvalidDataException("找不到当前程序或已下载的更新包。");
        EnsureNoReparseParents(root);
        EnsureNoReparseParents(work);
        EnsureNoReparseParents(package);
        EnsureNoReparseParents(data);
        EnsureSafeTree(Path.Combine(root, "App"), rejectPrivateNames: true);
        if (Directory.Exists(Path.Combine(root, "Diagnostics")))
            EnsureSafeTree(Path.Combine(root, "Diagnostics"), rejectPrivateNames: true);
        var manifest = Path.Combine(root, "package-manifest.json");
        if (File.Exists(manifest)) EnsureNotReparse(manifest);
        UpdatePackageLayout.ValidatePackageDirectory(package, plan.Version);
        var actualVersion = _options.ReadProductVersion(Path.Combine(package, "App", "SyncWallpaper.App.exe"));
        if (!SemanticVersion.TryParse(actualVersion, out var actual)
            || !SemanticVersion.TryParse(plan.Version, out var expected) || !actual!.Equals(expected))
            throw new InvalidDataException("更新程序的文件版本与所选版本不一致。");
    }

    public async Task<UpdateInstallResult> ExecuteAsync(UpdateInstallPlan plan, IUpdateApplicationLauncher launcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        string root;
        string? transaction = null;
        string candidate;
        string backup;
        try
        {
            Validate(plan);
            root = AbsoluteDirectory(plan.InstallRoot);
            transaction = Path.Combine(root, ".syncwallpaper-update-" + Guid.NewGuid().ToString("N"));
            candidate = Path.Combine(transaction, "candidate");
            backup = Path.Combine(transaction, "backup");
            CreatePreparationDirectory(transaction);
            File.WriteAllText(Path.Combine(transaction, "transaction.json"), JsonSerializer.Serialize(new
            {
                plan.Version, InstallRoot = root, CreatedAtUtc = DateTimeOffset.UtcNow
            }));
            CreatePreparationDirectory(candidate);
            CreatePreparationDirectory(backup);
        }
        catch (Exception error)
        {
            if (transaction is not null && (!Directory.Exists(transaction)
                || TryDeleteOwnedTransaction(transaction, AbsoluteDirectory(plan.InstallRoot))
                || TryDeleteEmptyTransaction(transaction, AbsoluteDirectory(plan.InstallRoot))))
                transaction = null;
            throw new UpdatePreparationException("更新准备失败，尚未修改原程序。" + error.Message, error, transaction);
        }
        var moved = new List<DirectoryChange>();
        var manifestChanged = false;
        var hadManifest = File.Exists(Path.Combine(root, "package-manifest.json"));
        IUpdateApplicationProcess? started = null;
        try
        {
            // Complete every copy before changing the installed application.
            CopyDirectory(Path.Combine(plan.PackageDirectory, "App"), Path.Combine(candidate, "App"));
            if (Directory.Exists(Path.Combine(plan.PackageDirectory, "Diagnostics")))
                CopyDirectory(Path.Combine(plan.PackageDirectory, "Diagnostics"), Path.Combine(candidate, "Diagnostics"));
            var desktopIcon = Path.Combine(root, "App", "SyncWallpaper-Mystery.ico");
            if (File.Exists(desktopIcon))
                File.Copy(desktopIcon, Path.Combine(candidate, "App", "SyncWallpaper-Mystery.ico"), true);
            File.Copy(Path.Combine(plan.PackageDirectory, "package-manifest.json"), Path.Combine(candidate, "package-manifest.json"));
            if (hadManifest) File.Copy(Path.Combine(root, "package-manifest.json"), Path.Combine(backup, "package-manifest.json"));
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var name in new[] { "App", "Diagnostics" })
            {
                var source = Path.Combine(candidate, name);
                if (!Directory.Exists(source)) continue;
                var change = new DirectoryChange(Path.Combine(root, name), Path.Combine(backup, name));
                moved.Add(change);
                if (Directory.Exists(change.Target))
                {
                    MoveDirectory(change.Target, change.Backup);
                    change.OldMoved = true;
                }
                MoveDirectory(source, change.Target);
                change.NewMoved = true;
            }
            // Set before the write so a partial write also restores the old manifest.
            manifestChanged = true;
            File.Copy(Path.Combine(candidate, "package-manifest.json"), Path.Combine(root, "package-manifest.json"), true);
            var readyPath = Path.Combine(plan.WorkDirectory, UpdateInstallPlan.AppReadyFileName);
            if (File.Exists(readyPath))
            {
                EnsureNotReparse(readyPath);
                File.Delete(readyPath);
            }
            started = await launcher.LaunchAsync(Path.Combine(root, "App", "SyncWallpaper.App.exe"),
                plan.DataRoot, readyPath, cancellationToken).ConfigureAwait(false);
            if (!await started.WaitUntilReadyAsync(readyPath, _options.StartupTimeout, cancellationToken).ConfigureAwait(false))
                throw new IOException("新版本未能正常启动。");
            var cleaned = TryDeleteOwnedTransaction(transaction, root);
            return new(true, false, cleaned ? "更新完成，已启动新版本。" : "更新完成，旧程序备份正在使用中，暂时保留。",
                cleaned ? null : transaction);
        }
        catch (Exception error)
        {
            // Never touch executable files while our new process is still running.
            if (started is not null)
            {
                bool stopped;
                try { stopped = await started.StopForRollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { stopped = false; }
                if (!stopped)
                    return new(false, false, "更新未能确认启动，而且新程序仍在运行。为保护文件，已保留原程序备份。" + error.Message, transaction);
            }
            var rollbackErrors = new List<string>();
            foreach (var change in moved.AsEnumerable().Reverse())
            {
                try
                {
                    if (change.NewMoved && Directory.Exists(change.Target))
                    {
                        // Move the failed new program aside instead of deleting anything before recovery.
                        MoveDirectory(change.Target, Path.Combine(transaction, "failed-" + Path.GetFileName(change.Target)));
                        change.NewMoved = false;
                    }
                    if (change.OldMoved)
                    {
                        MoveDirectory(change.Backup, change.Target);
                        change.OldMoved = false;
                    }
                }
                catch (Exception rollbackError) { rollbackErrors.Add(rollbackError.Message); }
            }
            if (manifestChanged)
            {
                try
                {
                    if (hadManifest) File.Copy(Path.Combine(backup, "package-manifest.json"), Path.Combine(root, "package-manifest.json"), true);
                    else if (File.Exists(Path.Combine(root, "package-manifest.json"))) File.Delete(Path.Combine(root, "package-manifest.json"));
                }
                catch (Exception rollbackError) { rollbackErrors.Add(rollbackError.Message); }
            }
            if (rollbackErrors.Count != 0)
                return new(false, false, "更新失败，部分程序文件未能自动恢复。原程序备份已保留：" + string.Join("；", rollbackErrors), transaction);
            // The original process has already exited. Return the user to the restored version.
            try
            {
                using var restored = await launcher.LaunchAsync(Path.Combine(root, "App", "SyncWallpaper.App.exe"),
                    plan.DataRoot, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception restartError)
            {
                return new(false, true, "更新失败，已恢复旧版本，但未能自动重新打开：" + restartError.Message, transaction);
            }
            var cleaned = TryDeleteOwnedTransaction(transaction, root);
            return new(false, true, "更新失败，已恢复并重新打开旧版本。" + error.Message, cleaned ? null : transaction);
        }
        finally
        {
            started?.Dispose();
        }
    }

    private void MoveDirectory(string source, string destination)
    {
        EnsureNoReparseParents(source);
        EnsureNoReparseParents(destination);
        _options.BeforeDirectoryMove?.Invoke(source, destination);
        Directory.Move(source, destination);
    }

    private void CreatePreparationDirectory(string path)
    {
        _options.BeforePreparationDirectoryCreate?.Invoke(path);
        Directory.CreateDirectory(path);
    }

    private static void CopyDirectory(string source, string destination)
    {
        EnsureSafeTree(source, rejectPrivateNames: true);
        Directory.CreateDirectory(destination);
        foreach (var item in Directory.EnumerateFileSystemEntries(source))
        {
            EnsureNotReparse(item);
            var target = Path.Combine(destination, Path.GetFileName(item));
            if (Directory.Exists(item)) CopyDirectory(item, target);
            else File.Copy(item, target);
        }
    }

    private static void EnsureSafeTree(string directory, bool rejectPrivateNames)
    {
        EnsureNotReparse(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNotReparse(entry);
            if (Directory.Exists(entry))
            {
                if (rejectPrivateNames && ProtectedNames.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("程序目录中发现用户数据目录，已取消自动更新。");
                EnsureSafeTree(entry, rejectPrivateNames);
            }
        }
    }

    public static void EnsureNoReparseParents(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current) || Directory.Exists(current)) EnsureNotReparse(current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureNotReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("更新路径包含链接或重定向目录，已取消自动更新。");
    }

    private static bool TryDeleteOwnedTransaction(string directory, string installRoot)
    {
        try
        {
            var full = AbsoluteDirectory(directory);
            if (!Same(Path.GetDirectoryName(full)!, installRoot)
                || !Path.GetFileName(full).StartsWith(".syncwallpaper-update-", StringComparison.Ordinal)
                || !File.Exists(Path.Combine(full, "transaction.json"))) return false;
            EnsureNoReparseParents(full);
            EnsureSafeTree(full, rejectPrivateNames: false);
            Directory.Delete(full, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return false; }
    }

    private static bool TryDeleteEmptyTransaction(string directory, string installRoot)
    {
        try
        {
            var full = AbsoluteDirectory(directory);
            if (!Same(Path.GetDirectoryName(full)!, installRoot)
                || !Path.GetFileName(full).StartsWith(".syncwallpaper-update-", StringComparison.Ordinal)) return false;
            EnsureNoReparseParents(full);
            // A manifest write may have failed before creating the ownership marker. Only remove an empty directory.
            Directory.Delete(full, recursive: false);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return false; }
    }

    public static bool IsWithin(string path, string parent) => AbsoluteDirectory(path).StartsWith(
        AbsoluteDirectory(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string left, string right) => string.Equals(AbsoluteDirectory(left), AbsoluteDirectory(right), StringComparison.OrdinalIgnoreCase);

    private static string AbsoluteDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new InvalidDataException("更新需要完整的本地路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private sealed class DirectoryChange(string target, string backup)
    {
        public string Target { get; } = target;
        public string Backup { get; } = backup;
        public bool OldMoved { get; set; }
        public bool NewMoved { get; set; }
    }
}
