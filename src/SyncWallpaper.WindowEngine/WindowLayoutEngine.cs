using System.Text.RegularExpressions;
using SyncWallpaper.Core;

namespace SyncWallpaper.WindowEngine;

public sealed class WindowIdentityMatcher : IWindowIdentityMatcher
{
    public bool IsMatch(WindowIdentity saved, WindowIdentity current, out WindowMatchKind? matchedBy)
    {
        matchedBy = null;
        if (!Matches(saved.UserRuleId, current.UserRuleId, WindowMatchKind.UserRuleId, ref matchedBy)) return false;
        if (!Matches(saved.ExecutablePath, current.ExecutablePath, WindowMatchKind.ExecutablePath, ref matchedBy, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Matches(saved.AppUserModelId, current.AppUserModelId, WindowMatchKind.AppUserModelId, ref matchedBy, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Matches(saved.ProcessName, current.ProcessName, WindowMatchKind.ProcessName, ref matchedBy, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Matches(saved.WindowClass, current.WindowClass, WindowMatchKind.WindowClass, ref matchedBy, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(saved.WindowTitle))
        {
            if (matchedBy is null) return false; // title-only matching is intentionally refused
            if (!PatternMatches(saved.WindowTitle, current.WindowTitle)) return false;
            matchedBy = WindowMatchKind.TitlePattern;
        }
        return matchedBy is not null;
    }

    private static bool Matches(string expected, string actual, WindowMatchKind kind, ref WindowMatchKind? matchedBy, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        if (!string.Equals(expected, actual, comparison)) return false;
        matchedBy ??= kind;
        return true;
    }

    private static bool PatternMatches(string pattern, string value)
    {
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            try { return Regex.IsMatch(value, pattern[6..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch { return false; }
        }
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

public sealed class WindowLayoutEngine : IWindowLayoutEngine
{
    private readonly IWindowPlatform _platform;
    private readonly IWindowIdentityMatcher _matcher;

    public WindowLayoutEngine(IWindowPlatform platform, IWindowIdentityMatcher? matcher = null)
    {
        _platform = platform; _matcher = matcher ?? new WindowIdentityMatcher();
    }

    public IReadOnlyList<WindowPositionSnapshot> Capture() => _platform.Enumerate();

    public WindowRestoreResult Restore(WindowPositionProfile profile, IReadOnlyList<MonitorIdentity> monitors, WindowRestoreOptions? options = null)
    {
        options ??= new WindowRestoreOptions { StartUnlaunchedApplications = profile.RestoreUnlaunchedApplications };
        var windows = _platform.Enumerate();
        var used = new HashSet<IntPtr>();
        var reasons = new List<string>();
        var applied = 0;
        var matched = 0;
        foreach (var placement in profile.Windows)
        {
            var current = windows.FirstOrDefault(x => !used.Contains(x.Handle) && Match(placement, x.Identity, out _));
            if (current is null)
            {
                if (options.StartUnlaunchedApplications && placement.AllowLaunch && IsSafeLaunchPath(placement.ProcessPath))
                {
                    if (!_platform.TryStartApplication(placement.ProcessPath, placement.LaunchArguments))
                        reasons.Add($"启动失败：{placement.ProcessPath}");
                    else reasons.Add($"已请求启动并等待：{placement.ProcessPath}");
                }
                else reasons.Add($"未找到窗口：{placement.ProcessPath} {placement.WindowClass}");
                continue;
            }
            matched++;
            used.Add(current.Handle);
            if (current.Identity.IsElevated && !placement.IsElevated)
            {
                reasons.Add($"跳过高权限窗口：{current.Identity.WindowTitle}");
                continue;
            }
            var targetMonitor = monitors.FirstOrDefault(x => x.MonitorDevicePath.Equals(placement.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
                ?? monitors.FirstOrDefault(x => x.IsPrimary)
                ?? monitors.FirstOrDefault();
            if (targetMonitor is null)
            {
                reasons.Add($"没有可用显示器：{placement.MonitorDevicePath}");
                continue;
            }
            var bounds = CalculateBounds(placement, targetMonitor, current.Dpi);
            bounds = KeepVisible(bounds, monitors);
            if (!_platform.TrySetPosition(current, bounds, placement.Maximize))
            {
                reasons.Add($"恢复窗口失败：{current.Identity.WindowTitle}");
                continue;
            }
            applied++;
        }
        return new WindowRestoreResult { Matched = matched, Applied = applied, Skipped = profile.Windows.Count - applied, Reasons = reasons };
    }

    private bool Match(WindowPlacement placement, WindowIdentity identity, out WindowMatchKind? kind)
    {
        return _matcher.IsMatch(new WindowIdentity
        {
            UserRuleId = placement.RuleId,
            ExecutablePath = placement.ProcessPath,
            ProcessName = placement.ProcessName,
            AppUserModelId = placement.AppUserModelId,
            WindowClass = placement.WindowClass,
            WindowTitle = placement.TitlePattern,
            IsUwp = placement.IsUwp,
            IsElevated = placement.IsElevated
        }, identity, out kind);
    }

    private static Int32Rect CalculateBounds(WindowPlacement placement, MonitorIdentity target, int currentDpi)
    {
        var sourceWidth = Math.Max(1, placement.SavedMonitorWidth);
        var sourceHeight = Math.Max(1, placement.SavedMonitorHeight);
        var xRatio = (placement.Left - placement.SavedMonitorX) / (double)sourceWidth;
        var yRatio = (placement.Top - placement.SavedMonitorY) / (double)sourceHeight;
        var widthRatio = placement.Width / (double)sourceWidth;
        var heightRatio = placement.Height / (double)sourceHeight;
        var dpiScale = placement.Dpi > 0 && currentDpi > 0 ? currentDpi / (double)placement.Dpi : 1.0;
        var width = Math.Max(80, (int)Math.Round(target.Width * widthRatio * dpiScale));
        var height = Math.Max(40, (int)Math.Round(target.Height * heightRatio * dpiScale));
        var left = target.DesktopX + (int)Math.Round(target.Width * xRatio);
        var top = target.DesktopY + (int)Math.Round(target.Height * yRatio);
        return new Int32Rect(left, top, width, height);
    }

    private static Int32Rect KeepVisible(Int32Rect bounds, IReadOnlyList<MonitorIdentity> monitors)
    {
        if (monitors.Count == 0) return bounds;
        var minX = monitors.Min(x => x.DesktopX);
        var minY = monitors.Min(x => x.DesktopY);
        var maxX = monitors.Max(x => x.DesktopX + x.Width);
        var maxY = monitors.Max(x => x.DesktopY + x.Height);
        var left = Math.Clamp(bounds.Left, minX - bounds.Width + 80, maxX - 80);
        var top = Math.Clamp(bounds.Top, minY - bounds.Height + 40, maxY - 40);
        return new Int32Rect(left, top, Math.Min(bounds.Width, maxX - minX), Math.Min(bounds.Height, maxY - minY));
    }

    private static bool IsSafeLaunchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)) return false;
        var temp = Path.GetTempPath();
        if (path.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return false;
        var system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !path.StartsWith(system, StringComparison.OrdinalIgnoreCase);
    }
}
