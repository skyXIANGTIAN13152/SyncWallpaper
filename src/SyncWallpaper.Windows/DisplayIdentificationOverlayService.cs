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
                AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 4, 15, 32)),
                Content = new TextBlock { Text = details, Foreground = System.Windows.Media.Brushes.White, FontSize = Math.Max(42, Math.Min(bounds.Width, bounds.Height) / 10 / scale),
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
    public Task<IReadOnlyList<ManualDisplayAssignment>> ShowAsync(IReadOnlyList<DisplayIdentificationMark> marks, CancellationToken cancellationToken = default)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || marks.Count == 0) return Task.FromResult<IReadOnlyList<ManualDisplayAssignment>>(Array.Empty<ManualDisplayAssignment>());
        return dispatcher.InvokeAsync(() => ShowCore(marks, cancellationToken)).Task.Unwrap();
    }

    private static Task<IReadOnlyList<ManualDisplayAssignment>> ShowCore(IReadOnlyList<DisplayIdentificationMark> marks, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<ManualDisplayAssignment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window { Title = "SyncWallpaper · Confirm monitor roles and wallpapers", Width = 900, Height = 600, MinWidth = 760, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(7, 13, 27)), Foreground = WpfBrushes.White, Topmost = true };
        var root = new DockPanel { Margin = new Thickness(20), LastChildFill = false };
        var heading = new TextBlock { Text = "Identical or serial-less monitors require manual confirmation", FontSize = 20, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 232, 255)), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var rows = new List<(DisplayIdentificationMark Mark, WpfComboBox Role, WpfTextBox Path)>();
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        AddHeader(grid, 0, 0, "Monitor"); AddHeader(grid, 0, 1, "Stable identity"); AddHeader(grid, 0, 2, "Logical role"); AddHeader(grid, 0, 3, "Wallpaper path");
        var inputForeground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 25, 42));
        var inputBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 253, 255));
        var comboItemStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem));
        comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, inputForeground));
        comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, inputBackground));
        comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 14d));
        comboItemStyle.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, inputForeground));
        var comboItemSelected = new Trigger { Property = System.Windows.Controls.ComboBoxItem.IsSelectedProperty, Value = true };
        comboItemSelected.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, inputForeground));
        comboItemSelected.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, inputForeground));
        comboItemSelected.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 225, 245))));
        comboItemStyle.Triggers.Add(comboItemSelected);
        var comboTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };
        comboTemplate.VisualTree.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        comboTemplate.VisualTree.SetValue(TextBlock.ForegroundProperty, inputForeground);
        comboTemplate.VisualTree.SetValue(TextBlock.FontSizeProperty, 14d);
        comboTemplate.VisualTree.SetValue(TextBlock.MarginProperty, new Thickness(2, 1, 2, 1));
        for (var i = 0; i < marks.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var mark = marks[i];
            var defaultRole = mark.IsInternal ? "Laptop" : mark.Width >= mark.Height ? "Landscape" : "Portrait";
            var role = new WpfComboBox
            {
                ItemsSource = new[] { "Laptop", "Landscape", "Portrait", "Custom" },
                SelectedIndex = defaultRole switch { "Laptop" => 0, "Landscape" => 1, "Portrait" => 2, _ => 3 },
                Margin = new Thickness(4),
                Foreground = inputForeground,
                Background = inputBackground,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 137, 170)),
                FontSize = 14,
                ItemContainerStyle = comboItemStyle,
                ItemTemplate = comboTemplate,
                ToolTip = "Select a logical role"
            };
            role.Resources[System.Windows.SystemColors.ControlTextBrushKey] = inputForeground;
            role.Resources[System.Windows.SystemColors.WindowTextBrushKey] = inputForeground;
            role.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = inputForeground;
            var path = new WpfTextBox
            {
                Margin = new Thickness(4), MinWidth = 250, FontSize = 14,
                Foreground = inputForeground, Background = inputBackground,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 137, 170)),
                CaretBrush = inputForeground,
                ToolTip = "Select or enter a wallpaper file path"
            };
            var browse = new System.Windows.Controls.Button { Content = "Browse…", Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5), Foreground = WpfBrushes.White, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 65, 98)) };
            browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" }; if (dialog.ShowDialog() == true) path.Text = dialog.FileName; };
            var pathPanel = new StackPanel { Orientation = WpfOrientation.Horizontal }; pathPanel.Children.Add(path); pathPanel.Children.Add(browse);
            AddCell(grid, i + 1, 0, new TextBlock { Text = mark.Label, FontSize = 20, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 232, 255)), Margin = new Thickness(4) });
            AddCell(grid, i + 1, 1, new TextBlock { Text = string.IsNullOrWhiteSpace(mark.StableId) ? "(no stable ID)" : MonitorIdentitySanitizer.Redact(mark.StableId), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4), Foreground = WpfBrushes.LightGray, ToolTip = "Stable identity is redacted" });
            AddCell(grid, i + 1, 2, role); AddCell(grid, i + 1, 3, pathPanel); rows.Add((mark, role, path));
        }
        var content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(content, Dock.Top); root.Children.Add(content);
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 16, 0, 0) };
        var save = new System.Windows.Controls.Button { Content = "Save bindings", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
        buttons.Children.Add(cancel); buttons.Children.Add(save); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); window.Content = root;
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
    private static void AddCell(Grid grid, int row, int column, UIElement element) { Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element); }
}
