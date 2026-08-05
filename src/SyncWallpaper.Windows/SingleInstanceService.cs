using System.Threading;

namespace SyncWallpaper.Windows;

public sealed class SingleInstanceService : IDisposable
{
    private Mutex? _mutex;
    public bool TryAcquire()
    {
        _mutex = new Mutex(true, "Local\\SyncWallpaper.SingleInstance", out var created);
        return created;
    }
    public void Dispose() { try { _mutex?.ReleaseMutex(); } catch { } _mutex?.Dispose(); }
}
