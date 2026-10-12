using System.Diagnostics;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Updater;

internal sealed class ApplicationLauncher : IUpdateApplicationLauncher
{
    public Task<IUpdateApplicationProcess> LaunchAsync(string executablePath, string dataRoot,
        string? readyFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        start.Environment["SYNCWALLPAPER_DATA_ROOT"] = dataRoot;
        if (readyFilePath is not null)
        {
            start.ArgumentList.Add("--update-ready");
            start.ArgumentList.Add(readyFilePath);
        }
        var process = Process.Start(start) ?? throw new IOException("无法打开屏序。");
        return Task.FromResult<IUpdateApplicationProcess>(new ApplicationProcess(process));
    }

    private sealed class ApplicationProcess(Process process) : IUpdateApplicationProcess
    {
        public async Task<bool> WaitUntilReadyAsync(string readyFilePath, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) return false;
                if (File.Exists(readyFilePath))
                {
                    UpdateInstaller.EnsureNoReparseParents(readyFilePath);
                    // Catch an immediate startup crash rather than committing a dead process.
                    await Task.Delay(750, cancellationToken);
                    return !process.HasExited;
                }
                await Task.Delay(200, cancellationToken);
            }
            return false;
        }

        public async Task<bool> StopForRollbackAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (process.HasExited) return true;
                // This is the unconfirmed new process launched by this helper, never an existing user session.
                process.Kill(entireProcessTree: false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(deadline.Token);
                return process.HasExited;
            }
            catch (InvalidOperationException) { return true; }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException)
            { return false; }
        }

        public void Dispose() => process.Dispose();
    }
}
