using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

public partial class WallpaperProfileEditorWindow : Window
{
    private readonly AppRuntime _runtime;
    private readonly WallpaperProfile _profile;
    private readonly ObservableCollection<RoleRow> _rows = new();
    private readonly IReadOnlyList<MonitorChoice> _monitorChoices;
    private readonly IReadOnlyList<WallpaperChoice> _wallpaperChoices;
    private readonly IReadOnlyList<FitChoice> _fitChoices = Enum.GetValues<WallpaperFitMode>()
        .Select(x => new FitChoice(x, FitLabel(x))).ToArray();

    public WallpaperProfileEditorWindow(AppRuntime runtime, WallpaperProfile profile)
    {
        InitializeComponent();
        _runtime = runtime;
        _profile = profile;
        NameText.Text = profile.Name;
        EnabledCheck.IsChecked = profile.Enabled;
        CompatibleCheck.IsChecked = profile.AllowCompatibleMatch;
        _monitorChoices = BuildMonitorChoices(runtime.Monitors, profile);
        _wallpaperChoices = new[] { new WallpaperChoice(string.Empty, "No wallpaper") }
            .Concat(runtime.Library.Assets.Where(x => !x.IsMissing).OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new WallpaperChoice(x.Id, x.DisplayName)))
            .ToArray();
        foreach (var role in profile.Roles)
            _rows.Add(CreateRow(role.RoleId, role.Role, MonitorKey(role.Fingerprint), role.WallpaperAssetId, role.FitMode, role.BackgroundColor));
        RoleItems.ItemsSource = _rows;
        RefreshHint();
    }

    private IReadOnlyList<MonitorChoice> BuildMonitorChoices(IReadOnlyList<MonitorIdentity> current, WallpaperProfile profile)
    {
        var choices = new List<MonitorChoice> { new(string.Empty, "Select a monitor", null) };
        foreach (var monitor in current.Where(HasStrongIdentity))
            AddChoice(choices, new MonitorChoice(MonitorKey(monitor), $"{monitor.DisplayLabel} · {monitor.Width}×{monitor.Height} · Connected", monitor.Clone()));
        foreach (var role in profile.Roles.Where(x => x.Fingerprint is not null))
            AddChoice(choices, new MonitorChoice(MonitorKey(role.Fingerprint), $"{role.Fingerprint.DisplayLabel} · Saved identity (may be disconnected)", role.Fingerprint.Clone()));
        return choices;
    }

    private static void AddChoice(List<MonitorChoice> choices, MonitorChoice choice)
    {
        if (string.IsNullOrWhiteSpace(choice.Key) || choices.Any(x => x.Key.Equals(choice.Key, StringComparison.OrdinalIgnoreCase))) return;
        choices.Add(choice);
    }

    private RoleRow CreateRow(string roleId, string role, string monitorKey, string wallpaperId, WallpaperFitMode fit, string background)
        => new(roleId, role, monitorKey, wallpaperId, fit, background, _monitorChoices, _wallpaperChoices, _fitChoices);

    private void AddCurrentMonitors_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Select(x => x.SelectedMonitorKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in _monitorChoices.Where(x => x.Fingerprint is not null && !selected.Contains(x.Key)))
        {
            var role = UniqueRole(DefaultRole(choice.Fingerprint!));
            _rows.Add(CreateRow(string.Empty, role, choice.Key, string.Empty, WallpaperFitMode.Fill, "#050B18"));
            selected.Add(choice.Key);
        }
        RefreshHint();
    }

    private void AddRole_Click(object sender, RoutedEventArgs e)
    {
        var used = _rows.Select(x => x.SelectedMonitorKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choice = _monitorChoices.FirstOrDefault(x => x.Fingerprint is not null && !used.Contains(x.Key));
        var role = UniqueRole(choice?.Fingerprint is { } monitor ? DefaultRole(monitor) : "Custom");
        _rows.Add(CreateRow(string.Empty, role, choice?.Key ?? string.Empty, string.Empty, WallpaperFitMode.Fill, "#050B18"));
        RefreshHint();
    }

    private void RemoveRole_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is RoleRow row) _rows.Remove(row);
        RefreshHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = new WallpaperProfileEditDraft
            {
                Name = NameText.Text,
                Enabled = EnabledCheck.IsChecked == true,
                AllowCompatibleMatch = CompatibleCheck.IsChecked == true,
                MinimumConfidence = _profile.MinimumConfidence,
                Roles = _rows.Select(row =>
                {
                    var monitor = _monitorChoices.FirstOrDefault(x => x.Key.Equals(row.SelectedMonitorKey ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    return new WallpaperRoleEditDraft
                    {
                        RoleId = row.RoleId,
                        Role = row.Role,
                        DisplayName = row.Role,
                        Fingerprint = monitor?.Fingerprint?.Clone(),
                        WallpaperAssetId = row.SelectedWallpaperId ?? string.Empty,
                        FitMode = row.FitMode,
                        BackgroundColor = row.BackgroundColor
                    };
                }).ToList()
            };
            _runtime.UpdateWallpaperProfile(_profile.Id, draft);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            System.Windows.MessageBox.Show(ex.Message, "Edit wallpaper profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string UniqueRole(string basis)
    {
        if (_rows.All(x => !x.Role.Equals(basis, StringComparison.OrdinalIgnoreCase))) return basis;
        for (var index = 2; index <= 99; index++)
        {
            var candidate = basis + index;
            if (_rows.All(x => !x.Role.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        return "Custom-" + Guid.NewGuid().ToString("N")[..6];
    }

    private static string DefaultRole(MonitorIdentity monitor)
        => monitor.IsInternal ? "Laptop" : monitor.Width >= monitor.Height ? "Landscape" : "Portrait";

    private void RefreshHint()
    {
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _rows.Count == 0 ? "Blank profiles are excluded from automatic monitor matching." : $"{_rows.Count} logical role(s).";
    }

    private static bool HasStrongIdentity(MonitorIdentity monitor)
        => !string.IsNullOrWhiteSpace(MonitorKey(monitor));

    private static string MonitorKey(MonitorIdentity monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableId)
            && monitor.StableIdSource is MonitorIdentitySource.EdidSerial or MonitorIdentitySource.MonitorDevicePath or MonitorIdentitySource.InstanceName or MonitorIdentitySource.HardwareTopology)
            return monitor.StableId;
        if (monitor.HasUsableSerial) return "edid:" + monitor.SerialKey;
        if (!string.IsNullOrWhiteSpace(monitor.MonitorDevicePath) && !monitor.MonitorDevicePath.StartsWith("fallback://", StringComparison.OrdinalIgnoreCase)) return "path:" + monitor.MonitorDevicePath;
        if (!string.IsNullOrWhiteSpace(monitor.InstanceName)) return "instance:" + monitor.InstanceName;
        if (!string.IsNullOrWhiteSpace(monitor.AdapterId)) return "topology:" + monitor.HardwareKey;
        return string.Empty;
    }

    private static string FitLabel(WallpaperFitMode mode) => mode switch
    {
        WallpaperFitMode.Fill => "Fill",
        WallpaperFitMode.Fit => "Fit",
        WallpaperFitMode.Stretch => "Stretch",
        WallpaperFitMode.Center => "Center",
        WallpaperFitMode.Tile => "Tile",
        WallpaperFitMode.Span => "Span",
        _ => mode.ToString()
    };

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record MonitorChoice(string Key, string Label, MonitorIdentity? Fingerprint);
    private sealed record WallpaperChoice(string Id, string Label);
    private sealed record FitChoice(WallpaperFitMode Value, string Label);
    private sealed class RoleRow
    {
        public RoleRow(string roleId, string role, string monitorKey, string wallpaperId, WallpaperFitMode fitMode, string backgroundColor,
            IReadOnlyList<MonitorChoice> monitors, IReadOnlyList<WallpaperChoice> wallpapers, IReadOnlyList<FitChoice> fits)
        {
            RoleId = roleId;
            Role = role;
            SelectedMonitorKey = monitorKey;
            SelectedWallpaperId = wallpaperId;
            FitMode = fitMode;
            BackgroundColor = backgroundColor;
            MonitorChoices = monitors;
            WallpaperChoices = wallpapers;
            FitChoices = fits;
        }
        public string RoleId { get; }
        public string Role { get; set; }
        public string? SelectedMonitorKey { get; set; }
        public string? SelectedWallpaperId { get; set; }
        public WallpaperFitMode FitMode { get; set; }
        public string BackgroundColor { get; }
        public IReadOnlyList<MonitorChoice> MonitorChoices { get; }
        public IReadOnlyList<WallpaperChoice> WallpaperChoices { get; }
        public IReadOnlyList<FitChoice> FitChoices { get; }
    }
}
