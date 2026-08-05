using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.AudioEngine;

/// <summary>
/// Core Audio adapter. SetDefaultEndpoint is an undocumented PolicyConfig boundary;
/// failures are surfaced to AudioConfigurationEngine and never escape as a process crash.
/// </summary>
public sealed class WindowsCoreAudioEndpointProvider : IAudioEndpointProvider, IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notification;
    private bool _disposed;

    /// <summary>
    /// Diagnostic information from the most recent Core Audio call.  Enumeration is
    /// intentionally best-effort, so the caller can distinguish an empty endpoint
    /// collection from a COM/API failure without turning a background helper crash
    /// into an application failure.
    /// </summary>
    public string? LastError { get; private set; }

    public event EventHandler? DevicesChanged;
    public event EventHandler? DefaultsChanged;

    public WindowsCoreAudioEndpointProvider()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _notification = new NotificationClient(this);
        try
        {
            CheckHResult(_enumerator.RegisterEndpointNotificationCallback(_notification), "RegisterEndpointNotificationCallback");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public IReadOnlyList<AudioEndpointReference> Enumerate()
    {
        var list = new List<AudioEndpointReference>();
        AddFlow(EDataFlow.Render, AudioEndpointKind.Playback, list);
        AddFlow(EDataFlow.Capture, AudioEndpointKind.Capture, list);
        return list;
    }

    public AudioEndpointReference? GetDefault(AudioEndpointRole role)
    {
        var flow = role == AudioEndpointRole.Recording ? EDataFlow.Capture : EDataFlow.Render;
        var audioRole = role switch
        {
            AudioEndpointRole.Communications => ERole.Communications,
            AudioEndpointRole.Multimedia => ERole.Multimedia,
            _ => ERole.Console
        };
        try
        {
            CheckHResult(_enumerator.GetDefaultAudioEndpoint(flow, audioRole, out var device), "GetDefaultAudioEndpoint");
            if (device is null) return null;
            return ReadEndpoint(device, flow == EDataFlow.Capture ? AudioEndpointKind.Capture : AudioEndpointKind.Playback);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public Task SetDefaultAsync(AudioEndpointReference endpoint, AudioEndpointRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(endpoint.DeviceId)) throw new ArgumentException("音频设备 ID 为空。", nameof(endpoint));
        var policy = (IPolicyConfig)new PolicyConfigClient();
        var audioRole = role switch
        {
            AudioEndpointRole.Communications => ERole.Communications,
            AudioEndpointRole.Multimedia => ERole.Multimedia,
            _ => ERole.Console
        };
        var hr = policy.SetDefaultEndpoint(endpoint.DeviceId, audioRole);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return Task.CompletedTask;
    }

    private void AddFlow(EDataFlow flow, AudioEndpointKind kind, List<AudioEndpointReference> list)
    {
        try
        {
            CheckHResult(_enumerator.EnumAudioEndpoints(flow, DeviceStateMaskAll, out var collection), $"EnumAudioEndpoints({flow})");
            if (collection is null) throw new InvalidOperationException($"EnumAudioEndpoints({flow}) 返回空集合。");
            CheckHResult(collection.GetCount(out var count), $"IMMDeviceCollection.GetCount({flow})");
            for (var i = 0; i < count; i++)
            {
                CheckHResult(collection.Item((uint)i, out var device), $"IMMDeviceCollection.Item({flow},{i})");
                var endpoint = ReadEndpoint(device, kind);
                if (endpoint is not null) list.Add(endpoint);
            }
            Marshal.ReleaseComObject(collection);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    private static AudioEndpointReference? ReadEndpoint(IMMDevice device, AudioEndpointKind kind)
    {
        try
        {
            CheckHResult(device.GetId(out var id), "IMMDevice.GetId");
            CheckHResult(device.GetState(out var state), "IMMDevice.GetState");
            var name = id;
            try
            {
                CheckHResult(device.OpenPropertyStore(StorageAccess.Read, out var store), "IMMDevice.OpenPropertyStore");
                if (store is null) throw new InvalidOperationException("OpenPropertyStore 返回空对象。");
                var key = PropertyKeys.DeviceFriendlyName;
                CheckHResult(store.GetValue(ref key, out var value), "IPropertyStore.GetValue");
                name = value.GetString() ?? id;
                value.Clear();
                Marshal.ReleaseComObject(store);
            }
            catch { }
            return new AudioEndpointReference
            {
                DeviceId = id,
                FriendlyName = name,
                Kind = kind,
                State = state switch
                {
                    DeviceState.Active => AudioEndpointState.Active,
                    DeviceState.Disabled => AudioEndpointState.Disabled,
                    DeviceState.NotPresent => AudioEndpointState.NotPresent,
                    DeviceState.Unplugged => AudioEndpointState.Unplugged,
                    _ => AudioEndpointState.Unknown
                }
            };
        }
        catch { return null; }
        finally { if (device is not null) Marshal.ReleaseComObject(device); }
    }

    private static void CheckHResult(int hResult, string operation)
    {
        if (hResult < 0)
            throw new COMException($"{operation} 失败。", hResult);
    }

    internal void NotifyDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);
    internal void NotifyDefaultsChanged() => DefaultsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(_notification); } catch { }
        if (_enumerator is not null) Marshal.ReleaseComObject(_enumerator);
        GC.SuppressFinalize(this);
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly WindowsCoreAudioEndpointProvider _owner;
        public NotificationClient(WindowsCoreAudioEndpointProvider owner) => _owner = owner;
        public int OnDeviceStateChanged(string deviceId, uint newState) { _owner.NotifyDevicesChanged(); return 0; }
        public int OnDeviceAdded(string deviceId) { _owner.NotifyDevicesChanged(); return 0; }
        public int OnDeviceRemoved(string deviceId) { _owner.NotifyDevicesChanged(); return 0; }
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string deviceId) { _owner.NotifyDefaultsChanged(); return 0; }
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) { _owner.NotifyDevicesChanged(); return 0; }
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }
    private enum DeviceState : uint { Active = 0x1, Disabled = 0x2, NotPresent = 0x4, Unplugged = 0x8 }
    private const DeviceState DeviceStateMaskAll = DeviceState.Active | DeviceState.Disabled | DeviceState.NotPresent | DeviceState.Unplugged;
    private enum StorageAccess : uint { Read = 0x0 }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)] private class MMDeviceEnumerator { }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(StorageAccess access, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out DeviceState state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint count);
        int GetAt(uint index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }
    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int Unused0(); int Unused1(); int Unused2(); int Unused3(); int Unused4(); int Unused5(); int Unused6(); int Unused7(); int Unused8(); int Unused9(); int Unused10();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), ClassInterface(ClassInterfaceType.None)] private class PolicyConfigClient { }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint PropertyId; }
    private static class PropertyKeys
    {
        public static PropertyKey DeviceFriendlyName => new() { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
    }
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr Pointer;
        public string? GetString() => VariantType == 31 && Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(Pointer) : null;
        public void Clear() { if (VariantType == 31 && Pointer != IntPtr.Zero) PropVariantClear(ref this); }
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant variant);
}
