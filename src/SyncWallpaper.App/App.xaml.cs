using System.Windows;
using System.Windows.Forms;
using SyncWallpaper.Update.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private AppRuntime? _runtime;
    private NotifyIcon? _tray;
    private MainWindow? _window;
    private bool _validationMode;
    private TrayIconState? _trayState;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        _validationMode = e.Args.Any(a => a.Equals("--validation", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        _runtime = new AppRuntime();
        _runtime.StateChanged += (_, _) => Dispatcher.InvokeAsync(UpdateTray);
        _trayState = _runtime.TrayVisualState;
        _tray = new NotifyIcon
        {
            Icon = TrayIconRenderer.Create(_trayState.Value),
            Visible = true,
            Text = "SyncWallpaper"
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.ContextMenuStrip = BuildTrayMenu();
        _singleInstance.StartActivationListener(() => Dispatcher.InvokeAsync(ShowMainWindow));
        _runtime.Start(_validationMode);
        if (!background) ShowMainWindow();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open SyncWallpaper", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Detect monitors", null, async (_, _) => await (_runtime?.DetectAsync() ?? Task.CompletedTask));
        menu.Items.Add("Reapply matched wallpapers", null, async (_, _) => await (_runtime?.ReapplyAsync() ?? Task.CompletedTask));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Enable automatic matching", null, (_, _) => _runtime?.SetAutoMatch(true));
        menu.Items.Add("Pause automatic switching", null, (_, _) => _runtime?.SetAutoMatch(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Check for updates", null, async (_, _) =>
        {
            if (_runtime is null) return;
            var result = await _runtime.CheckForUpdatesAsync(true);
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                ShowMainWindow();
                _tray?.ShowBalloonTip(2500, "SyncWallpaper", result.UserMessage ?? "New version available", ToolTipIcon.Info);
            }
            else if (result.Status == UpdateCheckStatus.UpToDate)
                _tray?.ShowBalloonTip(1800, "SyncWallpaper", "You are up to date.", ToolTipIcon.Info);
            else
                _tray?.ShowBalloonTip(2200, "SyncWallpaper", result.UserMessage ?? "Unable to check for updates right now.", ToolTipIcon.Warning);
        });
        menu.Items.Add("Open GitHub repository", null, (_, _) =>
        {
            if (_runtime is null || !_runtime.OpenReleasePage(ProjectLinks.Releases))
                _tray?.ShowBalloonTip(2200, "SyncWallpaper", "The GitHub repository URL is not configured.", ToolTipIcon.Info);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ShutdownFromTray());
        return menu;
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(_runtime!);
            _window.Closing += (_, args) =>
            {
                if (_window.AllowExit) return;
                args.Cancel = true;
                _window.Hide();
            };
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void UpdateTray()
    {
        if (_tray is null || _runtime is null) return;
        var state = _runtime.TrayVisualState;
        if (_trayState != state)
        {
            var previous = _tray.Icon;
            _tray.Icon = TrayIconRenderer.Create(state);
            previous?.Dispose();
            _trayState = state;
        }
        _tray.Text = $"SyncWallpaper · {_runtime.StatusText}";
    }

    private void ShutdownFromTray()
    {
        if (_window is not null) _window.AllowExit = true;
        _runtime?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
