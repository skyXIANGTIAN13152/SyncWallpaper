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
        // UI validation uses isolated data and must not activate the user's
        // running production instance instead of the build being checked.
        _singleInstance = _validationMode
            ? new SingleInstanceService("Local\\SyncWallpaper.Validation.SingleInstance", "Local\\SyncWallpaper.Validation.Activate")
            : new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        _runtime = new AppRuntime();
        ThemeManager.Apply(_runtime.Settings.UiTheme);
        _runtime.StateChanged += (_, _) => Dispatcher.InvokeAsync(UpdateTray);
        _trayState = _runtime.TrayVisualState;
        _tray = new NotifyIcon
        {
            Icon = TrayIconRenderer.Create(_trayState.Value),
            Visible = true,
            Text = "屏序 SyncWallpaper"
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.ContextMenuStrip = BuildTrayMenu();
        _singleInstance.StartActivationListener(() => Dispatcher.InvokeAsync(ShowMainWindow));
        _runtime.Start(_validationMode);
        if (!background) ShowMainWindow();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(13, 28, 25),
            ForeColor = System.Drawing.Color.FromArgb(243, 235, 220),
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9f),
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new SecretGroveMenuColors()) { RoundedEdges = false }
        };
        menu.Items.Add("打开屏序", null, (_, _) => ShowMainWindow());
        menu.Items.Add("重新检测显示器", null, async (_, _) => await (_runtime?.DetectAsync() ?? Task.CompletedTask));
        menu.Items.Add("重新应用已匹配壁纸", null, async (_, _) => await (_runtime?.ReapplyAsync() ?? Task.CompletedTask));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("启用自动匹配", null, (_, _) => _runtime?.SetAutoMatch(true));
        menu.Items.Add("暂停自动切换", null, (_, _) => _runtime?.SetAutoMatch(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("检查更新", null, async (_, _) =>
        {
            if (_runtime is null) return;
            var result = await _runtime.CheckForUpdatesAsync(true);
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                ShowMainWindow();
                _tray?.ShowBalloonTip(2500, "屏序 SyncWallpaper", result.UserMessage ?? "发现新版本", ToolTipIcon.Info);
            }
            else if (result.Status == UpdateCheckStatus.UpToDate)
                _tray?.ShowBalloonTip(1800, "屏序 SyncWallpaper", "当前已是最新版本。", ToolTipIcon.Info);
            else
                _tray?.ShowBalloonTip(2200, "屏序 SyncWallpaper", result.UserMessage ?? "暂时无法检查更新。", ToolTipIcon.Warning);
        });
        menu.Items.Add("查看 GitHub 项目", null, (_, _) =>
        {
            if (_runtime is null || !_runtime.OpenReleasePage(ProjectLinks.Releases))
                _tray?.ShowBalloonTip(2200, "屏序 SyncWallpaper", "尚未配置 GitHub 仓库地址。", ToolTipIcon.Info);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ShutdownFromTray());
        return menu;
    }

    private sealed class SecretGroveMenuColors : ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Ink = System.Drawing.Color.FromArgb(13, 28, 25);
        private static readonly System.Drawing.Color Leaf = System.Drawing.Color.FromArgb(32, 53, 45);
        private static readonly System.Drawing.Color Gold = System.Drawing.Color.FromArgb(210, 180, 122);
        private static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(77, 82, 58);

        public SecretGroveMenuColors() => UseSystemColors = false;
        public override System.Drawing.Color ToolStripDropDownBackground => Ink;
        public override System.Drawing.Color ImageMarginGradientBegin => Ink;
        public override System.Drawing.Color ImageMarginGradientMiddle => Ink;
        public override System.Drawing.Color ImageMarginGradientEnd => Ink;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Gold;
        public override System.Drawing.Color MenuItemSelected => Leaf;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Leaf;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Leaf;
        public override System.Drawing.Color MenuItemPressedGradientBegin => Leaf;
        public override System.Drawing.Color MenuItemPressedGradientMiddle => Leaf;
        public override System.Drawing.Color MenuItemPressedGradientEnd => Leaf;
        public override System.Drawing.Color SeparatorDark => Border;
        public override System.Drawing.Color SeparatorLight => Ink;
        public override System.Drawing.Color CheckBackground => Leaf;
        public override System.Drawing.Color CheckSelectedBackground => Leaf;
        public override System.Drawing.Color CheckPressedBackground => Leaf;
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
        _tray.Text = $"屏序 SyncWallpaper · {_runtime.StatusText}";
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
