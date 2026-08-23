using SyncWallpaper.Core;

namespace SyncWallpaper.WindowEngine;

public static class WindowZoneLayoutFactory
{
    public static WindowZoneLayout Create(string name, MonitorIdentity monitor, WindowZonePreset preset)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (string.IsNullOrWhiteSpace(monitor.StableId)
            || monitor.StableIdSource is MonitorIdentitySource.Unknown
                or MonitorIdentitySource.Ambiguous
                or MonitorIdentitySource.Geometry
                or MonitorIdentitySource.ContainerId)
            throw new InvalidOperationException("该显示器没有足够稳定的身份，不能自动绑定窗口区域。请先确认显示器身份。");

        var zones = preset switch
        {
            WindowZonePreset.TwoColumns => Columns(2),
            WindowZonePreset.ThreeColumns => Columns(3),
            WindowZonePreset.TwoRows => Rows(2),
            WindowZonePreset.Grid2X2 => Grid(2, 2),
            WindowZonePreset.PrimaryAndStack => PrimaryAndStack(monitor.Width < monitor.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        var now = DateTime.UtcNow;
        return new WindowZoneLayout
        {
            Name = string.IsNullOrWhiteSpace(name) ? PresetName(preset) : name.Trim(),
            Preset = preset,
            TargetMonitor = monitor.Clone(),
            CreatedAt = now,
            ModifiedAt = now,
            Zones = zones
        };
    }

    public static string PresetName(WindowZonePreset preset) => preset switch
    {
        WindowZonePreset.TwoColumns => "左右二等分",
        WindowZonePreset.ThreeColumns => "横向三等分",
        WindowZonePreset.TwoRows => "上下二等分",
        WindowZonePreset.Grid2X2 => "四宫格",
        WindowZonePreset.PrimaryAndStack => "主区 + 双副区",
        _ => preset.ToString()
    };

    private static List<WindowZone> Columns(int count)
        => Enumerable.Range(0, count).Select(i => Zone($"区域 {i + 1}", i / (double)count, 0, 1d / count, 1)).ToList();

    private static List<WindowZone> Rows(int count)
        => Enumerable.Range(0, count).Select(i => Zone($"区域 {i + 1}", 0, i / (double)count, 1, 1d / count)).ToList();

    private static List<WindowZone> Grid(int columns, int rows)
    {
        var result = new List<WindowZone>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            result.Add(Zone($"区域 {result.Count + 1}", column / (double)columns, row / (double)rows, 1d / columns, 1d / rows));
        return result;
    }

    private static List<WindowZone> PrimaryAndStack(bool portrait)
        => portrait
            ? new()
            {
                Zone("主区域", 0, 0, 1, 2d / 3),
                Zone("副区域 1", 0, 2d / 3, .5, 1d / 3),
                Zone("副区域 2", .5, 2d / 3, .5, 1d / 3)
            }
            : new()
            {
                Zone("主区域", 0, 0, 2d / 3, 1),
                Zone("副区域 1", 2d / 3, 0, 1d / 3, .5),
                Zone("副区域 2", 2d / 3, .5, 1d / 3, .5)
            };

    private static WindowZone Zone(string name, double left, double top, double width, double height)
        => new() { Name = name, Left = left, Top = top, Width = width, Height = height };
}

public static class WindowZoneLayoutValidator
{
    public static WindowZoneValidationResult Validate(WindowZoneLayout? layout)
    {
        var errors = new List<string>();
        if (layout is null) return new() { Errors = new[] { "区域布局为空。" } };
        if (layout.TargetMonitor is null || string.IsNullOrWhiteSpace(layout.TargetMonitor.StableId))
            errors.Add("缺少稳定显示器身份。");
        if (layout.Zones is null || layout.Zones.Count == 0)
            errors.Add("布局至少需要一个区域。");
        else if (layout.Zones.Count > 16)
            errors.Add("单个显示器最多支持 16 个区域。");

        var zones = layout.Zones ?? new List<WindowZone>();
        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!Finite(zone.Left) || !Finite(zone.Top) || !Finite(zone.Width) || !Finite(zone.Height)
                || zone.Left < 0 || zone.Top < 0 || zone.Width <= 0 || zone.Height <= 0
                || zone.Left + zone.Width > 1.000001 || zone.Top + zone.Height > 1.000001)
                errors.Add($"区域 {i + 1} 超出显示器的标准化边界。");
            for (var j = i + 1; j < zones.Count; j++)
            {
                if (Overlaps(zone, zones[j])) errors.Add($"区域 {i + 1} 与区域 {j + 1} 重叠。");
            }
        }
        return new() { Errors = errors.Distinct(StringComparer.Ordinal).ToArray() };
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Overlaps(WindowZone left, WindowZone right)
        => left.Left < right.Left + right.Width - 0.000001
            && left.Left + left.Width > right.Left + 0.000001
            && left.Top < right.Top + right.Height - 0.000001
            && left.Top + left.Height > right.Top + 0.000001;
}

public sealed class WindowZoneSnapService
{
    private readonly IWindowZonePlatform _platform;
    private readonly DisplayIdentityMatcher _identityMatcher = new();

    public WindowZoneSnapService(IWindowZonePlatform platform) => _platform = platform;

    public WindowZoneSnapResult TrySnap(
        IntPtr windowHandle,
        Int32Point pointer,
        WindowZoneLayoutsDocument document,
        IReadOnlyList<MonitorIdentity> monitors)
    {
        if (!document.ShiftDragEnabled)
            return Result(WindowZoneSnapStatus.Disabled, "Shift 拖动吸附已关闭。");
        var window = _platform.TryGetWindow(windowHandle);
        if (window is null)
            return Result(WindowZoneSnapStatus.NoWindow, "窗口已关闭或不允许移动。");
        if (window.Identity.IsElevated)
            return Result(WindowZoneSnapStatus.ElevatedWindow, "为避免越权操作，已跳过高权限窗口。");

        var monitor = monitors.FirstOrDefault(x => Contains(x, pointer));
        if (monitor is null)
            return Result(WindowZoneSnapStatus.NoMonitor, "指针不在活动显示器内。");

        var candidates = new List<WindowZoneLayout>();
        var ambiguous = false;
        foreach (var layout in document.Layouts.Where(x => x.Enabled))
        {
            var validation = WindowZoneLayoutValidator.Validate(layout);
            if (!validation.IsValid) continue;
            var match = _identityMatcher.Match(layout.TargetMonitor, monitors);
            if (match.Status == DisplayIdentityMatchStatus.Ambiguous)
            {
                if (match.TiedCandidates.Any(x => SameMonitor(x, monitor))) ambiguous = true;
                continue;
            }
            if (match.CanAutoApply && match.Monitor is not null && SameMonitor(match.Monitor, monitor))
                candidates.Add(layout);
        }

        if (candidates.Count == 0)
            return ambiguous
                ? Result(WindowZoneSnapStatus.AmbiguousMonitor, "显示器身份存在歧义，未自动猜测区域布局。")
                : Result(WindowZoneSnapStatus.NoLayout, "该显示器尚未配置窗口区域。");

        var selected = candidates.OrderByDescending(x => x.ModifiedAt).ThenBy(x => x.Id, StringComparer.Ordinal).First();
        var normalizedX = (pointer.X - monitor.DesktopX) / (double)Math.Max(1, monitor.Width);
        var normalizedY = (pointer.Y - monitor.DesktopY) / (double)Math.Max(1, monitor.Height);
        var zone = selected.Zones.FirstOrDefault(x => Contains(x, normalizedX, normalizedY));
        if (zone is null)
            return Result(WindowZoneSnapStatus.NoZone, "指针位置没有对应区域。", selected.Id);

        var bounds = CalculateBounds(zone, monitor, document.GapPixels);
        if (!_platform.TrySetPosition(window, bounds, maximize: false))
            return Result(WindowZoneSnapStatus.MoveFailed, "Windows 拒绝移动该窗口。", selected.Id, zone.Id, bounds);
        return Result(WindowZoneSnapStatus.Applied, $"已吸附到“{selected.Name} / {zone.Name}”。", selected.Id, zone.Id, bounds);
    }

    public static Int32Rect CalculateBounds(WindowZone zone, MonitorIdentity monitor, int gapPixels)
    {
        var gap = Math.Clamp(gapPixels, 0, Math.Max(0, Math.Min(monitor.Width, monitor.Height) / 4));
        var insetBefore = gap / 2;
        var insetAfter = gap - insetBefore;
        var rawLeft = monitor.DesktopX + (int)Math.Round(zone.Left * monitor.Width);
        var rawTop = monitor.DesktopY + (int)Math.Round(zone.Top * monitor.Height);
        var rawRight = monitor.DesktopX + (int)Math.Round((zone.Left + zone.Width) * monitor.Width);
        var rawBottom = monitor.DesktopY + (int)Math.Round((zone.Top + zone.Height) * monitor.Height);
        var left = rawLeft + insetBefore;
        var top = rawTop + insetBefore;
        var right = Math.Max(left + 80, rawRight - insetAfter);
        var bottom = Math.Max(top + 40, rawBottom - insetAfter);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static bool Contains(MonitorIdentity monitor, Int32Point point)
        => point.X >= monitor.DesktopX && point.X < monitor.DesktopX + monitor.Width
            && point.Y >= monitor.DesktopY && point.Y < monitor.DesktopY + monitor.Height;

    private static bool Contains(WindowZone zone, double x, double y)
        => x + 0.000001 >= zone.Left && x <= zone.Left + zone.Width + 0.000001
            && y + 0.000001 >= zone.Top && y <= zone.Top + zone.Height + 0.000001;

    private static bool SameMonitor(MonitorIdentity left, MonitorIdentity right)
        => ReferenceEquals(left, right)
            || (!string.IsNullOrWhiteSpace(left.StableId) && left.StableId.Equals(right.StableId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(left.MonitorDevicePath) && left.MonitorDevicePath.Equals(right.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));

    private static WindowZoneSnapResult Result(WindowZoneSnapStatus status, string message, string? layout = null, string? zone = null, Int32Rect? bounds = null)
        => new() { Status = status, Message = message, LayoutId = layout, ZoneId = zone, Bounds = bounds };
}

/// <summary>
/// Observes a Shift-assisted native move operation. It owns no timer or
/// additional hook: stopping Window Engine unsubscribes it from the engine's
/// existing WinEvent source.
/// </summary>
public sealed class WindowZoneSnapController : IDisposable
{
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjIdWindow = 0;

    private readonly IWindowEventSource _events;
    private readonly IWindowZonePlatform _platform;
    private readonly WindowZoneSnapService _service;
    private readonly Func<WindowZoneLayoutsDocument> _document;
    private readonly Func<IReadOnlyList<MonitorIdentity>> _monitors;
    private readonly Action<WindowZoneSnapResult>? _completed;
    private readonly object _gate = new();
    private IntPtr _movingWindow;
    private bool _shiftObserved;
    private Int32Point? _lastPointer;
    private bool _disposed;

    public WindowZoneSnapController(
        IWindowEventSource events,
        IWindowZonePlatform platform,
        Func<WindowZoneLayoutsDocument> document,
        Func<IReadOnlyList<MonitorIdentity>> monitors,
        Action<WindowZoneSnapResult>? completed = null)
    {
        _events = events;
        _platform = platform;
        _service = new WindowZoneSnapService(platform);
        _document = document;
        _monitors = monitors;
        _completed = completed;
        _events.EventReceived += OnWindowEvent;
    }

    private void OnWindowEvent(object? sender, WindowEvent e)
    {
        if (e.WindowHandle == IntPtr.Zero || (e.ObjectId != ObjIdWindow && e.EventType == EventObjectLocationChange)) return;
        WindowZoneSnapResult? result = null;
        lock (_gate)
        {
            if (_disposed) return;
            if (e.EventType == EventSystemMoveSizeStart)
            {
                _movingWindow = e.WindowHandle;
                _shiftObserved = _platform.IsShiftPressed();
                _lastPointer = _platform.GetCursorPosition();
                return;
            }
            if (e.EventType == EventObjectDestroy && e.WindowHandle == _movingWindow)
            {
                Reset();
                return;
            }
            if (e.WindowHandle != _movingWindow) return;
            if (e.EventType == EventObjectLocationChange)
            {
                if (_platform.IsShiftPressed()) _shiftObserved = true;
                _lastPointer = _platform.GetCursorPosition() ?? _lastPointer;
                return;
            }
            if (e.EventType != EventSystemMoveSizeEnd) return;
            if (_platform.IsShiftPressed()) _shiftObserved = true;
            var pointer = _platform.GetCursorPosition() ?? _lastPointer;
            if (_shiftObserved && pointer is { } position)
                result = _service.TrySnap(_movingWindow, position, _document(), _monitors());
            Reset();
        }
        if (result is not null) _completed?.Invoke(result);
    }

    private void Reset()
    {
        _movingWindow = IntPtr.Zero;
        _shiftObserved = false;
        _lastPointer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
            _events.EventReceived -= OnWindowEvent;
        }
    }
}
