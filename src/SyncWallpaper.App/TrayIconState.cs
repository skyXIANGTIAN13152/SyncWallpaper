namespace SyncWallpaper.App;

/// <summary>
/// Hollow landscape/portrait screens share the application's antique-gold
/// identity; a small separate mark distinguishes each notification-area state.
/// </summary>
public enum TrayIconState
{
    Normal,
    Paused,
    Recognizing,
    Error,
    Generic,
    Unmatched
}

internal static class TrayIconStateResolver
{
    public static TrayIconState Resolve(bool isRecognizing, bool autoMatchEnabled, string statusText, bool isGenericMatch)
    {
        if (isRecognizing) return TrayIconState.Recognizing;
        if (!autoMatchEnabled || statusText is "已暂停" or "安全模式"
            || statusText.StartsWith("验证模式", StringComparison.Ordinal))
            return TrayIconState.Paused;
        if (statusText is "保持当前壁纸" or "未发现显示器" or "监测中" or "等待显示器检测")
            return TrayIconState.Unmatched;
        if (statusText is "需要确认" or "应用未完成" or "应用失败")
            return TrayIconState.Error;
        return statusText == "通用匹配" && isGenericMatch ? TrayIconState.Generic : TrayIconState.Normal;
    }
}
