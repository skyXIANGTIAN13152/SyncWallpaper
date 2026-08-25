using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    public bool AllowExit { get; set; }

    public MainWindow(AppRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        _runtime.StateChanged += Runtime_StateChanged;
        Loaded += (_, _) =>
        {
            _windowVisible = true;
            Refresh();
        };
        IsVisibleChanged += (_, _) =>
        {
            _windowVisible = IsVisible;
            if (_windowVisible && _refreshDirty) QueueRefresh();
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
        OverviewStatus.Text = _runtime.StatusText;
        MonitorCount.Text = _runtime.Monitors.Count.ToString();
        ProfileName.Text = _runtime.LastMatch?.Profile?.Name ?? "未匹配";
        Confidence.Text = _runtime.LastMatch is null ? "—" : $"{_runtime.LastMatch.Confidence}%";
        LastMessage.Text = _runtime.LastMessage;
        WallpaperTransactionText.Text = $"{TransactionLabel(_runtime.LastWallpaperTransaction.State)} · {_runtime.LastWallpaperTransaction.DurationMilliseconds:0} ms · {_runtime.LastWallpaperTransaction.Message}";

        var monitorCards = _runtime.Monitors.Select(ToMonitorCard).ToList();
        MonitorItems.ItemsSource = monitorCards;
        MonitorDetails.ItemsSource = monitorCards;
        MatchEvidence.Text = _runtime.LastMatch is null
            ? "尚未执行组合匹配。"
            : string.Join(Environment.NewLine, _runtime.LastMatch.Evidence.Select(x => $"• {x.Role} ← {x.Monitor}：{x.Reason}（得分 {x.Score}）"))
              + Environment.NewLine + _runtime.LastMatch.Message;

        LibraryItems.ItemsSource = _runtime.Library.Assets
            .Where(x => !x.IsMissing)
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new LibraryItem(x.DisplayName, $"{x.Width} × {x.Height} · {x.Format.ToUpperInvariant()} · {FormatBytes(x.FileSize)}", x.ManagedRelativePath))
            .ToList();
        LibraryStatusText.Text = _libraryStatusText;

        var profiles = _runtime.Profiles.Profiles
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.ModifiedAt)
            .Select(ToProfileItem)
            .ToList();
        ProfilesList.ItemsSource = profiles;
        ProfilesList.SelectedItem = profiles.FirstOrDefault(x => x.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        ProfileStatusText.Text = profiles.Count == 0
            ? "尚未保存壁纸组合。"
            : $"已保存 {profiles.Count} 套组合；绿色表示当前拓扑已匹配，红色表示未匹配。";
        if (!ProfileNameInput.IsKeyboardFocusWithin && ProfilesList.SelectedItem is ProfileItem selected)
            ProfileNameInput.Text = selected.Name;

        LogsList.ItemsSource = _runtime.RecentLogs.Select(x => $"{x.Timestamp:HH:mm:ss}  [{x.Type}]  {x.Message}").ToList();

        AutoMatchCheck.IsChecked = _runtime.Settings.AutoMatchEnabled;
        StartupCheck.IsChecked = _runtime.Settings.StartWithWindows;
        LowPerformanceCheck.IsChecked = _runtime.Settings.LowPerformanceMode;
        DataPathText.Text = _runtime.Paths.Root;
        NebulaGlowTop.Visibility = NebulaGlowBottom.Visibility = _runtime.Settings.LowPerformanceMode ? Visibility.Collapsed : Visibility.Visible;

        CurrentVersionText.Text = _runtime.CurrentVersion;
        AboutVersionText.Text = "版本 " + _runtime.CurrentVersion;
        UpdateChannelCombo.SelectedIndex = string.Equals(_runtime.Settings.UpdateChannel, nameof(UpdateChannel.Beta), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        AutomaticUpdateCheck.IsChecked = _runtime.Settings.AutomaticUpdateCheckEnabled;
        LastUpdateCheckText.Text = _runtime.Settings.LastUpdateAttemptUtc is { } attempted
            ? $"上次检查：{attempted.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "上次检查：从未检查";
        var update = _runtime.LastUpdateResult;
        UpdateStatusText.Text = update?.UserMessage ?? "默认不联网。";
        UpdateNotesText.Text = update?.ReleaseNotes is { Length: > 0 } notes ? "更新内容：\n" + notes : string.Empty;
        OpenReleaseButton.IsEnabled = update?.ReleasePageUrl is { } releaseUrl
            && ReleaseUrlValidator.IsAllowed(releaseUrl, ProjectLinks.RepositorySettings);
        _refreshing = false;
    }

    private MonitorCard ToMonitorCard(MonitorIdentity monitor)
    {
        var binding = _runtime.GetBindingForMonitor(monitor);
        var role = binding?.DisplayName ?? (monitor.IsInternal ? "笔记本本体" : "未分配角色");
        var wallpaper = WallpaperName(binding);
        var refresh = monitor.RefreshRateDenominator == 0
            ? "未知"
            : $"{(double)monitor.RefreshRateNumerator / monitor.RefreshRateDenominator:0.##} Hz";
        var displayInfo = $"{monitor.Width} × {monitor.Height}（原生 {monitor.NativeWidth} × {monitor.NativeHeight}） · {refresh} · {FormatOrientation(monitor)} · 桌面坐标 {monitor.DesktopX},{monitor.DesktopY}";
        var colorInfo = $"DPI {monitor.Dpi}（{monitor.DpiScale * 100:0}%） · HDR {FormatHdr(monitor.HdrEnabled)} · 色彩 {Blank(monitor.ColorMode)} · {(monitor.IsPrimary ? "主显示器" : "非主显示器")} · {(monitor.IsInternal ? "内置屏幕" : "外接屏幕")}";
        var identityInfo = $"身份来源 {monitor.StableIdSource} · 稳定身份 {Blank(monitor.StableId)}\n"
            + $"EDID 厂商 {Blank(monitor.EdidManufactureId, monitor.ManufacturerName)} · 产品代码 {Blank(monitor.EdidProductCodeId, monitor.ProductCodeId)} · 序列号 {Blank(monitor.EdidSerialNumber)}\n"
            + $"monitorDevicePath {Blank(monitor.MonitorDevicePath)}\n"
            + $"InstanceName {Blank(monitor.InstanceName)} · Adapter {Blank(monitor.AdapterId)} · Source {monitor.SourceId} · Target {monitor.TargetId} · 接口 {OutputTechnologyLabel(monitor.OutputTechnology)}（{monitor.OutputTechnology}） · connectorInstance {monitor.ConnectorInstance}";
        return new MonitorCard(role, monitor.DisplayLabel, $"{monitor.Width} × {monitor.Height} · {FormatOrientation(monitor)}", "壁纸：" + wallpaper, displayInfo, colorInfo, identityInfo);
    }

    private ProfileItem ToProfileItem(WallpaperProfile profile)
    {
        var matched = _runtime.LastMatch?.Profile?.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true
            && _runtime.LastMatch.Status is MatchStatus.Exact or MatchStatus.Compatible;
        var bindings = profile.Roles.Count == 0
            ? "空白组合：尚未添加显示器角色"
            : string.Join(" · ", profile.Roles.Select(role => $"{role.DisplayName} → {WallpaperName(role)}"));
        var details = $"{profile.Roles.Count} 台显示器 · 优先级 {profile.Priority} · 修改于 {profile.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        return new ProfileItem(profile.Id, profile.Name, bindings, details, matched ? "已匹配" : "未匹配", matched ? System.Windows.Media.Brushes.LawnGreen : System.Windows.Media.Brushes.OrangeRed);
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
        foreach (var panel in new[] { OverviewPanel, MonitorsPanel, LibraryPanel, ProfilesPanel, LogsPanel, SettingsPanel, AboutPanel })
            panel.Visibility = Visibility.Collapsed;
        (tag switch
        {
            "Monitors" => MonitorsPanel,
            "Library" => LibraryPanel,
            "Profiles" => ProfilesPanel,
            "Logs" => LogsPanel,
            "Settings" => SettingsPanel,
            "About" => AboutPanel,
            _ => OverviewPanel
        }).Visibility = Visibility.Visible;
    }

    private async void Detect_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.DetectAsync();
        Refresh();
    }

    private async void Reapply_Click(object sender, RoutedEventArgs e)
    {
        await _runtime.ReapplyAsync();
        Refresh();
    }

    private async void Identify_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.Monitors.Count == 0)
        {
            System.Windows.MessageBox.Show("当前没有可识别的活动显示器。", "显示器识别");
            return;
        }
        var marks = await _identityOverlay.ShowAsync(_runtime.Monitors, TimeSpan.FromSeconds(10));
        if (marks.Count == 0)
        {
            System.Windows.MessageBox.Show("识别界面未能打开。", "显示器识别");
            return;
        }
        var assignments = await _roleAssignment.ShowAsync(marks);
        if (assignments.Count > 0)
        {
            _runtime.ApplyManualDisplayAssignments(assignments);
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
            System.Windows.MessageBox.Show(ex.Message, "导入壁纸", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            System.Windows.MessageBox.Show(ex.Message, "保存壁纸组合", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CreateBlankProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.CreateBlankWallpaperProfile(ProfileNameInput.Text);
        var dialog = new WallpaperProfileEditorWindow(_runtime, profile) { Owner = this };
        dialog.ShowDialog();
        Refresh();
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            System.Windows.MessageBox.Show("请先选择要编辑的壁纸组合。", "壁纸组合");
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
            System.Windows.MessageBox.Show("请先选择要应用的壁纸组合。", "壁纸组合");
            return;
        }
        if (!await _runtime.ApplyWallpaperProfileAsync(item.Id))
            System.Windows.MessageBox.Show(_runtime.LastMessage, "壁纸组合", MessageBoxButton.OK, MessageBoxImage.Warning);
        Refresh();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            System.Windows.MessageBox.Show("请先选择要重命名的壁纸组合。", "壁纸组合");
            return;
        }
        try
        {
            _runtime.RenameWallpaperProfile(item.Id, ProfileNameInput.Text);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "重命名壁纸组合", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            System.Windows.MessageBox.Show("请先选择要删除的壁纸组合。", "壁纸组合");
            return;
        }
        if (System.Windows.MessageBox.Show($"确定删除壁纸组合“{item.Name}”？\n壁纸图片不会被删除。", "删除壁纸组合", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _runtime.DeleteWallpaperProfile(item.Id);
        Refresh();
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ProfilesList.SelectedItem is not ProfileItem item) return;
        _runtime.SelectWallpaperProfileForEditing(item.Id);
        ProfileNameInput.Text = item.Name;
    }

    private void ExportDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _runtime.ExportWallpaperDiagnostic();
            System.Windows.MessageBox.Show("只读诊断已保存到：\n" + path, "壁纸诊断", MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "壁纸诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Logs);
    private void AutoMatch_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetAutoMatch(AutoMatchCheck.IsChecked == true); }
    private void Startup_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetStartup(StartupCheck.IsChecked == true); }
    private void LowPerformance_Click(object sender, RoutedEventArgs e) { if (!_refreshing) _runtime.SetLowPerformanceMode(LowPerformanceCheck.IsChecked == true); }

    private void UpdateChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || UpdateChannelCombo.SelectedItem is not ComboBoxItem item) return;
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
            System.Windows.MessageBox.Show("Release 地址不可用。", "检查更新");
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

    private sealed record MonitorCard(string Role, string Name, string Geometry, string Wallpaper, string DisplayInfo, string ColorInfo, string IdentityInfo);
    private sealed record LibraryItem(string Name, string Details, string Path);
    private sealed record ProfileItem(string Id, string Name, string Bindings, string Details, string MatchText, System.Windows.Media.Brush MatchBrush);
}
