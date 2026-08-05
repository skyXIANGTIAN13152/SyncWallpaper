using SyncWallpaper.Core;

namespace SyncWallpaper.DesktopEngine;

public sealed class DesktopIconLayoutEngine : IDesktopIconLayoutEngine
{
    private readonly IDesktopIconProvider _provider;
    public DesktopIconLayoutEngine(IDesktopIconProvider provider) => _provider = provider;

    public DesktopIconProfile Capture(string name)
    {
        var profile = new DesktopIconProfile { Name = name };
        foreach (var item in _provider.Capture())
        {
            var key = !string.IsNullOrWhiteSpace(item.ParsingName) ? item.ParsingName :
                (!string.IsNullOrWhiteSpace(item.PidlBase64) ? item.PidlBase64 : item.DesktopPath);
            if (!string.IsNullOrWhiteSpace(key)) profile.Positions[key] = item;
        }
        return profile;
    }

    public DesktopIconRestoreResult Restore(DesktopIconProfile profile, Int32Rect virtualBounds)
    {
        var applied = 0;
        var skipped = 0;
        var reasons = new List<string>();
        foreach (var position in profile.Positions.Values)
        {
            if (string.IsNullOrWhiteSpace(position.ParsingName) && string.IsNullOrWhiteSpace(position.PidlBase64) && string.IsNullOrWhiteSpace(position.DesktopPath))
            {
                skipped++;
                reasons.Add("跳过没有稳定 Shell 标识的桌面图标。");
                continue;
            }
            var safe = new DesktopIconPosition
            {
                ParsingName = position.ParsingName,
                DisplayName = position.DisplayName,
                PidlBase64 = position.PidlBase64,
                DesktopPath = position.DesktopPath,
                MonitorDevicePath = position.MonitorDevicePath,
                X = Math.Clamp(position.X, virtualBounds.Left, virtualBounds.Left + Math.Max(0, virtualBounds.Width - 1)),
                Y = Math.Clamp(position.Y, virtualBounds.Top, virtualBounds.Top + Math.Max(0, virtualBounds.Height - 1))
            };
            if (_provider.TrySetPosition(safe)) applied++;
            else { skipped++; reasons.Add($"未找到桌面项目：{position.DisplayName}"); }
        }
        if (!_provider.TrySetViewSettings(profile.IconSize, profile.AutoArrange, profile.AlignToGrid))
            reasons.Add("图标视图设置无法通过 Shell 接口验证。");
        return new DesktopIconRestoreResult { Applied = applied, Skipped = skipped, Reasons = reasons };
    }
}
