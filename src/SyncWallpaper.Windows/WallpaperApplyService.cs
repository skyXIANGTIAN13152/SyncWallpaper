using System;
using System.IO;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WallpaperApplyService
{
    private readonly WallpaperRenderService _renderer;
    private readonly Action<string> _log;
    private readonly Func<IWallpaperDesktop> _desktopFactory;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private WallpaperTransactionStatus _lastTransaction = new(0, WallpaperTransactionState.Completed, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true, "尚未执行壁纸事务");
    public WallpaperApplyService(WallpaperRenderService renderer, Action<string>? log = null, Func<IWallpaperDesktop>? desktopFactory = null)
    {
        _renderer = renderer;
        _log = log ?? (_ => { });
        _desktopFactory = desktopFactory ?? (() => new NativeWallpaperDesktop());
    }
    public WallpaperTransactionStatus LastTransaction => _lastTransaction;
    public event EventHandler<WallpaperTransactionStatus>? TransactionChanged;
    public event Action<string>? ExplorerUnavailable;

    public bool TryRefreshCacheProtection()
    {
        try
        {
            using var desktop = _desktopFactory();
            var complete = TryReadCurrentWallpapers(desktop, out var currentWallpapers);
            _renderer.UpdateActiveCacheProtection(currentWallpapers, complete);
            return complete;
        }
        catch (Exception)
        {
            _renderer.UpdateActiveCacheProtection(Array.Empty<string>(), completeSnapshot: false);
            return false;
        }
    }

    private static bool TryReadCurrentWallpapers(IWallpaperDesktop desktop, out HashSet<string> wallpapers)
    {
        wallpapers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in desktop.GetMonitorDevicePaths())
                wallpapers.Add(desktop.GetWallpaper(path) ?? string.Empty);
            return true;
        }
        catch (Exception) { return false; }
    }

    public async Task<ApplyResult> ApplyAsync(MatchResult match, IReadOnlyList<WallpaperAsset> assets, DataPaths paths, CancellationToken cancellationToken = default, long generation = 0, bool manual = false)
    {
        var started = DateTime.UtcNow;
        var state = new WallpaperTransactionStateMachine();
        var expected = match.IsGeneric ? match.RoleMatches.Count : match.Profile?.Roles.Count ?? 0;
        PublishTransaction(generation, state.Current, started, null, 0, expected, 0, true, "准备壁纸事务");
        if (match.Status is MatchStatus.Ambiguous or MatchStatus.NoMatch || !match.CanAutoApply || match.Profile is null)
        {
            state.Transition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, match.Message);
            return new ApplyResult(false, match.Message, 0);
        }
        if (!WallpaperBindingSafety.TrySnapshotTargets(match, out var targets, out var mappingError))
        {
            state.Transition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, mappingError);
            return new ApplyResult(false, mappingError, 0);
        }
        expected = targets.Count;
        var genericLayout = match.GenericLayoutMonitors.Select(monitor => monitor.Clone()).ToArray();
        // Span changes the global Windows placement mode and could alter an
        // unassigned screen. Generic rules only use per-screen rendering.
        if (match.IsGeneric && targets.Any(target => target.Binding.FitMode == WallpaperFitMode.Span))
        {
            const string message = "通用组合不支持跨屏填充，请选择填充、适应或其他单屏方式。";
            state.Transition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, message);
            return new ApplyResult(false, message, 0);
        }
        try { await _transactionGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            state.Transition(WallpaperTransactionState.Cancelled);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, "壁纸事务在开始前取消");
            return new ApplyResult(false, "壁纸事务已取消", 0);
        }
        IWallpaperDesktop? desktop = null;
        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var applied = 0;
        var failed = false;
        var missing = 0;
        var rollbackSucceeded = true;
        var retries = 0;
        var cacheProtected = false;
        try
        {
            state.Transition(WallpaperTransactionState.WaitingForStableTopology);
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "等待稳定显示器拓扑");
            desktop = _desktopFactory();
            foreach (var path in desktop.GetMonitorDevicePaths())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                currentPaths.Add(path);
                try { previous[path] = desktop.GetWallpaper(path) ?? string.Empty; }
                catch (COMException ex)
                {
                    _log($"无法读取原壁纸：{path}；{ex.Message}");
                    try { ExplorerUnavailable?.Invoke(ex.Message); } catch { }
                }
            }
            _renderer.BeginCacheProtection(previous.Values, completeSnapshot: previous.Count == currentPaths.Count);
            cacheProtected = true;

            if (match.IsGeneric && !IsGenericLayoutCurrent(desktop, genericLayout))
            {
                const string message = "显示器组合已变化，通用组合整套不再匹配，保持当前壁纸。";
                state.Transition(WallpaperTransactionState.Failed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, retries, true, message);
                return new ApplyResult(false, message, 0);
            }

            // Validate the entire one-to-one plan before the first write. A
            // missing fourth screen/image must not leave a half-applied set.
            var sources = new List<(WallpaperTarget Target, string Source, string Hash)>();
            foreach (var target in targets)
            {
                var role = target.Binding;
                var asset = assets.FirstOrDefault(a => string.Equals(a.Id, role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                var source = asset is null ? role.WallpaperPath : Resolve(asset, paths);
                if (!currentPaths.Contains(target.Monitor.MonitorDevicePath)
                    || !previous.ContainsKey(target.Monitor.MonitorDevicePath)
                    || string.IsNullOrWhiteSpace(source) || !File.Exists(source) || !IsSupportedImage(source))
                {
                    missing++;
                    _log($"显示器或壁纸不可用：{target.Monitor.DisplayLabel} → {source}");
                    continue;
                }
                sources.Add((target, source, asset?.Sha256 ?? FileUtilities.Sha256(source)));
            }
            if (missing > 0)
            {
                state.Transition(WallpaperTransactionState.Failed);
                var message = $"缺失 {missing} 台显示器或壁纸，整套壁纸保持不变";
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, retries, true, message);
                return new ApplyResult(false, message, 0);
            }

            // Decode and render every image before the first desktop mutation.
            // An unreadable later source must not leave earlier screens changed.
            var prepared = new List<(WallpaperTarget Target, string Rendered)>();
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "准备整套壁纸图片");
            foreach (var item in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var role = item.Target.Binding;
                var monitor = item.Target.Monitor;
                _log($"开始渲染：{role.DisplayName} → {monitor.DisplayLabel} {monitor.Width}x{monitor.Height}；绑定 {role.RoleId}；壁纸 {Path.GetFileName(item.Source)}");
                var rendered = _renderer.Render(item.Source, item.Hash, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height), role.FitMode, role.BackgroundColor);
                prepared.Add((item.Target, rendered));
                _log($"渲染完成：{Path.GetFileName(rendered)}");
            }
            // Rendering can evict old cache entries when the cache is full.
            // Refuse the whole transaction if any prepared image is unavailable.
            if (prepared.Any(item => !File.Exists(item.Rendered)))
                throw new IOException("整套壁纸缓存未能完整保留，请检查缓存空间后重试。");

            state.Transition(WallpaperTransactionState.Applying);
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "开始应用壁纸");
            foreach (var item in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (match.IsGeneric && !IsGenericLayoutCurrent(desktop, genericLayout))
                {
                    failed = true;
                    _log("通用组合应用期间显示器布局变化，开始恢复原壁纸");
                    break;
                }
                var role = item.Target.Binding;
                var monitor = item.Target.Monitor;
                var rendered = item.Rendered;
                desired.Add(monitor.MonitorDevicePath, rendered);
                if (role.FitMode == WallpaperFitMode.Span) desktop.SetSpanPosition();
                if (string.Equals(desktop.GetWallpaper(monitor.MonitorDevicePath), rendered, StringComparison.OrdinalIgnoreCase)) { applied++; continue; }
                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    if (match.IsGeneric && !IsGenericLayoutCurrent(desktop, genericLayout)) break;
                    if (attempt > 0)
                    {
                        retries++;
                        state.TryTransition(WallpaperTransactionState.Retrying);
                        PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, $"壁纸验证重试 {attempt}/2");
                        state.TryTransition(WallpaperTransactionState.Applying);
                    }
                    try
                    {
                        if (!changed.Contains(monitor.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)) changed.Add(monitor.MonitorDevicePath);
                        desktop.SetWallpaper(monitor.MonitorDevicePath, rendered);
                        state.TryTransition(WallpaperTransactionState.Verifying);
                        PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "回读壁纸路径");
                        await Task.Delay(120 * (attempt + 1), cancellationToken);
                        var actual = desktop.GetWallpaper(monitor.MonitorDevicePath);
                        success = string.Equals(actual, rendered, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (COMException ex) when (attempt < 2) { _log($"Explorer 壁纸接口暂不可用，重试 {attempt + 1}/2：{ex.Message}"); }
                }
                if (success) { applied++; _log($"壁纸验证成功：{monitor.DisplayLabel}"); }
                else { failed = true; _log($"壁纸验证失败：{monitor.DisplayLabel}，开始回滚"); break; }
            }
            if (!failed)
            {
                state.TryTransition(WallpaperTransactionState.Verifying);
                applied = 0;
                if (match.IsGeneric && !IsGenericLayoutCurrent(desktop, genericLayout)) failed = true;
                foreach (var target in desired)
                {
                    if (string.Equals(desktop.GetWallpaper(target.Key), target.Value, StringComparison.OrdinalIgnoreCase)) applied++;
                    else failed = true;
                }
                if (failed) _log("整套壁纸最终核验失败，开始恢复原壁纸");
            }
            if (failed)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "应用失败，开始回滚");
                rollbackSucceeded = await TryRollbackAsync(desktop, changed, previous).ConfigureAwait(false);
                _log($"壁纸事务已回滚：{changed.Count} 台，结果={(rollbackSucceeded ? "成功" : "失败")}");
                state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Failed : WallpaperTransactionState.RollbackFailed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, rollbackSucceeded ? "壁纸事务失败但已回滚" : "壁纸回滚失败，已停止自动切换");
            }
        }
        catch (OperationCanceledException)
        {
            if (desktop is not null && changed.Count > 0)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                rollbackSucceeded = await TryRollbackAsync(desktop, changed, previous).ConfigureAwait(false);
                state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Cancelled : WallpaperTransactionState.RollbackFailed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, rollbackSucceeded ? "壁纸事务已取消并回滚" : "壁纸事务取消且回滚失败");
            }
            else
            {
                state.TryTransition(WallpaperTransactionState.Cancelled);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "壁纸事务已取消");
            }
            return new ApplyResult(false, rollbackSucceeded ? "壁纸事务已取消并回滚" : "壁纸事务已取消", applied);
        }
        catch (COMException ex)
        {
            _log($"壁纸 COM 接口不可用：{ex.Message}");
            try { ExplorerUnavailable?.Invoke(ex.Message); } catch { }
            if (desktop is not null && changed.Count > 0)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                rollbackSucceeded = await TryRollbackAsync(desktop, changed, previous).ConfigureAwait(false);
            }
            state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Failed : WallpaperTransactionState.RollbackFailed);
            var message = changed.Count == 0 ? "Explorer 壁纸接口暂不可用，保留当前壁纸"
                : rollbackSucceeded ? "Explorer 壁纸接口暂不可用，已恢复原壁纸" : "Explorer 壁纸接口暂不可用，恢复原壁纸失败";
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, message);
            return new ApplyResult(false, message, applied);
        }
        catch (Exception ex)
        {
            _log($"壁纸事务失败：{ex.Message}");
            if (desktop is not null && changed.Count > 0)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                rollbackSucceeded = await TryRollbackAsync(desktop, changed, previous).ConfigureAwait(false);
            }
            state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Failed : WallpaperTransactionState.RollbackFailed);
            var message = changed.Count == 0 ? $"壁纸准备失败，整套保持不变：{ex.Message}"
                : rollbackSucceeded ? "壁纸事务失败，已恢复原壁纸" : "壁纸事务失败，恢复原壁纸失败";
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, message);
            return new ApplyResult(false, message, applied);
        }
        finally
        {
            try
            {
                if (cacheProtected)
                {
                    var complete = TryReadCurrentWallpapers(desktop!, out var activeWallpapers);
                    if (!complete)
                    {
                        // Explorer may still be unavailable. Keep both bounded
                        // candidates and defer cleanup until a complete refresh.
                        activeWallpapers.UnionWith(previous.Values);
                        activeWallpapers.UnionWith(desired.Values);
                    }
                    _renderer.EndCacheProtection(activeWallpapers, complete);
                }
            }
            finally
            {
                try { desktop?.Dispose(); }
                finally { _transactionGate.Release(); }
            }
        }
        var successResult = !failed && missing == 0 && applied == expected;
        if (successResult)
        {
            state.TryTransition(WallpaperTransactionState.Completed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, SuccessMessage(match, applied));
        }
        else if (state.Current is not WallpaperTransactionState.Failed and not WallpaperTransactionState.RollbackFailed and not WallpaperTransactionState.Cancelled)
        {
            state.TryTransition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, $"已应用 {applied}/{expected} 台；缺失 {missing} 台");
        }
        return new ApplyResult(successResult, successResult ? SuccessMessage(match, applied) : $"已应用 {applied}/{expected} 台；缺失 {missing} 台", applied);
    }

    private static string SuccessMessage(MatchResult match, int applied)
        => match.IsGeneric
            ? $"通用壁纸已应用并验证：{applied} 台；{match.UnassignedMonitorCount} 台保留当前壁纸"
            : "壁纸事务应用并验证成功";

    private static bool IsGenericLayoutCurrent(IWallpaperDesktop desktop, IReadOnlyList<MonitorIdentity> expected)
    {
        var paths = new HashSet<string>(desktop.GetMonitorDevicePaths(), StringComparer.OrdinalIgnoreCase);
        if (paths.Count != expected.Count || !paths.SetEquals(expected.Select(monitor => monitor.MonitorDevicePath))) return false;
        foreach (var monitor in expected)
        {
            var bounds = desktop.GetMonitorBounds(monitor.MonitorDevicePath);
            if (bounds is not null && (bounds.X != monitor.DesktopX || bounds.Y != monitor.DesktopY
                || bounds.Width != monitor.Width || bounds.Height != monitor.Height)) return false;
        }
        return true;
    }

    private void PublishTransaction(long generation, WallpaperTransactionState state, DateTime started, DateTime? completed, int applied, int expected, int retries, bool rollbackSucceeded, string message)
    {
        var status = new WallpaperTransactionStatus(generation, state, started, completed, applied, expected, retries, rollbackSucceeded, message);
        _lastTransaction = status;
        if (state is WallpaperTransactionState.Completed or WallpaperTransactionState.Failed
            or WallpaperTransactionState.RollbackFailed or WallpaperTransactionState.Cancelled)
            _log($"{message}；已处理 {applied}/{expected} 台；重试 {retries} 次");
        try { TransactionChanged?.Invoke(this, status); } catch { /* telemetry must never break wallpaper recovery */ }
    }

    private static async Task<bool> TryRollbackAsync(IWallpaperDesktop desktop, IReadOnlyList<string> changed, IReadOnlyDictionary<string, string> previous)
    {
        var success = true;
        foreach (var path in changed.AsEnumerable().Reverse())
        {
            if (!previous.TryGetValue(path, out var old)) { success = false; continue; }
            var restored = false;
            for (var attempt = 0; attempt < 3 && !restored; attempt++)
            {
                try
                {
                    desktop.SetWallpaper(path, old);
                    await Task.Delay(120 * (attempt + 1)).ConfigureAwait(false);
                    restored = string.Equals(desktop.GetWallpaper(path), old, StringComparison.OrdinalIgnoreCase);
                }
                // A failed screen must not prevent recovery of the remaining
                // screens, regardless of the underlying exception type.
                catch (Exception) { }
            }
            success &= restored;
        }
        return success;
    }

    private static string Resolve(WallpaperAsset asset, DataPaths paths)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase) ? asset.ExternalPath ?? string.Empty : Path.Combine(paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsSupportedImage(string path)
        => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private sealed class NativeWallpaperDesktop : IWallpaperDesktop
    {
        private readonly IDesktopWallpaper _desktop = (IDesktopWallpaper)new DesktopWallpaper();
        public IReadOnlyList<string> GetMonitorDevicePaths()
            => MonitorDiscoveryService.GetActiveMonitorDevicePaths();
        public string GetWallpaper(string path) => _desktop.GetWallpaper(path);
        public WallpaperMonitorBounds? GetMonitorBounds(string path)
            => ReadActiveBounds(path) ?? throw new COMException("显示器已断开，无法核对当前布局。");

        private WallpaperMonitorBounds? ReadActiveBounds(string path)
        {
            var result = _desktop.GetMonitorRECT(path, out var rect);
            if (result == 1) return null; // S_FALSE: remembered, but not attached.
            Marshal.ThrowExceptionForHR(result);
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
            return new WallpaperMonitorBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        public void SetWallpaper(string path, string wallpaper) => _desktop.SetWallpaper(path, wallpaper);
        public void SetSpanPosition() => _desktop.SetPosition(DesktopWallpaperPosition.Span);
        public void Dispose() { if (Marshal.IsComObject(_desktop)) Marshal.ReleaseComObject(_desktop); }
    }

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")] private class DesktopWallpaper { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
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
public sealed record WallpaperMonitorBounds(int X, int Y, int Width, int Height);

public interface IWallpaperDesktop : IDisposable
{
    IReadOnlyList<string> GetMonitorDevicePaths();
    WallpaperMonitorBounds? GetMonitorBounds(string monitorDevicePath) => null;
    string GetWallpaper(string monitorDevicePath);
    void SetWallpaper(string monitorDevicePath, string wallpaper);
    void SetSpanPosition();
}
