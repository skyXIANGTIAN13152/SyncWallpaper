using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            MessageBox.Show("无法启动屏序更新程序：\n" + error.Message, "屏序更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--plan")
            throw new InvalidDataException("请从屏序中的更新提示启动更新。");
        var planFile = Path.GetFullPath(args[1]);
        UpdateInstaller.EnsureNoReparseParents(planFile);
        if (!File.Exists(planFile) || new FileInfo(planFile).Length > 64 * 1024)
            throw new InvalidDataException("更新计划不存在或不完整。");
        var plan = JsonSerializer.Deserialize<UpdateInstallPlan>(await File.ReadAllTextAsync(planFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("更新计划无法读取。");
        var installer = new UpdateInstaller();
        installer.Validate(plan);
        if (!string.Equals(Path.GetDirectoryName(planFile), Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.WorkDirectory)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新计划不在本次独立工作目录中。");
        var ownPath = Environment.ProcessPath ?? throw new InvalidDataException("无法确认更新程序的位置。");
        if (!UpdateInstaller.IsWithin(ownPath, Path.Combine(plan.WorkDirectory, "runner"))
            || UpdateInstaller.IsWithin(ownPath, Path.Combine(plan.InstallRoot, "App")))
            throw new InvalidDataException("更新助手必须从独立工作目录运行。");
        var mutexKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(plan.InstallRoot).ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\SyncWallpaper.Update." + mutexKey);
        // Mutex ownership is thread-affine; this thread remains blocked until the async transaction ends.
        if (!mutex.WaitOne(0)) throw new InvalidOperationException("另一个屏序更新正在进行。");
        try
        {
            return RunOwnedAsync(plan, installer).GetAwaiter().GetResult();
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static async Task<int> RunOwnedAsync(UpdateInstallPlan plan, UpdateInstaller installer)
    {
        var resultPath = Path.Combine(plan.WorkDirectory, UpdateInstallPlan.ResultFileName);
        var logPath = Path.Combine(plan.WorkDirectory, "updater.log");
        UpdateInstallResult? completedResult = null;
        try
        {
            await LogAsync(logPath, "更新计划已验证，准备等待屏序正常退出。");
            using var parent = FindOriginalParent(plan);
            using var self = Process.GetCurrentProcess();
            await WriteJsonAsync(Path.Combine(plan.WorkDirectory, UpdateInstallPlan.HelperReadyFileName), new
            {
                ProcessId = self.Id,
                StartTimeUtcTicks = self.StartTime.ToUniversalTime().Ticks,
                plan.Version
            });
            if (parent is not null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await parent.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                { throw new IOException("屏序还未退出，更新已取消。请保存编辑后重新尝试。"); }
            }
            EnsureNoOtherApplication(plan);
            await LogAsync(logPath, "原程序已退出，开始替换程序文件。");
            var launcher = new ApplicationLauncher();
            UpdateInstallResult result;
            try { result = await installer.ExecuteAsync(plan, launcher); }
            catch (UpdatePreparationException preparationError)
            {
                // The core explicitly guarantees this exception is raised before changing installed files.
                var oldExecutable = Path.Combine(plan.InstallRoot, "App", "SyncWallpaper.App.exe");
                UpdateInstaller.EnsureNoReparseParents(oldExecutable);
                using var reopened = await launcher.LaunchAsync(oldExecutable, plan.DataRoot, null, CancellationToken.None);
                result = new(false, true, "更新准备失败，已重新打开旧版本。" + preparationError.Message, preparationError.RecoveryDirectory);
            }
            completedResult = result;
            await WriteJsonAsync(resultPath, result);
            await LogAsync(logPath, result.Message + (result.RecoveryDirectory is null ? "" : " 备份：" + result.RecoveryDirectory));
            if (!result.Success)
                MessageBox.Show(result.Message + (result.RecoveryDirectory is null ? "" : "\n恢复备份：" + result.RecoveryDirectory),
                    "屏序更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return result.Success ? 0 : 1;
        }
        catch (Exception error)
        {
            if (completedResult is not null)
            {
                MessageBox.Show(completedResult.Message + "\n更新结果或日志未能保存：" + error.Message,
                    "屏序更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return completedResult.Success ? 0 : 1;
            }
            var result = new UpdateInstallResult(false, false, "更新已取消，未继续替换程序：" + error.Message);
            // Reporting failure must not hide the original failure or claim a completed update was cancelled.
            try { await WriteJsonAsync(resultPath, result); await LogAsync(logPath, error.ToString()); }
            catch (Exception reportError) { result = result with { Message = result.Message + "\n无法保存日志：" + reportError.Message }; }
            MessageBox.Show(result.Message, "屏序更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    private static Process? FindOriginalParent(UpdateInstallPlan plan)
    {
        if (plan.ParentProcessId <= 0 || plan.ParentStartTimeUtcTicks <= 0)
            throw new InvalidDataException("更新计划缺少原程序的启动信息。");
        Process process;
        try { process = Process.GetProcessById(plan.ParentProcessId); }
        catch (ArgumentException) { return null; }
        try
        {
            if (process.StartTime.ToUniversalTime().Ticks != plan.ParentStartTimeUtcTicks
                || !string.Equals(process.MainModule?.FileName,
                    Path.Combine(Path.GetFullPath(plan.InstallRoot), "App", "SyncWallpaper.App.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("原程序身份发生变化，已取消更新。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    private static void EnsureNoOtherApplication(UpdateInstallPlan plan)
    {
        var expected = Path.Combine(Path.GetFullPath(plan.InstallRoot), "App", "SyncWallpaper.App.exe");
        foreach (var process in Process.GetProcessesByName("SyncWallpaper.App"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("安装位置仍有屏序进程运行，已取消更新。");
                }
                catch (InvalidOperationException) { /* The process exited during inspection. */ }
            }
        }
    }

    private static async Task WriteJsonAsync(string path, object value)
    {
        UpdateInstaller.EnsureNoReparseParents(path);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task LogAsync(string path, string message)
    {
        UpdateInstaller.EnsureNoReparseParents(path);
        await File.AppendAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    }
}
