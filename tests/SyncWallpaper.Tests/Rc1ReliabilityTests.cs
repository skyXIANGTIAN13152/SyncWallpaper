using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class Rc1ReliabilityTests
{
    [TestMethod]
    public void SanitizedDiagnosticsNeverExposeRawIdentifiers()
    {
        var monitor = new MonitorIdentity
        {
            FriendlyName = "ACME display",
            ManufacturerName = "ACME",
            ProductCodeId = "X-42",
            EdidSerialNumber = "SERIAL-123456",
            ContainerId = "{11111111-2222-3333-4444-555555555555}",
            MonitorDevicePath = @"\\?\DISPLAY#ACME123#7&abc",
            InstanceName = "DISPLAY\\ACME123\\SERIAL-123456",
            StableId = "edid:ACME|X-42|SERIAL-123456"
        };
        var safe = MonitorIdentitySanitizer.Sanitize(monitor);
        var json = System.Text.Json.JsonSerializer.Serialize(safe);
        Assert.IsFalse(json.Contains("SERIAL-123456", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("11111111-2222", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("ACME123#7", StringComparison.Ordinal));
        StringAssert.StartsWith(safe.Serial, "sha256:");
        StringAssert.StartsWith(safe.MonitorDevicePath, "device:");
    }

    [TestMethod]
    public void SnapshotComparerUsesStableIdentityAndReportsOnlyChanges()
    {
        var beforeMonitor = new MonitorIdentity { StableId = "edid:a", Width = 1920, Height = 1080, Rotation = 1, DesktopX = 0, DesktopY = 0, ConnectionState = "Connected" };
        var afterMonitor = beforeMonitor.Clone();
        afterMonitor.Width = 2560;
        afterMonitor.Height = 1440;
        var differences = DisplaySnapshotComparer.Compare(
            new DisplaySnapshot { Monitors = new() { beforeMonitor } },
            new DisplaySnapshot { Monitors = new() { afterMonitor } });
        Assert.AreEqual(1, differences.Count);
        Assert.AreEqual("resolution", differences[0].Field);
        Assert.AreEqual("1920x1080", differences[0].Before);
        Assert.AreEqual("2560x1440", differences[0].After);
    }

    [TestMethod]
    public void WallpaperTransactionStateMachineRejectsTerminalStateChanges()
    {
        var machine = new WallpaperTransactionStateMachine();
        machine.Transition(WallpaperTransactionState.WaitingForStableTopology);
        machine.Transition(WallpaperTransactionState.Applying);
        machine.Transition(WallpaperTransactionState.Verifying);
        machine.Transition(WallpaperTransactionState.Completed);
        Assert.IsFalse(machine.TryTransition(WallpaperTransactionState.Applying));
        Assert.AreEqual(WallpaperTransactionState.Completed, machine.Current);
    }

    [TestMethod]
    public async Task TopologyCoordinatorCoalescesFiftyThousandSignals()
    {
        var applied = 0;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new TopologyCoordinator(
            (_, token) => Task.FromResult((new DisplaySnapshot
            {
                Monitors = new() { new MonitorIdentity { StableId = "virtual:last" } }
            }, true)),
            (_, _, _) => { Interlocked.Increment(ref applied); completed.TrySetResult(true); return Task.CompletedTask; });
        for (var i = 0; i < 50_000; i++) coordinator.Signal(TopologySignalKind.Display, "sim-" + i);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(40);
        Assert.AreEqual(1, Volatile.Read(ref applied));
    }

    [TestMethod]
    public async Task ManualSignalSupersedesAutomaticApply()
    {
        var appliedReasons = new List<string>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new TopologyCoordinator(
            async (signal, token) =>
            {
                if (signal.Reason == "automatic") { firstStarted.TrySetResult(true); await Task.Delay(500, token); }
                return (new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = signal.Reason } } }, true);
            },
            (signal, _, _) => { lock (appliedReasons) appliedReasons.Add(signal.Reason); completed.TrySetResult(true); return Task.CompletedTask; });
        coordinator.Signal(TopologySignalKind.Display, "automatic");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        coordinator.Signal(TopologySignalKind.Manual, "manual", manual: true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("manual", appliedReasons.Single());
    }

    [TestMethod]
    public void SuspendResumeStateMachineRequiresStableTopologyBeforeActive()
    {
        var state = new SessionPowerStateMachine();
        Assert.IsTrue(state.BeginSuspend());
        Assert.IsTrue(state.MarkSuspended());
        Assert.IsTrue(state.BeginResume());
        Assert.IsTrue(state.ExplorerUnavailable());
        Assert.IsTrue(state.TopologySampling());
        Assert.IsTrue(state.TopologyStable());
        Assert.AreEqual(SessionPowerState.Active, state.Current);
    }

    [TestMethod]
    public void MixedDpiValidatorRejectsOverlapAndScalesCoordinates()
    {
        var result = MixedDpiLayoutValidator.Validate(new[]
        {
            new DpiLayoutDisplay("a", new Int32Rect(0, 0, 1920, 1080), 1.0, false),
            new DpiLayoutDisplay("b", new Int32Rect(1900, 0, 1440, 2560), 1.5, true)
        });
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(x => x.Contains("重叠", StringComparison.Ordinal)));
        Assert.AreEqual(1280, MixedDpiLayoutValidator.ScaleLogicalCoordinate(1920, 1.0, 1.5));
    }
}
