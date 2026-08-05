using System;
using System.IO;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WallpaperApplyService
{
    private readonly WallpaperRenderService _renderer;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private WallpaperTransactionStatus _lastTransaction = new(0, WallpaperTransactionState.Completed, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true, "尚未执行壁纸事务");
    public WallpaperApplyService(WallpaperRenderService renderer, Action<string>? log = null) { _renderer = renderer; _log = log ?? (_ => { }); }
    public WallpaperTransactionStatus LastTransaction => _lastTransaction;
    public event EventHandler<WallpaperTransactionStatus>? TransactionChanged;

    public async Task<ApplyResult> ApplyAsync(MatchResult match, IReadOnlyList<WallpaperAsset> assets, DataPaths paths, CancellationToken cancellationToken = default, long generation = 0, bool manual = false)
    {
        var started = DateTime.UtcNow;
        var state = new WallpaperTransactionStateMachine();
        var expected = match.Profile?.Roles.Count ?? 0;
        PublishTransaction(generation, state.Current, started, null, 0, expected, 0, true, "准备壁纸事务");
        if (match.Status is MatchStatus.Ambiguous or MatchStatus.NoMatch || !match.CanAutoApply || match.Profile is null)
        {
            state.Transition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, match.Message);
            return new ApplyResult(false, match.Message, 0);
        }
        try { await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            state.Transition(WallpaperTransactionState.Cancelled);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, "壁纸事务在开始前取消");
            return new ApplyResult(false, "壁纸事务已取消", 0);
        }
        IDesktopWallpaper? desktop = null;
        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var applied = 0;
        var failed = false;
        var missing = 0;
        var rollbackSucceeded = true;
        var retries = 0;
        try
        {
            state.Transition(WallpaperTransactionState.WaitingForStableTopology);
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "等待稳定显示器拓扑");
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            var count = desktop.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var path = desktop.GetMonitorDevicePathAt(i);
                if (string.IsNullOrWhiteSpace(path)) continue;
                currentPaths.Add(path);
                try { previous[path] = desktop.GetWallpaper(path) ?? string.Empty; }
                catch (COMException) { previous[path] = string.Empty; }
            }

            state.Transition(WallpaperTransactionState.Applying);
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "开始应用壁纸");
            foreach (var role in match.Profile.Roles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!match.RoleMatches.TryGetValue(role.Role, out var monitor)) continue;
                var asset = assets.FirstOrDefault(a => string.Equals(a.Id, role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                var source = asset is null ? role.WallpaperPath : Resolve(asset, paths);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) { missing++; _log($"壁纸文件不存在：{source}"); continue; }
                if (!IsSupportedImage(source)) { missing++; _log($"不支持的壁纸格式：{source}"); continue; }
                _log($"开始渲染并应用：{role.DisplayName} {monitor.Width}x{monitor.Height}");
                var hash = asset?.Sha256 ?? FileUtilities.Sha256(source);
                var rendered = _renderer.Render(source, hash, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height), role.FitMode, role.BackgroundColor);
                _log($"渲染完成：{Path.GetFileName(rendered)}");
                if (!currentPaths.Contains(monitor.MonitorDevicePath)) { missing++; _log($"当前路径未出现在 IDesktopWallpaper：{monitor.MonitorDevicePath}"); continue; }
                if (role.FitMode == WallpaperFitMode.Span) desktop.SetPosition(DesktopWallpaperPosition.Span);
                if (previous.TryGetValue(monitor.MonitorDevicePath, out var existing) && string.Equals(existing, rendered, StringComparison.OrdinalIgnoreCase)) { applied++; continue; }
                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    if (attempt > 0)
                    {
                        retries++;
                        state.TryTransition(WallpaperTransactionState.Retrying);
                        PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, $"壁纸验证重试 {attempt}/2");
                        state.TryTransition(WallpaperTransactionState.Applying);
                    }
                    try
                    {
                        desktop.SetWallpaper(monitor.MonitorDevicePath, rendered);
                        state.TryTransition(WallpaperTransactionState.Verifying);
                        PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "回读壁纸路径");
                        await Task.Delay(120 * (attempt + 1), cancellationToken);
                        var actual = desktop.GetWallpaper(monitor.MonitorDevicePath);
                        success = string.Equals(actual, rendered, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (COMException ex) when (attempt < 2) { _log($"Explorer 壁纸接口暂不可用，重试 {attempt + 1}/2：{ex.Message}"); }
                }
                if (success) { applied++; changed.Add(monitor.MonitorDevicePath); _log($"壁纸验证成功：{monitor.DisplayLabel}"); }
                else { failed = true; _log($"壁纸验证失败：{monitor.DisplayLabel}，开始回滚"); break; }
            }
            if (failed)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "应用失败，开始回滚");
                foreach (var path in changed.AsEnumerable().Reverse())
                {
                    if (!previous.TryGetValue(path, out var old) || string.IsNullOrWhiteSpace(old)) continue;
                    var restored = false;
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        try { desktop.SetWallpaper(path, old); restored = string.Equals(desktop.GetWallpaper(path), old, StringComparison.OrdinalIgnoreCase); if (restored) break; }
                        catch (COMException) when (attempt < 2) { await Task.Delay(120 * (attempt + 1), cancellationToken); }
                    }
                    rollbackSucceeded &= restored;
                }
                _log($"壁纸事务已回滚：{changed.Count} 台，结果={(rollbackSucceeded ? "成功" : "失败")}");
                state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Failed : WallpaperTransactionState.RollbackFailed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, rollbackSucceeded ? "壁纸事务失败但已回滚" : "壁纸回滚失败，已停止自动切换");
            }
        }
        catch (OperationCanceledException)
        {
            state.TryTransition(WallpaperTransactionState.Cancelled);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "壁纸事务已取消");
            return new ApplyResult(false, "壁纸事务已取消", applied);
        }
        catch (COMException ex)
        {
            _log($"壁纸 COM 接口不可用：{ex.Message}");
            state.TryTransition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "Explorer 壁纸接口暂不可用，保留当前壁纸");
            return new ApplyResult(false, "Explorer 壁纸接口暂不可用，保留当前壁纸", applied);
        }
        finally
        {
            if (desktop is not null && Marshal.IsComObject(desktop)) Marshal.ReleaseComObject(desktop);
            _transactionGate.Release();
        }
        var successResult = !failed && missing == 0 && applied == match.Profile.Roles.Count;
        if (successResult)
        {
            state.TryTransition(WallpaperTransactionState.Completed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "壁纸事务应用并验证成功");
        }
        else if (state.Current is not WallpaperTransactionState.Failed and not WallpaperTransactionState.RollbackFailed and not WallpaperTransactionState.Cancelled)
        {
            state.TryTransition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, $"已应用 {applied}/{match.Profile.Roles.Count} 台；缺失 {missing} 台");
        }
        return new ApplyResult(successResult, successResult ? "壁纸事务应用并验证成功" : $"已应用 {applied}/{match.Profile.Roles.Count} 台；缺失 {missing} 台", applied);
    }

    private void PublishTransaction(long generation, WallpaperTransactionState state, DateTime started, DateTime? completed, int applied, int expected, int retries, bool rollbackSucceeded, string message)
    {
        var status = new WallpaperTransactionStatus(generation, state, started, completed, applied, expected, retries, rollbackSucceeded, message);
        _lastTransaction = status;
        try { TransactionChanged?.Invoke(this, status); } catch { /* telemetry must never break wallpaper recovery */ }
    }

    private static string Resolve(WallpaperAsset asset, DataPaths paths)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase) ? asset.ExternalPath ?? string.Empty : Path.Combine(paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsSupportedImage(string path)
        => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")] private class DesktopWallpaper { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
        void SetSlideshow(IntPtr slideshow);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);
        void GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DesktopSlideshowDirection direction);
        DesktopSlideshowState GetStatus();
        bool Enable();
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private enum DesktopWallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }
    [Flags] private enum DesktopSlideshowOptions { None = 0, ShuffleImages = 1 }
    private enum DesktopSlideshowDirection { Forward = 0, Backward = 1 }
    [Flags] private enum DesktopSlideshowState { None = 0, Enabled = 1, Slideshow = 2, DisabledByRemoteSession = 4 }
}

public sealed record ApplyResult(bool Success, string Message, int AppliedCount);
