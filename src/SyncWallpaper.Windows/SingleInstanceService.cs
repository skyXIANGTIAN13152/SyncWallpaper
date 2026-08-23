using System.Threading;

namespace SyncWallpaper.Windows;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _ownsMutex;

    public SingleInstanceService(
        string mutexName = "Local\\SyncWallpaper.SingleInstance",
        string activationEventName = "Local\\SyncWallpaper.Activate")
    {
        _mutexName = mutexName;
        _activationEventName = activationEventName;
    }

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, _mutexName, out _ownsMutex);
        if (_ownsMutex)
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activationEventName);
        return _ownsMutex;
    }

    public void StartActivationListener(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (!_ownsMutex || _activationEvent is null)
            throw new InvalidOperationException("只有主实例可以监听激活请求。");

        _activationRegistration?.Unregister(null);
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) => { if (!timedOut) activate(); },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(_activationEventName);
            return activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        if (_ownsMutex) try { _mutex?.ReleaseMutex(); } catch { }
        _ownsMutex = false;
        _mutex?.Dispose();
        _mutex = null;
    }
}
