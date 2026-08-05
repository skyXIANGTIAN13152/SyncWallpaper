using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

public sealed class DiagnosticLaboratorySnapshot
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public string WindowsVersion { get; init; } = string.Empty;
    public string SoftwareVersion { get; init; } = string.Empty;
    public IReadOnlyList<MonitorIdentity> Displays { get; init; } = Array.Empty<MonitorIdentity>();
    public IReadOnlyList<AudioEndpointReference> AudioDevices { get; init; } = Array.Empty<AudioEndpointReference>();
    public IReadOnlyDictionary<AudioEndpointRole, AudioEndpointReference?> AudioDefaults { get; init; } = new Dictionary<AudioEndpointRole, AudioEndpointReference?>();
    public int WindowCount { get; init; }
    public int ElevatedWindowCount { get; init; }
    public string WindowListenerStatus { get; init; } = string.Empty;
    public string ExplorerStatus { get; init; } = string.Empty;
    public string ComInitializationStatus { get; init; } = string.Empty;
    public string LastSystemEvent { get; init; } = string.Empty;
    public string LastTransaction { get; init; } = string.Empty;
    public string LastRollback { get; init; } = string.Empty;
    public int DesktopShellItemCount { get; init; }
    public IReadOnlyList<ModuleStatusSnapshot> Modules { get; init; } = Array.Empty<ModuleStatusSnapshot>();
    public IReadOnlyList<ModulePerformanceRecord> PerformanceHistory { get; init; } = Array.Empty<ModulePerformanceRecord>();
    public WindowsResourceSnapshot Resources { get; init; } = new(DateTime.Now, 0, 0, 0, 0, 0, 0, 0);
}
