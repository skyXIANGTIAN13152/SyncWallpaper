using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FormsScreen = System.Windows.Forms.Screen;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBrushes = System.Windows.Media.Brushes;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed record DisplayIdentificationMark(string Label, string MonitorDevicePath, string StableId, string Details);
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
            var screen = i < screens.Length ? screens[i] : FormsScreen.PrimaryScreen;
            if (screen is null) continue;
            var details = $"{label}\n{monitor.DisplayLabel}\n{monitor.Width}×{monitor.Height}\n{monitor.StableIdSource}";
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                Left = screen.Bounds.Left, Top = screen.Bounds.Top, Width = screen.Bounds.Width, Height = screen.Bounds.Height,
                AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 4, 15, 32)),
                Content = new TextBlock { Text = details, Foreground = System.Windows.Media.Brushes.White, FontSize = Math.Max(42, Math.Min(screen.Bounds.Width, screen.Bounds.Height) / 10),
                    FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center }
            };
            window.MouseDown += (_, _) => FinishIfReady();
            windows.Add(window);
            marks.Add(new DisplayIdentificationMark(label, monitor.MonitorDevicePath, monitor.StableId, details.Replace('\n', ' ')));
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
        var window = new Window { Title = "屏序 · 确认逻辑角色与壁纸", Width = 760, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(7, 13, 27)), Foreground = WpfBrushes.White, Topmost = true };
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = "同型号或无序列号显示器需要手动确认", FontSize = 20, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 232, 255)), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var rows = new List<(DisplayIdentificationMark Mark, WpfComboBox Role, WpfTextBox Path)>();
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddHeader(grid, 0, 0, "屏幕"); AddHeader(grid, 0, 1, "稳定身份"); AddHeader(grid, 0, 2, "逻辑角色"); AddHeader(grid, 0, 3, "壁纸路径");
        for (var i = 0; i < marks.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var mark = marks[i]; var role = new WpfComboBox { ItemsSource = new[] { "Laptop", "Landscape", "Portrait", "Custom" }, SelectedIndex = i == 0 ? 0 : i == 1 ? 1 : 2, Margin = new Thickness(4) };
            var path = new WpfTextBox { Margin = new Thickness(4), MinWidth = 250 };
            var browse = new System.Windows.Controls.Button { Content = "选择…", Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
            browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp" }; if (dialog.ShowDialog() == true) path.Text = dialog.FileName; };
            var pathPanel = new StackPanel { Orientation = WpfOrientation.Horizontal }; pathPanel.Children.Add(path); pathPanel.Children.Add(browse);
            AddCell(grid, i + 1, 0, new TextBlock { Text = mark.Label, FontSize = 20, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 232, 255)), Margin = new Thickness(4) });
            AddCell(grid, i + 1, 1, new TextBlock { Text = string.IsNullOrWhiteSpace(mark.StableId) ? "（无稳定 ID）" : mark.StableId, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4), Foreground = WpfBrushes.LightGray });
            AddCell(grid, i + 1, 2, role); AddCell(grid, i + 1, 3, pathPanel); rows.Add((mark, role, path));
        }
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var save = new System.Windows.Controls.Button { Content = "保存绑定", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(6) };
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
