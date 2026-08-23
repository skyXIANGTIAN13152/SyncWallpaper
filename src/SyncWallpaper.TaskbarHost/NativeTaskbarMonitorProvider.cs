using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

/// <summary>
/// Lightweight CCD reader for the optional taskbar process. It intentionally
/// avoids WMI, SetupAPI and persistent hardware matching because taskbar bars
/// only need the current runtime topology.
/// </summary>
public sealed class NativeTaskbarMonitorProvider
{
    public IReadOnlyList<MonitorIdentity> Discover()
    {
        var status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (status != 0) Marshal.ThrowExceptionForHR(status);
        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        if (status == ErrorInsufficientBuffer)
        {
            status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            if (status != 0) Marshal.ThrowExceptionForHR(status);
            paths = new DisplayConfigPathInfo[pathCount];
            modes = new DisplayConfigModeInfo[modeCount];
            status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        }
        if (status != 0) Marshal.ThrowExceptionForHR(status);

        var result = new List<MonitorIdentity>();
        var primaryAssigned = false;
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            if (path.sourceInfo.modeInfoIdx >= modes.Length) continue;
            var source = modes[path.sourceInfo.modeInfoIdx].sourceMode;
            if (source.width == 0 || source.height == 0) continue;
            var target = ReadTarget(path.targetInfo.adapterId, path.targetInfo.id);
            var sourceName = ReadSourceName(path.sourceInfo.adapterId, path.sourceInfo.id);
            var rotation = path.targetInfo.rotation == 0 ? 1 : (int)path.targetInfo.rotation;
            var quarterTurn = rotation is 2 or 4;
            var width = quarterTurn ? checked((int)source.height) : checked((int)source.width);
            var height = quarterTurn ? checked((int)source.width) : checked((int)source.height);
            var atOrigin = source.position.x == 0 && source.position.y == 0;
            var isPrimary = atOrigin && !primaryAssigned;
            primaryAssigned |= isPrimary;
            var devicePath = string.IsNullOrWhiteSpace(target.monitorDevicePath)
                ? $"runtime://{sourceName}/{path.targetInfo.id}"
                : target.monitorDevicePath;
            result.Add(new MonitorIdentity
            {
                WindowsDisplayName = sourceName,
                MonitorDevicePath = devicePath,
                FriendlyName = string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName) ? sourceName : target.monitorFriendlyDeviceName,
                EdidManufactureId = target.edidManufactureId.ToString("X4"),
                EdidProductCodeId = target.edidProductCodeId.ToString("X4"),
                ManufacturerName = target.edidManufactureId.ToString("X4"),
                ProductCodeId = target.edidProductCodeId.ToString("X4"),
                AdapterId = $"{path.sourceInfo.adapterId.HighPart:X8}:{path.sourceInfo.adapterId.LowPart:X8}",
                SourceId = path.sourceInfo.id,
                TargetId = path.targetInfo.id,
                OutputTechnology = path.targetInfo.outputTechnology,
                ConnectorInstance = target.connectorInstance,
                Width = width,
                Height = height,
                NativeWidth = width,
                NativeHeight = height,
                Rotation = rotation,
                DesktopX = source.position.x,
                DesktopY = source.position.y,
                IsPrimary = isPrimary,
                Dpi = 96,
                DpiScale = 1,
                StableId = $"runtime:{sourceName}|{path.targetInfo.id}|{devicePath}",
                StableIdSource = MonitorIdentitySource.HardwareTopology,
                ConnectionState = "Connected"
            });
        }
        if (result.Count > 0 && !result.Any(x => x.IsPrimary)) result[0].IsPrimary = true;
        return result;
    }

    private static DisplayConfigTargetDeviceName ReadTarget(Luid adapter, uint targetId)
    {
        var value = new DisplayConfigTargetDeviceName
        {
            header = new DisplayConfigDeviceInfoHeader
            {
                type = DisplayConfigDeviceInfoGetTargetName,
                size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                adapterId = adapter,
                id = targetId
            }
        };
        return DisplayConfigGetDeviceInfo(ref value) == 0 ? value : default;
    }

    private static string ReadSourceName(Luid adapter, uint sourceId)
    {
        var value = new DisplayConfigSourceDeviceName
        {
            header = new DisplayConfigDeviceInfoHeader
            {
                type = DisplayConfigDeviceInfoGetSourceName,
                size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                adapterId = adapter,
                id = sourceId
            }
        };
        return DisplayConfigGetDeviceInfo(ref value) == 0 ? value.viewGdiDeviceName ?? string.Empty : string.Empty;
    }

    private const uint QdcOnlyActivePaths = 0x00000002;
    private const int ErrorInsufficientBuffer = 122;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll", SetLastError = true)] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, nint topologyId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct PointL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct Region2D { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoSignalInfo
    {
        public ulong pixelRate; public Rational hSyncFreq; public Rational vSyncFreq;
        public Region2D activeSize; public Region2D totalSize; public uint videoStandard; public uint scanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigTargetMode { public VideoSignalInfo targetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigSourceMode { public uint width; public uint height; public uint pixelFormat; public PointL position; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathSourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathTargetInfo
    {
        public Luid adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
        public Rational refreshRate; public uint scanLineOrdering; [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathInfo { public DisplayConfigPathSourceInfo sourceInfo; public DisplayConfigPathTargetInfo targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Explicit)] private struct DisplayConfigModeInfo
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public Luid adapterId;
        [FieldOffset(16)] public DisplayConfigTargetMode targetMode;
        [FieldOffset(16)] public DisplayConfigSourceMode sourceMode;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigDeviceInfoHeader { public uint type; public uint size; public Luid adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
}
