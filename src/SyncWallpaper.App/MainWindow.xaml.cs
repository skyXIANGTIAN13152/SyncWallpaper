using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Microsoft.Win32;
using SyncWallpaper.Core;
using SyncWallpaper.Update.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

public partial class MainWindow : Window
{
    private readonly AppRuntime _runtime;
    private readonly DisplayIdentificationOverlayService _identityOverlay = new();
    private readonly DisplayRoleAssignmentService _roleAssignment = new();
    private bool _refreshing;
    private volatile bool _refreshDirty = true;
    private volatile bool _windowVisible;
    private int _refreshRequestQueued;
    private string _libraryStatusText = "档案库已加载；在资源管理器中增删文件后请点击刷新。";
    private readonly WallpaperPreviewCache _previews = new();
    private IReadOnlyDictionary<string, string> _currentWallpapers = new Dictionary<string, string>();
    private IReadOnlyList<DesktopMonitorPreview> _desktopPreviews = Array.Empty<DesktopMonitorPreview>();
    private string? _wallpaperStateKey;
    private string _activePage = "Overview";

    public bool AllowExit { get; set; }

    public MainWindow(AppRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        UpdateNavigation("Overview");
        _runtime.StateChanged += Runtime_StateChanged;
        Loaded += (_, _) =>
        {
            _windowVisible = true;
            Refresh();
        };
        IsVisibleChanged += (_, _) =>
        {
            _windowVisible = IsVisible;
            if (_windowVisible)
            {
                _wallpaperStateKey = null;
                _refreshDirty = true;
                QueueRefresh();
            }
        };
    }

    private void Runtime_StateChanged(object? sender, EventArgs e)
    {
        _refreshDirty = true;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!_windowVisible || Interlocked.Exchange(ref _refreshRequestQueued, 1) != 0) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _refreshRequestQueued, 0);
            if (_windowVisible && _refreshDirty) Refresh();
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _refreshing = true;
        _refreshDirty = false;
        var selectedId = (ProfilesList.SelectedItem as ProfileItem)?.Id ?? _runtime.Settings.EditingProfileId;

        StatusBadge.Text = _runtime.StatusText;
        var statusColor = _runtime.TrayVisualState switch
        {
            TrayIconState.Normal => "Success",
            TrayIconState.Generic => "Purple",
            TrayIconState.Error => "Danger",
            TrayIconState.Paused => "Warning",
            TrayIconState.Unmatched => "TextSecondary",
            _ => "Cyan"
        };
        StatusBadge.SetResourceReference(TextBlock.ForegroundProperty, statusColor);
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, statusColor);
        OverviewStatus.SetResourceReference(TextBlock.ForegroundProperty, statusColor);
        OverviewStatus.Text = _runtime.StatusText;
        var connected = _runtime.Monitors.Where(x => x.IsConnected).ToArray();
        MonitorCount.Text = $"{connected.Count(x => x.Width >= x.Height)} 横屏 / {connected.Count(x => x.Width < x.Height)} 竖屏";
        DesktopSummary.Text = connected.Length == 0 ? "尚未检测到活动显示器。" : $"{connected.Length} 块显示器已连接 · {_runtime.StatusText}";
        MatchSymbol.Text = _runtime.TrayVisualState switch
        {
            TrayIconState.Normal => "✓", TrayIconState.Generic => "◇", TrayIconState.Error => "!", TrayIconState.Paused => "Ⅱ", _ => "—"
        };
        SidebarAutoMatchButton.Content = _runtime.Settings.AutoMatchEnabled ? "暂停自动匹配" : "恢复自动匹配";
        ProfileName.Text = _runtime.LastMatch?.Profile?.Name ?? "未匹配";
        ConfidenceLabel.Text = _runtime.LastMatch?.IsGeneric == true ? "通用组合要求" : "匹配置信度";
        Confidence.Text = _runtime.LastMatch?.Profile is { IsGeneric: true } genericProfile
            ? $"{GenericProfileMatcher.GetRequiredMonitorCount(genericProfile.Roles, GenericMonitorOrientation.Landscape)} 横 + {GenericProfileMatcher.GetRequiredMonitorCount(genericProfile.Roles, GenericMonitorOrientation.Portrait)} 竖"
            : _runtime.LastMatch is null ? "—" : $"{_runtime.LastMatch.Confidence}%";
        Confidence.ToolTip = _runtime.LastMatch?.IsGeneric == true ? "横屏和竖屏数量必须分别完全一致，再按方向与桌面位置分配壁纸。" : null;
        LastMessage.Text = _runtime.LastMessage;
        WallpaperTransactionText.Text = $"{TransactionLabel(_runtime.LastWallpaperTransaction.State)} · {_runtime.LastWallpaperTransaction.DurationMilliseconds:0} ms · {_runtime.LastWallpaperTransaction.Message}";

        RefreshDesktopPreviews();
        var monitorCards = _runtime.Monitors.Select(ToMonitorCard).ToList();
        TopologyView.Monitors = _desktopPreviews;
        TopologyView.ToolTip = string.Join(Environment.NewLine, _desktopPreviews.Select(x => $"{x.SlotLabel} · {x.Monitor.DisplayLabel} · {x.Monitor.Width} × {x.Monitor.Height} · {x.WallpaperName}"));
        MonitorDetails.ItemsSource = monitorCards;
        MatchEvidence.Text = _runtime.LastMatch is null
            ? "尚未执行组合匹配。"
            : string.Join(Environment.NewLine, _runtime.LastMatch.Evidence.Select(x => _runtime.LastMatch.IsGeneric
                ? $"• {x.Role} ← {x.Monitor}：{x.Reason}"
                : $"• {x.Role} ← {x.Monitor}：{x.Reason}（得分 {x.Score}）"))
              + Environment.NewLine + _runtime.LastMatch.Message;

        if (_activePage == "Library") RefreshLibraryItems();
        LibraryStatusText.Text = _libraryStatusText;

        var profiles = _runtime.Profiles.Profiles
            .Where(ProfilePassesFilter)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.ModifiedAt)
            .Select(ToProfileItem)
            .ToList();
        ProfilesList.ItemsSource = profiles;
        ProfilesList.SelectedItem = profiles.FirstOrDefault(x => x.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        ProfileStatusText.Text = _runtime.Profiles.Profiles.Count == 0
            ? "尚未保存壁纸组合。"
            : $"显示 {profiles.Count} / {_runtime.Profiles.Profiles.Count} 套组合 · 通用组合按各方向最高编号计算所需屏数。";
        if (!ProfileNameInput.IsKeyboardFocusWithin && ProfilesList.SelectedItem is ProfileItem selected)
            ProfileNameInput.Text = selected.Name;

        LogsList.ItemsSource = _runtime.RecentLogs.Select(x => new LogItem(x.Timestamp.ToString("HH:mm:ss"), x.Type.ToString(), x.Message)).ToList();

        AutoMatchCheck.IsChecked = _runtime.Settings.AutoMatchEnabled;
        StartupCheck.IsChecked = _runtime.Settings.StartWithWindows;
        LowPerformanceCheck.IsChecked = _runtime.Settings.LowPerformanceMode;
        DataPathText.Text = _runtime.Paths.Root;
        var theme = AppThemeCatalog.Get(_runtime.Settings.UiTheme);
        MysteryTheme.IsChecked = theme.Id == "Mystery";
        AuroraTheme.IsChecked = theme.Id == "Aurora";
        GraphiteTheme.IsChecked = theme.Id == "Graphite";
        DaylightTheme.IsChecked = theme.Id == "Daylight";
        ThemeHint.Text = $"已选择 {theme.Name} · 下次启动继续使用此主题";

        CurrentVersionText.Text = _runtime.CurrentVersion;
        AboutVersionText.Text = "版本 " + _runtime.CurrentVersion;
        UpdateChannelCombo.SelectedIndex = string.Equals(_runtime.Settings.UpdateChannel, nameof(UpdateChannel.Beta), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        AutomaticUpdateCheck.IsChecked = _runtime.Settings.AutomaticUpdateCheckEnabled;
        LastUpdateCheckText.Text = _runtime.Settings.LastUpdateAttemptUtc is { } attempted
            ? $"上次检查：{attempted.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "上次检查：从未检查";
        var update = _runtime.LastUpdateResult;
        UpdateStatusText.Text = update?.UserMessage ?? (_runtime.Settings.AutomaticUpdateCheckEnabled
            ? "已开启自动检查，发现新版后会询问是否更新。" : "自动检查已关闭，你仍可以手动检查更新。");
        UpdateNotesText.Text = update?.ReleaseNotes is { Length: > 0 } notes ? "更新内容：\n" + notes : string.Empty;
        OpenReleaseButton.IsEnabled = update?.ReleasePageUrl is { } releaseUrl
            && ReleaseUrlValidator.IsAllowed(releaseUrl, ProjectLinks.RepositorySettings);
        _refreshing = false;
    }

    private MonitorCard ToMonitorCard(MonitorIdentity monitor)
    {
        var binding = _runtime.GetBindingForMonitor(monitor);
        var role = binding?.DisplayName ?? (monitor.IsInternal ? "笔记本本体" : "未分配角色");
        var preview = _desktopPreviews.FirstOrDefault(x => ReferenceEquals(x.Monitor, monitor));
        var wallpaper = preview?.Image is not null ? "当前桌面壁纸" : "当前壁纸预览不可用";
        var refresh = monitor.RefreshRateDenominator == 0
            ? "未知"
            : $"{(double)monitor.RefreshRateNumerator / monitor.RefreshRateDenominator:0.##} Hz";
        var displayInfo = $"{monitor.Width} × {monitor.Height}（原生 {monitor.NativeWidth} × {monitor.NativeHeight}） · {refresh} · {FormatOrientation(monitor)} · 桌面坐标 {monitor.DesktopX},{monitor.DesktopY}";
        var colorInfo = $"DPI {monitor.Dpi}（{monitor.DpiScale * 100:0}%） · HDR {FormatHdr(monitor.HdrEnabled)} · 色彩 {Blank(monitor.ColorMode)} · {(monitor.IsPrimary ? "主显示器" : "非主显示器")} · {(monitor.IsInternal ? "内置屏幕" : "外接屏幕")}";
        var identityInfo = $"身份来源 {monitor.StableIdSource} · 稳定身份 {Blank(monitor.StableId)}\n"
            + $"EDID 厂商 {Blank(monitor.EdidManufactureId, monitor.ManufacturerName)} · 产品代码 {Blank(monitor.EdidProductCodeId, monitor.ProductCodeId)} · 序列号 {Blank(monitor.EdidSerialNumber)}\n"
            + $"monitorDevicePath {Blank(monitor.MonitorDevicePath)}\n"
            + $"InstanceName {Blank(monitor.InstanceName)} · Adapter {Blank(monitor.AdapterId)} · Source {monitor.SourceId} · Target {monitor.TargetId} · 接口 {OutputTechnologyLabel(monitor.OutputTechnology)}（{monitor.OutputTechnology}） · connectorInstance {monitor.ConnectorInstance}";
        return new MonitorCard(role, monitor.DisplayLabel, $"{monitor.Width} × {monitor.Height} · {FormatOrientation(monitor)}", wallpaper, displayInfo, colorInfo, identityInfo, preview?.SlotLabel ?? "未连接", preview?.Image);
    }

    private ProfileItem ToProfileItem(WallpaperProfile profile)
    {
        var requiredLandscape = profile.IsGeneric ? GenericProfileMatcher.GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Landscape) : 0;
        var requiredPortrait = profile.IsGeneric ? GenericProfileMatcher.GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Portrait) : 0;
        var countsMatch = !profile.IsGeneric
            || (requiredLandscape == _runtime.Monitors.Count(x => x.IsConnected && x.Width >= x.Height)
                && requiredPortrait == _runtime.Monitors.Count(x => x.IsConnected && x.Width < x.Height));
        var matched = _runtime.LastMatch?.Profile?.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true
            && _runtime.LastMatch.Status is MatchStatus.Exact or MatchStatus.Compatible or MatchStatus.Generic
            && countsMatch;
        var bindings = profile.Roles.Count == 0
            ? profile.IsGeneric ? "空白通用组合：添加横屏或竖屏，并选择壁纸" : "空白组合：尚未添加显示器角色"
            : string.Join(" · ", profile.Roles.Select(role => $"{role.DisplayName} → {WallpaperName(role)}"));
        var details = profile.IsGeneric
            ? $"要求 {requiredLandscape} 块横屏 + {requiredPortrait} 块竖屏，数量需完全一致 · 优先级 {profile.Priority} · 修改于 {profile.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"{profile.Roles.Count} 台显示器 · 优先级 {profile.Priority} · 修改于 {profile.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        var hasWallpaper = profile.Roles.Any(x => !string.IsNullOrWhiteSpace(x.WallpaperAssetId) || !string.IsNullOrWhiteSpace(x.WallpaperPath));
        var matchText = !profile.Enabled ? "已停用" : matched ? profile.IsGeneric ? "通用匹配" : "已匹配"
            : profile.IsGeneric ? !hasWallpaper ? "未配置" : countsMatch ? "后备待用" : "数量不符" : "未匹配";
        var matchColor = !profile.Enabled ? "TextSecondary" : matched ? profile.IsGeneric ? "Purple" : "Success"
            : profile.IsGeneric ? hasWallpaper && !countsMatch ? "Warning" : "TextSecondary" : "Danger";
        return new ProfileItem(profile.Id, profile.Name, profile.IsGeneric ? "通用组合" : "特定组合", bindings, details, matchText, (System.Windows.Media.Brush)FindResource(matchColor));
    }

    private string WallpaperName(MonitorRoleBinding? binding)
    {
        if (binding is null) return "未配置";
        var asset = _runtime.Library.Assets.FirstOrDefault(x => x.Id.Equals(binding.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
        if (asset is not null) return asset.IsMissing ? $"{asset.DisplayName}（文件不存在）" : asset.DisplayName;
        if (!string.IsNullOrWhiteSpace(binding.WallpaperPath)) return File.Exists(binding.WallpaperPath) ? Path.GetFileNameWithoutExtension(binding.WallpaperPath) : "文件不存在";
        return "未配置";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.Button)?.Tag?.ToString() ?? "Overview";
        _activePage = tag;
        foreach (var panel in new FrameworkElement[] { OverviewPanel, MonitorsPanel, LibraryPanel, ProfilesPanel, LogsPanel, SettingsPanel, AboutPanel })
            panel.Visibility = Visibility.Collapsed;
        FrameworkElement selectedPanel = tag switch
        {
            "Monitors" => MonitorsPanel,
            "Library" => LibraryPanel,
            "Profiles" => ProfilesPanel,
            "Logs" => LogsPanel,
            "Settings" => SettingsPanel,
            "About" => AboutPanel,
            _ => OverviewPanel
        };
        selectedPanel.Visibility = Visibility.Visible;
        UpdateNavigation(tag);
        if (tag == "Library") RefreshLibraryItems();
        ScrollViewer? contentScroll = tag switch
        {
            "Overview" => OverviewScroll,
            "Monitors" => MonitorsScroll,
            "Library" => LibraryScroll,
            "Settings" => SettingsScroll,
            "About" => AboutScroll,
            _ => null
        };
        contentScroll?.ScrollToTop();
        if (tag == "Logs" && LogsList.Items.Count > 0) LogsList.ScrollIntoView(LogsList.Items[0]);
    }

    private void UpdateNavigation(string tag)
    {
        foreach (var button in NavigationPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            if (string.Equals(button.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SelectionBackground");
                button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SelectionText");
                button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Cyan");
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                button.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
                button.ClearValue(System.Windows.Controls.Control.FontWeightProperty);
            }
        }
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _runtime is null || sender is not System.Windows.Controls.RadioButton { Tag: string id }) return;
        ThemeManager.Apply(id);
        _runtime.SetUiTheme(id);
        Refresh();
    }

    private async void Detect_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.DetectAsync();
        _wallpaperStateKey = null;
        Refresh();
    }

    private async void Reapply_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.ReapplyAsync();
        _wallpaperStateKey = null;
        Refresh();
    }

    private async void Identify_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.Monitors.Count == 0)
        {
            ShowMessage("当前没有可识别的活动显示器。", "显示器识别");
            return;
        }
        var marks = await _identityOverlay.ShowAsync(_runtime.Monitors, TimeSpan.FromSeconds(10));
        if (marks.Count == 0)
        {
            ShowMessage("识别界面未能打开。", "显示器识别");
            return;
        }
        var assignments = await _roleAssignment.ShowAsync(marks, initialAssignments: _runtime.GetManualDisplayAssignments());
        if (assignments.Count > 0)
        {
            try { _runtime.ApplyManualDisplayAssignments(assignments); }
            catch (InvalidOperationException ex)
            {
                ShowMessage(ex.Message, "保存显示器绑定", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Refresh();
        }
    }

    private void ImportWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入壁纸",
            Filter = "支持的图片|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var file in dialog.FileNames) _runtime.ImportWallpaper(file);
            _libraryStatusText = $"导入完成：{dialog.FileNames.Length} 个文件。";
            Refresh();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "导入壁纸", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenWallpapers_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Wallpapers);

    private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        var result = _runtime.RefreshLibrary();
        _libraryStatusText = result.MissingCount == 0 && result.RecoveredCount == 0
            ? $"刷新完成：{_runtime.Library.Assets.Count(x => !x.IsMissing)} 个壁纸可用。"
            : $"刷新完成：隐藏 {result.MissingCount} 个已删除文件，恢复 {result.RecoveredCount} 个文件。";
        Refresh();
    }

    private void SaveCurrentProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = _runtime.SaveCurrentWallpaperProfile(ProfileNameInput.Text);
            ProfilesList.SelectedItem = (ProfilesList.ItemsSource as IEnumerable<ProfileItem>)?.FirstOrDefault(x => x.Id == profile.Id);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "保存壁纸组合", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CreateBlankProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.CreateBlankWallpaperProfile(ProfileNameInput.Text);
        var dialog = new WallpaperProfileEditorWindow(_runtime, profile) { Owner = this };
        dialog.ShowDialog();
        Refresh();
    }

    private void CreateGenericProfile_Click(object sender, RoutedEventArgs e)
    {
        var previousSelection = _runtime.Settings.EditingProfileId;
        var profile = _runtime.CreateBlankWallpaperProfile("通用组合", WallpaperProfileMatchMode.Generic);
        var dialog = new WallpaperProfileEditorWindow(_runtime, profile) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            _runtime.DeleteWallpaperProfile(profile.Id);
            if (!string.IsNullOrEmpty(previousSelection) && _runtime.FindWallpaperProfile(previousSelection) is not null)
                _runtime.SelectWallpaperProfileForEditing(previousSelection);
        }
        Refresh();
        ProfilesList.SelectedItem = (ProfilesList.ItemsSource as IEnumerable<ProfileItem>)?.FirstOrDefault(x => x.Id == _runtime.Settings.EditingProfileId);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            ShowMessage("请先选择要编辑的壁纸组合。", "壁纸组合");
            return;
        }
        var profile = _runtime.FindWallpaperProfile(item.Id);
        if (profile is null) return;
        new WallpaperProfileEditorWindow(_runtime, profile) { Owner = this }.ShowDialog();
        Refresh();
    }

    private async void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            ShowMessage("请先选择要应用的壁纸组合。", "壁纸组合");
            return;
        }
        if (!await _runtime.ApplyWallpaperProfileAsync(item.Id))
            ShowMessage(_runtime.LastMessage, "壁纸组合", MessageBoxButton.OK, MessageBoxImage.Warning);
        Refresh();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            ShowMessage("请先选择要重命名的壁纸组合。", "壁纸组合");
            return;
        }
        try
        {
            _runtime.RenameWallpaperProfile(item.Id, ProfileNameInput.Text);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "重命名壁纸组合", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            ShowMessage("请先选择要删除的壁纸组合。", "壁纸组合");
            return;
        }
        if (ShowMessage($"确定删除壁纸组合“{item.Name}”？\n壁纸图片不会被删除。", "删除壁纸组合", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _runtime.DeleteWallpaperProfile(item.Id);
        Refresh();
    }

    private void NestedControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => MouseWheelRouting.ForwardToAncestorScrollViewer(sender, e);

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _runtime is null || ProfilesList.SelectedItem is not ProfileItem item) return;
        _runtime.SelectWallpaperProfileForEditing(item.Id);
        ProfileNameInput.Text = item.Name;
    }

    private void ExportDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _runtime.ExportWallpaperDiagnostic();
            ShowMessage("只读诊断已保存到：\n" + path, "壁纸诊断", MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "壁纸诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Logs);
    private void AutoMatch_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetAutoMatch(AutoMatchCheck.IsChecked == true); }
    private void Startup_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetStartup(StartupCheck.IsChecked == true); }
    private void LowPerformance_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetLowPerformanceMode(LowPerformanceCheck.IsChecked == true); }

    private void UpdateChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _runtime is null || UpdateChannelCombo.SelectedItem is not ComboBoxItem item) return;
        _runtime.Settings.UpdateChannel = item.Tag?.ToString() ?? nameof(UpdateChannel.Stable);
        _runtime.SaveUpdateSettings();
    }

    private void AutomaticUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _runtime.Settings.AutomaticUpdateCheckEnabled = AutomaticUpdateCheck.IsChecked == true;
        _runtime.SaveUpdateSettings();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "检查中…";
        try { await _runtime.CheckForUpdatesAsync(true); }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "检查更新";
            Refresh();
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        var url = _runtime.LastUpdateResult?.ReleasePageUrl;
        if (!_runtime.OpenReleasePage(url))
            ShowMessage("Release 地址不可用。", "检查更新");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { Maximize_Click(sender, e); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshDesktopPreviews()
    {
        var active = _runtime.Monitors.Where(x => x.IsConnected).ToArray();
        var transaction = _runtime.LastWallpaperTransaction;
        var stateKey = string.Join(";", active.Select(x => $"{x.MonitorDevicePath}|{x.DesktopX},{x.DesktopY},{x.Width},{x.Height}"))
            + $"|{transaction.Generation}|{transaction.State}|{transaction.CompletedAtUtc:O}";
        if (!string.Equals(_wallpaperStateKey, stateKey, StringComparison.Ordinal))
        {
            _currentWallpapers = WallpaperPreviewCache.ReadCurrentWallpapers(active);
            _wallpaperStateKey = stateKey;
        }
        var previews = new List<DesktopMonitorPreview>();
        foreach (var group in active.GroupBy(x => x.Width >= x.Height))
        {
            var index = 0;
            foreach (var monitor in group.OrderBy(x => x.DesktopX).ThenBy(x => x.DesktopY).ThenBy(x => x.MonitorDevicePath, StringComparer.Ordinal))
            {
                index++;
                _currentWallpapers.TryGetValue(monitor.MonitorDevicePath, out var path);
                var image = _previews.Load(path, _runtime.Settings.LowPerformanceMode ? 192 : 384);
                previews.Add(new DesktopMonitorPreview(monitor, $"{(group.Key ? "横屏" : "竖屏")} {index}", image,
                    string.IsNullOrWhiteSpace(path) ? "预览不可用" : Path.GetFileName(path)));
            }
        }
        _desktopPreviews = previews;
    }

    private void RefreshLibraryItems()
    {
        if (_runtime is null || LibraryItems is null) return;
        var search = LibrarySearchText.Text.Trim();
        var items = _runtime.Library.Assets.Where(x => !x.IsMissing && (search.Length == 0 || x.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(asset =>
            {
                var path = asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
                    ? asset.ExternalPath ?? string.Empty
                    : Path.Combine(_runtime.Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                return new LibraryItem(asset.DisplayName, $"{asset.Width} × {asset.Height} · {asset.Format.ToUpperInvariant()} · {FormatBytes(asset.FileSize)}",
                    asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase) ? path : asset.ManagedRelativePath, path,
                    _previews.Load(path, _runtime.Settings.LowPerformanceMode ? 192 : 384));
            }).ToList();
        LibraryItems.ItemsSource = items;
        LibraryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryEmptyText.Text = search.Length == 0 ? "档案库里还没有壁纸。点击“导入壁纸”添加第一张图片。" : "没有找到这个名称的壁纸。";
    }

    private void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_runtime is not null && !_refreshing && _activePage == "Library") RefreshLibraryItems();
    }

    private bool ProfilePassesFilter(WallpaperProfile profile) => (ProfileFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "specific" => !profile.IsGeneric,
        "generic" => profile.IsGeneric,
        _ => true
    };

    private void ProfileFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_runtime is not null && !_refreshing) Refresh();
    }

    private void ProfileFilter_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The fixed toolbar has no scrolling ancestor; a closed selector must not change on wheel input.
        if (sender is System.Windows.Controls.ComboBox { IsDropDownOpen: false }) e.Handled = true;
    }

    private void SidebarAutoMatch_Click(object sender, RoutedEventArgs e)
    {
        _runtime.SetAutoMatch(!_runtime.Settings.AutoMatchEnabled);
        Refresh();
    }

    private void EditProfileRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        OpenProfileEditor(id);
    }

    private void EditMatchedProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.LastMatch?.Profile is { } profile) OpenProfileEditor(profile.Id);
        else ShowMessage("当前没有匹配的组合。请到“壁纸组合”选择或新建组合。", "编辑组合");
    }

    private void OpenProfileEditor(string id)
    {
        var profile = _runtime.FindWallpaperProfile(id);
        if (profile is null) return;
        new WallpaperProfileEditorWindow(_runtime, profile) { Owner = this }.ShowDialog();
        Refresh();
    }

    private void PreviewWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryItem item }) return;
        var image = _previews.Load(item.FullPath, _runtime.Settings.LowPerformanceMode ? 960 : 1600);
        if (image is null) { ShowMessage("图片暂时无法读取，请刷新档案库后重试。", "壁纸预览"); return; }
        var (dialog, body, actions) = CreateDialog(item.Name, Math.Min(900, ActualWidth - 70));
        body.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, MaxHeight = Math.Max(220, Math.Min(500, ActualHeight - 220)), Margin = new Thickness(0, 4, 0, 16) });
        var metadata = CopyableText(item.Details + Environment.NewLine + item.FullPath, 12);
        metadata.PreviewMouseWheel += NestedControl_PreviewMouseWheel;
        body.Children.Add(metadata);
        var close = new Button { Content = "关闭预览", IsDefault = true, IsCancel = true, MinWidth = 96 };
        close.Click += (_, _) => dialog.Close();
        actions.Children.Add(close);
        dialog.ShowDialog();
    }

    private MessageBoxResult ShowMessage(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        var (dialog, body, actions) = CreateDialog(title);
        var text = CopyableText(message, 14);
        text.MaxHeight = Math.Max(180, Math.Min(380, ActualHeight - 230));
        text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        body.Children.Add(text);
        var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        if (buttons == MessageBoxButton.YesNo)
        {
            var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, MinWidth = 84 };
            cancel.Click += (_, _) => dialog.Close();
            actions.Children.Add(cancel);
            var confirm = new Button { Content = "确认删除", MinWidth = 96 };
            confirm.SetResourceReference(StyleProperty, "DangerButton");
            confirm.Click += (_, _) => { result = MessageBoxResult.Yes; dialog.DialogResult = true; };
            actions.Children.Add(confirm);
        }
        else
        {
            var confirm = new Button { Content = "知道了", IsDefault = true, IsCancel = true, MinWidth = 96 };
            confirm.SetResourceReference(StyleProperty, "PrimaryButton");
            confirm.Click += (_, _) => dialog.Close();
            actions.Children.Add(confirm);
        }
        dialog.ShowDialog();
        return result;
    }

    private (Window Window, StackPanel Body, StackPanel Actions) CreateDialog(string title, double width = 540)
    {
        var dialog = new Window
        {
            Owner = this, Title = title, Width = Math.Max(380, width), SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false, FontFamily = FontFamily, FontSize = 14
        };
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        var frame = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(26) };
        frame.SetResourceReference(Border.BackgroundProperty, "WindowSurface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Border");
        var layers = new Grid();
        layers.Children.Add(new MysteryBackdrop { IsHitTestVisible = false, Opacity = .5 });
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new BrandGlyph { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left });
        var titleText = CopyableText(title, 20);
        titleText.FontWeight = FontWeights.Medium;
        Grid.SetColumn(titleText, 1);
        heading.Children.Add(titleText);
        var dismiss = new Button { Content = "×", Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(12, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "关闭" };
        dismiss.Click += (_, _) => dialog.Close();
        Grid.SetColumn(dismiss, 2);
        heading.Children.Add(dismiss);
        heading.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Grid) dialog.DragMove(); };
        content.Children.Add(heading);
        var body = new StackPanel();
        var bodyScroll = new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent,
            MaxHeight = Math.Max(120, dialog.MaxHeight - 190), Padding = new Thickness(0, 0, 6, 0)
        };
        Grid.SetRow(bodyScroll, 1);
        content.Children.Add(bodyScroll);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        Grid.SetRow(actions, 2);
        content.Children.Add(actions);
        layers.Children.Add(content);
        frame.Child = layers;
        dialog.Content = frame;
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        return (dialog, body, actions);
    }

    private static TextBox CopyableText(string text, double size)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = size, Padding = new Thickness(0), CaretBrush = Brushes.Transparent };
        box.SetResourceReference(ForegroundProperty, "TextPrimary");
        return box;
    }

    private static string FormatOrientation(MonitorIdentity monitor)
    {
        var rotation = monitor.Rotation is >= 1 and <= 4 ? monitor.Rotation : 1;
        return rotation switch
        {
            1 => "横向 · 未翻转",
            2 => "纵向 · 未翻转",
            3 => "横向 · 已翻转",
            4 => "纵向 · 已翻转",
            _ => "横向 · 未翻转"
        };
    }

    private static string FormatHdr(bool? value) => value switch { true => "已启用", false => "未启用", _ => "不可用" };
    private static string Blank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "不可用";
    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:0.##} MB" : $"{value / 1024d:0} KB";
    private static string TransactionLabel(WallpaperTransactionState value) => value switch
    {
        WallpaperTransactionState.Completed => "已完成",
        WallpaperTransactionState.Failed => "失败",
        WallpaperTransactionState.Cancelled => "已取消",
        WallpaperTransactionState.RollbackFailed => "回滚失败",
        WallpaperTransactionState.RollingBack => "正在回滚",
        WallpaperTransactionState.Applying => "正在应用",
        WallpaperTransactionState.Verifying => "正在验证",
        WallpaperTransactionState.Retrying => "正在重试",
        _ => "准备中"
    };

    private static string OutputTechnologyLabel(uint value) => value switch
    {
        0 => "VGA/HD15",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        10 => "DisplayPort",
        11 => "内置 DisplayPort/eDP",
        15 => "Miracast",
        16 => "间接有线（常见 USB-C/扩展坞）",
        17 => "间接虚拟",
        0x80000000 => "内部接口",
        0xFFFFFFFF => "其他",
        _ => "其他"
    };

    private sealed record MonitorCard(string Role, string Name, string Geometry, string Wallpaper, string DisplayInfo, string ColorInfo, string IdentityInfo, string SlotLabel, ImageSource? Preview);
    private sealed record LibraryItem(string Name, string Details, string Path, string FullPath, ImageSource? Preview);
    private sealed record LogItem(string Time, string Category, string Message);
    private sealed record ProfileItem(string Id, string Name, string KindText, string Bindings, string Details, string MatchText, System.Windows.Media.Brush MatchBrush);
}
