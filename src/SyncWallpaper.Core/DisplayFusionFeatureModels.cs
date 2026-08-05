namespace SyncWallpaper.Core;

public sealed class MonitorProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<MonitorProfile> Profiles { get; set; } = new();
}

public sealed class MonitorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<MonitorProfileEntry> Monitors { get; set; } = new();
    public List<MonitorSplit> Splits { get; set; } = new();
    public string? WallpaperProfileId { get; set; }
    public string? WindowPositionProfileId { get; set; }
    public string? DesktopIconProfileId { get; set; }
    public TaskbarSettings Taskbar { get; set; } = new();
    public MouseManagementSettings Mouse { get; set; } = new();
    public MonitorFadingSettings Fading { get; set; } = new();
}

public sealed class MonitorProfileEntry
{
    public MonitorIdentity Fingerprint { get; set; } = new();
    public string AdapterLuid { get; set; } = string.Empty;
    public uint SourceId { get; set; }
    public uint TargetId { get; set; }
    public bool Enabled { get; set; } = true;
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public uint RefreshRateNumerator { get; set; } = 60;
    public uint RefreshRateDenominator { get; set; } = 1;
    public int Rotation { get; set; } = 1;
    public double DpiScale { get; set; } = 1.0;
    public bool? HdrEnabled { get; set; }
    public string ColorMode { get; set; } = string.Empty;
    public int DesktopX { get; set; }
    public int DesktopY { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class MonitorSplit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MonitorDevicePath { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int PaddingLeft { get; set; }
    public int PaddingTop { get; set; }
    public int PaddingRight { get; set; }
    public int PaddingBottom { get; set; }
    public bool WallpaperEnabled { get; set; } = true;
    public bool TaskbarEnabled { get; set; } = true;
    public bool WindowManagementEnabled { get; set; } = true;
}

public sealed class WindowPositionProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<WindowPositionProfile> Profiles { get; set; } = new();
}

public sealed class WindowPositionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 2;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public bool RestoreUnlaunchedApplications { get; set; }
    public List<WindowPlacement> Windows { get; set; } = new();
}

public sealed class WindowPlacement
{
    public string RuleId { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string AppUserModelId { get; set; } = string.Empty;
    public bool IsUwp { get; set; }
    public bool IsElevated { get; set; }
    public string WindowClass { get; set; } = string.Empty;
    public string TitlePattern { get; set; } = string.Empty;
    public WindowMatchKind MatchKind { get; set; } = WindowMatchKind.ExecutablePath;
    public string LaunchArguments { get; set; } = string.Empty;
    public bool AllowLaunch { get; set; }
    public string MonitorDevicePath { get; set; } = string.Empty;
    public int SavedMonitorX { get; set; }
    public int SavedMonitorY { get; set; }
    public int SavedMonitorWidth { get; set; }
    public int SavedMonitorHeight { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dpi { get; set; } = 96;
    public int ShowState { get; set; } = 1;
    public bool Maximize { get; set; }
    public bool Minimize { get; set; }
    public bool RestoreZOrder { get; set; }
}

public sealed class DesktopIconProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<DesktopIconProfile> Profiles { get; set; } = new();
}

public sealed class DesktopIconProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int IconSize { get; set; } = 32;
    public bool AutoArrange { get; set; }
    public bool AlignToGrid { get; set; } = true;
    public Dictionary<string, DesktopIconPosition> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DesktopIconPosition
{
    public string ParsingName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PidlBase64 { get; set; } = string.Empty;
    public string DesktopPath { get; set; } = string.Empty;
    public string MonitorDevicePath { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class TaskbarSettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowOnlyWindowsOnMonitor { get; set; } = true;
    public bool ShowPinnedItems { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool ShowTray { get; set; } = true;
    public bool AutoHide { get; set; }
    public int Height { get; set; } = 40;
}

public sealed class DesktopIconLayoutSettings
{
    public int IconSize { get; set; } = 32;
    public bool AutoArrange { get; set; }
    public bool AlignToGrid { get; set; } = true;
}

public sealed class MouseManagementSettings
{
    public bool MoveToNextMonitorOnMiddleClick { get; set; }
    public bool WrapCursorAtEdges { get; set; }
    public bool PreventStickyCorners { get; set; }
    public bool ScrollInactiveWindows { get; set; }
}

public sealed class MonitorFadingSettings
{
    public bool Enabled { get; set; }
    public double InactiveOpacity { get; set; } = 0.35;
    public bool FadeInactiveMonitors { get; set; } = true;
    public bool IgnoreFullscreenWindows { get; set; } = true;
}

public enum TriggerEvent
{
    ApplicationStarted, DisplayConfigurationChanged, WindowCreated, WindowFocused, WindowDestroyed,
    DesktopLocked, DesktopUnlocked, SessionConnected, SessionDisconnected, PowerResumed, SystemIdle
}

public sealed class TriggerRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public TriggerEvent Event { get; set; }
    public string? MonitorProfileId { get; set; }
    public string ProcessNamePattern { get; set; } = string.Empty;
    public string WindowTitlePattern { get; set; } = string.Empty;
    public int DelayMilliseconds { get; set; } = 0;
    public List<FunctionAction> Actions { get; set; } = new();
    public int Priority { get; set; }
    public int CooldownMilliseconds { get; set; }
    public int DebounceMilliseconds { get; set; }
    public int MaximumExecutionTimeMilliseconds { get; set; } = 120000;
    public bool ContinueOnError { get; set; }
    public bool StopProcessing { get; set; }
}

public sealed class TriggerDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<TriggerRule> Rules { get; set; } = new();
    public List<AutomationRule> AutomationRules { get; set; } = new();
}

public enum FunctionActionType { MoveWindow, ResizeWindow, LoadMonitorProfile, LoadWallpaperProfile, RunProcess, SendKey, SetMonitorPower, SetMonitorFading, ShowNotification }

public sealed class FunctionAction
{
    public FunctionActionType Type { get; set; }
    public string Argument { get; set; } = string.Empty;
    public string? Argument2 { get; set; }
    public string? Argument3 { get; set; }
}

public sealed class GlobalHotkeyDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public List<FunctionAction> Actions { get; set; } = new();
}
