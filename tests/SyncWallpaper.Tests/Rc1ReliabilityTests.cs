using SyncWallpaper.Core;
using SyncWallpaper.Windows;

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
    public void SanitizedDiagnosticsKeepReadOnlyDisplayFacts()
    {
        var safe = MonitorIdentitySanitizer.Sanitize(new MonitorIdentity
        {
            Width = 3840,
            Height = 2160,
            RefreshRateNumerator = 144000,
            RefreshRateDenominator = 1000,
            Rotation = 3,
            Dpi = 144,
            DpiScale = 1.5,
            HdrEnabled = true,
            ColorMode = "AdvancedColor",
            DesktopX = -2160,
            DesktopY = 0,
            OutputTechnology = 10,
            ConnectorInstance = 2
        });
        Assert.AreEqual(3840, safe.Width);
        Assert.AreEqual(144000u, safe.RefreshRateNumerator);
        Assert.AreEqual(3, safe.Rotation);
        Assert.AreEqual(144, safe.Dpi);
        Assert.AreEqual(true, safe.HdrEnabled);
        Assert.AreEqual(-2160, safe.DesktopX);
        Assert.AreEqual(10u, safe.OutputTechnology);
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
    }

    [TestMethod]
    public void WallpaperTransactionCanCompleteWhenEveryWallpaperIsAlreadyCorrect()
    {
        var machine = new WallpaperTransactionStateMachine();
        machine.Transition(WallpaperTransactionState.WaitingForStableTopology);
        machine.Transition(WallpaperTransactionState.Applying);

        machine.Transition(WallpaperTransactionState.Completed);

        Assert.AreEqual(WallpaperTransactionState.Completed, machine.Current);
    }

    [TestMethod]
    public async Task TopologyCoordinatorCoalescesFiftyThousandSignals()
    {
        var applied = 0;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new TopologyCoordinator(
            (_, _) => Task.FromResult((new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = "virtual:last" } } }, true)),
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
    public void SuspendResumeRequiresStableTopologyBeforeActive()
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
    public void ExplorerBackoffIsBoundedAndResetsAfterSuccess()
    {
        var backoff = new ExplorerRecoveryBackoff(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(40));
        backoff.RecordFailure();
        backoff.RecordFailure();
        Assert.IsTrue(backoff.NextDelay <= TimeSpan.FromMilliseconds(40));
        backoff.RecordSuccess();
        Assert.AreEqual(0, backoff.ConsecutiveFailures);
    }

    [TestMethod]
    public void WallpaperRollbackFailureEntersSafeModeImmediately()
    {
        var policy = new SafeModePolicy();
        Assert.IsTrue(policy.Record(SafeModeTrigger.WallpaperRollbackFailure, "rollback"));
        Assert.AreEqual("rollback", policy.Reason);
    }
}
