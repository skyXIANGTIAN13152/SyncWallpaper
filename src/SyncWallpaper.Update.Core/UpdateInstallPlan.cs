using System.Diagnostics;

namespace SyncWallpaper.Update.Core;

/// <summary>Only program files may be changed by this plan. User data is never an installation target.</summary>
public sealed class UpdateInstallPlan
{
    public string InstallRoot { get; set; } = string.Empty;
    public string PackageDirectory { get; set; } = string.Empty;
    public string WorkDirectory { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int ParentProcessId { get; set; }
    public long ParentStartTimeUtcTicks { get; set; }
    public string DataRoot { get; set; } = string.Empty;

    public const string HelperReadyFileName = "updater-ready.json";
    public const string AppReadyFileName = "app-ready.txt";
    public const string ResultFileName = "update-result.json";
}

public sealed record UpdateInstallResult(bool Success, bool RolledBack, string Message, string? RecoveryDirectory = null);

/// <summary>Indicates that no installed file was changed, so the helper can reopen the original app.</summary>
public sealed class UpdatePreparationException(string message, Exception innerException, string? recoveryDirectory = null)
    : IOException(message, innerException)
{
    public string? RecoveryDirectory { get; } = recoveryDirectory;
}

/// <summary>Separates process startup/readiness from the directory transaction for deterministic tests.</summary>
public interface IUpdateApplicationLauncher
{
    Task<IUpdateApplicationProcess> LaunchAsync(string executablePath, string dataRoot,
        string? readyFilePath, CancellationToken cancellationToken);
}

public interface IUpdateApplicationProcess : IDisposable
{
    Task<bool> WaitUntilReadyAsync(string readyFilePath, TimeSpan timeout, CancellationToken cancellationToken);
    /// <summary>Must stop only the process started by this launcher, before rollback changes any files.</summary>
    Task<bool> StopForRollbackAsync(CancellationToken cancellationToken);
}

public sealed class UpdateInstallerOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public Func<string, string?> ReadProductVersion { get; init; } = path => FileVersionInfo.GetVersionInfo(path).ProductVersion;
    // A hook for deterministic locked-file/failure testing, never used by the production helper.
    public Action<string, string>? BeforeDirectoryMove { get; init; }
    public Action<string>? BeforePreparationDirectoryCreate { get; init; }
}
