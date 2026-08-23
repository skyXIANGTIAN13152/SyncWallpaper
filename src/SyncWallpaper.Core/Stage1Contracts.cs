namespace SyncWallpaper.Core;

public interface IDisplayTopologyReader
{
    DisplayTopologySnapshot Capture();
}

public interface IDisplayConfigurationValidator
{
    DisplayValidationResult Validate(DisplayConfigurationProfile target, DisplayTopologySnapshot current);
}

public interface IDisplayConfigurationApplier
{
    Task ApplyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken);
}

/// <summary>
/// Optional two-phase display applier.  Implementations first commit topology and
/// basic modes, wait for Windows to settle, then commit position/rotation/final
/// mode fields.  Test and non-CCD adapters can implement only the base applier and
/// the transaction service will use its single-phase fallback.
/// </summary>
public interface IStagedDisplayConfigurationApplier : IDisplayConfigurationApplier
{
    Task ApplyTopologyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken);
    Task ApplyFinalAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken);
}

public interface IDisplayConfigurationVerifier
{
    Task<DisplayValidationResult> VerifyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken);
}

public interface IDisplayConfigurationRollbackService
{
    Task RollbackAsync(DisplayTopologySnapshot snapshot, CancellationToken cancellationToken);
}

public interface IDisplayChangeStabilizer
{
    Task WaitForStableAsync(CancellationToken cancellationToken);
}

public interface IDisplayProfileRepository
{
    IReadOnlyList<DisplayConfigurationProfile> List();
    void Save(DisplayConfigurationProfile profile);
    bool Delete(string profileId);
}

public interface IDisplayConfirmationService
{
    Task<bool> ConfirmAsync(DisplayConfigurationProfile profile, DisplayValidationResult validation, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IStage1Logger
{
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message);
}

public interface IAudioEndpointProvider
{
    IReadOnlyList<AudioEndpointReference> Enumerate();
    AudioEndpointReference? GetDefault(AudioEndpointRole role);
    Task SetDefaultAsync(AudioEndpointReference endpoint, AudioEndpointRole role, CancellationToken cancellationToken);
    event EventHandler? DevicesChanged;
    event EventHandler? DefaultsChanged;
}

public interface IAudioConfigurationEngine
{
    Task<AudioConfigurationResult> ApplyAsync(AudioProfile profile, AudioStepMode overallMode = AudioStepMode.Optional, CancellationToken cancellationToken = default);
}

public interface IWindowPlatform
{
    IReadOnlyList<WindowPositionSnapshot> Enumerate();
    bool TrySetPosition(WindowPositionSnapshot window, Int32Rect physicalBounds, bool maximize);
    bool TryStartApplication(string executablePath, string arguments);
}

public interface IWindowZonePlatform
{
    WindowPositionSnapshot? TryGetWindow(IntPtr handle);
    Int32Point? GetCursorPosition();
    bool IsShiftPressed();
    bool TrySetPosition(WindowPositionSnapshot window, Int32Rect physicalBounds, bool maximize);
}

public interface IWindowIdentityMatcher
{
    bool IsMatch(WindowIdentity saved, WindowIdentity current, out WindowMatchKind? matchedBy);
}

public interface IWindowLayoutEngine
{
    IReadOnlyList<WindowPositionSnapshot> Capture();
    WindowRestoreResult Restore(WindowPositionProfile profile, IReadOnlyList<MonitorIdentity> monitors, WindowRestoreOptions? options = null);
}

public interface IAutomationClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IAutomationActionExecutor
{
    Task<AutomationExecutionResult> ExecuteAsync(ActionDefinition action, AutomationExecutionContext context, CancellationToken cancellationToken);
}

public interface IAutomationEngine
{
    Task<IReadOnlyList<AutomationExecutionResult>> FireAsync(TriggerDefinition trigger, IEnumerable<AutomationRule> rules, AutomationExecutionContext context, CancellationToken cancellationToken = default);
}

public interface IDesktopIconProvider
{
    IReadOnlyList<DesktopIconPosition> Capture();
    bool TrySetPosition(DesktopIconPosition position);
    bool TrySetViewSettings(int iconSize, bool autoArrange, bool alignToGrid);
}

public interface IDesktopIconLayoutEngine
{
    DesktopIconProfile Capture(string name);
    DesktopIconRestoreResult Restore(DesktopIconProfile profile, Int32Rect virtualBounds);
}

public sealed class TriggerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public AutomationTriggerType Type { get; set; }
    public string? Value { get; set; }
}

public enum AutomationTriggerType
{
    ApplicationStarted,
    WindowsLoggedIn,
    DisplayConfigurationChanged,
    DisplayConfigurationStable,
    PowerResumed,
    WindowsUnlocked,
    ScheduledTime,
    ProcessStarted,
    ProcessExited,
    WindowCreated,
    WindowFocused,
    DisplayProfileApplied,
    NetworkChanged,
    Manual
}

public sealed class ConditionDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}

public sealed class ActionDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AutomationActionType Type { get; set; }
    public string Argument { get; set; } = string.Empty;
    public string? Argument2 { get; set; }
    public bool Required { get; set; }
    public bool AllowShellCommand { get; set; }
}

public enum AutomationActionType
{
    ApplyDisplayProfile,
    ApplyWallpaperProfile,
    ApplyAudioProfile,
    RestoreWindowProfile,
    StartProgram,
    CloseProgram,
    Wait,
    Notify,
    WriteLog,
    InternalCommand,
    CustomShellCommand
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public TriggerDefinition Trigger { get; set; } = new();
    public List<ConditionDefinition> Conditions { get; set; } = new();
    public List<ActionDefinition> Actions { get; set; } = new();
    public TimeSpan Cooldown { get; set; }
    public TimeSpan Debounce { get; set; }
    public TimeSpan MaximumExecutionTime { get; set; } = TimeSpan.FromMinutes(2);
    public bool ContinueOnError { get; set; }
    public bool StopProcessing { get; set; }
}

public sealed class AutomationExecutionContext
{
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N");
    public AutomationTriggerType TriggerType { get; init; }
    public string? Value { get; init; }
    public string? ActiveDisplayProfileId { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public ISet<string> Ancestors { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, string> Data { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AutomationExecutionResult
{
    public string RuleId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}

public sealed class DesktopIconRestoreResult
{
    public int Applied { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}
