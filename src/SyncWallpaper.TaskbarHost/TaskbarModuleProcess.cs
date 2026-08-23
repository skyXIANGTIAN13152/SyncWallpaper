using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

/// <summary>
/// Owns the complete taskbar module lifetime inside the isolated host process.
/// The core application never creates these windows or hooks directly.
/// </summary>
public sealed class TaskbarModuleProcess : IAsyncDisposable
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private TaskbarCoordinator? _coordinator;
    private TaskbarHostStatus _status = TaskbarHostStatus.Stopped;
    private bool _stopRequested;

    public Task Completion => _completion.Task;
    public TaskbarHostStatus Status => Volatile.Read(ref _status);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_thread is not null) throw new InvalidOperationException("任务栏宿主不能重复启动。");
            Volatile.Write(ref _status, TaskbarHostStatus.Stopped with { State = "Starting" });
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "SyncWallpaper.TaskbarHost.UI"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        try { await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false); }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void ThreadMain()
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcher = dispatcher;
            dispatcher.UnhandledException += Dispatcher_UnhandledException;
            // Two small taskbars do not benefit from loading a full GPU driver
            // stack. Software rendering saves a large optional-process working
            // set and avoids vendor-driver handle growth on mixed-DPI systems.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            var discovery = new NativeTaskbarMonitorProvider();
            var platform = new WindowsTaskWindowPlatform();
            var changeSource = new WindowsTaskbarChangeSource();
            var dataRoot = TaskbarDataRootResolver.Resolve();
            var pins = new JsonTaskbarPinStore(dataRoot);
            var preferencesStore = new ConfigurationStore(new DataPaths(dataRoot));
            var preferences = TaskbarHostPreferences.Normalize(preferencesStore.Load(
                TaskbarHostPreferences.FileName,
                new TaskbarHostPreferences(),
                TaskbarHostPreferences.Validate));
            var view = new WpfTaskbarViewHost(dispatcher, pins, preferences);
            var coordinator = new TaskbarCoordinator(discovery.Discover, platform, changeSource, view);
            coordinator.StatusChanged += Coordinator_StatusChanged;
            _coordinator = coordinator;
            coordinator.Start();
            Volatile.Write(ref _status, coordinator.Status);
            _ready.TrySetResult();

            Dispatcher.Run();
            if (!_stopRequested && !_completion.Task.IsCompleted)
                throw new InvalidOperationException("任务栏 UI 消息循环意外结束。");
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _status, Status with { State = "Faulted", LastError = ex.Message });
            _ready.TrySetException(ex);
            _completion.TrySetException(ex);
        }
        finally
        {
            try
            {
                if (_coordinator is not null)
                {
                    _coordinator.StatusChanged -= Coordinator_StatusChanged;
                    _coordinator.Dispose();
                }
            }
            catch { }
            _coordinator = null;
            if (_stopRequested) Volatile.Write(ref _status, TaskbarHostStatus.Stopped);
        }
    }

    private void Coordinator_StatusChanged(object? sender, TaskbarHostStatus status) => Volatile.Write(ref _status, status);

    private void Dispatcher_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Volatile.Write(ref _status, Status with { State = "Faulted", LastError = e.Exception.Message });
        _completion.TrySetException(e.Exception);
        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Thread? thread;
        Dispatcher? dispatcher;
        lock (_gate)
        {
            _stopRequested = true;
            thread = _thread;
            dispatcher = _dispatcher;
        }
        if (thread is null) return;

        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    _coordinator?.Dispose();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }, DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
        }

        await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
        lock (_gate) _thread = null;
        if (!_completion.Task.IsCompleted) _completion.TrySetResult();
        Volatile.Write(ref _status, TaskbarHostStatus.Stopped);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
