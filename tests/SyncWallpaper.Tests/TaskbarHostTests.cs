using SyncWallpaper.Core;
using SyncWallpaper.TaskbarHost;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class TaskbarHostTests
{
    [TestMethod]
    public void FilterIncludesNormalAndUwpTaskWindows()
    {
        Assert.IsTrue(TaskbarWindowFilter.ShouldInclude(Candidate()));
        Assert.IsTrue(TaskbarWindowFilter.ShouldInclude(Candidate() with
        {
            WindowClass = "ApplicationFrameWindow",
            IsUwp = true,
            AppUserModelId = "Contoso.App_123!App"
        }));
        Assert.IsTrue(TaskbarWindowFilter.ShouldInclude(Candidate() with
        {
            WindowClass = "Windows.UI.Core.CoreWindow",
            IsUwp = true,
            AppUserModelId = "Contoso.LegacyUwp_123!App"
        }));
    }

    [TestMethod]
    public void FilterRejectsShellHiddenCloakedOwnedToolAndOwnWindows()
    {
        var normal = Candidate();
        var rejected = new[]
        {
            normal with { IsVisible = false },
            normal with { IsCloaked = true },
            normal with { IsOwnProcess = true },
            normal with { Title = "" },
            normal with { Bounds = default },
            normal with { WindowClass = "Progman" },
            normal with { WindowClass = "WorkerW" },
            normal with { WindowClass = "Shell_TrayWnd" },
            normal with { WindowClass = "Shell_SecondaryTrayWnd" },
            normal with { IsToolWindow = true },
            normal with { IsNoActivate = true },
            normal with { HasOwner = true },
            normal with { IsRootOwner = false }
        };
        Assert.IsTrue(rejected.All(x => !TaskbarWindowFilter.ShouldInclude(x)));
    }

    [TestMethod]
    public void ExplicitAppWindowSurvivesToolAndOwnerHeuristics()
    {
        Assert.IsTrue(TaskbarWindowFilter.ShouldInclude(Candidate() with
        {
            IsToolWindow = true,
            HasOwner = true,
            IsRootOwner = false,
            IsAppWindow = true
        }));
    }

    [TestMethod]
    public void SnapshotAssignsWindowToLargestIntersection()
    {
        var monitors = new[]
        {
            Monitor("left", 0, 0, 1000, 800, true),
            Monitor("right", 1000, 0, 1000, 800, false)
        };
        var snapshot = TaskbarSnapshotBuilder.Build(monitors, new[] { Candidate() with { Bounds = new Int32Rect(850, 100, 700, 500) } });
        var rightKey = snapshot.Monitors.Single(x => !x.IsPrimary).RuntimeKey;
        Assert.AreEqual(rightKey, snapshot.Tasks.Single().MonitorKey);
    }

    [TestMethod]
    public void SnapshotSupportsNegativeDesktopCoordinates()
    {
        var monitors = new[]
        {
            Monitor("portrait", -1200, -200, 1200, 1920, false),
            Monitor("laptop", 0, 0, 1920, 1080, true)
        };
        var snapshot = TaskbarSnapshotBuilder.Build(monitors, new[] { Candidate() with { Bounds = new Int32Rect(-1100, 100, 600, 800) } });
        Assert.AreEqual(snapshot.Monitors.Single(x => x.DisplayLabel == "portrait").RuntimeKey, snapshot.Tasks.Single().MonitorKey);
    }

    [TestMethod]
    public void IdenticalSerialLessMonitorsKeepSeparateRuntimeBars()
    {
        var left = Monitor("same", -1920, 0, 1920, 1080, false);
        var right = Monitor("same", 1920, 0, 1920, 1080, false);
        left.StableId = right.StableId = "device:same-model";
        left.MonitorDevicePath = right.MonitorDevicePath = "same-path";
        var snapshot = TaskbarSnapshotBuilder.Build(new[] { left, right, Monitor("primary", 0, 0, 1920, 1080, true) }, new[]
        {
            Candidate() with { Handle = 11, Bounds = new Int32Rect(-1800, 100, 900, 600) },
            Candidate() with { Handle = 12, Bounds = new Int32Rect(2200, 100, 900, 600) }
        });
        var secondaryKeys = snapshot.Monitors.Where(x => !x.IsPrimary).Select(x => x.RuntimeKey).ToArray();
        Assert.AreEqual(2, secondaryKeys.Distinct().Count());
        Assert.AreEqual(2, snapshot.Tasks.Select(x => x.MonitorKey).Distinct().Count());
    }

    [TestMethod]
    public void EmptyIntersectionFallsBackToNearestMonitor()
    {
        var monitors = new[]
        {
            Monitor("left", 0, 0, 1000, 800, true),
            Monitor("right", 2000, 0, 1000, 800, false)
        };
        var snapshot = TaskbarSnapshotBuilder.Build(monitors, new[] { Candidate() with { Bounds = new Int32Rect(1700, 100, 100, 100) } });
        Assert.AreEqual(snapshot.Monitors.Single(x => x.DisplayLabel == "right").RuntimeKey, snapshot.Tasks.Single().MonitorKey);
    }

    [TestMethod]
    public void CoordinatorRendersAndInvokesWindowAction()
    {
        var platform = new FakePlatform { Candidates = new[] { Candidate() } };
        var source = new FakeChangeSource();
        var view = new FakeView();
        using var coordinator = new TaskbarCoordinator(
            () => new[] { Monitor("secondary", 0, 0, 1920, 1080, false) },
            platform, source, view,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(1));
        coordinator.Start();
        Assert.AreEqual(1, view.RenderCount);
        Assert.AreEqual(1, coordinator.Status.BarCount);
        Assert.IsNotNull(view.Actions);
        Assert.AreEqual(TaskWindowActionResult.Activated, view.Actions!.ActivateOrMinimize(1));
        Assert.AreEqual(1, platform.ActionCount);
        Assert.AreEqual(TaskWindowCloseResult.Requested, view.Actions.Close(1));
        Assert.AreEqual(1, platform.CloseCount);
    }

    [TestMethod]
    public void CoordinatorDebouncesEventsAndStopsRespondingAfterDispose()
    {
        var platform = new FakePlatform { Candidates = new[] { Candidate() } };
        var source = new FakeChangeSource();
        var view = new FakeView();
        var coordinator = new TaskbarCoordinator(
            () => new[] { Monitor("secondary", 0, 0, 1920, 1080, false) },
            platform, source, view,
            TimeSpan.FromMilliseconds(15), TimeSpan.FromHours(1));
        coordinator.Start();
        source.Raise(); source.Raise(); source.Raise();
        Assert.IsTrue(SpinWait.SpinUntil(() => view.RenderCount >= 2, TimeSpan.FromSeconds(1)));
        Thread.Sleep(40);
        Assert.AreEqual(2, view.RenderCount);
        coordinator.Dispose();
        var count = view.RenderCount;
        source.Raise();
        Thread.Sleep(50);
        Assert.AreEqual(count, view.RenderCount);
        Assert.IsTrue(source.Disposed);
        Assert.IsTrue(view.Disposed);
        Assert.IsTrue(platform.Disposed);
    }

    [TestMethod]
    public void CoordinatorRejectsInactiveHookBeforeCreatingBars()
    {
        var platform = new FakePlatform();
        var source = new FakeChangeSource { IsActiveValue = false };
        var view = new FakeView();
        using var coordinator = new TaskbarCoordinator(() => Array.Empty<MonitorIdentity>(), platform, source, view);
        Assert.ThrowsException<InvalidOperationException>(() => coordinator.Start());
        Assert.AreEqual(0, view.RenderCount);
    }

    [TestMethod]
    public void PeriodicRefreshRecoversAfterExplorerProcessChanges()
    {
        var platform = new FakePlatform { Candidates = new[] { Candidate() }, ExplorerProcessIdValue = 900 };
        var source = new FakeChangeSource();
        var view = new FakeView();
        using var coordinator = new TaskbarCoordinator(
            () => new[] { Monitor("secondary", 0, 0, 1920, 1080, false) },
            platform, source, view,
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(20));
        coordinator.Start();
        platform.ExplorerProcessIdValue = 901;
        Assert.IsTrue(SpinWait.SpinUntil(() => coordinator.LastSnapshot.ExplorerProcessId == 901, TimeSpan.FromSeconds(1)));
        Assert.IsTrue(view.RenderCount >= 2);
        Assert.IsTrue(coordinator.Status.HookActive);
    }

    [TestMethod]
    public void GroupingUsesAumidThenProcessPathAndKeepsWindows()
    {
        var tasks = new[]
        {
            Task(1, "First", @"C:\Apps\one.exe", "Contoso.App_1!App"),
            Task(2, "Second", @"C:\Different\host.exe", "Contoso.App_1!App") with { IsForeground = true },
            Task(3, "Third", @"C:\Apps\one.exe", ""),
            Task(4, "Fourth", @"C:\Apps\one.exe", "")
        };

        var groups = TaskbarGrouping.Build(tasks);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(2, groups.Single(x => x.Key.StartsWith("aumid:")).Count);
        Assert.AreEqual(2, groups.Single(x => x.Key.StartsWith("path:")).Count);
        Assert.AreEqual(2, groups.SelectMany(x => x.Tasks).Select(x => x.Handle).Distinct().Count(x => x is 1 or 2));
        Assert.AreEqual((nint)2, groups.Single(x => x.Key.StartsWith("aumid:")).PreviewTask.Handle);
    }

    [TestMethod]
    public void PinStorePersistsAtomicallyWithoutRecoveryBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "syncwallpaper-taskbar-pins-" + Guid.NewGuid().ToString("N"));
        var executable = Path.Combine(root, "sample.exe");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(executable, string.Empty);
            var group = TaskbarGrouping.Build(new[] { Task(10, "Pinned", executable, "") }).Single();
            using (var store = new JsonTaskbarPinStore(root))
            {
                Assert.IsTrue(store.CanPin(group));
                Assert.IsTrue(store.Toggle(group));
                Assert.IsTrue(store.IsPinned(group.Key));
            }
            using (var reloaded = new JsonTaskbarPinStore(root))
            {
                Assert.AreEqual(1, reloaded.Items.Count);
                Assert.AreEqual(group.Key, reloaded.Items[0].Id);
            }
            Assert.IsTrue(File.Exists(Path.Combine(root, "Config", JsonTaskbarPinStore.FileName)));
            Assert.AreEqual(0, Directory.GetFiles(Path.Combine(root, "Backups"), "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PinStoreRejectsMissingExecutableWithoutAumid()
    {
        var root = Path.Combine(Path.GetTempPath(), "syncwallpaper-taskbar-pins-" + Guid.NewGuid().ToString("N"));
        try
        {
            var group = TaskbarGrouping.Build(new[] { Task(11, "Missing", Path.Combine(root, "missing.exe"), "") }).Single();
            using var store = new JsonTaskbarPinStore(root);
            Assert.IsFalse(store.CanPin(group));
            Assert.IsFalse(store.Toggle(group));
            Assert.AreEqual(0, store.Items.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ThumbnailLayoutPreservesAspectRatioAndBounds()
    {
        var layout = TaskbarThumbnailLayoutCalculator.Calculate(1920, 1080, 360, 220, 30, 8);
        Assert.AreEqual(360, layout.ContentWidth);
        Assert.AreEqual(203, layout.ContentHeight);
        Assert.AreEqual(376, layout.WindowWidth);
        Assert.AreEqual(249, layout.WindowHeight);
        Assert.IsTrue(layout.ContentWidth <= 360);
        Assert.IsTrue(layout.ContentHeight <= 220);
    }

    [TestMethod]
    public void AutoHidePreferencesDisablePermanentReservationAndClampValues()
    {
        var normalized = TaskbarHostPreferences.Normalize(new TaskbarHostPreferences
        {
            AutoHide = true,
            ReserveWorkArea = true,
            Height = 500,
            RevealThickness = 0,
            HideDelayMilliseconds = 10
        });

        Assert.IsTrue(normalized.AutoHide);
        Assert.IsFalse(normalized.ReserveWorkArea);
        Assert.AreEqual(72, normalized.Height);
        Assert.AreEqual(1, normalized.RevealThickness);
        Assert.AreEqual(150, normalized.HideDelayMilliseconds);
        Assert.IsTrue(TaskbarHostPreferences.Validate(normalized));
    }

    [TestMethod]
    public void WorkAreaReservationUsesAppBarOnlyForOneSecondaryMonitor()
    {
        var preferences = new TaskbarHostPreferences { ReserveWorkArea = true };

        var one = TaskbarWorkAreaReservationPolicy.Evaluate(preferences, 1);
        var several = TaskbarWorkAreaReservationPolicy.Evaluate(preferences, 2);

        Assert.IsTrue(one.Reserve);
        Assert.IsNull(one.FallbackReason);
        Assert.IsFalse(several.Reserve);
        StringAssert.Contains(several.FallbackReason!, "多个副屏");
    }

    [TestMethod]
    public void WorkAreaReservationIsOffForAutoHideOrDisabledPreference()
    {
        var autoHide = TaskbarWorkAreaReservationPolicy.Evaluate(
            new TaskbarHostPreferences { AutoHide = true, ReserveWorkArea = true }, 1);
        var disabled = TaskbarWorkAreaReservationPolicy.Evaluate(
            new TaskbarHostPreferences { ReserveWorkArea = false }, 1);

        Assert.IsFalse(autoHide.Reserve);
        Assert.IsNull(autoHide.FallbackReason);
        Assert.IsFalse(disabled.Reserve);
        Assert.IsNull(disabled.FallbackReason);
    }

    [TestMethod]
    public void AutoHideStateMachineDelaysHideAndRevealsFromEdge()
    {
        var state = new TaskbarAutoHideStateMachine();
        var now = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc);
        var delay = TimeSpan.FromMilliseconds(650);

        Assert.AreEqual(TaskbarAutoHideAction.None, state.Update(false, false, false, now, delay));
        Assert.AreEqual(TaskbarAutoHideAction.None, state.Update(false, false, false, now.AddMilliseconds(649), delay));
        Assert.AreEqual(TaskbarAutoHideAction.Hide, state.Update(false, false, false, now.AddMilliseconds(650), delay));
        Assert.IsTrue(state.IsHidden);
        Assert.AreEqual(TaskbarAutoHideAction.None, state.Update(false, false, false, now.AddMilliseconds(700), delay));
        Assert.AreEqual(TaskbarAutoHideAction.Show, state.Update(true, false, false, now.AddMilliseconds(750), delay));
        Assert.IsFalse(state.IsHidden);
        Assert.AreEqual(TaskbarAutoHideAction.None, state.Update(false, false, true, now.AddSeconds(3), delay));
        Assert.IsFalse(state.IsHidden);
    }

    [TestMethod]
    public void EdgePositionCalculatorSupportsNegativeCoordinatesAndRevealStrip()
    {
        var visible = new Int32Rect(-1920, 1032, 1920, 48);
        var positions = TaskbarEdgePositionCalculator.Calculate(visible, 2);

        Assert.AreEqual(visible, positions.Visible);
        Assert.AreEqual(new Int32Rect(-1920, 1078, 1920, 48), positions.Hidden);
        Assert.IsTrue(TaskbarEdgePositionCalculator.Contains(visible, -1, 1079));
        Assert.IsTrue(TaskbarEdgePositionCalculator.IsInRevealZone(visible, 2, -1, 1079));
        Assert.IsFalse(TaskbarEdgePositionCalculator.IsInRevealZone(visible, 2, 1, 1079));
    }

    private static TaskWindowCandidate Candidate() => new()
    {
        Handle = 1,
        ProcessId = 42,
        Title = "Test window",
        ProcessName = "test",
        ProcessPath = @"C:\Test\test.exe",
        WindowClass = "TestWindow",
        Bounds = new Int32Rect(100, 100, 800, 600),
        IsVisible = true,
        IsRootOwner = true
    };

    private static TaskbarTaskItem Task(nint handle, string title, string path, string appUserModelId) => new(
        handle,
        42,
        title,
        Path.GetFileNameWithoutExtension(path),
        path,
        "TestWindow",
        appUserModelId,
        "secondary",
        false,
        false,
        !string.IsNullOrWhiteSpace(appUserModelId),
        false);

    private static MonitorIdentity Monitor(string label, int x, int y, int width, int height, bool primary) => new()
    {
        FriendlyName = label,
        StableId = "stable:" + label,
        MonitorDevicePath = "path:" + label,
        DesktopX = x,
        DesktopY = y,
        Width = width,
        Height = height,
        Dpi = 96,
        IsPrimary = primary
    };

    private sealed class FakePlatform : ITaskWindowPlatform
    {
        public IReadOnlyList<TaskWindowCandidate> Candidates { get; init; } = Array.Empty<TaskWindowCandidate>();
        public int ExplorerProcessIdValue { get; set; } = 900;
        public int ExplorerProcessId => ExplorerProcessIdValue;
        public int ActionCount { get; private set; }
        public int CloseCount { get; private set; }
        public bool Disposed { get; private set; }
        public IReadOnlyList<TaskWindowCandidate> Enumerate() => Candidates;
        public TaskWindowActionResult ActivateOrMinimize(nint handle) { ActionCount++; return TaskWindowActionResult.Activated; }
        public TaskWindowCloseResult Close(nint handle) { CloseCount++; return TaskWindowCloseResult.Requested; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeChangeSource : ITaskbarChangeSource
    {
        public event EventHandler? Changed;
        public bool IsActiveValue { get; init; } = true;
        public bool IsActive => IsActiveValue && !Disposed;
        public bool Disposed { get; private set; }
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Disposed = true; Changed = null; }
    }

    private sealed class FakeView : ITaskbarViewHost
    {
        public event EventHandler? StatusChanged;
        public int BarCount { get; private set; }
        public IReadOnlyList<TaskbarBarStatus> Bars { get; private set; } = Array.Empty<TaskbarBarStatus>();
        public int RenderCount { get; private set; }
        public bool Disposed { get; private set; }
        public TaskbarWindowActions? Actions { get; private set; }
        public void Render(TaskbarSnapshot snapshot, TaskbarWindowActions actions)
        {
            RenderCount++;
            BarCount = snapshot.SecondaryMonitorCount;
            Bars = snapshot.Monitors.Where(x => !x.IsPrimary)
                .Select(x =>
                {
                    var tasks = snapshot.Tasks.Where(t => t.MonitorKey == x.RuntimeKey).ToArray();
                    return new TaskbarBarStatus(x.RuntimeKey, x.DisplayLabel, x.Bounds, tasks.Length, TaskbarGrouping.Build(tasks).Count, 0);
                })
                .ToArray();
            Actions = actions;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose() { Disposed = true; BarCount = 0; Bars = Array.Empty<TaskbarBarStatus>(); StatusChanged = null; }
    }
}
