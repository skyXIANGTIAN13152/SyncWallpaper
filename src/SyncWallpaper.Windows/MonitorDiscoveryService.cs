using System.Management;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class MonitorDiscoveryService
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint ModeInfoTypeSource = 1;
    private const uint ModeInfoTypeTarget = 2;
    public string LastError { get; private set; } = string.Empty;
    private static readonly SetupApiMonitorProvider SetupApi = new();

    public IReadOnlyList<MonitorIdentity> Discover()
    {
        try
        {
            var result = QueryDisplayConfigMonitors();
            if (result.Count > 0) return MonitorIdentityBuilder.AssignStableIds(result);
        }
        catch (Exception ex) { LastError = ex.Message; /* a transient driver reset is handled by the next event */ }
        return MonitorIdentityBuilder.AssignStableIds(FallbackScreenMonitors());
    }

    private static List<MonitorIdentity> QueryDisplayConfigMonitors()
    {
        var status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (status != 0) Marshal.ThrowExceptionForHR(status);
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (status == ErrorInsufficientBuffer)
        {
            status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            if (status != 0) Marshal.ThrowExceptionForHR(status);
            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        }
        if (status != 0) Marshal.ThrowExceptionForHR(status);

        var wmi = ReadWmiMonitorIds();
        var screens = Screen.AllScreens;
        var result = new List<MonitorIdentity>();
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            var target = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
            var windowsDisplayName = GetSourceName(path.sourceInfo.adapterId, path.sourceInfo.id);
            var sourceMode = FindSourceMode(path.sourceInfo.modeInfoIdx, modes);
            var targetMode = FindTargetMode(path.targetInfo.modeInfoIdx, modes);
            var bounds = FindScreenBounds(screens, windowsDisplayName, sourceMode);
            var wmiInfo = FindWmi(target.MonitorDevicePath, target.MonitorFriendlyDeviceName, wmi);
            var monitorDevicePath = FirstNonEmpty(target.MonitorDevicePath, wmiInfo.InstanceName);
            if (string.IsNullOrWhiteSpace(monitorDevicePath)) continue;
            var manufacturer = FirstNonEmpty(wmiInfo.ManufacturerName, target.EdidManufactureId.ToString("X4"));
            var product = FirstNonEmpty(wmiInfo.ProductCodeId, target.EdidProductCodeId.ToString("X4"));
            var refresh = targetMode.targetVideoSignalInfo.vSyncFreq;
            var nativeWidth = targetMode.targetVideoSignalInfo.activeSize.cx;
            var nativeHeight = targetMode.targetVideoSignalInfo.activeSize.cy;
            var screen = FindScreen(screens, windowsDisplayName);
            var identity = new MonitorIdentity
            {
                WindowsDisplayName = windowsDisplayName,
                MonitorDevicePath = monitorDevicePath,
                ContainerId = FirstNonEmpty(SetupApi.TryGetContainerId(wmiInfo.InstanceName, monitorDevicePath),
                    ReadContainerId(monitorDevicePath, wmiInfo.InstanceName)),
                EdidManufactureId = target.EdidManufactureId.ToString("X4"),
                EdidProductCodeId = target.EdidProductCodeId.ToString("X4"),
                EdidSerialNumber = DecodeChars(wmiInfo.SerialNumberId),
                InstanceName = wmiInfo.InstanceName,
                ManufacturerName = manufacturer,
                ProductCodeId = product,
                FriendlyName = FirstNonEmpty(wmiInfo.UserFriendlyName, target.MonitorFriendlyDeviceName, manufacturer + " " + product),
                AdapterId = LuidString(path.targetInfo.adapterId),
                SourceId = path.sourceInfo.id,
                TargetId = path.targetInfo.id,
                OutputTechnology = unchecked((uint)target.OutputTechnology),
                ConnectorInstance = target.ConnectorInstance,
                IsInternal = IsInternalTechnology(unchecked((uint)target.OutputTechnology)),
                Width = sourceMode.width > 0 ? (int)sourceMode.width : Math.Abs(bounds.Width),
                Height = sourceMode.height > 0 ? (int)sourceMode.height : Math.Abs(bounds.Height),
                NativeWidth = (int)nativeWidth,
                NativeHeight = (int)nativeHeight,
                RefreshRateNumerator = refresh.Numerator,
                RefreshRateDenominator = refresh.Denominator == 0 ? 1u : refresh.Denominator,
                Rotation = targetMode.rotation == 0 ? 1 : (int)targetMode.rotation,
                DesktopX = sourceMode.width > 0 ? sourceMode.position.x : bounds.Left,
                DesktopY = sourceMode.height > 0 ? sourceMode.position.y : bounds.Top,
                IsPrimary = screen?.Primary == true,
                ConnectionState = path.targetInfo.targetAvailable ? "Connected" : "Disconnected"
            };
            result.Add(identity);
        }
        return result;
    }

    private static MonitorBounds FindScreenBounds(Screen[] screens, string sourceName, DISPLAYCONFIG_SOURCE_MODE source)
    {
        var screen = FindScreen(screens, sourceName);
        if (screen is not null) return new MonitorBounds(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
        return new MonitorBounds(source.position.x, source.position.y, (int)source.width, (int)source.height);
    }

    private static Screen? FindScreen(Screen[] screens, string sourceName)
        => screens.FirstOrDefault(screen => !string.IsNullOrWhiteSpace(sourceName)
            && string.Equals(screen.DeviceName, sourceName, StringComparison.OrdinalIgnoreCase));

    private static List<MonitorIdentity> FallbackScreenMonitors()
    {
        return Screen.AllScreens.Select((screen, i) => new MonitorIdentity
        {
            WindowsDisplayName = screen.DeviceName,
            // The fallback is only used when QueryDisplayConfig is unavailable.
            // Keep a non-Windows-number diagnostic path so it cannot be mistaken
            // for a permanent hardware identity by the matcher.
            MonitorDevicePath = $"fallback://geometry/{screen.Bounds.Width}x{screen.Bounds.Height}/{screen.Bounds.Left},{screen.Bounds.Top}/{(screen.Bounds.Width >= screen.Bounds.Height ? 1 : 2)}",
            FriendlyName = screen.DeviceName,
            ManufacturerName = "UNKNOWN",
            ProductCodeId = "UNKNOWN",
            AdapterId = "UNKNOWN",
            SourceId = (uint)i,
            TargetId = (uint)i,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height,
            Rotation = screen.Bounds.Width >= screen.Bounds.Height ? 1 : 2,
            DesktopX = screen.Bounds.Left,
            DesktopY = screen.Bounds.Top,
            IsPrimary = screen.Primary,
            ConnectionState = "Unknown"
        }).ToList();
    }

    private static TargetInfo GetTargetName(LUID adapter, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigDeviceInfoGetTargetName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapter,
                id = targetId
            }
        };
        return DisplayConfigGetDeviceInfo(ref request) == 0 ? new TargetInfo(
            request.monitorDevicePath ?? string.Empty, request.monitorFriendlyDeviceName ?? string.Empty,
            request.outputTechnology, request.edidManufactureId, request.edidProductCodeId, request.connectorInstance) : new TargetInfo();
    }

    private static string GetSourceName(LUID adapter, uint sourceId)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigDeviceInfoGetSourceName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = adapter,
                id = sourceId
            }
        };
        return DisplayConfigGetDeviceInfo(ref request) == 0 ? request.viewGdiDeviceName ?? string.Empty : string.Empty;
    }

    private static DISPLAYCONFIG_SOURCE_MODE FindSourceMode(uint index, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        if (index < modes.Length && modes[index].infoType == ModeInfoTypeSource) return modes[index].sourceMode;
        return new DISPLAYCONFIG_SOURCE_MODE();
    }

    private static DISPLAYCONFIG_TARGET_MODE FindTargetMode(uint index, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        if (index < modes.Length && modes[index].infoType == ModeInfoTypeTarget) return modes[index].targetMode;
        return new DISPLAYCONFIG_TARGET_MODE { rotation = 1 };
    }

    private static WmiInfo FindWmi(string path, string friendly, IReadOnlyList<WmiInfo> all)
    {
        var normalizedPath = NormalizeDevicePath(path);
        return all.FirstOrDefault(x => normalizedPath.Contains(NormalizeDevicePath(x.InstanceName), StringComparison.OrdinalIgnoreCase)
            || (normalizedPath.Length > 0 && NormalizeDevicePath(x.InstanceName).Contains(normalizedPath, StringComparison.OrdinalIgnoreCase))
            || string.Equals(x.UserFriendlyName, friendly, StringComparison.OrdinalIgnoreCase)) ?? new WmiInfo();
    }

    private static string ReadContainerId(string monitorPath, string instanceName)
    {
        try
        {
            using var displayRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (displayRoot is null) return string.Empty;
            var relative = instanceName;
            var marker = relative.IndexOf('\\');
            if (marker >= 0) relative = relative[(marker + 1)..];
            var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                using var device = displayRoot.OpenSubKey(parts[0] + "\\" + parts[1]);
                var direct = device?.GetValue("ContainerID")?.ToString();
                if (!string.IsNullOrWhiteSpace(direct)) return direct;
            }

            foreach (var model in displayRoot.GetSubKeyNames())
            {
                using var modelKey = displayRoot.OpenSubKey(model);
                if (modelKey is null) continue;
                foreach (var instance in modelKey.GetSubKeyNames())
                {
                    using var key = modelKey.OpenSubKey(instance);
                    var value = key?.GetValue("ContainerID")?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && (instanceName.Contains(instance, StringComparison.OrdinalIgnoreCase)
                        || monitorPath.Contains(model, StringComparison.OrdinalIgnoreCase))) return value;
                }
            }
        }
        catch (SecurityException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
        catch (IOException) { return string.Empty; }
        return string.Empty;
    }

    private static IReadOnlyList<WmiInfo> ReadWmiMonitorIds()
    {
        var list = new List<WmiInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT InstanceName, SerialNumberID, ManufacturerName, ProductCodeID, UserFriendlyName FROM WmiMonitorID");
            foreach (ManagementObject item in searcher.Get())
            {
                list.Add(new WmiInfo
                {
                    InstanceName = item["InstanceName"]?.ToString() ?? string.Empty,
                    SerialNumberId = ToUshortArray(item["SerialNumberID"]),
                    ManufacturerName = DecodeChars(ToUshortArray(item["ManufacturerName"])),
                    ProductCodeId = DecodeChars(ToUshortArray(item["ProductCodeID"])),
                    UserFriendlyName = DecodeChars(ToUshortArray(item["UserFriendlyName"]))
                });
            }
        }
        catch (ManagementException) { return list; }
        catch (UnauthorizedAccessException) { return list; }
        catch (COMException) { return list; }
        return list;
    }

    private static ushort[] ToUshortArray(object? value)
    {
        if (value is ushort[] us) return us;
        if (value is uint[] ui) return ui.Select(x => (ushort)x).ToArray();
        if (value is Array array) return array.Cast<object>().Select(x => Convert.ToUInt16(x)).ToArray();
        return Array.Empty<ushort>();
    }

    private static string DecodeChars(ushort[] chars)
    {
        if (chars.Length == 0) return string.Empty;
        var builder = new StringBuilder(chars.Length);
        foreach (var c in chars) { if (c == 0) break; builder.Append((char)c); }
        return builder.ToString().Trim();
    }

    private static string NormalizeDevicePath(string value) => value.Replace("#", "\\", StringComparison.Ordinal).TrimEnd('\\').ToUpperInvariant();
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static string LuidString(LUID luid) => $"{luid.HighPart:X8}:{luid.LowPart:X8}";
    private static bool IsInternalTechnology(uint tech) => tech == 0x80000000 || tech == 6 || tech == 13;

    private readonly record struct TargetInfo(string MonitorDevicePath = "", string MonitorFriendlyDeviceName = "", int OutputTechnology = 0, ushort EdidManufactureId = 0, ushort EdidProductCodeId = 0, uint ConnectorInstance = 0);
    private readonly record struct MonitorBounds(int Left, int Top, int Width, int Height);
    private sealed class WmiInfo
    {
        public string InstanceName { get; init; } = string.Empty;
        public ushort[] SerialNumberId { get; init; } = Array.Empty<ushort>();
        public string ManufacturerName { get; init; } = string.Empty;
        public string ProductCodeId { get; init; } = string.Empty;
        public string UserFriendlyName { get; init; } = string.Empty;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint modeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate; public DISPLAYCONFIG_RATIONAL hSyncFreq; public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize; public DISPLAYCONFIG_2DREGION totalSize; public uint videoStandard; public uint scanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo; public uint rotation; public uint scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; public uint rotation; public uint scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_TARGET_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering; public bool targetAvailable; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Explicit)] private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
}
