using System.ComponentModel;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;
using SyncWallpaper.DisplayEngine;

namespace SyncWallpaper.Windows;

/// <summary>
/// Windows-only adapter for CCD. All P/Invoke calls stay in this adapter; the transaction
/// service depends only on the interfaces in SyncWallpaper.Core.
/// </summary>
public sealed class WindowsDisplayConfigurationAdapter :
    IDisplayTopologyReader,
    IDisplayModeCatalog,
    IStagedDisplayConfigurationApplier,
    IDisplayConfigurationVerifier,
    IDisplayConfigurationRollbackService
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcNoOptimization = 0x00000100;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    private const uint ModeInfoTypeSource = 1;
    private const uint ModeInfoTypeTarget = 2;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const int ErrorInsufficientBuffer = 122;
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const int DmPelsWidth = 0x80000;
    private const int DmPelsHeight = 0x100000;
    private const int DmPosition = 0x20;
    private const int DmDisplayOrientation = 0x80;
    private const int DmDisplayFrequency = 0x400000;

    private readonly MonitorDiscoveryService _discovery;
    private readonly object _gate = new();

    public WindowsDisplayConfigurationAdapter(MonitorDiscoveryService discovery) => _discovery = discovery;

    public DisplayTopologySnapshot Capture()
    {
        var monitors = _discovery.Discover().ToList();
        var raw = QueryRawState();
        var entries = new List<DisplayConfigurationEntry>();
        foreach (var monitor in monitors)
        {
            var rawPath = raw.Paths.FirstOrDefault(x => LuidString(x.targetInfo.adapterId).Equals(monitor.AdapterId, StringComparison.OrdinalIgnoreCase)
                && x.sourceInfo.id == monitor.SourceId && x.targetInfo.id == monitor.TargetId);
            var targetMode = rawPath.Equals(default(DISPLAYCONFIG_PATH_INFO)) ? default : FindTargetMode(rawPath.targetInfo.modeInfoIdx, raw.Modes);
            var refresh = rawPath.Equals(default(DISPLAYCONFIG_PATH_INFO)) ? new DISPLAYCONFIG_RATIONAL { Numerator = 60, Denominator = 1 } : rawPath.targetInfo.refreshRate;
            if (refresh.Numerator == 0) refresh = new DISPLAYCONFIG_RATIONAL { Numerator = 60, Denominator = 1 };
            entries.Add(new DisplayConfigurationEntry
            {
                MonitorFingerprint = monitor.Clone(),
                AdapterLuid = monitor.AdapterId,
                SourceId = monitor.SourceId,
                TargetId = monitor.TargetId,
                Enabled = true,
                IsPrimary = monitor.IsPrimary,
                DesktopX = monitor.DesktopX,
                DesktopY = monitor.DesktopY,
                Width = monitor.Width,
                Height = monitor.Height,
                RefreshRateNumerator = refresh.Numerator,
                RefreshRateDenominator = refresh.Denominator,
                Rotation = rawPath.Equals(default(DISPLAYCONFIG_PATH_INFO))
                    ? monitor.Rotation
                    : (rawPath.targetInfo.rotation == 0 ? 1 : (int)rawPath.targetInfo.rotation),
                DpiScale = 1.0,
                HdrEnabled = null,
                ColorMode = rawPath.Equals(default(DISPLAYCONFIG_PATH_INFO)) ? string.Empty : rawPath.targetInfo.scaling.ToString()
            });
        }
        var profile = new DisplayConfigurationProfile { Name = "当前 Windows 显示配置", Displays = entries };
        return new DisplayTopologySnapshot { Profile = profile, NativeSignature = Signature(profile), NativeState = raw };
    }

    public IReadOnlyList<DisplayModeInfo> GetModes(MonitorIdentity monitor)
    {
        var deviceName = ScreenDeviceName(monitor);
        if (string.IsNullOrWhiteSpace(deviceName)) return Array.Empty<DisplayModeInfo>();
        var modes = new List<DisplayModeInfo>();
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (var index = 0; EnumDisplaySettingsEx(deviceName, index, ref mode, 0); index++)
        {
            var denominator = 1u;
            var numerator = (uint)Math.Max(1, mode.dmDisplayFrequency);
            modes.Add(new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, numerator, denominator, mode.dmDisplayOrientation + 1));
            mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        }
        return modes.Distinct().ToArray();
    }

    public Task ApplyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
        => ApplyFinalAsync(target, cancellationToken);

    public Task ApplyTopologyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
        => ApplyInternalAsync(target, cancellationToken, applyFinalFields: false);

    public Task ApplyFinalAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
        => ApplyInternalAsync(target, cancellationToken, applyFinalFields: true);

    private async Task ApplyInternalAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken, bool applyFinalFields)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = Capture();
        if (Equivalent(target, captured.Profile) && captured.NativeState is RawDisplayState validationState)
        {
            var validationResult = SetDisplayConfig((uint)validationState.Paths.Length, validationState.Paths,
                (uint)validationState.Modes.Length, validationState.Modes, SdcValidate | SdcUseSuppliedDisplayConfig);
            if (validationResult != 0) throw new Win32Exception(validationResult, $"SetDisplayConfig 预验证失败（0x{validationResult:X8}）。");
            await Task.CompletedTask;
            return;
        }
        var raw = QueryRawState();
        var pathByKey = raw.Paths.Select((path, index) => (path, index)).ToDictionary(x => PathKey(x.path), x => x.index);
        var currentPaths = raw.Paths.ToArray();
        var currentModes = raw.Modes.ToArray();

        foreach (var entry in target.Displays)
        {
            var key = $"{entry.AdapterLuid}|{entry.SourceId}|{entry.TargetId}";
            var index = pathByKey.TryGetValue(key, out var indexed)
                ? indexed
                : Array.FindIndex(currentPaths, path => TargetDevicePath(path).Equals(entry.MonitorFingerprint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                if (entry.Enabled) throw new InvalidOperationException($"显示器在应用过程中消失：{entry.MonitorFingerprint.DisplayLabel}");
                continue;
            }

            var path = currentPaths[index];
            path.flags = entry.Enabled ? path.flags | DisplayConfigPathActive : path.flags & ~DisplayConfigPathActive;
            if (entry.Enabled)
            {
                path.targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = entry.RefreshRateNumerator, Denominator = entry.RefreshRateDenominator };
                if (applyFinalFields)
                    path.targetInfo.rotation = (uint)Math.Clamp(entry.Rotation, 1, 4);
                var sourceIndex = (int)path.sourceInfo.modeInfoIdx;
                if (sourceIndex >= 0 && sourceIndex < currentModes.Length && currentModes[sourceIndex].infoType == ModeInfoTypeSource)
                {
                    var source = currentModes[sourceIndex].sourceMode;
                    source.width = (uint)entry.Width;
                    source.height = (uint)entry.Height;
                    if (entry.Rotation is 2 or 4)
                    {
                        source.width = (uint)entry.Height;
                        source.height = (uint)entry.Width;
                    }
                    if (applyFinalFields)
                        source.position = new POINTL { x = entry.DesktopX, y = entry.DesktopY };
                    currentModes[sourceIndex].sourceMode = source;
                }
                var targetIndex = (int)path.targetInfo.modeInfoIdx;
                if (targetIndex >= 0 && targetIndex < currentModes.Length && currentModes[targetIndex].infoType == ModeInfoTypeTarget)
                {
                    var mode = currentModes[targetIndex].targetMode;
                    mode.targetVideoSignalInfo.vSyncFreq = path.targetInfo.refreshRate;
                    currentModes[targetIndex].targetMode = mode;
                }
            }
            currentPaths[index] = path;
        }

        var flags = SdcApply | SdcUseSuppliedDisplayConfig | SdcNoOptimization;
        if (applyFinalFields) flags |= SdcSaveToDatabase;
        var result = SetDisplayConfig((uint)currentPaths.Length, currentPaths, (uint)currentModes.Length, currentModes, flags);
        if (result != 0) throw new Win32Exception(result, "SetDisplayConfig 应用失败。");
        await Task.CompletedTask;
    }

    public async Task<DisplayValidationResult> VerifyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var actual = Capture().Profile;
        var errors = new List<string>();
        foreach (var expected in target.Displays.Where(x => x.Enabled))
        {
            var actualEntry = actual.Displays.FirstOrDefault(x => SameDisplay(expected, x));
            if (actualEntry is null)
            {
                errors.Add($"未找到已启用显示器：{expected.MonitorFingerprint.DisplayLabel}");
                continue;
            }
            if (actualEntry.Width != expected.Width || actualEntry.Height != expected.Height)
                errors.Add($"{expected.MonitorFingerprint.DisplayLabel} 分辨率实际为 {actualEntry.Width}×{actualEntry.Height}。");
            if (actualEntry.DesktopX != expected.DesktopX || actualEntry.DesktopY != expected.DesktopY)
                errors.Add($"{expected.MonitorFingerprint.DisplayLabel} 桌面位置实际为 {actualEntry.DesktopX},{actualEntry.DesktopY}。");
            if (actualEntry.Rotation != expected.Rotation)
                errors.Add($"{expected.MonitorFingerprint.DisplayLabel} 旋转实际为 {actualEntry.Rotation}。");
        }
        var warnings = target.Displays.Where(x => x.HdrEnabled is not null || Math.Abs(x.DpiScale - 1.0) > 0.01)
            .Select(x => $"{x.MonitorFingerprint.DisplayLabel} 的 HDR/DPI 需要系统会话级验证。").ToArray();
        return errors.Count == 0
            ? DisplayValidationResult.Valid(warnings)
            : new DisplayValidationResult { IsValid = false, Errors = errors, Warnings = warnings };
    }

    public async Task RollbackAsync(DisplayTopologySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.NativeState is RawDisplayState raw)
        {
            var result = SetDisplayConfig((uint)raw.Paths.Length, raw.Paths, (uint)raw.Modes.Length, raw.Modes,
                SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase | SdcNoOptimization);
            if (result != 0) throw new Win32Exception(result, "SetDisplayConfig 回滚失败。");
        }
        else
        {
            await ApplyAsync(snapshot.Profile, cancellationToken);
        }
    }

    private RawDisplayState QueryRawState()
    {
        lock (_gate)
        {
            var status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (status != 0) throw new Win32Exception(status, "GetDisplayConfigBufferSizes 失败。");
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == ErrorInsufficientBuffer)
            {
                status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
                if (status != 0) throw new Win32Exception(status);
                paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            }
            if (status != 0) throw new Win32Exception(status, "QueryDisplayConfig 失败。");
            return new RawDisplayState(paths.Take((int)pathCount).ToArray(), modes.Take((int)modeCount).ToArray());
        }
    }

    private string? ScreenDeviceName(MonitorIdentity monitor)
    {
        return System.Windows.Forms.Screen.AllScreens
            .FirstOrDefault(x => x.Bounds.Left == monitor.DesktopX && x.Bounds.Top == monitor.DesktopY &&
                x.Bounds.Width == monitor.Width && x.Bounds.Height == monitor.Height)?.DeviceName;
    }

    private static bool SameDisplay(DisplayConfigurationEntry expected, DisplayConfigurationEntry actual)
    {
        if (!string.IsNullOrWhiteSpace(expected.MonitorFingerprint.MonitorDevicePath))
            return expected.MonitorFingerprint.MonitorDevicePath.Equals(actual.MonitorFingerprint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase);
        return expected.AdapterLuid.Equals(actual.AdapterLuid, StringComparison.OrdinalIgnoreCase) &&
            expected.SourceId == actual.SourceId && expected.TargetId == actual.TargetId;
    }

    private static bool Equivalent(DisplayConfigurationProfile left, DisplayConfigurationProfile right)
    {
        if (left.Displays.Count != right.Displays.Count) return false;
        return left.Displays.All(expected =>
        {
            var actual = right.Displays.FirstOrDefault(x => SameDisplay(expected, x));
            return actual is not null && actual.Width == expected.Width && actual.Height == expected.Height &&
                actual.DesktopX == expected.DesktopX && actual.DesktopY == expected.DesktopY &&
                actual.Rotation == expected.Rotation &&
                actual.RefreshRateNumerator * expected.RefreshRateDenominator == expected.RefreshRateNumerator * actual.RefreshRateDenominator;
        });
    }

    private static string Signature(DisplayConfigurationProfile profile) => string.Join(";", profile.Displays.OrderBy(x => x.MonitorFingerprint.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
        .Select(x => $"{x.MonitorFingerprint.MonitorDevicePath}|{x.Enabled}|{x.Width}x{x.Height}|{x.RefreshRateNumerator}/{x.RefreshRateDenominator}|{x.Rotation}|{x.DesktopX},{x.DesktopY}"));
    private static string PathKey(DISPLAYCONFIG_PATH_INFO path) => $"{LuidString(path.targetInfo.adapterId)}|{path.sourceInfo.id}|{path.targetInfo.id}";
    private static string TargetDevicePath(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigDeviceInfoGetTargetName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id
            }
        };
        return DisplayConfigGetDeviceInfo(ref request) == 0 ? request.monitorDevicePath ?? string.Empty : string.Empty;
    }
    private static DISPLAYCONFIG_TARGET_MODE FindTargetMode(uint index, DISPLAYCONFIG_MODE_INFO[] modes)
        => index < modes.Length && modes[index].infoType == ModeInfoTypeTarget ? modes[index].targetMode : new DISPLAYCONFIG_TARGET_MODE();
    private static string LuidString(LUID luid) => $"{luid.HighPart:X8}:{luid.LowPart:X8}";

    private sealed record RawDisplayState(DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes);

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll", SetLastError = true)] private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetDisplayConfig(uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO[] pathArray, uint numModeInfoArrayElements, DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsEx(string? deviceName, int modeNum, ref DEVMODE devMode, uint flags);

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate; public DISPLAYCONFIG_RATIONAL hSyncFreq; public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize; public DISPLAYCONFIG_2DREGION totalSize; public uint videoStandard; public uint scanLineOrdering;
    }
    // These mode structures mirror the Windows CCD ABI. Rotation and scaling
    // are fields of DISPLAYCONFIG_PATH_TARGET_INFO, not mode records.
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering; public bool targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Explicit)] private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint flags; public int outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
        public int dmPositionX, dmPositionY; public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}
