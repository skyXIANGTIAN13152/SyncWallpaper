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
    bool? HdrEnabled,
    string ColorMode,
    int DesktopX,
    int DesktopY,
    bool IsPrimary,
    string ConnectionState,
    string StableId,
    MonitorIdentitySource StableIdSource);

/// <summary>Privacy-safe diagnostic projection; live matching still uses raw local identities.</summary>
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
            monitor.HdrEnabled,
            monitor.ColorMode,
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
        return string.IsNullOrWhiteSpace(home) ? value : value.Replace(home, "<user>", StringComparison.OrdinalIgnoreCase);
    }
}
