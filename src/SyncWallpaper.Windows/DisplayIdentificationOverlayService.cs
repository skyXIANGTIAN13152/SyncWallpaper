using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using FormsScreen = System.Windows.Forms.Screen;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBrushes = System.Windows.Media.Brushes;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed record DisplayIdentificationMark(string Label, string MonitorDevicePath, string StableId, string Details,
    bool IsInternal = false, int Width = 0, int Height = 0);
public sealed record ManualDisplayAssignment(string StableId, string MonitorDevicePath, string Role, string WallpaperPath);

/// <summary>Non-destructive A/B/C overlay used when the identity matcher reports ambiguity.</summary>
public sealed class DisplayIdentificationOverlayService
{
    public async Task<IReadOnlyList<DisplayIdentificationMark>> ShowAsync(IReadOnlyList<MonitorIdentity> monitors, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || monitors.Count == 0) return Array.Empty<DisplayIdentificationMark>();
        return await dispatcher.InvokeAsync(() => ShowCore(monitors, timeout ?? TimeSpan.FromSeconds(10), cancellationToken, dispatcher)).Task.Unwrap();
    }

    private static Task<IReadOnlyList<DisplayIdentificationMark>> ShowCore(IReadOnlyList<MonitorIdentity> monitors, TimeSpan timeout, CancellationToken cancellationToken, System.Windows.Threading.Dispatcher dispatcher)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<DisplayIdentificationMark>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<Window>();
        var screens = FormsScreen.AllScreens;
        var marks = new List<DisplayIdentificationMark>();
        DispatcherTimer? timer = null;
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var label = ((char)('A' + i)).ToString();
            // QueryDisplayConfig order is not a permanent screen order. Match
            // the overlay to the GDI device name (and only then geometry), so
            // A/B/C cannot land on the wrong physical panel after reconnects.
            var screen = FindScreen(monitor, screens);
            if (screen is null) continue;
            var scale = GetMonitorScale(screen);
            var bounds = screen.Bounds;
            var details = $"{label}\n{monitor.DisplayLabel}\n{monitor.Width}×{monitor.Height}\n{monitor.StableIdSource}";
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0, Top = 0, Width = Math.Max(1, bounds.Width / scale), Height = Math.Max(1, bounds.Height / scale),
                AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(205, 13, 28, 25)),
                Content = new TextBlock { Text = details, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 235, 220)), FontSize = Math.Max(42, Math.Min(bounds.Width, bounds.Height) / 10 / scale),
                    FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center }
            };
            window.MouseDown += (_, _) => FinishIfReady();
            windows.Add(window);
            marks.Add(new DisplayIdentificationMark(label, monitor.MonitorDevicePath, monitor.StableId,
                details.Replace('\n', ' '), monitor.IsInternal, monitor.Width, monitor.Height));
            window.SourceInitialized += (_, _) => PlaceWindow(window, bounds);
        }
        void FinishIfReady()
        {
            timer?.Stop();
            foreach (var window in windows) if (window.IsVisible) window.Close();
            tcs.TrySetResult(marks);
        }
        timer = new DispatcherTimer { Interval = timeout };
        timer.Tick += (_, _) => FinishIfReady();
        foreach (var window in windows) window.Show();
        timer.Start();
        var registration = cancellationToken.Register(() => dispatcher?.BeginInvoke(FinishIfReady));
        _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return tcs.Task;
    }

    private static FormsScreen? FindScreen(MonitorIdentity monitor, FormsScreen[] screens)
        => screens.FirstOrDefault(x => !string.IsNullOrWhiteSpace(monitor.WindowsDisplayName)
                && string.Equals(x.DeviceName, monitor.WindowsDisplayName, StringComparison.OrdinalIgnoreCase))
            ?? screens.FirstOrDefault(x => x.Bounds.Left == monitor.DesktopX && x.Bounds.Top == monitor.DesktopY);

    private static void PlaceWindow(Window window, System.Drawing.Rectangle bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
        // Window.Left/Top/Width/Height are WPF DIPs while SetWindowPos uses
        // physical pixels. Reconcile the WPF layout after Windows applies the
        // target monitor DPI, then set the native rectangle once more.
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        window.Width = Math.Max(1, bounds.Width / scale);
        window.Height = Math.Max(1, bounds.Height / scale);
        window.UpdateLayout();
        SetWindowPos(handle, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
    }

    private static double GetMonitorScale(FormsScreen screen)
    {
        try
        {
            var point = new POINT { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 };
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0) return x / 96.0;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return 1.0;
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, uint dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>Non-modal role and wallpaper confirmation shown after the A/B/C overlay.</summary>
public sealed class DisplayRoleAssignmentService
{
    public Task<IReadOnlyList<ManualDisplayAssignment>> ShowAsync(IReadOnlyList<DisplayIdentificationMark> marks, CancellationToken cancellationToken = default,
        IReadOnlyList<ManualDisplayAssignment>? initialAssignments = null)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || marks.Count == 0) return Task.FromResult<IReadOnlyList<ManualDisplayAssignment>>(Array.Empty<ManualDisplayAssignment>());
        return dispatcher.InvokeAsync(() => ShowCore(marks, cancellationToken, initialAssignments)).Task.Unwrap();
    }

    private static Task<IReadOnlyList<ManualDisplayAssignment>> ShowCore(IReadOnlyList<DisplayIdentificationMark> marks, CancellationToken cancellationToken,
        IReadOnlyList<ManualDisplayAssignment>? initialAssignments)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<ManualDisplayAssignment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window { Title = "屏序 · 确认逻辑角色与壁纸", Width = 1080, Height = 640, MinWidth = 900, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = WpfBrushes.Transparent, Foreground = WpfBrushes.White, Topmost = true };
        window.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
        var root = new DockPanel { Margin = new Thickness(26), LastChildFill = true };
        var heading = new TextBlock { Text = "核对 A / B / C 的显示器身份，再选择角色与壁纸。", FontSize = 14, Margin = new Thickness(0, 0, 0, 22) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var rows = new List<(DisplayIdentificationMark Mark, WpfComboBox Role, WpfTextBox Path)>();
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        AddHeader(grid, 0, 0, "屏幕"); AddHeader(grid, 0, 1, "稳定身份"); AddHeader(grid, 0, 2, "逻辑角色"); AddHeader(grid, 0, 3, "壁纸路径");
        for (var i = 0; i < marks.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var mark = marks[i];
            var initial = initialAssignments?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(mark.MonitorDevicePath)
                && x.MonitorDevicePath.Equals(mark.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            var defaultRole = initial?.Role ?? (mark.IsInternal ? "Laptop" : mark.Width >= mark.Height ? "Landscape" : "Portrait");
            var role = new WpfComboBox
            {
                ItemsSource = new[] { "Laptop", "Landscape", "Portrait", "Custom", defaultRole }.Distinct().ToArray(),
                SelectedItem = defaultRole,
                Margin = new Thickness(4),
                FontSize = 14,
                ToolTip = "选择逻辑角色"
            };
            var path = new WpfTextBox
            {
                Text = initial?.WallpaperPath ?? string.Empty,
                Margin = new Thickness(4), MinWidth = 100, FontSize = 14,
                ToolTip = "未重新选择时保留该显示器已保存壁纸；清除壁纸请使用组合编辑器"
            };
            var browse = new System.Windows.Controls.Button { Content = "选择…", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5) };
            browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp" }; if (dialog.ShowDialog() == true) path.Text = dialog.FileName; };
            var pathPanel = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); pathPanel.Children.Add(browse); pathPanel.Children.Add(path);
            AddCell(grid, i + 1, 0, new TextBlock { Text = mark.Label, FontSize = 20, Margin = new Thickness(4) });
            AddCell(grid, i + 1, 1, new TextBlock { Text = string.IsNullOrWhiteSpace(mark.StableId) ? "（无稳定 ID）" : MonitorIdentitySanitizer.Redact(mark.StableId), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4), Foreground = WpfBrushes.LightGray, ToolTip = "稳定身份已脱敏" });
            AddCell(grid, i + 1, 2, role); AddCell(grid, i + 1, 3, pathPanel); rows.Add((mark, role, path));
        }
        var content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(content, Dock.Top); root.Children.Add(content);
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 16, 0, 0) };
        var save = new System.Windows.Controls.Button { Content = "保存绑定", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
        save.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        buttons.Children.Add(cancel); buttons.Children.Add(save); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Remove(content); root.Children.Add(content);
        var shell = new Grid();
        var atmosphere = new ContentControl { Content = string.Empty, IsHitTestVisible = false,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        atmosphere.SetResourceReference(ContentControl.ContentTemplateProperty, "AtmosphereTemplate");
        shell.Children.Add(atmosphere);
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); layout.RowDefinitions.Add(new RowDefinition());
        var titleBar = new Border { Padding = new Thickness(24, 0, 14, 0), CornerRadius = new CornerRadius(14, 14, 0, 0) };
        titleBar.SetResourceReference(Border.BackgroundProperty, "TranslucentHeader");
        var titleContent = new DockPanel();
        var close = new System.Windows.Controls.Button { Content = "×", Width = 36, Height = 32, Padding = new Thickness(0), FontSize = 20 };
        close.Click += (_, _) => window.Close(); DockPanel.SetDock(close, Dock.Right); titleContent.Children.Add(close);
        titleContent.Children.Add(new TextBlock { Text = "确认显示器角色与壁纸", FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
        titleBar.Child = titleContent; titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) window.DragMove(); };
        layout.Children.Add(titleBar); Grid.SetRow(root, 1); layout.Children.Add(root); shell.Children.Add(layout);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = shell };
        frame.SetResourceReference(Border.BackgroundProperty, "WindowSurface"); frame.SetResourceReference(Border.BorderBrushProperty, "GlassBorder"); window.Content = frame;
        window.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { window.Close(); e.Handled = true; } };
        var finished = false;
        void Finish(IReadOnlyList<ManualDisplayAssignment> value) { if (finished) return; finished = true; tcs.TrySetResult(value); if (window.IsVisible) window.Close(); }
        save.Click += (_, _) => Finish(rows.Select(x => new ManualDisplayAssignment(x.Mark.StableId, x.Mark.MonitorDevicePath, x.Role.SelectedItem?.ToString() ?? "Custom", x.Path.Text.Trim())).ToArray());
        cancel.Click += (_, _) => Finish(Array.Empty<ManualDisplayAssignment>());
        window.Closed += (_, _) => tcs.TrySetResult(Array.Empty<ManualDisplayAssignment>());
        var registration = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(() => Finish(Array.Empty<ManualDisplayAssignment>())));
        _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        window.Show();
        return tcs.Task;
    }

    private static void AddHeader(Grid grid, int row, int column, string text) => AddCell(grid, row, column, new TextBlock { Text = text, Foreground = WpfBrushes.LightGray, Margin = new Thickness(4, 4, 4, 8) });
    private static void AddCell(Grid grid, int row, int column, UIElement element) { Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element); if (element is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, column == 0 && row > 0 ? "Cyan" : "TextSecondary"); }
}
