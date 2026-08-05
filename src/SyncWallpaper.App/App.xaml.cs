using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private AppRuntime? _runtime;
    private NotifyIcon? _tray;
    private MainWindow? _window;
    private GlobalHotkeyService? _hotkeys;
    private bool _background;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire()) { Shutdown(); return; }
        _runtime = new AppRuntime();
        _runtime.StateChanged += (_, _) => Dispatcher.InvokeAsync(UpdateTray);
        _tray = new NotifyIcon { Icon = CreateTrayIcon(), Visible = true, Text = "屏序 SyncWallpaper" };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.ContextMenuStrip = BuildTrayMenu();
        _runtime.Start();
        if (!_background) ShowMainWindow();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("当前状态", null, (_, _) => ShowMainWindow());
        menu.Items.Add("重新检测显示器", null, async (_, _) => await (_runtime?.DetectAsync() ?? Task.CompletedTask));
        menu.Items.Add("重新应用壁纸", null, async (_, _) => await (_runtime?.ReapplyAsync() ?? Task.CompletedTask));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开屏序", null, (_, _) => ShowMainWindow());
        menu.Items.Add("启用独立多显示器任务栏宿主", null, async (_, _) => { if (_runtime is not null) await _runtime.SetModuleEnabledAsync(SyncWallpaper.Core.SyncWallpaperModule.TaskbarHost, true); });
        menu.Items.Add("启用独立屏保宿主", null, async (_, _) => { if (_runtime is not null) await _runtime.SetModuleEnabledAsync(SyncWallpaper.Core.SyncWallpaperModule.ScreenSaverHost, true); });
        menu.Items.Add("启用自动匹配", null, (_, _) => { if (_runtime is not null) _runtime.Settings.AutoMatchEnabled = true; });
        menu.Items.Add("暂停自动切换", null, (_, _) => { if (_runtime is not null) _runtime.Settings.AutoMatchEnabled = false; });
        menu.Items.Add("退出", null, (_, _) => ShutdownFromTray());
        return menu;
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(_runtime!);
            _window.Closing += (_, args) => { if (!_window.AllowExit) { args.Cancel = true; _window.Hide(); } };
            if (_runtime!.Settings.Modules.IsEnabled(SyncWallpaper.Core.SyncWallpaperModule.Automation))
            {
                _hotkeys = new GlobalHotkeyService();
                _hotkeys.Attach(_window);
            }
        }
        _window.Show(); _window.WindowState = WindowState.Normal; _window.Activate();
        if (_runtime!.Settings.Modules.IsEnabled(SyncWallpaper.Core.SyncWallpaperModule.Automation))
            _hotkeys?.Register((uint)(ModifierKeys.Control | ModifierKeys.Alt), Key.S, ShowMainWindow);
    }

    private void UpdateTray()
    {
        if (_tray is null || _runtime is null) return;
        _tray.Text = $"屏序 SyncWallpaper · {_runtime.StatusText}";
    }

    private void ShutdownFromTray()
    {
        if (_window is not null) _window.AllowExit = true;
        _runtime?.Dispose(); _hotkeys?.Dispose(); _tray?.Dispose(); _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose(); _hotkeys?.Dispose(); _tray?.Dispose(); _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(7, 13, 27));
        using var pen = new Pen(Color.FromArgb(54, 232, 255), 4);
        g.DrawEllipse(pen, 7, 13, 22, 32); g.DrawRectangle(pen, 22, 19, 25, 19); g.DrawEllipse(pen, 39, 8, 17, 37);
        var h = bitmap.GetHicon(); return (Icon)Icon.FromHandle(h).Clone();
    }
}
