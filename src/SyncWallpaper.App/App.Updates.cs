using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.App;

public partial class App
{
    private UpdateWindow? _updateWindow;
    private UpdateCheckCompletedEventArgs? _pendingUpdate;
    private DispatcherTimer? _updatePromptTimer;
    private bool _shuttingDown;

    private void InitializeUpdateSupport()
    {
        if (_runtime is null || _validationMode) return;
        _runtime.UpdateCheckCompleted += (_, result) =>
        {
            if (_shuttingDown || !result.Result.IsUpdateAvailable) return;
            Dispatcher.BeginInvoke(() => QueueUpdatePrompt(result));
        };
        _updatePromptTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _updatePromptTimer.Tick += (_, _) => TryPresentUpdate();
        _ = AppUpdateService.CleanupCompletedAsync(message => _runtime?.LogUpdateFailure(message));
    }

    private void QueueUpdatePrompt(UpdateCheckCompletedEventArgs result)
    {
        if (_shuttingDown || _runtime is null || _updateWindow is not null) return;
        var version = result.Result.LatestSemanticVersion?.ToString();
        if (version is null || !UpdatePromptPolicy.ShouldPrompt(version, result.Manual,
            _runtime.Settings.LastUpdatePromptVersion, _runtime.Settings.LastUpdatePromptUtc, DateTimeOffset.UtcNow)) return;
        _pendingUpdate = result;
        TryPresentUpdate();
    }

    private void TryPresentUpdate()
    {
        if (_shuttingDown || _runtime is null || _pendingUpdate is null || _updateWindow is not null) return;
        if (!_pendingUpdate.Manual && !_runtime.Settings.AutomaticUpdateCheckEnabled)
        {
            _pendingUpdate = null;
            _updatePromptTimer?.Stop();
            return;
        }
        // Do not dismiss or restart over unsaved profile edits or other modal work.
        if (ComponentDispatcher.IsThreadModal || Windows.Cast<Window>().Any(window => window != _window && window.IsVisible))
        {
            _updatePromptTimer?.Start();
            return;
        }
        var release = _pendingUpdate.Result;
        _pendingUpdate = null;
        _updatePromptTimer?.Stop();
        _runtime.RecordUpdatePrompt(release.LatestSemanticVersion!.ToString());
        ShowMainWindow();
        _updateWindow = new UpdateWindow(_runtime, release) { Owner = _window };
        try
        {
            if (_updateWindow.ShowDialog() == true && _updateWindow.HandoffReady) ShutdownFromTray();
        }
        finally { _updateWindow = null; }
    }

    private async void SignalUpdateStartupReady(string[] args)
    {
        var index = Array.IndexOf(args, "--update-ready");
        if (index < 0 || index + 1 >= args.Length || _validationMode) return;
        try
        {
            // Let startup layout and initial dispatcher callbacks complete first.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!_shuttingDown) AppUpdateService.WriteApplicationReady(args[index + 1]);
        }
        catch (Exception ex) { _runtime?.LogUpdateFailure("更新启动确认失败：" + ex.Message); }
    }
}
