using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SyncWallpaper.Windows;

/// <summary>
/// Read-only SetupAPI lookup for monitor ContainerId.  This is deliberately
/// best-effort: display discovery must continue when a driver exposes no
/// property or when the caller has no device-enumeration permission.
/// </summary>
internal sealed class SetupApiMonitorProvider
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint ERROR_NO_MORE_ITEMS = 259;
    private static readonly Guid ContainerIdKey = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");

    public string TryGetContainerId(string instanceName, string monitorDevicePath)
    {
        try
        {
            var wanted = NormalizeInstance(instanceName, monitorDevicePath);
            if (wanted.Length == 0) return string.Empty;
            var infoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1)) return string.Empty;
            try
            {
            for (uint index = 0; index < 512; index++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(infoSet, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NO_MORE_ITEMS) break;
                    continue;
                }
                var instance = GetInstanceId(infoSet, ref data);
                if (instance.Length == 0 || !InstanceMatches(wanted, instance)) continue;
                if (TryReadContainerId(infoSet, ref data, out var id)) return id;
            }
            }
            finally { _ = SetupDiDestroyDeviceInfoList(infoSet); }
        }
        catch (EntryPointNotFoundException) { return string.Empty; }
        catch (DllNotFoundException) { return string.Empty; }
        catch (BadImageFormatException) { return string.Empty; }
        return string.Empty;
    }

    private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        var length = 256u;
        var buffer = new char[length];
        if (SetupDiGetDeviceInstanceId(set, ref data, buffer, (int)length, out var required))
            return new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end && end >= 0 ? end : buffer.Length).Trim();
        if (Marshal.GetLastWin32Error() == ERROR_INSUFFICIENT_BUFFER && required > 0)
        {
            buffer = new char[required];
            if (SetupDiGetDeviceInstanceId(set, ref data, buffer, buffer.Length, out _))
                return new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end && end >= 0 ? end : buffer.Length).Trim();
        }
        return string.Empty;
    }

    private static bool TryReadContainerId(IntPtr set, ref SP_DEVINFO_DATA data, out string value)
    {
        value = string.Empty;
        var key = new DEVPROPKEY { fmtid = ContainerIdKey, pid = 2 };
        var buffer = new byte[16];
        if (!SetupDiGetDeviceProperty(set, ref data, ref key, out var type, buffer, buffer.Length, out var required, 0))
        {
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || required != 16) return false;
            buffer = new byte[required];
            if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, buffer, buffer.Length, out _, 0)) return false;
        }
        if (type == 0x0000000d && buffer.Length >= 16)
        {
            value = new Guid(buffer.AsSpan(0, 16)).ToString("D");
            return true;
        }
        return false;
    }

    private static bool InstanceMatches(string wanted, string actual)
    {
        var normalized = NormalizeInstance(actual, string.Empty);
        return string.Equals(wanted, normalized, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(wanted, StringComparison.OrdinalIgnoreCase)
            || wanted.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInstance(string instanceName, string monitorPath)
    {
        var candidate = !string.IsNullOrWhiteSpace(instanceName) ? instanceName : monitorPath;
        candidate = candidate.Replace("\\?\\", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\\", "#", StringComparison.Ordinal).Trim('#');
        var guid = candidate.IndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) candidate = candidate[..guid];
        return candidate.Replace("#", "\\", StringComparison.Ordinal).ToUpperInvariant();
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInfo", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, [Out] char[] deviceInstanceId, int deviceInstanceIdSize, out uint requiredSize);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey, out uint propertyType, [Out] byte[] propertyBuffer, int propertyBufferSize, out uint requiredSize, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DEVPROPKEY { public Guid fmtid; public uint pid; }
}
