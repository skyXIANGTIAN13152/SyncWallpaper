using System.Text.Json.Serialization;

namespace SyncWallpaper.Core;

public enum MatchStatus { Exact, Compatible, Ambiguous, NoMatch }
public enum WallpaperFitMode { Fill, Fit, Stretch, Center, Tile, Span }
public enum DisplayCombinationKind { LaptopOnly, ThreeMonitorSetup, Custom }
public enum MonitorIdentitySource { Unknown, EdidSerial, ContainerId, MonitorDevicePath, InstanceName, HardwareTopology, Geometry, Ambiguous }

public sealed class MonitorIdentity
{
    public int SchemaVersion { get; set; } = 4;
    /// <summary>Temporary Windows name (for example \\.\DISPLAY1). Never use this as a permanent key.</summary>
    public string WindowsDisplayName { get; set; } = string.Empty;
    public string MonitorDevicePath { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string EdidManufactureId { get; set; } = string.Empty;
    public string EdidProductCodeId { get; set; } = string.Empty;
    public string EdidSerialNumber { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string ProductCodeId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string AdapterId { get; set; } = string.Empty;
    public uint SourceId { get; set; }
    public uint TargetId { get; set; }
    public uint OutputTechnology { get; set; }
    public uint ConnectorInstance { get; set; }
    public bool IsInternal { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }
    public uint RefreshRateNumerator { get; set; }
    public uint RefreshRateDenominator { get; set; } = 1;
    public int Rotation { get; set; }
    public int Dpi { get; set; } = 96;
    public double DpiScale { get; set; } = 1.0;
    public bool? HdrEnabled { get; set; }
    public string ColorMode { get; set; } = string.Empty;
    public int DesktopX { get; set; }
    public int DesktopY { get; set; }
    public bool IsPrimary { get; set; }
    public string ConnectionState { get; set; } = "Connected";
    /// <summary>Explainable stable identity, intentionally not a hash-only value.</summary>
    public string StableId { get; set; } = string.Empty;
    public MonitorIdentitySource StableIdSource { get; set; } = MonitorIdentitySource.Unknown;

    [JsonIgnore] public string DisplayLabel => string.IsNullOrWhiteSpace(FriendlyName)
        ? $"{ManufacturerName} {ProductCodeId}".Trim()
        : FriendlyName;

    [JsonIgnore] public bool HasUsableSerial => !string.IsNullOrWhiteSpace(EdidSerialNumber)
        && EdidSerialNumber.Trim('0', ' ', '\0').Length > 0
        && !string.Equals(EdidSerialNumber, "0", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(EdidSerialNumber, "unknown", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public string SerialKey => $"{Normalize(ManufacturerName, EdidManufactureId)}|{Normalize(ProductCodeId, EdidProductCodeId)}|{Normalize(EdidSerialNumber)}";
    [JsonIgnore] public string ModelKey => $"{Normalize(ManufacturerName, EdidManufactureId)}|{Normalize(ProductCodeId, EdidProductCodeId)}";
    [JsonIgnore] public string HardwareKey => $"{AdapterId}|{TargetId}|{OutputTechnology}|{ConnectorInstance}";
    [JsonIgnore] public bool IsConnected => !string.Equals(ConnectionState, "Disconnected", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ConnectionState, "Unknown", StringComparison.OrdinalIgnoreCase);

    public MonitorIdentity Clone() => (MonitorIdentity)MemberwiseClone();

    public static string Normalize(params string[] values)
    {
        return string.Join("|", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToUpperInvariant()));
    }
}

public sealed class MonitorRoleBinding
{
    public int SchemaVersion { get; set; } = 4;
    public string RoleId { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "Laptop";
    public string DisplayName { get; set; } = "笔记本本体";
    public MonitorIdentity Fingerprint { get; set; } = new();
    public string WallpaperAssetId { get; set; } = string.Empty;
    public string WallpaperPath { get; set; } = string.Empty;
    public WallpaperFitMode FitMode { get; set; } = WallpaperFitMode.Fill;
    public string BackgroundColor { get; set; } = "#050B18";
    public bool AllowAutoRebind { get; set; }
    public DateTime? LastSuccessfulMatchAt { get; set; }
    public string LastKnownMonitorDevicePath { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class WallpaperProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int SchemaVersion { get; set; } = 4;
    public string Name { get; set; } = string.Empty;
    public DisplayCombinationKind Combination { get; set; } = DisplayCombinationKind.Custom;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public bool Enabled { get; set; } = true;
    public bool AllowCompatibleMatch { get; set; } = true;
    public bool AutoApply { get; set; } = true;
    public int ExpectedMonitorCount { get; set; }
    public int MinimumConfidence { get; set; } = 80;
    public int Priority { get; set; }
    public List<MonitorRoleBinding> Roles { get; set; } = new();
    public DateTime? LastAppliedAt { get; set; }
    public DateTime? LastSuccessfulMatchAt { get; set; }
}

public sealed class WallpaperAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ManagedRelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string Format { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string StorageMode { get; set; } = "Managed";
    public string? ExternalPath { get; set; }
    public bool IsMissing { get; set; }
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public bool AutoMatchEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool LowPerformanceMode { get; set; } = true;
    public bool SafeMode { get; set; }
    public string SafeModeReason { get; set; } = string.Empty;
    public int ConsecutiveStartupFailures { get; set; }
    /// <summary>The profile last chosen for editing; applying a profile never changes this value.</summary>
    public string? EditingProfileId { get; set; }
    /// <summary>The profile most recently matched and applied successfully.</summary>
    public string? LastMatchedProfileId { get; set; }
    /// <summary>Legacy v1 field, consumed once during migration and no longer written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveProfileId { get; set; }
    public string? DataRoot { get; set; }
    /// <summary>GitHub Release checks are opt-in and never download or install files.</summary>
    public bool AutomaticUpdateCheckEnabled { get; set; } = false;
    public string UpdateChannel { get; set; } = "Stable";
    public DateTimeOffset? LastUpdateSuccessfulCheckUtc { get; set; }
    public DateTimeOffset? LastUpdateAttemptUtc { get; set; }
    /// <summary>Default is intentionally lightweight: only the core host and wallpaper matcher run.</summary>
    public ModuleConfiguration Modules { get; set; } = new();
}

public sealed class LibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<WallpaperAsset> Assets { get; set; } = new();
}

public sealed class ProfilesDocument
{
    public int SchemaVersion { get; set; } = 4;
    public List<WallpaperProfile> Profiles { get; set; } = new();
}

public sealed class MatchEvidence
{
    public string Role { get; init; } = string.Empty;
    public string Monitor { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DisplayIdentityMatchStatus IdentityStatus { get; init; } = DisplayIdentityMatchStatus.Unknown;
    public IReadOnlyList<string> ConflictingFields { get; init; } = Array.Empty<string>();
}

public sealed class MatchResult
{
    public MatchStatus Status { get; init; }
    public WallpaperProfile? Profile { get; init; }
    /// <summary>
    /// Runtime monitor assignments keyed by the binding's stable RoleId.
    /// Logical role names are not unique: a profile may legitimately contain
    /// two Landscape displays, so using Role as the key would drop one screen.
    /// </summary>
    public Dictionary<string, MonitorIdentity> RoleMatches { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MatchEvidence> Evidence { get; init; } = new();
    public int Score { get; init; }
    public int RunnerUpScore { get; init; }
    /// <summary>Normalized from the weakest role; independent of monitor count.</summary>
    public int Confidence { get; init; }
    public string Message { get; init; } = string.Empty;
    public DisplayIdentityMatchStatus IdentityStatus { get; init; } = DisplayIdentityMatchStatus.Unknown;
    public bool CanAutoApply { get; init; }
    public IReadOnlyList<string> ConflictingFields { get; init; } = Array.Empty<string>();

    public bool TryGetMonitor(MonitorRoleBinding binding, out MonitorIdentity monitor)
    {
        if (!string.IsNullOrWhiteSpace(binding.RoleId)
            && RoleMatches.TryGetValue(binding.RoleId, out monitor!))
            return true;

        // Compatibility for MatchResult instances created before RoleId
        // became the runtime assignment key.
        return RoleMatches.TryGetValue(binding.Role, out monitor!);
    }
}

public sealed class DisplaySnapshot
{
    public List<MonitorIdentity> Monitors { get; init; } = new();
    public string Signature => string.Join(";", Monitors.OrderBy(m => m.StableId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
        .Select(m => $"{m.StableId}|{m.StableIdSource}|{m.WindowsDisplayName}|{m.MonitorDevicePath}|{m.Width}x{m.Height}|{m.NativeWidth}x{m.NativeHeight}|{m.RefreshRateNumerator}/{m.RefreshRateDenominator}|{m.Rotation}|{m.DesktopX},{m.DesktopY}|{m.ConnectionState}"));
}

public sealed record DiagnosticEvent(DateTime Timestamp, string Type, string Message, string? Profile = null, int? MonitorCount = null, int? Confidence = null);
