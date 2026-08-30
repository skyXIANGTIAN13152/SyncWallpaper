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
    private string _libraryStatusText = "Library loaded; click Refresh after adding or removing files in Explorer.";

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
        ProfileName.Text = _runtime.LastMatch?.Profile?.Name ?? "Unmatched";
        Confidence.Text = _runtime.LastMatch is null ? "—" : $"{_runtime.LastMatch.Confidence}%";
        LastMessage.Text = _runtime.LastMessage;
        WallpaperTransactionText.Text = $"{TransactionLabel(_runtime.LastWallpaperTransaction.State)} · {_runtime.LastWallpaperTransaction.DurationMilliseconds:0} ms · {_runtime.LastWallpaperTransaction.Message}";

        var monitorCards = _runtime.Monitors.Select(ToMonitorCard).ToList();
        MonitorItems.ItemsSource = monitorCards;
        MonitorDetails.ItemsSource = monitorCards;
        MatchEvidence.Text = _runtime.LastMatch is null
            ? "No profile matching has run yet."
            : string.Join(Environment.NewLine, _runtime.LastMatch.Evidence.Select(x => $"• {x.Role} ← {x.Monitor}: {x.Reason} (score {x.Score})"))
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
            ? "No wallpaper profiles saved yet."
            : $"{profiles.Count} wallpaper profile(s) saved; green means the current topology is matched and red means unmatched.";
        if (!ProfileNameInput.IsKeyboardFocusWithin && ProfilesList.SelectedItem is ProfileItem selected)
            ProfileNameInput.Text = selected.Name;

        LogsList.ItemsSource = _runtime.RecentLogs.Select(x => $"{x.Timestamp:HH:mm:ss}  [{x.Type}]  {x.Message}").ToList();

        AutoMatchCheck.IsChecked = _runtime.Settings.AutoMatchEnabled;
        StartupCheck.IsChecked = _runtime.Settings.StartWithWindows;
        LowPerformanceCheck.IsChecked = _runtime.Settings.LowPerformanceMode;
        DataPathText.Text = _runtime.Paths.Root;
        NebulaGlowTop.Visibility = NebulaGlowBottom.Visibility = _runtime.Settings.LowPerformanceMode ? Visibility.Collapsed : Visibility.Visible;

        CurrentVersionText.Text = _runtime.CurrentVersion;
        AboutVersionText.Text = "Version " + _runtime.CurrentVersion;
        UpdateChannelCombo.SelectedIndex = string.Equals(_runtime.Settings.UpdateChannel, nameof(UpdateChannel.Beta), StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        AutomaticUpdateCheck.IsChecked = _runtime.Settings.AutomaticUpdateCheckEnabled;
        LastUpdateCheckText.Text = _runtime.Settings.LastUpdateAttemptUtc is { } attempted
            ? $"Last check: {attempted.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "Last check: never";
        var update = _runtime.LastUpdateResult;
        UpdateStatusText.Text = update?.UserMessage ?? "Offline by default.";
        UpdateNotesText.Text = update?.ReleaseNotes is { Length: > 0 } notes ? "Release notes:\n" + notes : string.Empty;
        OpenReleaseButton.IsEnabled = update?.ReleasePageUrl is { } releaseUrl
            && ReleaseUrlValidator.IsAllowed(releaseUrl, ProjectLinks.RepositorySettings);
        _refreshing = false;
    }

    private MonitorCard ToMonitorCard(MonitorIdentity monitor)
    {
        var binding = _runtime.GetBindingForMonitor(monitor);
        var role = binding?.DisplayName ?? (monitor.IsInternal ? "Laptop" : "Unassigned role");
        var wallpaper = WallpaperName(binding);
        var refresh = monitor.RefreshRateDenominator == 0
            ? "Unknown"
            : $"{(double)monitor.RefreshRateNumerator / monitor.RefreshRateDenominator:0.##} Hz";
        var displayInfo = $"{monitor.Width} × {monitor.Height} (native {monitor.NativeWidth} × {monitor.NativeHeight}) · {refresh} · {FormatOrientation(monitor)} · desktop position {monitor.DesktopX},{monitor.DesktopY}";
        var colorInfo = $"DPI {monitor.Dpi} ({monitor.DpiScale * 100:0}%) · HDR {FormatHdr(monitor.HdrEnabled)} · Color {Blank(monitor.ColorMode)} · {(monitor.IsPrimary ? "Primary monitor" : "Non-primary monitor")} · {(monitor.IsInternal ? "Internal display" : "External display")}";
        var identityInfo = $"Identity source {monitor.StableIdSource} · Stable identity {Blank(monitor.StableId)}\n"
            + $"EDID manufacturer {Blank(monitor.EdidManufactureId, monitor.ManufacturerName)} · product code {Blank(monitor.EdidProductCodeId, monitor.ProductCodeId)} · serial {Blank(monitor.EdidSerialNumber)}\n"
            + $"monitorDevicePath {Blank(monitor.MonitorDevicePath)}\n"
            + $"InstanceName {Blank(monitor.InstanceName)} · Adapter {Blank(monitor.AdapterId)} · Source {monitor.SourceId} · Target {monitor.TargetId} · Connector {OutputTechnologyLabel(monitor.OutputTechnology)} ({monitor.OutputTechnology}) · connectorInstance {monitor.ConnectorInstance}";
        return new MonitorCard(role, monitor.DisplayLabel, $"{monitor.Width} × {monitor.Height} · {FormatOrientation(monitor)}", "Wallpaper: " + wallpaper, displayInfo, colorInfo, identityInfo);
    }

    private ProfileItem ToProfileItem(WallpaperProfile profile)
    {
        var matched = _runtime.LastMatch?.Profile?.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true
            && _runtime.LastMatch.Status is MatchStatus.Exact or MatchStatus.Compatible;
        var bindings = profile.Roles.Count == 0
            ? "Blank profile: no monitor roles added"
            : string.Join(" · ", profile.Roles.Select(role => $"{role.DisplayName} → {WallpaperName(role)}"));
        var details = $"{profile.Roles.Count} monitor(s) · priority {profile.Priority} · modified {profile.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        return new ProfileItem(profile.Id, profile.Name, bindings, details, matched ? "Matched" : "Unmatched", matched ? System.Windows.Media.Brushes.LawnGreen : System.Windows.Media.Brushes.OrangeRed);
    }

    private string WallpaperName(MonitorRoleBinding? binding)
    {
        if (binding is null) return "Not configured";
        var asset = _runtime.Library.Assets.FirstOrDefault(x => x.Id.Equals(binding.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
        if (asset is not null) return asset.IsMissing ? $"{asset.DisplayName} (file missing)" : asset.DisplayName;
        if (!string.IsNullOrWhiteSpace(binding.WallpaperPath)) return File.Exists(binding.WallpaperPath) ? Path.GetFileNameWithoutExtension(binding.WallpaperPath) : "File missing";
        return "Not configured";
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
            System.Windows.MessageBox.Show("No active monitors are available for identification.", "Monitor Identification");
            return;
        }
        var marks = await _identityOverlay.ShowAsync(_runtime.Monitors, TimeSpan.FromSeconds(10));
        if (marks.Count == 0)
        {
            System.Windows.MessageBox.Show("The identification overlay could not be opened.", "Monitor Identification");
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
            Title = "Import wallpapers",
            Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var file in dialog.FileNames) _runtime.ImportWallpaper(file);
            _libraryStatusText = $"Import complete: {dialog.FileNames.Length} file(s).";
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Import wallpapers", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenWallpapers_Click(object sender, RoutedEventArgs e) => _runtime.OpenFolder(_runtime.Paths.Wallpapers);

    private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        var result = _runtime.RefreshLibrary();
        _libraryStatusText = result.MissingCount == 0 && result.RecoveredCount == 0
            ? $"Refresh complete: {_runtime.Library.Assets.Count(x => !x.IsMissing)} wallpaper(s) available."
            : $"Refresh complete: hid {result.MissingCount} deleted file(s), recovered {result.RecoveredCount} file(s).";
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
            System.Windows.MessageBox.Show(ex.Message, "Save wallpaper profile", MessageBoxButton.OK, MessageBoxImage.Information);
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
            System.Windows.MessageBox.Show("Select a wallpaper profile to edit first.", "Wallpaper Profiles");
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
            System.Windows.MessageBox.Show("Select a wallpaper profile to apply first.", "Wallpaper Profiles");
            return;
        }
        if (!await _runtime.ApplyWallpaperProfileAsync(item.Id))
            System.Windows.MessageBox.Show(_runtime.LastMessage, "Wallpaper Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        Refresh();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            System.Windows.MessageBox.Show("Select a wallpaper profile to rename first.", "Wallpaper Profiles");
            return;
        }
        try
        {
            _runtime.RenameWallpaperProfile(item.Id, ProfileNameInput.Text);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Rename wallpaper profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not ProfileItem item)
        {
            System.Windows.MessageBox.Show("Select a wallpaper profile to delete first.", "Wallpaper Profiles");
            return;
        }
        if (System.Windows.MessageBox.Show($"Delete wallpaper profile \"{item.Name}\"?\nWallpaper image files will not be deleted.", "Delete wallpaper profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
            System.Windows.MessageBox.Show("Read-only diagnostics saved to:\n" + path, "Wallpaper diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Wallpaper diagnostics", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        CheckUpdatesButton.Content = "Checking…";
        try { await _runtime.CheckForUpdatesAsync(true); }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check for updates";
            Refresh();
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        var url = _runtime.LastUpdateResult?.ReleasePageUrl;
        if (!_runtime.OpenReleasePage(url))
            System.Windows.MessageBox.Show("The Release URL is unavailable.", "Update checks");
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
            1 => "Landscape · Normal",
            2 => "Portrait · Normal",
            3 => "Landscape · Flipped",
            4 => "Portrait · Flipped",
            _ => "Landscape · Normal"
        };
    }

    private static string FormatHdr(bool? value) => value switch { true => "Enabled", false => "Disabled", _ => "Unavailable" };
    private static string Blank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unavailable";
    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:0.##} MB" : $"{value / 1024d:0} KB";
    private static string TransactionLabel(WallpaperTransactionState value) => value switch
    {
        WallpaperTransactionState.Completed => "Completed",
        WallpaperTransactionState.Failed => "Failed",
        WallpaperTransactionState.Cancelled => "Cancelled",
        WallpaperTransactionState.RollbackFailed => "Rollback failed",
        WallpaperTransactionState.RollingBack => "Rolling back",
        WallpaperTransactionState.Applying => "Applying",
        WallpaperTransactionState.Verifying => "Verifying",
        WallpaperTransactionState.Retrying => "Retrying",
        _ => "Preparing"
    };

    private static string OutputTechnologyLabel(uint value) => value switch
    {
        0 => "VGA/HD15",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        10 => "DisplayPort",
        11 => "Internal DisplayPort/eDP",
        15 => "Miracast",
        16 => "Indirect wired (common USB-C/dock)",
        17 => "Indirect virtual",
        0x80000000 => "Internal connector",
        0xFFFFFFFF => "Other",
        _ => "Other"
    };

    private sealed record MonitorCard(string Role, string Name, string Geometry, string Wallpaper, string DisplayInfo, string ColorInfo, string IdentityInfo);
    private sealed record LibraryItem(string Name, string Details, string Path);
    private sealed record ProfileItem(string Id, string Name, string Bindings, string Details, string MatchText, System.Windows.Media.Brush MatchBrush);
}
