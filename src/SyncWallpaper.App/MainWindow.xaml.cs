using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

public partial class MainWindow : Window
{
    private readonly AppRuntime _runtime;
    public bool AllowExit { get; set; }
    private readonly ObservableCollection<DisplayItem> _displayItems = new();
    private readonly ObservableCollection<LibraryItem> _libraryItems = new();
    private readonly MonitorFadingService _fading = new();
    private readonly DisplayIdentificationOverlayService _identityOverlay = new();
    private readonly DisplayRoleAssignmentService _roleAssignment = new();

    public MainWindow(AppRuntime runtime)
    {
        InitializeComponent(); _runtime = runtime; MonitorItems.ItemsSource = _displayItems; LibraryItems.ItemsSource = _libraryItems;
        DisplayProfilesItems.ItemsSource = Array.Empty<DisplayProfileItem>();
        AudioProfilesItems.ItemsSource = Array.Empty<AudioProfileItem>();
        DesktopProfilesItems.ItemsSource = Array.Empty<DesktopProfileItem>();
        _runtime.StateChanged += Runtime_StateChanged; Loaded += (_, _) => Refresh();
    }

    private void Runtime_StateChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);
    private void Refresh()
    {
        StatusBadge.Text = _runtime.StatusText; OverviewStatus.Text = _runtime.StatusText; LastMessage.Text = _runtime.LastMessage;
        WallpaperTransactionText.Text = $"{_runtime.LastWallpaperTransaction.State} · {_runtime.LastWallpaperTransaction.DurationMilliseconds:0} ms · {_runtime.LastWallpaperTransaction.Message}";
        MonitorCount.Text = _runtime.Monitors.Count.ToString(); ProfileName.Text = _runtime.LastMatch?.Profile?.Name ?? "未选择";
        Confidence.Text = _runtime.LastMatch is null ? "—" : $"{_runtime.LastMatch.Confidence}%";
        _displayItems.Clear();
        foreach (var m in _runtime.Monitors)
        {
            var role = _runtime.LastMatch?.RoleMatches.FirstOrDefault(x => x.Value.MonitorDevicePath.Equals(m.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)).Key ?? (m.IsInternal ? "Laptop" : m.Width >= m.Height ? "Landscape" : "Portrait");
            _displayItems.Add(new DisplayItem(role, m.DisplayLabel, $"{m.Width} × {m.Height} · 方向 {m.Rotation}", $"{m.ManufacturerName} / {m.ProductCodeId}"));
        }
        DisplayDetails.ItemsSource = _runtime.Monitors.Select(m =>
        {
            var safe = MonitorIdentitySanitizer.Sanitize(m);
            return new DisplayItem(
                m.IsInternal ? "Laptop" : (m.Width >= m.Height ? "Landscape" : "Portrait"),
                string.IsNullOrWhiteSpace(m.WindowsDisplayName) ? m.DisplayLabel : $"{m.DisplayLabel} · {m.WindowsDisplayName}",
                $"{m.Width} × {m.Height} · 原生 {m.NativeWidth} × {m.NativeHeight} · {m.RefreshRateNumerator}/{Math.Max(1, m.RefreshRateDenominator)} Hz · 方向 {m.Rotation} · 桌面 {m.DesktopX},{m.DesktopY}",
                $"身份 {safe.StableIdSource}: {safe.StableId} · Container {safe.ContainerId} · 路径 {safe.MonitorDevicePath} · 序列号 {safe.Serial} · Adapter {safe.AdapterId} / Target {safe.TargetId} · 接口 {safe.OutputTechnology}/{safe.ConnectorInstance} · {safe.ConnectionState}");
        });
        EvidenceText.Text = _runtime.LastMatch is null ? "尚未检测" : string.Join(Environment.NewLine, _runtime.LastMatch.Evidence.Select(x => $"· {x.Role} ← {x.Monitor}：{x.Reason}（{x.Score}）")) + Environment.NewLine + _runtime.LastMatch.Message;
        _libraryItems.Clear(); foreach (var a in _runtime.Library.Assets) _libraryItems.Add(new LibraryItem(a.DisplayName, $"{a.Width} × {a.Height} · {a.Format} · {a.FileSize / 1024} KB", a.ManagedRelativePath));
        DisplayProfilesItems.ItemsSource = _runtime.DisplayConfigurations.Profiles.Select(x => new DisplayProfileItem(x.Name, x.Displays.Count, x.ModifiedAt.ToLocalTime().ToString("MM-dd HH:mm"))).ToList();
        DisplayTransactionText.Text = _runtime.LastDisplayTransaction is null ? "尚未应用显示配置。" :
            $"{_runtime.LastDisplayTransaction.Status}：{_runtime.LastDisplayTransaction.Message}{Environment.NewLine}{string.Join(Environment.NewLine, _runtime.LastDisplayTransaction.Steps)}";
        AudioProfilesItems.ItemsSource = _runtime.AudioProfiles.Profiles.Select(x => new AudioProfileItem(x.Name, x.Assignments.Count)).ToList();
        AudioStatusText.Text = _runtime.LastAudioResult?.Message ?? "尚未应用音频配置。";
        WindowStatusText.Text = _runtime.LastWindowRestore is null ? "尚未恢复窗口布局。" :
            $"已匹配 {_runtime.LastWindowRestore.Matched}，已应用 {_runtime.LastWindowRestore.Applied}，跳过 {_runtime.LastWindowRestore.Skipped}。";
        AutomationStatusText.Text = _runtime.LastAutomationResults.Count == 0 ? "尚未执行自动化。" :
            $"最近执行 {_runtime.LastAutomationResults.Count} 步，成功 {_runtime.LastAutomationResults.Count(x => x.Success)} 步。";
        DesktopProfilesItems.ItemsSource = _runtime.DesktopIconProfiles.Profiles.Select(x => new DesktopProfileItem(x.Name, x.Positions.Count)).ToList();
        DesktopStatusText.Text = _runtime.LastDesktopRestore is null ? "尚未恢复桌面图标。" :
            $"已应用 {_runtime.LastDesktopRestore.Applied}，跳过 {_runtime.LastDesktopRestore.Skipped}。";
        LogsList.ItemsSource = _runtime.RecentLogs.Select(x => $"{x.Timestamp:HH:mm:ss}  {x.Type}  {x.Message}").ToList();
        AutoMatchCheck.IsChecked = _runtime.Settings.AutoMatchEnabled; StartupCheck.IsChecked = _runtime.Settings.StartWithWindows; DataPathText.Text = _runtime.Paths.Root;
        ModuleModeCombo.SelectedIndex = _runtime.Settings.Modules.Mode switch { ModuleMode.Lightweight => 0, ModuleMode.Standard => 1, ModuleMode.Full => 2, _ => 3 };
        ModuleItems.ItemsSource = _runtime.Modules.Snapshot(_runtime.Settings.Modules).Select(x => new ModuleItem(
            x.Id,
            x.DisplayName,
            x.State switch { ModuleLifecycleState.Running => "运行中", ModuleLifecycleState.Starting => "启动中", ModuleLifecycleState.Stopping => "停止中", ModuleLifecycleState.Faulted => "故障", _ => "已停止" },
            $"PID={(x.ProcessId?.ToString() ?? "—")} · WS={x.Resources.WorkingSetBytes / 1024 / 1024} MiB · Private={x.Resources.PrivateBytes / 1024 / 1024} MiB · Handles={x.Resources.HandleCount} · CPU={x.Resources.CpuSeconds:F2}s · {(x.OutOfProcess ? "独立进程" : "同进程")} · 依赖={(x.Dependencies.Count == 0 ? "无" : string.Join(",", x.Dependencies))} · 失败={x.FailureCount} · 原因={x.LastTransitionReason} · 心跳={(x.LastHeartbeatAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—")}" + (x.LastError is null ? string.Empty : $" · 错误：{x.LastError}"))).ToList();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.Button)?.Tag?.ToString() ?? "Overview";
        foreach (var panel in new[] { OverviewPanel, DisplaysPanel, AudioPanel, LibraryPanel, RulesPanel, FeaturePanel, DesktopPanel, DiagnosticPanel, LogsPanel, SettingsPanel, AboutPanel }) panel.Visibility = Visibility.Collapsed;
        if (tag is "Window" or "Functions" or "Splits" or "Behavior")
        {
            FeatureTitle.Text = tag switch { "Functions" => "触发器与函数", "Splits" => "屏幕分割", "Behavior" => "任务栏与行为", _ => "窗口与布局" };
            FeaturePanel.Visibility = Visibility.Visible;
        }
        else (tag switch { "Displays" => DisplaysPanel, "Audio" => AudioPanel, "Library" => LibraryPanel, "Rules" => RulesPanel, "Desktop" => DesktopPanel, "Diagnostics" => DiagnosticPanel, "Logs" => LogsPanel, "Settings" => SettingsPanel, "About" => AboutPanel, _ => OverviewPanel }).Visibility = Visibility.Visible;
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) { await _runtime.DetectAsync(); Refresh(); }
    private async void Reapply_Click(object sender, RoutedEventArgs e) { await _runtime.ReapplyAsync(); Refresh(); }
    private async void Identify_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime.Monitors.Count == 0) { System.Windows.MessageBox.Show("当前没有可识别的活动显示器。", "显示器识别"); return; }
        var marks = await _identityOverlay.ShowAsync(_runtime.Monitors, TimeSpan.FromSeconds(10));
        if (marks.Count == 0) { System.Windows.MessageBox.Show("识别界面未能打开。", "显示器识别"); return; }
        var assignments = await _roleAssignment.ShowAsync(marks);
        if (assignments.Count > 0) { _runtime.ApplyManualDisplayAssignments(assignments); Refresh(); }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();
    private void AutoMatch_Click(object sender, RoutedEventArgs e) { _runtime.Settings.AutoMatchEnabled = AutoMatchCheck.IsChecked == true; }
    private void Startup_Click(object sender, RoutedEventArgs e) => _runtime.SetStartup(StartupCheck.IsChecked == true);
    private async void ApplyModuleMode_Click(object sender, RoutedEventArgs e)
    {
        var tag = (ModuleModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!Enum.TryParse<ModuleMode>(tag, true, out var mode)) return;
        await _runtime.ApplyModuleModeAsync(mode);
        Refresh();
    }
    private async void EnableModule_Click(object sender, RoutedEventArgs e)
    {
        if (ModuleItems.SelectedItem is not ModuleItem item) return;
        await _runtime.SetModuleEnabledAsync(item.Id, true);
        Refresh();
    }
    private async void DisableModule_Click(object sender, RoutedEventArgs e)
    {
        if (ModuleItems.SelectedItem is not ModuleItem item) return;
        if (item.Id == SyncWallpaperModule.Wallpaper)
        {
            System.Windows.MessageBox.Show("壁纸自动匹配是核心功能，不能关闭。", "按需模块", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await _runtime.SetModuleEnabledAsync(item.Id, false);
        Refresh();
    }
    private void RefreshModules_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
        if (dialog.ShowDialog() == true) { foreach (var file in dialog.FileNames) _runtime.ImportWallpaper(file); Refresh(); }
    }
    private void OpenData_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Root);
    private void OpenWallpapers_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Wallpapers);
    private void OpenConfig_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Config);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Logs);
    private void OpenCache_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Cache);
    private void CaptureWindows_Click(object sender, RoutedEventArgs e) { try { _runtime.CaptureWindowProfile("布局 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm")); Refresh(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "窗口模块"); } }
    private void ApplyWindows_Click(object sender, RoutedEventArgs e) { var profile = _runtime.WindowProfiles.Profiles.LastOrDefault(); if (profile is not null) System.Windows.MessageBox.Show($"已恢复 {_runtime.ApplyWindowProfile(profile.Id)} 个窗口。", "屏序"); }
    private async void TestTrigger_Click(object sender, RoutedEventArgs e) { await _runtime.FireTestTriggerAsync(); Refresh(); }
    private void CaptureDisplayProfile_Click(object sender, RoutedEventArgs e) { try { _runtime.CaptureDisplayProfile("显示配置 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm")); Refresh(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "显示配置"); } }
    private async void ApplyDisplayProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.DisplayConfigurations.Profiles.LastOrDefault(); if (profile is not null) { await _runtime.ApplyDisplayProfileAsync(profile.ProfileId); Refresh(); } }
    private void ValidateDisplayProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.DisplayConfigurations.Profiles.LastOrDefault();
        var result = profile is null ? null : _runtime.ValidateDisplayProfile(profile.ProfileId);
        if (result is null) { System.Windows.MessageBox.Show("还没有保存的显示配置。", "显示配置"); return; }
        var details = result.IsValid ? "预检查通过。" : "预检查失败：" + string.Join("；", result.Errors);
        if (result.Warnings.Count > 0) details += Environment.NewLine + "警告：" + string.Join("；", result.Warnings);
        System.Windows.MessageBox.Show(details, "显示配置预检查", MessageBoxButton.OK, result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
    private void ShowDisplayDiff_Click(object sender, RoutedEventArgs e)
    {
        var profile = _runtime.DisplayConfigurations.Profiles.LastOrDefault();
        var result = profile is null ? null : _runtime.ValidateDisplayProfile(profile.ProfileId);
        if (result is null) { System.Windows.MessageBox.Show("还没有保存的显示配置。", "显示器差异"); return; }
        var text = result.Differences.Count == 0
            ? "当前真实状态与保存目标没有可见差异。"
            : string.Join(Environment.NewLine, result.Differences.Select(x => $"{x.Subject}：{x.CurrentValue} → {x.TargetValue}"));
        System.Windows.MessageBox.Show(text, "显示器差异", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void CopyDisplayProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.DisplayConfigurations.Profiles.LastOrDefault(); if (profile is not null) { _runtime.CopyDisplayProfile(profile.ProfileId, profile.Name + " 副本"); Refresh(); } }
    private void DeleteDisplayProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.DisplayConfigurations.Profiles.LastOrDefault(); if (profile is not null && System.Windows.MessageBox.Show($"删除“{profile.Name}”？", "删除保护", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _runtime.DeleteDisplayProfile(profile.ProfileId); Refresh(); } }
    private void ExportDisplayProfile_Click(object sender, RoutedEventArgs e) => ExportJson("display-configurations.json", _runtime.DisplayConfigurations);
    private void CaptureAudioProfile_Click(object sender, RoutedEventArgs e) { try { _runtime.CaptureAudioProfile("音频配置 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm")); Refresh(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "音频模块"); } }
    private async void ApplyAudioProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.AudioProfiles.Profiles.LastOrDefault(); if (profile is not null) { await _runtime.ApplyAudioProfileAsync(profile.Id); Refresh(); } }
    private void DeleteAudioProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.AudioProfiles.Profiles.LastOrDefault(); if (profile is not null && ConfirmDelete(profile.Name)) { _runtime.DeleteAudioProfile(profile.Id); Refresh(); } }
    private void ExportAudioProfile_Click(object sender, RoutedEventArgs e) => ExportJson("audio-profiles.json", _runtime.AudioProfiles);
    private void CaptureDesktopProfile_Click(object sender, RoutedEventArgs e) { try { _runtime.CaptureDesktopIconProfile("桌面图标 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm")); Refresh(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "桌面模块"); } }
    private void ApplyDesktopProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.DesktopIconProfiles.Profiles.LastOrDefault(); if (profile is not null) { _runtime.ApplyDesktopIconProfile(profile.Id); Refresh(); } }
    private void DeleteDesktopProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.DesktopIconProfiles.Profiles.LastOrDefault(); if (profile is not null && ConfirmDelete(profile.Name)) { _runtime.DeleteDesktopIconProfile(profile.Id); Refresh(); } }
    private void ExportDesktopProfile_Click(object sender, RoutedEventArgs e) => ExportJson("desktop-icons.json", _runtime.DesktopIconProfiles);
    private void DeleteWindowProfile_Click(object sender, RoutedEventArgs e) { var profile = _runtime.WindowProfiles.Profiles.LastOrDefault(); if (profile is not null && ConfirmDelete(profile.Name)) { _runtime.DeleteWindowProfile(profile.Id); Refresh(); } }
    private void ExportWindowProfile_Click(object sender, RoutedEventArgs e) => ExportJson("window-profiles.json", _runtime.WindowProfiles);
    private void ExportAutomation_Click(object sender, RoutedEventArgs e) => ExportJson("triggers.json", _runtime.Triggers);
    private void DiagnosticReadOnly_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _runtime.CaptureDiagnosticSnapshot();
            DiagnosticSummaryText.Text = $"{snapshot.WindowsVersion} · 软件 {snapshot.SoftwareVersion} · {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss}";
            var displays = string.Join(Environment.NewLine, snapshot.Displays.Select(x =>
            {
                var safe = MonitorIdentitySanitizer.Sanitize(x);
                return $"显示器：{safe.FriendlyName} [{x.WindowsDisplayName}] | Stable={safe.StableIdSource}:{safe.StableId} | Container={safe.ContainerId} | Path={safe.MonitorDevicePath} | Adapter {safe.AdapterId} Target {safe.TargetId} | Connector {safe.OutputTechnology}/{safe.ConnectorInstance} | {safe.Width}×{safe.Height} native={safe.NativeWidth}×{safe.NativeHeight} {safe.RefreshRateNumerator}/{Math.Max(1, safe.RefreshRateDenominator)}Hz rot={safe.Rotation} pos={safe.DesktopX},{safe.DesktopY} | 主屏={safe.IsPrimary} | {safe.ConnectionState}";
            }));
            var audio = string.Join(Environment.NewLine, snapshot.AudioDevices.Select(x => $"音频：{x.Kind} | {x.FriendlyName} | {x.State} | {x.DeviceId}"));
            var defaults = string.Join(Environment.NewLine, snapshot.AudioDefaults.Select(x => $"默认 {x.Key}：{x.Value?.FriendlyName ?? "无"}"));
            var resources = snapshot.Resources;
            DiagnosticDetailsText.Text = string.Join(Environment.NewLine, new[]
            {
                displays,
                audio,
                defaults,
                $"窗口：{snapshot.WindowCount} 个，Elevated={snapshot.ElevatedWindowCount}；监听={snapshot.WindowListenerStatus}",
                $"Explorer：{snapshot.ExplorerStatus}；桌面 Shell 项目={snapshot.DesktopShellItemCount}",
                $"COM：{snapshot.ComInitializationStatus}；最近系统事件={snapshot.LastSystemEvent}",
                $"事务：{snapshot.LastTransaction}；回滚：{snapshot.LastRollback}",
                "模块：" + string.Join("；", snapshot.Modules.Select(x => $"{x.DisplayName}={x.State}/PID={x.ProcessId?.ToString() ?? "—"}/Hook={x.HookStatus}")),
                $"资源：WorkingSet={resources.WorkingSetBytes / 1024 / 1024} MiB，Private={resources.PrivateBytes / 1024 / 1024} MiB，GC={resources.GcHeapBytes / 1024 / 1024} MiB，Handles={resources.HandleCount}，GDI={resources.GdiObjects}，USER={resources.UserObjects}，CPU={resources.CpuSeconds:F2}s"
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch (Exception ex) { DiagnosticDetailsText.Text = "只读检查失败：" + ex.Message; }
    }
    private void ExportDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _runtime.CaptureDiagnosticSnapshot();
        ExportJson("diagnostic-report.json", new
        {
            snapshot.CapturedAt,
            snapshot.WindowsVersion,
            snapshot.SoftwareVersion,
            Displays = snapshot.Displays.Select(MonitorIdentitySanitizer.Sanitize).ToArray(),
            snapshot.AudioDevices,
            snapshot.AudioDefaults,
            snapshot.WindowCount,
            snapshot.ElevatedWindowCount,
            snapshot.WindowListenerStatus,
            snapshot.ExplorerStatus,
            snapshot.ComInitializationStatus,
            snapshot.LastSystemEvent,
            snapshot.LastTransaction,
            snapshot.LastRollback,
            snapshot.DesktopShellItemCount,
            snapshot.Modules,
            snapshot.PerformanceHistory,
            snapshot.Resources
        });
    }
    private void DiagnosticDisplayRollback_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("显示配置回滚", "B/C：需要明确确认。将先保存 CCD 快照，再选择已报告支持的低风险位置变化，应用后等待 15 秒，不保留则自动恢复并逐项验证。当前未执行任何变化。");
    private void DiagnosticAudioSwitch_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("音频切换恢复", "B：需要明确选择目标播放/录音设备并确认。只允许 Active 且可恢复的设备；当前未执行切换。");
    private void DiagnosticWindowLayout_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("窗口布局验证", "A/B：将只创建 TestWindowA/B/C 临时窗口，保存、移动、恢复后关闭；不会触碰用户应用。当前未执行。");
    private void DiagnosticDesktopIcon_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("临时桌面图标验证", "A/B：只创建 SyncWallpaper Desktop Test.lnk，验证官方 Shell 接口后删除；不会移动现有桌面图标。当前未执行。");
    private void DiagnosticExplorer_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("Explorer 恢复验证", "高风险：将正常结束并重新启动 Explorer，可能短暂消失任务栏和桌面。必须在你明确确认后执行；当前未执行。");
    private void DiagnosticSleepWait_Click(object sender, RoutedEventArgs e) => ShowRiskPlan("睡眠唤醒记录", "用户手动：软件只保存快照并等待你自行睡眠/唤醒，不会自动触发电源操作。当前未开始等待。");
    private static void ShowRiskPlan(string title, string text) => System.Windows.MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    private async void ShowTaskbar_Click(object sender, RoutedEventArgs e)
    {
        try { await _runtime.SetModuleEnabledAsync(SyncWallpaperModule.TaskbarHost, true); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "任务栏宿主"); }
        Refresh();
    }
    private async void StartSaver_Click(object sender, RoutedEventArgs e)
    {
        try { await _runtime.SetModuleEnabledAsync(SyncWallpaperModule.ScreenSaverHost, true); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "屏保宿主"); }
        Refresh();
    }
    private void ClearFade_Click(object sender, RoutedEventArgs e) => _fading.Clear();
    protected override void OnClosed(EventArgs e) { _runtime.StateChanged -= Runtime_StateChanged; _fading.Dispose(); base.OnClosed(e); }
    private bool ConfirmDelete(string name) => System.Windows.MessageBox.Show($"删除“{name}”？此操作会从本机配置中移除档案。", "删除保护", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private static void ExportJson(string fileName, object value)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "JSON 配置|*.json|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        var node = JsonSerializer.SerializeToNode(value);
        SanitizeExportNode(node);
        File.WriteAllText(dialog.FileName, node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
    }

    private static void SanitizeExportNode(JsonNode? node)
    {
        if (node is not JsonObject obj) return;
        foreach (var property in obj.ToList())
        {
            if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                && (property.Key.Contains("Serial", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Contains("Container", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("MonitorDevicePath", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("InstanceName", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("StableId", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("AdapterLuid", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("DeviceId", StringComparison.OrdinalIgnoreCase)
                    || property.Key.Equals("Path", StringComparison.OrdinalIgnoreCase)))
                obj[property.Key] = JsonValue.Create(MonitorIdentitySanitizer.RedactPath(text));
            else SanitizeExportNode(property.Value);
        }
    }
    private sealed record DisplayItem(string Role, string Label, string Resolution, string Identity);
    private sealed record LibraryItem(string Name, string Details, string Path);
    private sealed record DisplayProfileItem(string Name, int MonitorCount, string Modified);
    private sealed record AudioProfileItem(string Name, int AssignmentCount);
    private sealed record DesktopProfileItem(string Name, int ItemCount);
    private sealed record ModuleItem(SyncWallpaperModule Id, string Name, string State, string Details);
}
