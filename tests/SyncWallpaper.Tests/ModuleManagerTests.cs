using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class ModuleManagerTests
{
    [TestMethod]
    public void LightweightModeKeepsOnlyWallpaperCoreEnabled()
    {
        var configuration = new ModuleConfiguration();
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.Wallpaper));
        Assert.IsFalse(configuration.IsEnabled(SyncWallpaperModule.AudioEngine));
        Assert.IsFalse(configuration.IsEnabled(SyncWallpaperModule.TaskbarHost));
        Assert.IsFalse(configuration.IsEnabled(SyncWallpaperModule.RemoteHost));
    }

    [TestMethod]
    public void StandardModeEnablesOnlyInProcessFeatureModules()
    {
        var configuration = new ModuleConfiguration();
        configuration.ApplyPreset(ModuleMode.Standard);
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.DisplayEngine));
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.DesktopEngine));
        Assert.IsFalse(configuration.IsEnabled(SyncWallpaperModule.ShellHost));
        Assert.IsFalse(configuration.IsEnabled(SyncWallpaperModule.OnlineWallpaperProviders));
    }

    [TestMethod]
    public void FullModeIncludesOptionalProcessHosts()
    {
        var configuration = new ModuleConfiguration();
        configuration.ApplyPreset(ModuleMode.Full);
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.TaskbarHost));
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.ShellHost));
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.RemoteHost));
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.OnlineWallpaperProviders));
    }

    [TestMethod]
    public void CustomModeCannotDisableWallpaperCore()
    {
        var configuration = new ModuleConfiguration();
        configuration.SetEnabled(SyncWallpaperModule.Wallpaper, false);
        Assert.AreEqual(ModuleMode.Custom, configuration.Mode);
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.Wallpaper));
    }

    [TestMethod]
    public async Task ManagerStartsAndStopsAControllerExactlyOnce()
    {
        var fake = new FakeController();
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.AudioEngine, "audio", false, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.AudioEngine);
        await manager.StartAsync(SyncWallpaperModule.AudioEngine);
        Assert.AreEqual(ModuleLifecycleState.Running, manager.GetState(SyncWallpaperModule.AudioEngine));
        Assert.AreEqual(1, fake.Starts);
        await manager.StopAsync(SyncWallpaperModule.AudioEngine);
        await manager.StopAsync(SyncWallpaperModule.AudioEngine);
        Assert.AreEqual(ModuleLifecycleState.Stopped, manager.GetState(SyncWallpaperModule.AudioEngine));
        Assert.AreEqual(1, fake.Stops);
        Assert.IsTrue(manager.VerifyStopped(SyncWallpaperModule.AudioEngine));
    }

    [TestMethod]
    public async Task ConcurrentStartRequestsDoNotCreateDuplicateControllerInstances()
    {
        var fake = new FakeController(delayOnStart: TimeSpan.FromMilliseconds(20));
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.AudioEngine, "audio", false, Array.Empty<SyncWallpaperModule>()), fake);
        await Task.WhenAll(manager.StartAsync(SyncWallpaperModule.AudioEngine), manager.StartAsync(SyncWallpaperModule.AudioEngine));
        Assert.AreEqual(1, fake.Starts);
        Assert.AreEqual(ModuleLifecycleState.Running, manager.GetState(SyncWallpaperModule.AudioEngine));
    }

    [TestMethod]
    public async Task ManagerStartsDependenciesBeforeDependent()
    {
        var order = new List<string>();
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.DisplayEngine, "display", false, Array.Empty<SyncWallpaperModule>()), new FakeController(() => order.Add("display")));
        manager.Register(new ModuleDefinition(SyncWallpaperModule.Automation, "automation", false, new[] { SyncWallpaperModule.DisplayEngine }), new FakeController(() => order.Add("automation")));
        await manager.StartAsync(SyncWallpaperModule.Automation);
        CollectionAssert.AreEqual(new[] { "display", "automation" }, order);
    }

    [TestMethod]
    public async Task ControllerFailureIsFaultedWithoutThrowingOutOfManager()
    {
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.WindowEngine, "window", false, Array.Empty<SyncWallpaperModule>()), new FakeController(throwOnStart: true));
        await manager.StartAsync(SyncWallpaperModule.WindowEngine);
        var snapshot = manager.Snapshot(SyncWallpaperModule.WindowEngine)!;
        Assert.AreEqual(ModuleLifecycleState.Faulted, snapshot.State);
        StringAssert.Contains(snapshot.LastError!, "start failed");
    }

    [TestMethod]
    public async Task DisabledProcessHostIsNotStartedByLightweightPreset()
    {
        var configuration = new ModuleConfiguration();
        var fake = new FakeController();
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.RemoteHost, "remote", true, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartEnabledAsync(configuration);
        Assert.AreEqual(0, fake.Starts);
        Assert.IsNull(manager.Snapshot(SyncWallpaperModule.RemoteHost)!.ProcessId);
    }

    [TestMethod]
    public async Task StopFailureIsReportedAsFaulted()
    {
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.DesktopEngine, "desktop", false, Array.Empty<SyncWallpaperModule>()), new FakeController(throwOnStop: true));
        await manager.StartAsync(SyncWallpaperModule.DesktopEngine);
        await manager.StopAsync(SyncWallpaperModule.DesktopEngine);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.DesktopEngine));
    }

    [TestMethod]
    public async Task SnapshotExposesStateProcessAndResourceFields()
    {
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.Wallpaper, "wallpaper", false, Array.Empty<SyncWallpaperModule>()), new FakeController { FakeProcessId = Environment.ProcessId });
        await manager.StartAsync(SyncWallpaperModule.Wallpaper);
        var snapshot = manager.Snapshot(SyncWallpaperModule.Wallpaper)!;
        Assert.AreEqual(Environment.ProcessId, snapshot.ProcessId);
        Assert.IsTrue(snapshot.Resources.WorkingSetBytes > 0);
        Assert.IsTrue(snapshot.Resources.HandleCount >= 0);
    }

    [TestMethod]
    public async Task UnexpectedControllerFaultMovesModuleToFaulted()
    {
        var fake = new FakeController();
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.TaskbarHost, "taskbar", true, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.TaskbarHost);
        fake.RaiseFault("host crashed");
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.TaskbarHost));
        Assert.IsFalse(fake.IsRunning);
        StringAssert.Contains(manager.Snapshot(SyncWallpaperModule.TaskbarHost)!.LastError!, "host crashed");
    }

    [TestMethod]
    public async Task StartTimeoutTransitionsToFaultedWithoutHanging()
    {
        var fake = new FakeController(delayOnStart: TimeSpan.FromMilliseconds(250));
        using var manager = new ModuleManager(options: new ModuleLifecycleOptions { StartTimeout = TimeSpan.FromMilliseconds(25), EnableAutoRecovery = false });
        manager.Register(new ModuleDefinition(SyncWallpaperModule.WindowEngine, "window", false, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.WindowEngine);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.WindowEngine));
        StringAssert.Contains(manager.Snapshot(SyncWallpaperModule.WindowEngine)!.LastError!, "timed out");
    }

    [TestMethod]
    public async Task StopTimeoutTransitionsToFaultedWithoutHanging()
    {
        var fake = new FakeController(delayOnStop: TimeSpan.FromMilliseconds(250));
        using var manager = new ModuleManager(options: new ModuleLifecycleOptions { StopTimeout = TimeSpan.FromMilliseconds(25), EnableAutoRecovery = false });
        manager.Register(new ModuleDefinition(SyncWallpaperModule.DesktopEngine, "desktop", false, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.DesktopEngine);
        await manager.StopAsync(SyncWallpaperModule.DesktopEngine);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.DesktopEngine));
        StringAssert.Contains(manager.Snapshot(SyncWallpaperModule.DesktopEngine)!.LastError!, "timed out");
    }

    [TestMethod]
    public async Task OneCrashGetsOneBackoffRecoveryThenCrashLoopIsDisabled()
    {
        var fake = new FakeController();
        var runtime = new ModuleRuntimeDocument();
        using var manager = new ModuleManager(options: new ModuleLifecycleOptions { RecoveryBackoff = TimeSpan.FromMilliseconds(10), MaxAutoRecoveryAttempts = 1 }, runtime: runtime);
        manager.Register(new ModuleDefinition(SyncWallpaperModule.TaskbarHost, "taskbar", true, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.TaskbarHost);
        fake.RaiseFault("first crash");
        await Task.Delay(100);
        Assert.AreEqual(2, fake.Starts);
        fake.RaiseFault("second crash");
        Assert.IsTrue(manager.Snapshot(SyncWallpaperModule.TaskbarHost)!.FaultDisabled);
        Assert.IsTrue(runtime.Modules[nameof(SyncWallpaperModule.TaskbarHost)].DisabledByCrashLoop);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.TaskbarHost));
    }

    [TestMethod]
    public async Task DisablingFaultedModuleCancelsPendingRecovery()
    {
        var fake = new FakeController();
        var configuration = new ModuleConfiguration { Mode = ModuleMode.Custom };
        configuration.SetEnabled(SyncWallpaperModule.RemoteHost, true);
        using var manager = new ModuleManager(options: new ModuleLifecycleOptions { RecoveryBackoff = TimeSpan.FromMilliseconds(100) });
        manager.Register(new ModuleDefinition(SyncWallpaperModule.RemoteHost, "remote", true, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.RemoteHost);
        fake.RaiseFault("crash");
        await manager.SetEnabledAsync(SyncWallpaperModule.RemoteHost, false, configuration);
        await Task.Delay(150);
        Assert.AreEqual(1, fake.Starts);
        Assert.AreEqual(ModuleLifecycleState.Stopped, manager.GetState(SyncWallpaperModule.RemoteHost));
    }

    [TestMethod]
    public async Task PersistedCrashLoopDoesNotStartUntilManualReenable()
    {
        var runtime = new ModuleRuntimeDocument();
        runtime.Modules[nameof(SyncWallpaperModule.RemoteHost)] = new ModuleRuntimeState { DisabledByCrashLoop = true, CrashCount = 3, LastFault = "persisted" };
        var fake = new FakeController();
        using var manager = new ModuleManager(runtime: runtime);
        manager.Register(new ModuleDefinition(SyncWallpaperModule.RemoteHost, "remote", true, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartEnabledAsync(new ModuleConfiguration { Mode = ModuleMode.Full });
        Assert.AreEqual(0, fake.Starts);
        var configuration = new ModuleConfiguration { Mode = ModuleMode.Custom };
        configuration.SetEnabled(SyncWallpaperModule.RemoteHost, true);
        await manager.SetEnabledAsync(SyncWallpaperModule.RemoteHost, true, configuration);
        Assert.AreEqual(1, fake.Starts);
    }

    [TestMethod]
    public async Task LifecycleSnapshotContainsTransitionReasonAndTimestamp()
    {
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.Wallpaper, "wallpaper", false, Array.Empty<SyncWallpaperModule>()), new FakeController());
        await manager.StartAsync(SyncWallpaperModule.Wallpaper);
        var running = manager.Snapshot(SyncWallpaperModule.Wallpaper)!;
        Assert.AreEqual("启动成功", running.LastTransitionReason);
        Assert.IsNotNull(running.LastTransitionAt);
        await manager.StopAsync(SyncWallpaperModule.Wallpaper);
        var stopped = manager.Snapshot(SyncWallpaperModule.Wallpaper)!;
        Assert.AreEqual("停止成功", stopped.LastTransitionReason);
        Assert.IsTrue(stopped.LastTransitionAt >= running.LastTransitionAt);
    }

    [TestMethod]
    public async Task WallpaperCoreCannotBeDisabledThroughManager()
    {
        var fake = new FakeController();
        var configuration = new ModuleConfiguration();
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.Wallpaper, "wallpaper", false, Array.Empty<SyncWallpaperModule>()), fake);
        await manager.StartAsync(SyncWallpaperModule.Wallpaper);
        await manager.SetEnabledAsync(SyncWallpaperModule.Wallpaper, false, configuration);
        Assert.IsTrue(configuration.IsEnabled(SyncWallpaperModule.Wallpaper));
        Assert.AreEqual(ModuleLifecycleState.Running, manager.GetState(SyncWallpaperModule.Wallpaper));
        Assert.AreEqual(0, fake.Stops);
    }

    private sealed class FakeController : IModuleController
    {
        private readonly Action? _onStart;
        private readonly bool _throwOnStart;
        private readonly bool _throwOnStop;
        private readonly TimeSpan _delayOnStart;
        private readonly TimeSpan _delayOnStop;
        public FakeController(Action? onStart = null, bool throwOnStart = false, bool throwOnStop = false, TimeSpan? delayOnStart = null, TimeSpan? delayOnStop = null) { _onStart = onStart; _throwOnStart = throwOnStart; _throwOnStop = throwOnStop; _delayOnStart = delayOnStart ?? TimeSpan.Zero; _delayOnStop = delayOnStop ?? TimeSpan.Zero; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int? FakeProcessId { get; set; }
        public event Action<string>? Faulted;
        public bool IsRunning { get; private set; }
        public int? ProcessId => IsRunning ? (FakeProcessId ?? Environment.ProcessId) : null;
        public string HookStatus => IsRunning ? "registered" : "unregistered";
        public string? LastError { get; private set; }
        public async Task StartAsync(CancellationToken cancellationToken = default) { Starts++; if (_throwOnStart) throw new InvalidOperationException("start failed"); if (_delayOnStart > TimeSpan.Zero) await Task.Delay(_delayOnStart, cancellationToken); _onStart?.Invoke(); IsRunning = true; }
        public async Task StopAsync(CancellationToken cancellationToken = default) { Stops++; if (_throwOnStop) throw new InvalidOperationException("stop failed"); if (_delayOnStop > TimeSpan.Zero) await Task.Delay(_delayOnStop, cancellationToken); IsRunning = false; }
        public void RaiseFault(string message) { IsRunning = false; Faulted?.Invoke(message); }
        public void Dispose() { IsRunning = false; }
    }
}
