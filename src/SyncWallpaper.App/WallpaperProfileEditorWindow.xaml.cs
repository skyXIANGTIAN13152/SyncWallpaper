using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

public partial class WallpaperProfileEditorWindow : Window
{
    public static readonly DependencyProperty SlotCardWidthProperty = DependencyProperty.Register(
        nameof(SlotCardWidth), typeof(double), typeof(WallpaperProfileEditorWindow), new PropertyMetadata(330d));
    public double SlotCardWidth { get => (double)GetValue(SlotCardWidthProperty); set => SetValue(SlotCardWidthProperty, value); }

    private readonly AppRuntime _runtime;
    private readonly WallpaperProfile _profile;
    private readonly ObservableCollection<RoleRow> _rows = new();
    private readonly IReadOnlyList<MonitorChoice> _monitorChoices;
    private readonly ObservableCollection<WallpaperChoice> _wallpaperChoices;
    private readonly IReadOnlyList<FitChoice> _fitChoices;
    private readonly Dictionary<string, ImageSource?> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private RoleRow? _pickerRow;
    private System.Windows.Controls.Button? _pickerTrigger;

    public WallpaperProfileEditorWindow(AppRuntime runtime, WallpaperProfile profile)
    {
        InitializeComponent();
        _runtime = runtime;
        _profile = profile;
        _fitChoices = Enum.GetValues<WallpaperFitMode>()
            .Where(x => !profile.IsGeneric || x != WallpaperFitMode.Span)
            .Select(x => new FitChoice(x, FitLabel(x))).ToArray();
        NameText.Text = profile.Name;
        PriorityText.Text = profile.Priority.ToString(CultureInfo.CurrentCulture);
        EnabledCheck.IsChecked = profile.Enabled;
        CompatibleCheck.IsChecked = profile.AllowCompatibleMatch;
        ReliableIdentityRuleText.Text = $"2. 可靠身份按序列号 → 设备路径 → 实例信息 → 硬件连接判断；必须唯一对应，并达到最低置信度。当前组合要求 {_profile.MinimumConfidence}%，整套以最弱屏幕为准。";
        _monitorChoices = BuildMonitorChoices(runtime.Monitors, profile);
        _wallpaperChoices = new ObservableCollection<WallpaperChoice>(runtime.Library.Assets
            .Where(x => !x.IsMissing || profile.Roles.Any(role => role.WallpaperAssetId.Equals(x.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(CreateWallpaperChoice)
            .Concat(profile.Roles.Where(x => string.IsNullOrWhiteSpace(x.WallpaperAssetId) && !string.IsNullOrWhiteSpace(x.WallpaperPath))
                .Select(x => new WallpaperChoice("path:" + x.WallpaperPath, Path.GetFileName(x.WallpaperPath), x.WallpaperPath,
                    x.WallpaperPath, "已保存的本地图片", LoadThumbnail)))
            .Concat(profile.Roles.Where(x => !string.IsNullOrWhiteSpace(x.WallpaperAssetId)
                    && runtime.Library.Assets.All(asset => !asset.Id.Equals(x.WallpaperAssetId, StringComparison.OrdinalIgnoreCase)))
                .Select(x => new WallpaperChoice(x.WallpaperAssetId, "已保存的壁纸（档案暂未找到）", string.Empty,
                    x.WallpaperPath, "保留原有选择，可重新选取壁纸", LoadThumbnail)))
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase));
        if (profile.IsGeneric)
        {
            Title = EditorTitle.Text = "编辑通用组合";
            EditorDescription.Text = "自由添加横屏与竖屏，再为每个位置选择壁纸。";
            ProfileKindText.Text = "◇ 通用组合";
            RequirementTitle.Text = "匹配所需屏幕";
            PriorityHint.Text = "多套通用组合数量都符合时，数值越高越优先。";
            CompatibleCheck.Visibility = Visibility.Collapsed;
            SpecificMatchingHelp.Visibility = Visibility.Collapsed;
            EnabledCheck.ToolTip = "勾选后，横屏和竖屏数量均完全一致且没有安全完整的特定匹配时，才使用此组合。";
            ApplyHint.Text = "两种方向的数量都完全一致，且没有特定匹配时，才会使用这个组合。";
            RulesText.Text = "同方向按桌面位置从左到右、同列从上到下编号。\n\n每类需求数量取最高编号。保留横屏1、横屏3，仍要求3块横屏。空缺编号和未选壁纸的位置保留当前壁纸。";
            GenericRequirementHint.Visibility = Visibility.Visible;
            SpecificActions.Visibility = Visibility.Collapsed;
            GenericActions.Visibility = Visibility.Visible;
            EmptyHint.Text = "添加横屏或竖屏，再为需要更换壁纸的位置选择图片。\n从0块横屏、0块竖屏开始，可先保存空白组合，稍后继续配置。";
        }
        else
        {
            Title = EditorTitle.Text = "编辑特定组合";
            ProfileKindText.Text = "特定组合";
        }
        foreach (var role in profile.IsGeneric ? profile.Roles.OrderBy(x => x.GenericOrientation).ThenBy(x => x.GenericIndex) : profile.Roles.AsEnumerable())
            _rows.Add(CreateRow(role.RoleId, role.Role, MonitorKey(role.Fingerprint),
                !string.IsNullOrWhiteSpace(role.WallpaperAssetId) ? role.WallpaperAssetId
                    : string.IsNullOrWhiteSpace(role.WallpaperPath) ? string.Empty : "path:" + role.WallpaperPath,
                role.FitMode, role.BackgroundColor, role.GenericOrientation, role.GenericIndex));
        RoleItems.ItemsSource = _rows;
        RefreshHint();
    }

    private WallpaperChoice CreateWallpaperChoice(WallpaperAsset asset)
    {
        var path = asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? asset.ExternalPath ?? string.Empty
            : Path.Combine(_runtime.Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var details = asset.IsMissing ? "图片文件不存在" : $"{asset.Width} × {asset.Height} · {asset.Format.ToUpperInvariant()}";
        return new WallpaperChoice(asset.Id, asset.IsMissing ? asset.DisplayName + "（文件不存在）" : asset.DisplayName,
            string.Empty, path, details, LoadThumbnail);
    }

    private ImageSource? LoadThumbnail(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_thumbnailCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? thumbnail = null;
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = _runtime.Settings.LowPerformanceMode ? 240 : 480;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                thumbnail = image;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Keep the saved selection visible even if its source cannot be decoded.
        }
        _thumbnailCache[path] = thumbnail;
        return thumbnail;
    }

    private IReadOnlyList<MonitorChoice> BuildMonitorChoices(IReadOnlyList<MonitorIdentity> current, WallpaperProfile profile)
    {
        var choices = new List<MonitorChoice> { new(string.Empty, "请选择显示器", null) };
        foreach (var monitor in current.Where(HasStrongIdentity))
            AddChoice(choices, new MonitorChoice(MonitorKey(monitor), $"{monitor.DisplayLabel} · {monitor.Width}×{monitor.Height} · 当前已连接", monitor.Clone()));
        foreach (var role in profile.Roles.Where(x => x.Fingerprint is not null))
            AddChoice(choices, new MonitorChoice(MonitorKey(role.Fingerprint), $"{role.Fingerprint.DisplayLabel} · 已保存身份（当前可能未连接）", role.Fingerprint.Clone()));
        return choices;
    }

    private static void AddChoice(List<MonitorChoice> choices, MonitorChoice choice)
    {
        if (string.IsNullOrWhiteSpace(choice.Key) || choices.Any(x => x.Key.Equals(choice.Key, StringComparison.OrdinalIgnoreCase))) return;
        choices.Add(choice);
    }

    private RoleRow CreateRow(string roleId, string role, string monitorKey, string wallpaperId, WallpaperFitMode fit, string background,
        GenericMonitorOrientation orientation = GenericMonitorOrientation.Landscape, int genericIndex = 0)
    {
        var row = new RoleRow(roleId, _profile.IsGeneric ? GenericProfileMatcher.GetSlotDisplayName(orientation, genericIndex) : role,
            monitorKey, wallpaperId, _profile.IsGeneric && fit == WallpaperFitMode.Span ? WallpaperFitMode.Fill : fit,
            background, _monitorChoices, _wallpaperChoices, _fitChoices, _profile.IsGeneric, orientation, genericIndex,
            _profile.IsGeneric ? CurrentMonitorHint(orientation, genericIndex) : string.Empty);
        row.PropertyChanged += Row_PropertyChanged;
        return row;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RoleRow.SelectedMonitorKey) or nameof(RoleRow.SelectedWallpaperId)) RefreshHint();
    }

    private string CurrentMonitorHint(GenericMonitorOrientation orientation, int index)
    {
        var monitor = _runtime.Monitors.Where(x => x.IsConnected && (x.Width >= x.Height) == (orientation == GenericMonitorOrientation.Landscape))
            .OrderBy(x => x.DesktopX).ThenBy(x => x.DesktopY).ThenBy(x => x.MonitorDevicePath, StringComparer.Ordinal)
            .Skip(Math.Max(0, index - 1)).FirstOrDefault();
        return monitor is null ? "当前没有对应位置的显示器；整套数量一致时才应用。"
            : $"当前位置：{monitor.DisplayLabel} · {monitor.Width} × {monitor.Height} · ({monitor.DesktopX}, {monitor.DesktopY})";
    }

    private void AddLandscape_Click(object sender, RoutedEventArgs e) => AddGenericSlot(GenericMonitorOrientation.Landscape);
    private void AddPortrait_Click(object sender, RoutedEventArgs e) => AddGenericSlot(GenericMonitorOrientation.Portrait);

    private void AddGenericSlot(GenericMonitorOrientation orientation)
    {
        if (!_profile.IsGeneric) return;
        var index = GenericProfileMatcher.GetNextAvailableIndex(_rows.Where(x => x.GenericOrientation == orientation).Select(x => x.GenericIndex));
        var insertIndex = _rows.Count(x => (int)x.GenericOrientation < (int)orientation || (x.GenericOrientation == orientation && x.GenericIndex < index));
        _rows.Insert(insertIndex, CreateRow(string.Empty, string.Empty, string.Empty, string.Empty, WallpaperFitMode.Fill, "#050B18", orientation, index));
        RefreshHint();
    }

    private void BrowseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is RoleRow row) ImportWallpaperForRow(row);
    }

    private bool ImportWallpaperForRow(RoleRow row)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择壁纸图片", Filter = "壁纸图片|*.jpg;*.jpeg;*.png;*.bmp", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return false;
        var extension = Path.GetExtension(dialog.FileName);
        if (!new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ShowEditorError("请选择 JPG、PNG 或 BMP 图片。");
            return false;
        }
        try
        {
            var asset = _runtime.ImportWallpaper(dialog.FileName);
            if (_wallpaperChoices.All(x => !x.Id.Equals(asset.Id, StringComparison.OrdinalIgnoreCase)))
                _wallpaperChoices.Add(CreateWallpaperChoice(asset));
            row.SelectedWallpaperId = asset.Id;
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
            StatusText.Text = $"已为 {row.Role} 选择 {asset.DisplayName}；保存组合后生效。";
            return true;
        }
        catch (Exception ex)
        {
            ShowEditorError(ex.Message);
            return false;
        }
    }

    private void OpenWallpaperPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: RoleRow row } trigger) return;
        _pickerRow = row;
        _pickerTrigger = trigger;
        WallpaperPickerDescription.Text = $"为“{row.Role}”选择壁纸，点击图片即可使用。";
        PickerStatusText.Visibility = Visibility.Collapsed;
        ClearWallpaperButton.Content = _profile.IsGeneric ? "保留当前壁纸" : "暂不选择壁纸";
        WallpaperSearchText.Text = string.Empty;
        RefreshWallpaperPicker();
        EditorContent.IsEnabled = false;
        WallpaperPicker.Visibility = Visibility.Visible;
        WallpaperSearchText.Focus();
    }

    private void WallpaperSearchText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (WallpaperPicker?.Visibility == Visibility.Visible) RefreshWallpaperPicker();
    }

    private void RefreshWallpaperPicker()
    {
        var query = WallpaperSearchText.Text.Trim();
        var choices = _wallpaperChoices.Where(choice => query.Length == 0 || choice.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        WallpaperPickerItems.ItemsSource = choices;
        WallpaperPickerEmpty.Visibility = choices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        WallpaperPickerEmpty.Text = query.Length == 0 ? "档案库中还没有可选壁纸。点击“浏览并导入”添加图片。" : "没有找到这个名称的壁纸，可更换搜索文字。";
    }

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerRow is null || (sender as System.Windows.Controls.Button)?.Tag is not WallpaperChoice choice) return;
        _pickerRow.SelectedWallpaperId = choice.Id;
        CloseWallpaperPicker();
    }

    private void ClearWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerRow is not null) _pickerRow.SelectedWallpaperId = string.Empty;
        CloseWallpaperPicker();
    }

    private void ImportPickerWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerRow is not null && ImportWallpaperForRow(_pickerRow)) CloseWallpaperPicker();
    }

    private void CloseWallpaperPicker_Click(object sender, RoutedEventArgs e) => CloseWallpaperPicker();

    private void CloseWallpaperPicker()
    {
        WallpaperPicker.Visibility = Visibility.Collapsed;
        EditorContent.IsEnabled = true;
        WallpaperPickerItems.ItemsSource = null;
        _pickerRow = null;
        _pickerTrigger?.Focus();
        _pickerTrigger = null;
    }

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
        if ((sender as System.Windows.Controls.Button)?.Tag is RoleRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
            _rows.Remove(row);
        }
        RefreshHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PriorityText.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var priority))
        {
            ShowEditorError("匹配优先级需要填写整数。");
            PriorityText.Focus();
            PriorityText.SelectAll();
            return;
        }
        try
        {
            var draft = new WallpaperProfileEditDraft
            {
                Name = NameText.Text,
                MatchMode = _profile.MatchMode,
                Enabled = EnabledCheck.IsChecked == true,
                Priority = priority,
                AllowCompatibleMatch = !_profile.IsGeneric && CompatibleCheck.IsChecked == true,
                MinimumConfidence = _profile.MinimumConfidence,
                Roles = _rows.Select(row =>
                {
                    var monitor = _monitorChoices.FirstOrDefault(x => x.Key.Equals(row.SelectedMonitorKey ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    var wallpaper = _wallpaperChoices.FirstOrDefault(x => x.Id.Equals(row.SelectedWallpaperId, StringComparison.OrdinalIgnoreCase));
                    return new WallpaperRoleEditDraft
                    {
                        RoleId = row.RoleId,
                        Role = row.Role,
                        DisplayName = row.Role,
                        GenericOrientation = row.GenericOrientation,
                        GenericIndex = row.GenericIndex,
                        Fingerprint = monitor?.Fingerprint?.Clone(),
                        WallpaperAssetId = string.IsNullOrEmpty(wallpaper?.ExternalPath) ? row.SelectedWallpaperId ?? string.Empty : string.Empty,
                        WallpaperPath = wallpaper?.ExternalPath ?? string.Empty,
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
            ShowEditorError(ex.Message);
        }
    }

    private void ShowEditorError(string message)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Cyan");
        if (WallpaperPicker.Visibility != Visibility.Visible) return;
        PickerStatusText.Text = message;
        PickerStatusText.Visibility = Visibility.Visible;
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
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var configured = _rows.Count(row => !string.IsNullOrWhiteSpace(row.SelectedWallpaperId));
        if (_profile.IsGeneric)
        {
            var landscape = _rows.Where(x => x.GenericOrientation == GenericMonitorOrientation.Landscape).Select(x => x.GenericIndex).DefaultIfEmpty(0).Max();
            var portrait = _rows.Where(x => x.GenericOrientation == GenericMonitorOrientation.Portrait).Select(x => x.GenericIndex).DefaultIfEmpty(0).Max();
            var currentLandscape = _runtime.Monitors.Count(x => x.IsConnected && x.Width >= x.Height);
            var currentPortrait = _runtime.Monitors.Count(x => x.IsConnected && x.Width < x.Height);
            var countsMatch = landscape == currentLandscape && portrait == currentPortrait;
            LandscapeCountText.Text = landscape.ToString(CultureInfo.CurrentCulture);
            PortraitCountText.Text = portrait.ToString(CultureInfo.CurrentCulture);
            GenericRequirementHint.Text = $"当前连接 {currentLandscape} 横屏、{currentPortrait} 竖屏。\n"
                + (_rows.Count == 0 ? "空白组合不会应用。" : countsMatch ? configured > 0 ? "数量一致，已配置壁纸。" : "数量一致，尚未选择壁纸。" : "数量不符，整套组合不会应用。");
            StatusText.Text = _rows.Count == 0 ? "从0块屏幕开始，按需添加，数量不限。"
                : $"已选择 {configured} 张壁纸。空缺编号和未选壁纸的位置保持原样。";
        }
        else
        {
            var assigned = _rows.Select(row => _monitorChoices.FirstOrDefault(choice => choice.Key.Equals(row.SelectedMonitorKey, StringComparison.OrdinalIgnoreCase))?.Fingerprint).Where(x => x is not null).ToArray();
            LandscapeCountText.Text = assigned.Count(x => x!.Width >= x.Height).ToString(CultureInfo.CurrentCulture);
            PortraitCountText.Text = assigned.Count(x => x!.Width < x.Height).ToString(CultureInfo.CurrentCulture);
            StatusText.Text = _rows.Count == 0 ? "空白组合可先保存，稍后继续添加屏幕。" : $"{_rows.Count} 个角色，已选择 {configured} 张壁纸；保存后按显示器身份匹配。";
        }
    }

    private void SlotsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // One or two cards per row. Reserve scrollbar room instead of clipping
        // controls when the dialog is resized or shown with larger DPI text.
        var available = Math.Max(240, e.NewSize.Width - 24);
        SlotCardWidth = available >= 610 ? available / 2 : available;
    }

    private static bool HasStrongIdentity(MonitorIdentity monitor) => !string.IsNullOrWhiteSpace(MonitorKey(monitor));

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
        WallpaperFitMode.Fill => "填充", WallpaperFitMode.Fit => "适应", WallpaperFitMode.Stretch => "拉伸",
        WallpaperFitMode.Center => "居中", WallpaperFitMode.Tile => "平铺", WallpaperFitMode.Span => "跨区", _ => mode.ToString()
    };

    private void ToggleMatchingRules_Click(object sender, RoutedEventArgs e)
    {
        var expanded = MatchingRulesDetails.Visibility != Visibility.Visible;
        MatchingRulesDetails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MatchingRulesButton.Content = expanded ? "收起匹配规则" : "查看完整匹配规则";
    }

    private void NestedControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => MouseWheelRouting.ForwardToAncestorScrollViewer(sender, e);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return;
        if (WallpaperPicker.Visibility == Visibility.Visible) CloseWallpaperPicker();
        else Close();
        e.Handled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record MonitorChoice(string Key, string Label, MonitorIdentity? Fingerprint);
    private sealed record FitChoice(WallpaperFitMode Value, string Label);
    private sealed record WallpaperChoice(string Id, string Label, string ExternalPath, string SourcePath, string Details, Func<string, ImageSource?> Load)
    {
        public ImageSource? Thumbnail => Load(SourcePath);
        public string Placeholder => Thumbnail is null ? "图片暂不可用" : string.Empty;
    }

    private sealed class RoleRow : INotifyPropertyChanged
    {
        private string _role;
        private string? _selectedMonitorKey;
        private string? _selectedWallpaperId;
        public RoleRow(string roleId, string role, string monitorKey, string wallpaperId, WallpaperFitMode fitMode, string backgroundColor,
            IReadOnlyList<MonitorChoice> monitors, IReadOnlyList<WallpaperChoice> wallpapers, IReadOnlyList<FitChoice> fits,
            bool isGeneric, GenericMonitorOrientation orientation, int genericIndex, string currentMonitorHint)
        {
            RoleId = roleId;
            _role = role;
            _selectedMonitorKey = monitorKey;
            _selectedWallpaperId = wallpaperId;
            FitMode = fitMode;
            BackgroundColor = backgroundColor;
            MonitorChoices = monitors;
            WallpaperChoices = wallpapers;
            FitChoices = fits;
            IsGeneric = isGeneric;
            GenericOrientation = orientation;
            GenericIndex = genericIndex;
            CurrentMonitorHint = currentMonitorHint;
        }
        public string RoleId { get; }
        public string Role
        {
            get => _role;
            set { if (_role == value) return; _role = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role))); }
        }
        public string? SelectedMonitorKey
        {
            get => _selectedMonitorKey;
            set { if (_selectedMonitorKey == value) return; _selectedMonitorKey = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitorKey))); }
        }
        public string? SelectedWallpaperId
        {
            get => _selectedWallpaperId;
            set
            {
                if (_selectedWallpaperId == value) return;
                _selectedWallpaperId = value;
                foreach (var property in new[] { nameof(SelectedWallpaperId), nameof(Thumbnail), nameof(PlaceholderVisibility), nameof(PreviewPlaceholder), nameof(WallpaperCaption), nameof(WallpaperStatus) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
            }
        }
        private WallpaperChoice? Wallpaper => WallpaperChoices.FirstOrDefault(choice => choice.Id.Equals(SelectedWallpaperId, StringComparison.OrdinalIgnoreCase));
        public ImageSource? Thumbnail => Wallpaper?.Thumbnail;
        public Visibility PlaceholderVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        public string PreviewPlaceholder => string.IsNullOrWhiteSpace(SelectedWallpaperId) ? "＋ 选择壁纸" : "图片暂不可用";
        public string WallpaperCaption => string.IsNullOrWhiteSpace(SelectedWallpaperId) ? IsGeneric ? "此屏幕保留当前壁纸" : "尚未选择壁纸" : (Wallpaper?.Label ?? "已保存的壁纸") + " · 更换壁纸";
        public string WallpaperStatus => string.IsNullOrWhiteSpace(SelectedWallpaperId) ? IsGeneric ? "不更改此屏幕当前壁纸" : "选择壁纸后完成此角色配置" : Thumbnail is null ? "图片暂不可用，可重新选择" : Wallpaper?.Details ?? "壁纸随对应屏幕恢复";
        public bool IsGeneric { get; }
        public GenericMonitorOrientation GenericOrientation { get; }
        public int GenericIndex { get; }
        public string CurrentMonitorHint { get; }
        public string WallpaperHint => IsGeneric ? "从壁纸档案库选择；不选择时保留对应屏幕当前壁纸" : "从壁纸档案库选择此显示器的壁纸";
        public Visibility SpecificVisibility => IsGeneric ? Visibility.Collapsed : Visibility.Visible;
        public Visibility GenericVisibility => IsGeneric ? Visibility.Visible : Visibility.Collapsed;
        public WallpaperFitMode FitMode { get; set; }
        public string BackgroundColor { get; }
        public IReadOnlyList<MonitorChoice> MonitorChoices { get; }
        public IReadOnlyList<WallpaperChoice> WallpaperChoices { get; }
        public IReadOnlyList<FitChoice> FitChoices { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
