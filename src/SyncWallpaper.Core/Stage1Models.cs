namespace SyncWallpaper.Core;

public readonly record struct Int32Rect(int Left, int Top, int Width, int Height);

public sealed class DisplayConfigurationProfile
{
    public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<DisplayConfigurationEntry> Displays { get; set; } = new();
    public string? AssociatedWallpaperProfileId { get; set; }
    public string? AssociatedAudioProfileId { get; set; }
    public List<string> PreActions { get; set; } = new();
    public List<string> PostActions { get; set; } = new();
    public bool IsAutoMatchTarget { get; set; }
    public bool IsShortcut { get; set; }
}

public sealed class DisplayConfigurationEntry
{
    public MonitorIdentity MonitorFingerprint { get; set; } = new();
    public string AdapterLuid { get; set; } = string.Empty;
    public uint SourceId { get; set; }
    public uint TargetId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsPrimary { get; set; }
    public int DesktopX { get; set; }
    public int DesktopY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public uint RefreshRateNumerator { get; set; } = 60;
    public uint RefreshRateDenominator { get; set; } = 1;
    public int Rotation { get; set; } = 1;
    public double DpiScale { get; set; } = 1.0;
    public bool? HdrEnabled { get; set; }
    public string ColorMode { get; set; } = string.Empty;
}

public sealed class DisplayTopologySnapshot
{
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public DisplayConfigurationProfile Profile { get; init; } = new();
    public string NativeSignature { get; init; } = string.Empty;
    public object? NativeState { get; init; }
}

public sealed class DisplayValidationResult
{
    public bool IsValid { get; init; }
    public bool RequiresConfirmation { get; init; } = true;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DisplayConfigurationDiff> Differences { get; init; } = Array.Empty<DisplayConfigurationDiff>();

    public static DisplayValidationResult Valid(
        IEnumerable<string>? warnings = null,
        IEnumerable<DisplayConfigurationDiff>? differences = null)
        => new() { IsValid = true, Warnings = warnings?.ToArray() ?? Array.Empty<string>(), Differences = differences?.ToArray() ?? Array.Empty<DisplayConfigurationDiff>() };

    public static DisplayValidationResult Invalid(params string[] errors) => new() { IsValid = false, Errors = errors };
}

public sealed record DisplayConfigurationDiff(string Subject, string CurrentValue, string TargetValue);

public sealed class DisplayConfigurationApplyOptions
{
    public bool RequireConfirmation { get; init; } = true;
    public TimeSpan ConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public bool ValidationOnly { get; init; }
    public bool RestoreOnConfirmationTimeout { get; init; } = true;
}

public sealed class DisplayConfigurationApplyResult
{
    public DisplayConfigurationTransactionStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DisplayValidationResult? Validation { get; init; }
    public bool Applied { get; init; }
    public bool Verified { get; init; }
    public bool RollbackAttempted { get; init; }
    public bool RollbackSucceeded { get; init; }
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
}

public enum DisplayConfigurationTransactionStatus
{
    Planned,
    PrecheckFailed,
    Applied,
    VerificationFailed,
    ConfirmationExpired,
    RolledBack,
    RollbackFailed,
    Cancelled,
    Failed
}

public sealed class DisplayConfigurationDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<DisplayConfigurationProfile> Profiles { get; set; } = new();
}

public enum AudioEndpointKind
{
    Playback,
    Capture
}

public enum AudioEndpointRole
{
    Console,
    Multimedia,
    Communications,
    Recording
}

public enum AudioStepMode
{
    Required,
    Optional,
    Disabled
}

public enum AudioEndpointState
{
    Unknown,
    Active,
    Disabled,
    NotPresent,
    Unplugged
}

public sealed class AudioEndpointReference
{
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public AudioEndpointKind Kind { get; set; }
    public AudioEndpointState State { get; set; } = AudioEndpointState.Unknown;
}

public sealed class AudioRoleAssignment
{
    public AudioEndpointRole Role { get; set; }
    public AudioEndpointReference Endpoint { get; set; } = new();
    public AudioStepMode Mode { get; set; } = AudioStepMode.Optional;
}

public sealed class AudioProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<AudioRoleAssignment> Assignments { get; set; } = new();
}

public sealed class AudioProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<AudioProfile> Profiles { get; set; } = new();
}

public sealed class AudioEndpointStateSnapshot
{
    public IReadOnlyList<AudioEndpointReference> Endpoints { get; init; } = Array.Empty<AudioEndpointReference>();
    public IReadOnlyDictionary<AudioEndpointRole, AudioEndpointReference?> Defaults { get; init; } = new Dictionary<AudioEndpointRole, AudioEndpointReference?>();
}

public sealed class AudioConfigurationResult
{
    public bool Success { get; init; }
    public bool RequiredFailure { get; init; }
    public bool Cancelled { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RollbackAttempted { get; init; }
    public bool RollbackSucceeded { get; init; }
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
}

public enum WindowMatchKind
{
    UserRuleId,
    ExecutablePath,
    AppUserModelId,
    ProcessName,
    WindowClass,
    TitlePattern,
    ManualBinding
}

public sealed class WindowIdentity
{
    public string UserRuleId { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public string AppUserModelId { get; init; } = string.Empty;
    public bool IsUwp { get; init; }
    public bool IsElevated { get; init; }
}

public sealed class WindowPositionSnapshot
{
    public IntPtr Handle { get; init; }
    public WindowIdentity Identity { get; init; } = new();
    public string MonitorDevicePath { get; init; } = string.Empty;
    public Int32Rect PhysicalBounds { get; init; }
    public int Dpi { get; init; } = 96;
    public int ShowState { get; init; } = 1;
    public bool IsMaximized { get; init; }
    public bool IsMinimized { get; init; }
}

public sealed class WindowRestoreOptions
{
    public bool StartUnlaunchedApplications { get; init; }
    public TimeSpan ApplicationWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 2;
    public bool RestoreZOrder { get; init; }
}

public sealed class WindowRestoreResult
{
    public int Matched { get; init; }
    public int Applied { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}
