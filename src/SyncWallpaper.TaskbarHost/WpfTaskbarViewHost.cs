using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public sealed class WpfTaskbarViewHost : ITaskbarViewHost
{
    private readonly Dispatcher _dispatcher;
    private readonly ITaskbarPinStore _pinStore;
    private readonly TaskbarHostPreferences _preferences;
    private readonly Dictionary<string, SecondaryTaskbarWindow> _windows = new(StringComparer.Ordinal);
    private IReadOnlyList<TaskbarBarStatus> _bars = Array.Empty<TaskbarBarStatus>();
    private TaskbarSnapshot _lastSnapshot = TaskbarSnapshot.Empty;
    private TaskbarWindowActions? _lastActions;
    private int _barCount;
    private bool _disposed;

    public event EventHandler? StatusChanged;

    public WpfTaskbarViewHost(Dispatcher dispatcher, ITaskbarPinStore pinStore, TaskbarHostPreferences preferences)
    {
        _dispatcher = dispatcher;
        _pinStore = pinStore;
        _preferences = TaskbarHostPreferences.Normalize(preferences);
    }

    public int BarCount => Volatile.Read(ref _barCount);
    public IReadOnlyList<TaskbarBarStatus> Bars => Volatile.Read(ref _bars);

    public void Render(TaskbarSnapshot snapshot, TaskbarWindowActions actions)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => RenderCore(snapshot, actions));
            return;
        }
        RenderCore(snapshot, actions);
    }

    private void RenderCore(TaskbarSnapshot snapshot, TaskbarWindowActions actions)
    {
        if (_disposed) return;
        _lastSnapshot = snapshot;
        _lastActions = actions;
        var secondary = snapshot.Monitors.Where(x => !x.IsPrimary).ToArray();
        var reservation = TaskbarWorkAreaReservationPolicy.Evaluate(_preferences, secondary.Length);
        var activeKeys = secondary.Select(x => x.RuntimeKey).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _windows.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            _windows[stale].Dispose();
            _windows.Remove(stale);
        }

        foreach (var monitor in secondary)
        {
            if (!_windows.TryGetValue(monitor.RuntimeKey, out var window))
            {
                window = new SecondaryTaskbarWindow(_pinStore, _preferences, RefreshPinnedViews, RefreshBarStatus);
                _windows[monitor.RuntimeKey] = window;
            }
            var tasks = _lastSnapshot.Tasks.Where(x => x.MonitorKey == monitor.RuntimeKey).ToArray();
            window.UpdateView(monitor, tasks, actions, reservation);
        }

        Volatile.Write(ref _barCount, _windows.Count);
        PublishBarStatus();
    }

    private void RefreshBarStatus()
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) PublishBarStatus();
        else _dispatcher.BeginInvoke(PublishBarStatus, DispatcherPriority.Background);
    }

    private void PublishBarStatus()
    {
        var secondary = _lastSnapshot.Monitors.Where(x => !x.IsPrimary && _windows.ContainsKey(x.RuntimeKey)).ToArray();
        var pinnedCount = _preferences.ShowPinnedItems ? _pinStore.Items.Count : 0;
        Volatile.Write(ref _bars, secondary.Select(monitor =>
        {
            var window = _windows[monitor.RuntimeKey];
            var tasks = _lastSnapshot.Tasks.Where(x => x.MonitorKey == monitor.RuntimeKey).ToArray();
            return new TaskbarBarStatus(
                monitor.RuntimeKey,
                monitor.DisplayLabel,
                window.LastBounds,
                tasks.Length,
                TaskbarGrouping.Build(tasks).Count,
                pinnedCount,
                window.AutoHide,
                window.IsAutoHidden,
                window.WorkAreaReserved,
                window.PlacementError);
        }).ToArray());
        try { StatusChanged?.Invoke(this, EventArgs.Empty); } catch { }
    }

    private void RefreshPinnedViews()
    {
        if (_disposed || _lastActions is null) return;
        if (_dispatcher.CheckAccess()) RenderCore(_lastSnapshot, _lastActions);
        else _dispatcher.BeginInvoke(() => RenderCore(_lastSnapshot, _lastActions));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        void CloseAll()
        {
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
            _pinStore.Dispose();
            Volatile.Write(ref _bars, Array.Empty<TaskbarBarStatus>());
            Volatile.Write(ref _barCount, 0);
            StatusChanged = null;
        }
        if (_dispatcher.CheckAccess()) CloseAll();
        else
        {
            try { _dispatcher.Invoke(CloseAll); }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }
}

internal sealed class SecondaryTaskbarWindow : Window, IDisposable
{
    private static readonly Brush ForegroundBrush = Brushes.White;
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(149, 164, 184));
    private static readonly Brush ActiveBackground = new SolidColorBrush(Color.FromArgb(220, 18, 110, 148));
    private static readonly Brush NormalBackground = new SolidColorBrush(Color.FromArgb(165, 28, 45, 69));
    private static readonly Brush PinnedBackground = new SolidColorBrush(Color.FromArgb(150, 38, 50, 82));
    private readonly ITaskbarPinStore _pinStore;
    private readonly TaskbarHostPreferences _preferences;
    private readonly Action _pinsChanged;
    private readonly Action _stateChanged;
    private readonly WindowsAppBarReservation _appBar = new();
    private TaskbarWorkAreaReservationDecision _reservationDecision = new(false);
    private readonly TaskbarAutoHideStateMachine _autoHideState = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _monitorLabel = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(125, 224, 255)),
        FontSize = 11,
        Margin = new Thickness(10, 0, 10, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _clock = new()
    {
        Foreground = Brushes.White,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(10, 0, 10, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DwmThumbnailPreviewWindow _preview = new();
    private TaskbarTaskItem? _pendingPreviewTask;
    private FrameworkElement? _pendingPreviewAnchor;
    private TaskbarMonitor? _lastMonitor;
    private MonitorPlacement? _lastPlacement;
    private TaskbarEdgePositions? _edgePositions;
    private HwndSource? _source;
    private nint _handle;
    private int _openMenus;
    private int _positionRefreshPending;
    private bool _positioning;
    private bool _disposed;

    public SyncWallpaper.Core.Int32Rect LastBounds { get; private set; }
    public bool AutoHide => _preferences.AutoHide;
    public bool IsAutoHidden => _preferences.AutoHide && _autoHideState.IsHidden;
    public bool WorkAreaReserved { get; private set; }
    public string? PlacementError { get; private set; }

    public SecondaryTaskbarWindow(
        ITaskbarPinStore pinStore,
        TaskbarHostPreferences preferences,
        Action pinsChanged,
        Action stateChanged)
    {
        _pinStore = pinStore;
        _preferences = TaskbarHostPreferences.Normalize(preferences);
        _pinsChanged = pinsChanged;
        _stateChanged = stateChanged;
        Title = "屏序副屏任务栏";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.Manual;

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(360)
        };
        _previewTimer.Tick += PreviewTimer_Tick;
        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_monitorLabel, Dock.Left);
        DockPanel.SetDock(_clock, Dock.Right);
        content.Children.Add(_monitorLabel);
        content.Children.Add(_clock);
        content.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _buttons
        });
        Content = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(175, 51, 139, 181)),
            Background = new SolidColorBrush(Color.FromArgb(238, 8, 18, 34)),
            Child = content
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(_handle, GwlExStyle, new nint(style | WsExToolWindow | WsExNoActivate));
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WindowProcedure);
        };
    }

    public void UpdateView(
        TaskbarMonitor monitor,
        IReadOnlyList<TaskbarTaskItem> tasks,
        TaskbarWindowActions actions,
        TaskbarWorkAreaReservationDecision reservationDecision)
    {
        if (_disposed) return;
        CancelPreview();
        _reservationDecision = reservationDecision;
        _lastMonitor = monitor;
        _monitorLabel.Text = monitor.DisplayLabel;
        _monitorLabel.ToolTip = $"{monitor.DisplayLabel}\n独立副屏任务栏（不会替换 Explorer）";
        _clock.Text = DateTime.Now.ToString("HH:mm\nMM/dd");
        _clock.Visibility = _preferences.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        _buttons.Children.Clear();

        var groups = TaskbarGrouping.Build(tasks);
        var renderedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in _preferences.ShowPinnedItems ? _pinStore.Items : Array.Empty<TaskbarPinnedItem>())
        {
            var group = groups.FirstOrDefault(x => x.Key.Equals(pin.Id, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
            {
                _buttons.Children.Add(CreateGroupButton(group, actions, true));
                renderedGroups.Add(group.Key);
            }
            else
            {
                _buttons.Children.Add(CreatePinnedButton(pin));
            }
        }
        foreach (var group in groups.Where(x => !renderedGroups.Contains(x.Key)))
            _buttons.Children.Add(CreateGroupButton(group, actions, false));

        if (!IsVisible) Show();
        PositionOnMonitor(monitor);
        if (_preferences.AutoHide)
        {
            if (!_autoHideTimer.IsEnabled) _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
            _autoHideState.Reset();
        }
    }

    private Button CreateGroupButton(TaskbarTaskGroup group, TaskbarWindowActions actions, bool pinned)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var previewTask = group.PreviewTask;
        var icon = GetIcon(previewTask);
        if (icon is not null)
            panel.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = group.Count == 1 ? previewTask.Title : group.DisplayName,
            Foreground = group.AllMinimized ? MutedBrush : ForegroundBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = group.Count == 1 ? 170 : 130,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (group.Count > 1)
        {
            panel.Children.Add(new Border
            {
                Margin = new Thickness(7, 0, 0, 0),
                Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(170, 8, 18, 34)),
                Child = new TextBlock { Text = group.Count.ToString(), Foreground = Brushes.White, FontSize = 10 }
            });
        }
        if (pinned)
            panel.Children.Add(new TextBlock { Text = " •", Foreground = new SolidColorBrush(Color.FromRgb(52, 227, 255)), FontSize = 13 });

        var button = CreateBaseButton(panel, group.IsForeground ? ActiveBackground : NormalBackground, group.IsForeground);
        button.ToolTip = string.Join(Environment.NewLine, group.Tasks.Select(x => x.Title))
            + (pinned ? "\n已固定" : string.Empty)
            + (group.Tasks.Any(x => x.IsElevated) ? "\n含高权限窗口" : string.Empty);
        button.Click += (_, _) =>
        {
            CancelPreview();
            if (group.Count == 1) actions.ActivateOrMinimize(group.Tasks[0].Handle);
            else OpenMenu(button, CreateGroupMenu(group, actions));
        };
        button.ContextMenu = CreateGroupMenu(group, actions);
        button.ContextMenuOpening += (_, _) => CancelPreview();
        button.MouseEnter += (_, _) => SchedulePreview(previewTask, button);
        button.MouseLeave += (_, _) => CancelPreview(button);
        return button;
    }

    private Button CreatePinnedButton(TaskbarPinnedItem pin)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = GetPinnedIcon(pin);
        if (icon is not null)
            panel.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = pin.DisplayName,
            Foreground = MutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock { Text = " •", Foreground = new SolidColorBrush(Color.FromRgb(52, 227, 255)), FontSize = 13 });
        var button = CreateBaseButton(panel, PinnedBackground, false);
        button.ToolTip = $"{pin.DisplayName}\n已固定；点击启动";
        button.Click += (_, _) => _pinStore.Launch(pin.Id);
        var menu = CreateMenu();
        var remove = CreateMenuItem("从副屏任务栏取消固定");
        remove.Click += (_, _) => { if (_pinStore.Remove(pin.Id)) _pinsChanged(); };
        menu.Items.Add(remove);
        button.ContextMenu = menu;
        return button;
    }

    private ContextMenu CreateGroupMenu(TaskbarTaskGroup group, TaskbarWindowActions actions)
    {
        var menu = CreateMenu();
        var isPinned = _pinStore.IsPinned(group.Key);
        var pinItem = CreateMenuItem(isPinned ? "从副屏任务栏取消固定" : "固定到副屏任务栏");
        pinItem.IsEnabled = isPinned || _pinStore.CanPin(group);
        pinItem.Click += (_, _) => { _pinStore.Toggle(group); _pinsChanged(); };
        menu.Items.Add(pinItem);
        menu.Items.Add(new Separator());

        foreach (var task in group.Tasks)
        {
            var windowItem = CreateMenuItem(task.Title);
            var activate = CreateMenuItem(task.IsForeground && !task.IsMinimized ? "最小化窗口" : "切换到此窗口");
            activate.Click += (_, _) => actions.ActivateOrMinimize(task.Handle);
            var close = CreateMenuItem(task.IsElevated ? "关闭窗口（可能需要管理员权限）" : "关闭窗口");
            close.Click += (_, _) => actions.Close(task.Handle);
            windowItem.Items.Add(activate);
            windowItem.Items.Add(close);
            menu.Items.Add(windowItem);
        }
        return menu;
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 22, 39)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(41, 159, 205)),
            BorderThickness = new Thickness(1),
            Placement = PlacementMode.Top,
            StaysOpen = false
        };
        menu.Opened += (_, _) =>
        {
            _openMenus++;
            if (_preferences.AutoHide && _autoHideState.IsHidden)
            {
                _autoHideState.Reset();
                ApplyEdgePosition(hidden: false);
            }
        };
        menu.Closed += (_, _) => _openMenus = Math.Max(0, _openMenus - 1);
        return menu;
    }

    private static MenuItem CreateMenuItem(string text) => new()
    {
        Header = text,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(10, 22, 39)),
        Padding = new Thickness(8, 4, 8, 4),
        MaxWidth = 360
    };

    private static void OpenMenu(Button button, ContextMenu menu)
    {
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private static Button CreateBaseButton(object content, Brush background, bool active) => new()
    {
        Content = content,
        Height = 34,
        MinWidth = 42,
        MaxWidth = 220,
        Margin = new Thickness(2, 0, 2, 0),
        Padding = new Thickness(10, 2, 10, 2),
        Background = background,
        BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(50, 217, 255))
            : new SolidColorBrush(Color.FromArgb(120, 72, 106, 139)),
        BorderThickness = new Thickness(1),
        Foreground = Brushes.White
    };

    private void SchedulePreview(TaskbarTaskItem task, FrameworkElement anchor)
    {
        _pendingPreviewTask = task;
        _pendingPreviewAnchor = anchor;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        if (_pendingPreviewTask is null || _pendingPreviewAnchor is null || !_pendingPreviewAnchor.IsMouseOver) return;
        _preview.ShowFor(_pendingPreviewTask, _pendingPreviewAnchor);
    }

    private void CancelPreview(FrameworkElement? anchor = null)
    {
        if (anchor is not null && !ReferenceEquals(anchor, _pendingPreviewAnchor)) return;
        _previewTimer.Stop();
        _pendingPreviewTask = null;
        _pendingPreviewAnchor = null;
        _preview.HidePreview();
    }

    private ImageSource? GetIcon(TaskbarTaskItem task)
    {
        var key = !string.IsNullOrWhiteSpace(task.ProcessPath) ? task.ProcessPath : $"{task.WindowClass}|{task.AppUserModelId}";
        if (_iconCache.TryGetValue(key, out var cached)) return cached;
        if (_iconCache.Count >= 128) _iconCache.Clear();
        var icon = WindowIconReader.TryRead(task.Handle, task.ProcessPath);
        _iconCache[key] = icon;
        return icon;
    }

    private ImageSource? GetPinnedIcon(TaskbarPinnedItem pin)
    {
        var key = "pin:" + pin.Id;
        if (_iconCache.TryGetValue(key, out var cached)) return cached;
        if (_iconCache.Count >= 128) _iconCache.Clear();
        var icon = WindowIconReader.TryRead(0, pin.ExecutablePath);
        _iconCache[key] = icon;
        return icon;
    }

    private void PositionOnMonitor(TaskbarMonitor monitor)
    {
        if (_disposed || _positioning) return;
        if (_handle == 0) _handle = new WindowInteropHelper(this).Handle;
        _positioning = true;
        try
        {
            _lastMonitor = monitor;
            PlacementError = null;
            WorkAreaReserved = false;

            // Auto-hide bars deliberately do not reserve work area, avoiding a
            // permanent desktop inset and a collision with Explorer's own
            // per-monitor auto-hide AppBar slot.
            if (!_reservationDecision.Reserve)
                _appBar.Remove();

            var placement = WindowsMonitorPlacement.Resolve(monitor);
            var nativeHeight = Math.Max(36, (int)Math.Round(_preferences.Height * placement.ScaleY));
            var visible = new SyncWallpaper.Core.Int32Rect(
                placement.NativeWorkArea.Left,
                placement.NativeWorkArea.Top + placement.NativeWorkArea.Height - nativeHeight,
                placement.NativeWorkArea.Width,
                nativeHeight);

            if (_reservationDecision.Reserve)
            {
                var reservation = _appBar.Reserve(_handle, placement.NativeMonitorArea, nativeHeight);
                if (reservation.Success)
                {
                    visible = reservation.Bounds;
                    WorkAreaReserved = true;
                }
                else
                    PlacementError = reservation.Error ?? "AppBar 工作区预留失败，已回退为覆盖模式。";
            }
            else if (!string.IsNullOrWhiteSpace(_reservationDecision.FallbackReason))
                PlacementError = _reservationDecision.FallbackReason;

            _lastPlacement = placement;
            var reveal = Math.Max(1, (int)Math.Round(_preferences.RevealThickness * placement.ScaleY));
            _edgePositions = TaskbarEdgePositionCalculator.Calculate(visible, reveal);
            var logicalVisible = placement.ToLogical(visible);
            Width = logicalVisible.Width;
            Height = logicalVisible.Height;
            ApplyEdgePosition(_preferences.AutoHide && _autoHideState.IsHidden);
        }
        finally { _positioning = false; }
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        if (_disposed || !_preferences.AutoHide || _edgePositions is null || !GetCursorPos(out var cursor)) return;
        var visible = _edgePositions.Visible;
        var reveal = Math.Max(1, (int)Math.Round(_preferences.RevealThickness * (_lastPlacement?.ScaleY ?? 1d)));
        var inside = TaskbarEdgePositionCalculator.Contains(visible, cursor.X, cursor.Y);
        var revealRequested = IsMouseOver || TaskbarEdgePositionCalculator.IsInRevealZone(visible, reveal, cursor.X, cursor.Y);
        var keepOpen = _openMenus > 0 || _preview.IsPreviewVisible;
        var action = _autoHideState.Update(
            revealRequested,
            inside,
            keepOpen,
            DateTime.UtcNow,
            TimeSpan.FromMilliseconds(_preferences.HideDelayMilliseconds));
        if (action == TaskbarAutoHideAction.Show) ApplyEdgePosition(hidden: false);
        else if (action == TaskbarAutoHideAction.Hide)
        {
            CancelPreview();
            ApplyEdgePosition(hidden: true);
        }
    }

    private void ApplyEdgePosition(bool hidden)
    {
        if (_handle == 0 || _edgePositions is null || _lastPlacement is null) return;
        var bounds = hidden ? _edgePositions.Hidden : _edgePositions.Visible;
        SetWindowPos(_handle, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
        LastBounds = _lastPlacement.ToLogical(bounds);
        _stateChanged();
    }

    private nint WindowProcedure(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_disposed && _appBar.IsPositionChangedMessage(message, wParam)
            && Interlocked.Exchange(ref _positionRefreshPending, 1) == 0)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _positionRefreshPending, 0);
                if (!_disposed && _lastMonitor is not null) PositionOnMonitor(_lastMonitor);
            }, DispatcherPriority.Background);
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoHideTimer.Stop();
        _autoHideTimer.Tick -= AutoHideTimer_Tick;
        _previewTimer.Stop();
        _previewTimer.Tick -= PreviewTimer_Tick;
        _preview.Dispose();
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        _appBar.Dispose();
        _iconCache.Clear();
        Close();
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
}

internal static class WindowIconReader
{
    public static ImageSource? TryRead(nint window, string processPath)
    {
        nint copied = 0;
        try
        {
            var borrowed = window == 0 ? 0 : ReadWindowIcon(window);
            if (borrowed != 0) copied = CopyIcon(borrowed);
            if (copied == 0 && !string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                var info = new ShellFileInfo();
                if (SHGetFileInfo(processPath, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiSmallIcon) != 0)
                    copied = info.Icon;
            }
            if (copied == 0) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(copied, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(20, 20));
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally { if (copied != 0) DestroyIcon(copied); }
    }

    private static nint ReadWindowIcon(nint window)
    {
        foreach (var size in new nint[] { IconSmall2, IconSmall, IconBig })
        {
            if (SendMessageTimeout(window, WmGetIcon, size, 0, SmtoAbortIfHung, 100, out var icon) != 0 && icon != 0) return icon;
        }
        var classIcon = GetClassLongPtr(window, GclpHiconSm);
        return classIcon != 0 ? classIcon : GetClassLongPtr(window, GclpHicon);
    }

    private const uint WmGetIcon = 0x007F;
    private static readonly nint IconSmall = new(0);
    private static readonly nint IconBig = new(1);
    private static readonly nint IconSmall2 = new(2);
    private const uint SmtoAbortIfHung = 0x0002;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
}
