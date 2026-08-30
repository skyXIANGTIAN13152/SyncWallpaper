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
    private WallpaperTransactionStatus _lastTransaction = new(0, WallpaperTransactionState.Completed, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true, "No wallpaper transaction has run yet");
    public WallpaperApplyService(WallpaperRenderService renderer, Action<string>? log = null) { _renderer = renderer; _log = log ?? (_ => { }); }
    public WallpaperTransactionStatus LastTransaction => _lastTransaction;
    public event EventHandler<WallpaperTransactionStatus>? TransactionChanged;
    public event Action<string>? ExplorerUnavailable;

    public async Task<ApplyResult> ApplyAsync(MatchResult match, IReadOnlyList<WallpaperAsset> assets, DataPaths paths, CancellationToken cancellationToken = default, long generation = 0, bool manual = false)
    {
        var started = DateTime.UtcNow;
        var state = new WallpaperTransactionStateMachine();
        var expected = match.Profile?.Roles.Count ?? 0;
            PublishTransaction(generation, state.Current, started, null, 0, expected, 0, true, "Preparing wallpaper transaction");
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
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, 0, expected, 0, true, "Wallpaper transaction cancelled before start");
            return new ApplyResult(false, "Wallpaper transaction cancelled", 0);
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
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "Waiting for stable monitor topology");
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
            PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "Applying wallpapers");
            foreach (var role in match.Profile.Roles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!match.TryGetMonitor(role, out var monitor)) continue;
                var asset = assets.FirstOrDefault(a => string.Equals(a.Id, role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                var source = asset is null ? role.WallpaperPath : Resolve(asset, paths);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) { missing++; _log($"Wallpaper file missing: {source}"); continue; }
                if (!IsSupportedImage(source)) { missing++; _log($"Unsupported wallpaper format: {source}"); continue; }
                _log($"Rendering and applying: {role.DisplayName} {monitor.Width}x{monitor.Height}");
                var hash = asset?.Sha256 ?? FileUtilities.Sha256(source);
                var rendered = _renderer.Render(source, hash, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height), role.FitMode, role.BackgroundColor);
                _log($"Rendering complete: {Path.GetFileName(rendered)}");
                if (!currentPaths.Contains(monitor.MonitorDevicePath)) { missing++; _log($"Current path is not present in IDesktopWallpaper: {monitor.MonitorDevicePath}"); continue; }
                if (role.FitMode == WallpaperFitMode.Span) desktop.SetPosition(DesktopWallpaperPosition.Span);
                if (previous.TryGetValue(monitor.MonitorDevicePath, out var existing) && string.Equals(existing, rendered, StringComparison.OrdinalIgnoreCase)) { applied++; continue; }
                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    if (attempt > 0)
                    {
                        retries++;
                        state.TryTransition(WallpaperTransactionState.Retrying);
                        PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, $"Wallpaper verification retry {attempt}/2");
                        state.TryTransition(WallpaperTransactionState.Applying);
                    }
                    try
                    {
                        desktop.SetWallpaper(monitor.MonitorDevicePath, rendered);
                        state.TryTransition(WallpaperTransactionState.Verifying);
                    PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "Reading back wallpaper paths");
                        await Task.Delay(120 * (attempt + 1), cancellationToken);
                        var actual = desktop.GetWallpaper(monitor.MonitorDevicePath);
                        success = string.Equals(actual, rendered, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (COMException ex) when (attempt < 2) { _log($"Explorer wallpaper API unavailable; retry {attempt + 1}/2: {ex.Message}"); }
                }
                if (success) { applied++; changed.Add(monitor.MonitorDevicePath); _log($"Wallpaper verification succeeded: {monitor.DisplayLabel}"); }
                else { failed = true; _log($"Wallpaper verification failed: {monitor.DisplayLabel}; starting rollback"); break; }
            }
            if (failed)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                PublishTransaction(generation, state.Current, started, null, applied, expected, retries, rollbackSucceeded, "Application failed; starting rollback");
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
                _log($"Wallpaper transaction rolled back for {changed.Count} monitor(s); result={(rollbackSucceeded ? "success" : "failure")}");
                state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Failed : WallpaperTransactionState.RollbackFailed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, rollbackSucceeded ? "Wallpaper transaction failed but was rolled back" : "Wallpaper rollback failed; automatic switching stopped");
            }
        }
        catch (OperationCanceledException)
        {
            if (desktop is not null && changed.Count > 0)
            {
                state.TryTransition(WallpaperTransactionState.RollingBack);
                rollbackSucceeded = await TryRollbackAsync(desktop, changed, previous).ConfigureAwait(false);
                state.TryTransition(rollbackSucceeded ? WallpaperTransactionState.Cancelled : WallpaperTransactionState.RollbackFailed);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, rollbackSucceeded ? "Wallpaper transaction cancelled and rolled back" : "Wallpaper transaction cancelled; rollback failed");
            }
            else
            {
                state.TryTransition(WallpaperTransactionState.Cancelled);
                PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "Wallpaper transaction cancelled");
            }
            return new ApplyResult(false, rollbackSucceeded ? "Wallpaper transaction cancelled and rolled back" : "Wallpaper transaction cancelled", applied);
        }
        catch (COMException ex)
        {
            _log($"Wallpaper COM API unavailable: {ex.Message}");
            try { ExplorerUnavailable?.Invoke(ex.Message); } catch { }
            state.TryTransition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "Explorer wallpaper API unavailable; keeping current wallpapers");
            return new ApplyResult(false, "Explorer wallpaper API unavailable; keeping current wallpapers", applied);
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
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, "Wallpaper transaction applied and verified successfully");
        }
        else if (state.Current is not WallpaperTransactionState.Failed and not WallpaperTransactionState.RollbackFailed and not WallpaperTransactionState.Cancelled)
        {
            state.TryTransition(WallpaperTransactionState.Failed);
            PublishTransaction(generation, state.Current, started, DateTime.UtcNow, applied, expected, retries, rollbackSucceeded, $"Applied to {applied}/{match.Profile.Roles.Count}; missing {missing}");
        }
        return new ApplyResult(successResult, successResult ? "Wallpaper transaction applied and verified successfully" : $"Applied to {applied}/{match.Profile.Roles.Count}; missing {missing}", applied);
    }

    private void PublishTransaction(long generation, WallpaperTransactionState state, DateTime started, DateTime? completed, int applied, int expected, int retries, bool rollbackSucceeded, string message)
    {
        var status = new WallpaperTransactionStatus(generation, state, started, completed, applied, expected, retries, rollbackSucceeded, message);
        _lastTransaction = status;
        if (state is WallpaperTransactionState.Completed or WallpaperTransactionState.Failed
            or WallpaperTransactionState.RollbackFailed or WallpaperTransactionState.Cancelled)
            _log($"{message}; processed {applied}/{expected}; retries {retries}");
        try { TransactionChanged?.Invoke(this, status); } catch { /* telemetry must never break wallpaper recovery */ }
    }

    private static async Task<bool> TryRollbackAsync(IDesktopWallpaper desktop, IReadOnlyList<string> changed, IReadOnlyDictionary<string, string> previous)
    {
        var success = true;
        foreach (var path in changed.AsEnumerable().Reverse())
        {
            if (!previous.TryGetValue(path, out var old) || string.IsNullOrWhiteSpace(old)) continue;
            var restored = false;
            for (var attempt = 0; attempt < 3 && !restored; attempt++)
            {
                try
                {
                    desktop.SetWallpaper(path, old);
                    await Task.Delay(120 * (attempt + 1)).ConfigureAwait(false);
                    restored = string.Equals(desktop.GetWallpaper(path), old, StringComparison.OrdinalIgnoreCase);
                }
                catch (COMException) when (attempt < 2) { }
            }
            success &= restored;
        }
        return success;
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
