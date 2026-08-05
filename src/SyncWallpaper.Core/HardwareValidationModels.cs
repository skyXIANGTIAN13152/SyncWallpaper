using System.Security.Cryptography;
using System.Text;

namespace SyncWallpaper.Core;

public sealed record SanitizedMonitorDiagnostic(
    string FriendlyName,
    string Manufacturer,
    string ProductCode,
    string EdidManufactureId,
    string EdidProductCodeId,
    string Serial,
    string ContainerId,
    string MonitorDevicePath,
    string InstanceName,
    string AdapterId,
    uint SourceId,
    uint TargetId,
    uint OutputTechnology,
    uint ConnectorInstance,
    bool IsInternal,
    int Width,
    int Height,
    int NativeWidth,
    int NativeHeight,
    uint RefreshRateNumerator,
    uint RefreshRateDenominator,
    int Rotation,
    int Dpi,
    double DpiScale,
    int DesktopX,
    int DesktopY,
    bool IsPrimary,
    string ConnectionState,
    string StableId,
    MonitorIdentitySource StableIdSource);

/// <summary>Privacy-safe diagnostics: raw serials, container IDs and paths stay local.</summary>
public static class MonitorIdentitySanitizer
{
    public static SanitizedMonitorDiagnostic Sanitize(MonitorIdentity monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new(
            monitor.DisplayLabel,
            monitor.ManufacturerName,
            monitor.ProductCodeId,
            monitor.EdidManufactureId,
            monitor.EdidProductCodeId,
            Redact(monitor.EdidSerialNumber),
            Redact(monitor.ContainerId),
            RedactPath(monitor.MonitorDevicePath),
            Redact(monitor.InstanceName),
            Redact(monitor.AdapterId),
            monitor.SourceId,
            monitor.TargetId,
            monitor.OutputTechnology,
            monitor.ConnectorInstance,
            monitor.IsInternal,
            monitor.Width,
            monitor.Height,
            monitor.NativeWidth,
            monitor.NativeHeight,
            monitor.RefreshRateNumerator,
            monitor.RefreshRateDenominator,
            monitor.Rotation,
            monitor.Dpi,
            monitor.DpiScale,
            monitor.DesktopX,
            monitor.DesktopY,
            monitor.IsPrimary,
            monitor.ConnectionState,
            Redact(monitor.StableId),
            monitor.StableIdSource);
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return "sha256:" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    public static string RedactPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim();
        var prefix = normalized.StartsWith(@"\\?\", StringComparison.Ordinal) ? "device" : "path";
        return prefix + ":sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();
    }

    public static string RedactUserPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        if (!string.IsNullOrWhiteSpace(home)) value = value.Replace(home, "<user>", StringComparison.OrdinalIgnoreCase);
        return value;
    }
}

public sealed record MonitorSnapshotDifference(string Key, string Change, string Field, string Before, string After);

public static class DisplaySnapshotComparer
{
    public static IReadOnlyList<MonitorSnapshotDifference> Compare(DisplaySnapshot before, DisplaySnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var differences = new List<MonitorSnapshotDifference>();
        var oldByKey = before.Monitors.Select((monitor, index) => (monitor, index))
            .ToDictionary(x => Key(x.monitor, x.index), x => x.monitor, StringComparer.OrdinalIgnoreCase);
        var newByKey = after.Monitors.Select((monitor, index) => (monitor, index))
            .ToDictionary(x => Key(x.monitor, x.index), x => x.monitor, StringComparer.OrdinalIgnoreCase);
        foreach (var key in oldByKey.Keys.Except(newByKey.Keys, StringComparer.OrdinalIgnoreCase))
            differences.Add(new(key, "Removed", "monitor", Describe(oldByKey[key]), string.Empty));
        foreach (var key in newByKey.Keys.Except(oldByKey.Keys, StringComparer.OrdinalIgnoreCase))
            differences.Add(new(key, "Added", "monitor", string.Empty, Describe(newByKey[key])));
        foreach (var key in oldByKey.Keys.Intersect(newByKey.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var left = oldByKey[key];
            var right = newByKey[key];
            Compare(differences, key, "resolution", left.Width + "x" + left.Height, right.Width + "x" + right.Height);
            Compare(differences, key, "nativeResolution", left.NativeWidth + "x" + left.NativeHeight, right.NativeWidth + "x" + right.NativeHeight);
            Compare(differences, key, "rotation", left.Rotation.ToString(), right.Rotation.ToString());
            Compare(differences, key, "refreshRate", left.RefreshRateNumerator + "/" + left.RefreshRateDenominator, right.RefreshRateNumerator + "/" + right.RefreshRateDenominator);
            Compare(differences, key, "desktopPosition", left.DesktopX + "," + left.DesktopY, right.DesktopX + "," + right.DesktopY);
            Compare(differences, key, "connectionState", left.ConnectionState, right.ConnectionState);
            Compare(differences, key, "stableIdSource", left.StableIdSource.ToString(), right.StableIdSource.ToString());
        }
        return differences;
    }

    private static void Compare(List<MonitorSnapshotDifference> result, string key, string field, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) result.Add(new(key, "Changed", field, before, after));
    }

    private static string Key(MonitorIdentity monitor, int index)
        => !string.IsNullOrWhiteSpace(monitor.StableId) ? "stable:" + monitor.StableId
            : !string.IsNullOrWhiteSpace(monitor.MonitorDevicePath) ? "path:" + monitor.MonitorDevicePath
            : "position:" + index;

    private static string Describe(MonitorIdentity monitor)
        => monitor.DisplayLabel + " " + monitor.Width + "x" + monitor.Height + " @ " + monitor.DesktopX + "," + monitor.DesktopY;
}

public enum HardwareValidationStatus { Passed, Failed, Blocked, NotRun, EnvironmentUnavailable }

public sealed record HardwareValidationStep(
    int Number,
    string Name,
    HardwareValidationStatus Status,
    string Message,
    DateTime CompletedAtUtc,
    bool MutatedSystem);

public sealed class HardwareValidationReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string ToolVersion { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = Environment.OSVersion.VersionString;
    public List<HardwareValidationStep> Steps { get; init; } = new();
    public List<SanitizedMonitorDiagnostic> InitialDisplays { get; init; } = new();
    public List<SanitizedMonitorDiagnostic> FinalDisplays { get; init; } = new();
    public bool SystemMutationConfirmed { get; set; }
    public bool IsComplete => CompletedAtUtc.HasValue && Steps.Count > 0 && Steps.All(x => x.Status is HardwareValidationStatus.Passed or HardwareValidationStatus.NotRun or HardwareValidationStatus.EnvironmentUnavailable);
}
