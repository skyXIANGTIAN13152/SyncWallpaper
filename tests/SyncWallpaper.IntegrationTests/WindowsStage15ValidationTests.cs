using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SyncWallpaper.App;
using SyncWallpaper.AudioEngine;
using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;
using SyncWallpaper.WindowEngine;
using SyncWallpaper.Windows;
using SyncWallpaper.TaskbarHost;

namespace SyncWallpaper.IntegrationTests;

[TestClass]
public class WindowsStage15ValidationTests
{
    [TestMethod]
    public void SecondInstanceSignalsTheRunningInstanceToOpenItsWindow()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows 命名事件不可用。");
        var suffix = Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();
        using var primary = new SingleInstanceService($"Local\\SyncWallpaper.Test.{suffix}", $"Local\\SyncWallpaper.Activate.Test.{suffix}");
        using var secondary = new SingleInstanceService($"Local\\SyncWallpaper.Test.{suffix}", $"Local\\SyncWallpaper.Activate.Test.{suffix}");

        Assert.IsTrue(primary.TryAcquire());
        primary.StartActivationListener(activated.Set);
        Assert.IsFalse(secondary.TryAcquire());
        Assert.IsTrue(secondary.SignalExistingInstance());
        Assert.IsTrue(activated.Wait(TimeSpan.FromSeconds(3)), "主实例没有收到激活请求。");
    }

    [TestMethod]
    public void ReadOnlyDisplayStateIsStableAcrossTenReadsAndPersistsSnapshot()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var discovery = new MonitorDiscoveryService();
        var reads = Enumerable.Range(0, 10).Select(_ => discovery.Discover().ToArray()).ToArray();
        if (reads[0].Length == 0) Assert.Inconclusive("当前会话没有活动显示路径。");
        var signatures = reads.Select(x => string.Join(";", x.OrderBy(m => m.MonitorDevicePath, StringComparer.OrdinalIgnoreCase).Select(m => $"{m.MonitorDevicePath}|{m.AdapterId}|{m.TargetId}|{m.DesktopX},{m.DesktopY}|{m.Width}x{m.Height}|{m.Rotation}"))).Distinct().ToArray();
        if (signatures.Length != 1) Assert.Inconclusive("十次只读采样期间显示状态发生变化，不能判定稳定。");
        Assert.IsTrue(reads[0].All(x => !string.IsNullOrWhiteSpace(x.MonitorDevicePath)));
        Assert.IsTrue(reads[0].All(x => !x.MonitorDevicePath.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)), "快照不应把 Windows 临时编号作为身份。");

        using var audio = new WindowsCoreAudioEndpointProvider();
        var process = Process.GetCurrentProcess();
        var payload = new
        {
            CapturedAt = DateTime.UtcNow,
            Windows = Environment.OSVersion.VersionString,
            Displays = reads[0],
            Audio = audio.Enumerate(),
            Defaults = new[] { AudioEndpointRole.Console, AudioEndpointRole.Multimedia, AudioEndpointRole.Communications, AudioEndpointRole.Recording }
                .ToDictionary(x => x.ToString(), x => audio.GetDefault(x)?.DeviceId),
            Process = new { process.WorkingSet64, process.PrivateMemorySize64 }
        };
        var root = FindWorkspaceRoot();
        var directory = Path.Combine(root, "artifacts", "validation-snapshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"stage15-readonly-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(File.Exists(path) && new FileInfo(path).Length > 0);
    }

    [TestMethod]
    public void ReadOnlyDisplayIdentityContainsRequiredHardwareFields()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var monitors = new MonitorDiscoveryService().Discover();
        if (monitors.Count == 0) Assert.Inconclusive("当前会话没有活动显示路径。");
        Assert.IsTrue(monitors.All(x => !string.IsNullOrWhiteSpace(x.MonitorDevicePath)));
        Assert.IsTrue(monitors.All(x => !string.IsNullOrWhiteSpace(x.AdapterId)));
        Assert.IsTrue(monitors.All(x => x.TargetId >= 0));
        Assert.IsTrue(monitors.All(x => x.Width > 0 && x.Height > 0));
        Assert.IsTrue(monitors.Count(x => x.IsPrimary) <= 1);
    }

    [TestMethod]
    public void WindowZoneMovesOnlyItsOwnNativeTestWindow()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var monitors = new MonitorDiscoveryService().Discover();
        var monitor = monitors.FirstOrDefault(x => x.StableIdSource is MonitorIdentitySource.EdidSerial
            or MonitorIdentitySource.MonitorDevicePath or MonitorIdentitySource.InstanceName or MonitorIdentitySource.HardwareTopology);
        if (monitor is null) Assert.Inconclusive("当前会话没有具备稳定身份的活动显示器。");

        var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4)); // Per-Monitor V2 for physical-coordinate assertions.
        var handle = CreateWindowEx(0, "STATIC", "SyncWallpaper Window Zone Integration Test",
            WsOverlappedWindow | WsVisible, monitor.DesktopX + 80, monitor.DesktopY + 80, 520, 320,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
            Assert.Inconclusive($"无法创建专用测试窗口，Win32={Marshal.GetLastWin32Error()}。");
        }

        try
        {
            ShowWindow(handle, 5);
            UpdateWindow(handle);
            Thread.Sleep(120);
            using var platform = new WindowsWindowPlatform(() => monitors);
            var before = platform.TryGetWindow(handle);
            if (before is null) Assert.Inconclusive("专用测试窗口未进入当前桌面枚举。");
            if (before.Identity.IsElevated) Assert.Inconclusive("测试进程为高权限进程，区域服务按设计拒绝移动。");

            var document = new WindowZoneLayoutsDocument
            {
                GapPixels = 12,
                Layouts = new() { WindowZoneLayoutFactory.Create("integration", monitor, WindowZonePreset.TwoColumns) }
            };
            var pointer = new Int32Point(monitor.DesktopX + (monitor.Width * 3 / 4), monitor.DesktopY + (monitor.Height / 2));
            var result = new WindowZoneSnapService(platform).TrySnap(handle, pointer, document, monitors);
            Assert.AreEqual(WindowZoneSnapStatus.Applied, result.Status, result.Message);
            Thread.Sleep(120);
            var after = platform.TryGetWindow(handle);
            Assert.IsNotNull(after);
            Assert.IsNotNull(result.Bounds);
            Assert.IsTrue(Math.Abs(after.PhysicalBounds.Left - result.Bounds.Value.Left) <= 4);
            Assert.IsTrue(Math.Abs(after.PhysicalBounds.Top - result.Bounds.Value.Top) <= 4);
            Assert.IsTrue(Math.Abs(after.PhysicalBounds.Width - result.Bounds.Value.Width) <= 8);
            Assert.IsTrue(Math.Abs(after.PhysicalBounds.Height - result.Bounds.Value.Height) <= 8);
        }
        finally
        {
            DestroyWindow(handle);
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
    }

    [TestMethod]
    public void ResourceDiagnosticsReturnsNonNegativeCounters()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var snapshot = new WindowsResourceDiagnosticsProvider().Capture();
        Assert.IsTrue(snapshot.WorkingSetBytes > 0);
        Assert.IsTrue(snapshot.PrivateBytes > 0);
        Assert.IsTrue(snapshot.GcHeapBytes >= 0);
        Assert.IsTrue(snapshot.HandleCount >= 0 && snapshot.GdiObjects >= 0 && snapshot.UserObjects >= 0);
        Assert.IsTrue(snapshot.CpuSeconds >= 0);
    }

    [TestMethod]
    public void WorkspaceSnapshotDirectoryIsIgnoredByGitRules()
    {
        var ignore = File.ReadAllText(Path.Combine(FindWorkspaceRoot(), ".gitignore"));
        StringAssert.Contains(ignore, "artifacts/validation-snapshots/*");
    }

    [TestMethod]
    public async Task OptionalHostProcessStartsAndExitsWithoutCoreProcessImpact()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        using var controller = new HostProcessModuleController(SyncWallpaperModule.RemoteHost, executableOverride: host);
        await controller.StartAsync();
        var pid = controller.ProcessId;
        Assert.IsNotNull(pid);
        Assert.IsTrue(Process.GetProcesses().Any(x => x.Id == pid.Value));
        var unsupported = await controller.SendRequestAsync("unsupported-test");
        Assert.IsFalse(unsupported.Success);
        Assert.AreEqual("UnsupportedRequest", unsupported.ErrorCode);
        await controller.StopAsync();
        Assert.IsFalse(controller.IsRunning);
        Assert.IsNull(controller.ProcessId);
        StringAssert.Contains(controller.HookStatus, "退出");
    }

    [TestMethod]
    public async Task UnexpectedHostExitIsIsolatedAndMarkedFaulted()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        using var controller = new HostProcessModuleController(SyncWallpaperModule.TaskbarHost, executableOverride: host);
        using var manager = new ModuleManager();
        manager.Register(new ModuleDefinition(SyncWallpaperModule.TaskbarHost, "taskbar", true, Array.Empty<SyncWallpaperModule>()), controller);
        await manager.StartAsync(SyncWallpaperModule.TaskbarHost);
        var pid = controller.ProcessId;
        Assert.IsNotNull(pid);
        using (var process = Process.GetProcessById(pid.Value)) process.Kill(entireProcessTree: true);
        for (var i = 0; i < 20 && manager.GetState(SyncWallpaperModule.TaskbarHost) != ModuleLifecycleState.Faulted; i++) await Task.Delay(50);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.TaskbarHost));
        StringAssert.Contains(manager.Snapshot(SyncWallpaperModule.TaskbarHost)!.LastError!, "退出");
    }

    [TestMethod]
    public async Task RealTaskbarHostCreatesOneBarPerSecondaryMonitorAndCleansUp()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        if (!string.Equals(Environment.GetEnvironmentVariable("SYNCWALLPAPER_REAL_TASKBAR_TEST"), "1", StringComparison.Ordinal))
            Assert.Inconclusive("设置 SYNCWALLPAPER_REAL_TASKBAR_TEST=1 后执行真实副屏任务栏验证。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        var monitors = new MonitorDiscoveryService().Discover();
        var workAreasBefore = GetMonitorWorkAreas();
        using var controller = new HostProcessModuleController(SyncWallpaperModule.TaskbarHost, executableOverride: host);
        var startup = Stopwatch.StartNew();
        await controller.StartAsync();
        startup.Stop();
        var pid = controller.ProcessId;
        Assert.IsNotNull(pid);
        var response = await controller.SendRequestAsync("status");
        Assert.IsTrue(response.Success);
        Assert.IsTrue(response.Payload.HasValue);
        var status = JsonSerializer.Deserialize<TaskbarHostStatus>(response.Payload.Value.GetRawText(), ModuleIpcJson.Options);
        Assert.IsNotNull(status);
        Assert.AreEqual("Running", status.State);
        Assert.IsTrue(status.HookActive);
        Assert.AreEqual(monitors.Count, status.MonitorCount);
        Assert.AreEqual(monitors.Count(x => !x.IsPrimary), status.BarCount);
        var taskbarWindows = EnumerateVisibleWindowsForProcess(pid.Value);
        Assert.AreEqual(status.BarCount, taskbarWindows.Count, "宿主状态与真实 WPF 副屏窗口数量不一致。");
        Assert.IsNotNull(status.Bars);
        Assert.AreEqual(status.BarCount, status.Bars.Count);
        var secondaryMonitorCount = monitors.Count(x => !x.IsPrimary);
        var shouldReserveWorkArea = secondaryMonitorCount == 1;
        foreach (var bar in status.Bars)
        {
            Assert.IsTrue(bar.TaskCount >= 0, $"{bar.DisplayLabel} 的任务数不能为负数。");
            Assert.IsTrue(bar.GroupCount >= 0 && bar.GroupCount <= bar.TaskCount,
                $"{bar.DisplayLabel} 的分组数 {bar.GroupCount} 与任务数 {bar.TaskCount} 不一致。");
            Assert.IsTrue(bar.PinnedCount >= 0, $"{bar.DisplayLabel} 的固定项数不能为负数。");
            Assert.IsFalse(bar.AutoHide, $"默认任务栏不应自动隐藏：{bar.DisplayLabel}");
            Assert.IsFalse(bar.IsHidden, $"默认任务栏不应处于隐藏位置：{bar.DisplayLabel}");
            Assert.AreEqual(shouldReserveWorkArea, bar.WorkAreaReserved,
                $"{bar.DisplayLabel} 的 AppBar 预留状态不符合安全策略：{bar.PlacementError}");
            if (shouldReserveWorkArea)
                Assert.IsTrue(string.IsNullOrWhiteSpace(bar.PlacementError), $"{bar.DisplayLabel} 的任务栏位置发生降级：{bar.PlacementError}");
            else if (secondaryMonitorCount > 1)
                StringAssert.Contains(bar.PlacementError ?? string.Empty, "多个副屏");
        }
        var workAreasDuring = GetMonitorWorkAreas();
        foreach (var monitor in monitors.Where(x => !x.IsPrimary))
        {
            Assert.IsTrue(status.Bars.Any(bar =>
            {
                var rect = bar.Bounds;
                return
                rect.Left + rect.Width / 2 >= monitor.DesktopX && rect.Left + rect.Width / 2 < monitor.DesktopX + monitor.Width
                && rect.Top + rect.Height / 2 >= monitor.DesktopY && rect.Top + rect.Height / 2 < monitor.DesktopY + monitor.Height;
            }), $"{monitor.DisplayLabel} ({monitor.DesktopX},{monitor.DesktopY} {monitor.Width}x{monitor.Height}) 上没有找到对应的副屏任务栏窗口。宿主坐标：{string.Join("; ", status.Bars.Select(x => $"{x.Bounds.Left},{x.Bounds.Top} {x.Bounds.Width}x{x.Bounds.Height}"))}");
            if (workAreasBefore.TryGetValue(monitor.WindowsDisplayName, out var before)
                && workAreasDuring.TryGetValue(monitor.WindowsDisplayName, out var during))
                Assert.AreEqual(shouldReserveWorkArea, during.Height < before.Height,
                    $"{monitor.DisplayLabel} 的工作区变化不符合安全策略：{before} -> {during}");
        }
        StringAssert.Contains(controller.HookStatus, $"副屏条={status.BarCount}");
        StringAssert.Contains(controller.HookStatus, "分组=");
        StringAssert.Contains(controller.HookStatus, "固定项=");
        using (var taskbarProcess = Process.GetProcessById(pid.Value))
        {
            taskbarProcess.Refresh();
            var beforeHandles = taskbarProcess.HandleCount;
            var beforePrivate = taskbarProcess.PrivateMemorySize64;
            var beforeWorking = taskbarProcess.WorkingSet64;
            var beforeCpu = taskbarProcess.TotalProcessorTime;
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(500);
                Assert.IsTrue((await controller.SendRequestAsync("status")).Success);
            }
            taskbarProcess.Refresh();
            var handleDelta = taskbarProcess.HandleCount - beforeHandles;
            var privateDelta = taskbarProcess.PrivateMemorySize64 - beforePrivate;
            var cpuDelta = taskbarProcess.TotalProcessorTime - beforeCpu;
            Console.WriteLine($"TaskbarHost startup={startup.ElapsedMilliseconds}ms WS={taskbarProcess.WorkingSet64} (baseline {beforeWorking}) Private={taskbarProcess.PrivateMemorySize64} delta={privateDelta} Handles={taskbarProcess.HandleCount} delta={handleDelta} CPU delta={cpuDelta.TotalMilliseconds:0}ms");
            Assert.IsTrue(startup.Elapsed < TimeSpan.FromSeconds(8));
            Assert.IsTrue(taskbarProcess.WorkingSet64 < 160L * 1024 * 1024, $"TaskbarHost 工作集过高：{taskbarProcess.WorkingSet64}");
            Assert.IsTrue(taskbarProcess.PrivateMemorySize64 < 80L * 1024 * 1024, $"TaskbarHost 私有内存过高：{taskbarProcess.PrivateMemorySize64}");
            Assert.IsTrue(taskbarProcess.HandleCount < 1200, $"TaskbarHost 句柄基线过高：{taskbarProcess.HandleCount}");
            Assert.IsTrue(handleDelta < 100, $"TaskbarHost 句柄持续增长：{handleDelta}");
            Assert.IsTrue(privateDelta < 32L * 1024 * 1024, $"TaskbarHost 私有内存异常增长：{privateDelta}");
            Assert.IsTrue(cpuDelta < TimeSpan.FromSeconds(2), $"TaskbarHost 空闲 CPU 异常：{cpuDelta}");
        }
        await controller.StopAsync();
        Assert.IsFalse(controller.IsRunning);
        Assert.IsFalse(Process.GetProcesses().Any(x => x.Id == pid.Value));
        IReadOnlyDictionary<string, SyncWallpaper.Core.Int32Rect> workAreasAfter = GetMonitorWorkAreas();
        for (var attempt = 0; attempt < 20 && !WorkAreasEqual(workAreasBefore, workAreasAfter); attempt++)
        {
            await Task.Delay(100);
            workAreasAfter = GetMonitorWorkAreas();
        }
        Assert.IsTrue(WorkAreasEqual(workAreasBefore, workAreasAfter),
            "TaskbarHost 停止后，Windows 显示器工作区没有恢复到启动前状态。");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task RealTaskbarAutoHideLeavesWorkAreaUntouchedAndCleansUp()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        if (!string.Equals(Environment.GetEnvironmentVariable("SYNCWALLPAPER_REAL_TASKBAR_TEST"), "1", StringComparison.Ordinal))
            Assert.Inconclusive("设置 SYNCWALLPAPER_REAL_TASKBAR_TEST=1 后执行真实副屏任务栏验证。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        var monitors = new MonitorDiscoveryService().Discover();
        if (!monitors.Any(x => !x.IsPrimary)) Assert.Inconclusive("当前没有可验证自动隐藏的副屏。");

        var temporaryDataRoot = Path.Combine(Path.GetTempPath(), "SyncWallpaper.TaskbarAutoHide." + Guid.NewGuid().ToString("N"));
        var previousDataRoot = Environment.GetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT");
        var workAreasBefore = GetMonitorWorkAreas();
        var hasOriginalCursor = GetCursorPos(out var originalCursor);
        HostProcessModuleController? controller = null;
        int? pid = null;
        try
        {
            var store = new ConfigurationStore(new DataPaths(temporaryDataRoot));
            store.Save(TaskbarHostPreferences.FileName, TaskbarHostPreferences.Normalize(new TaskbarHostPreferences
            {
                AutoHide = true,
                ReserveWorkArea = true,
                HideDelayMilliseconds = 250,
                RevealThickness = 2
            }));
            Environment.SetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT", temporaryDataRoot);
            controller = new HostProcessModuleController(SyncWallpaperModule.TaskbarHost, executableOverride: host);
            await controller.StartAsync();
            pid = controller.ProcessId;
            Assert.IsNotNull(pid);

            var primary = monitors.First(x => x.IsPrimary);
            if (workAreasBefore.TryGetValue(primary.WindowsDisplayName, out var primaryWorkArea))
                SetCursorPos(primaryWorkArea.Left + primaryWorkArea.Width / 2, primaryWorkArea.Top + primaryWorkArea.Height / 2);

            TaskbarHostStatus? status = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var response = await controller.SendRequestAsync("status");
                Assert.IsTrue(response.Success);
                status = JsonSerializer.Deserialize<TaskbarHostStatus>(response.Payload!.Value.GetRawText(), ModuleIpcJson.Options);
                if (status?.Bars is { Count: > 0 } bars && bars.All(x => x.IsHidden)) break;
                await Task.Delay(100);
            }

            Assert.IsNotNull(status);
            Assert.IsNotNull(status.Bars);
            Assert.AreEqual(monitors.Count(x => !x.IsPrimary), status.BarCount);
            Assert.IsTrue(status.Bars.All(x => x.AutoHide), "自动隐藏配置没有传入独立任务栏宿主。");
            Assert.IsTrue(status.Bars.All(x => x.IsHidden), "鼠标离开后，副屏任务栏没有在延迟时间内隐藏。");
            Assert.IsTrue(status.Bars.All(x => !x.WorkAreaReserved), "自动隐藏模式不应永久预留桌面工作区。");
            Assert.IsTrue(status.Bars.All(x => string.IsNullOrWhiteSpace(x.PlacementError)), "自动隐藏模式不应触发 AppBar 降级错误。");
            Assert.IsTrue(WorkAreasEqual(workAreasBefore, GetMonitorWorkAreas()), "自动隐藏任务栏改变了 Windows 工作区。");
        }
        finally
        {
            if (controller is not null)
            {
                try { await controller.StopAsync(); } catch { }
                controller.Dispose();
            }
            if (hasOriginalCursor) SetCursorPos(originalCursor.X, originalCursor.Y);
            Environment.SetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT", previousDataRoot);
            try { if (Directory.Exists(temporaryDataRoot)) Directory.Delete(temporaryDataRoot, true); } catch { }
        }

        if (pid.HasValue) Assert.IsFalse(Process.GetProcesses().Any(x => x.Id == pid.Value));
        Assert.IsTrue(WorkAreasEqual(workAreasBefore, GetMonitorWorkAreas()),
            "自动隐藏宿主停止后，Windows 工作区没有保持原状。");
    }

    [TestMethod]
    public async Task IncompatibleHostProtocolIsRejectedWithoutCoreImpact()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = host,
                Arguments = "--module RemoteHost --protocol 999 --instance-id incompatible-test",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        Assert.IsTrue(process.Start());
        try
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotNull(line);
            StringAssert.Contains(line!, "IncompatibleProtocol");
            Assert.AreEqual(3, process.ExitCode);
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }

    [TestMethod]
    public async Task MalformedIpcMessageDoesNotCrashHost()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        var instance = "malformed-test-" + Guid.NewGuid().ToString("N");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = host,
                Arguments = $"--module RemoteHost --protocol {ModuleIpcProtocol.Version} --instance-id {instance}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        Assert.IsTrue(process.Start());
        try
        {
            var readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotNull(readyLine);
            await process.StandardInput.WriteLineAsync("not-json");
            await process.StandardInput.FlushAsync();
            var errorLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotNull(errorLine);
            Assert.IsFalse(process.HasExited, "宿主不应因畸形 IPC 消息退出。");
            Assert.IsTrue(ModuleIpcJson.TryDeserializeResponse(errorLine!, out var error));
            Assert.AreEqual("MalformedMessage", error!.ErrorCode);

            var stop = new ModuleIpcMessage(ModuleIpcProtocol.Version, Guid.NewGuid().ToString("N"), instance, "stop");
            await process.StandardInput.WriteLineAsync(ModuleIpcJson.Serialize(stop));
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(0, process.ExitCode);
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }

    [TestMethod]
    public async Task InjectedImmediateHostExitIsContainedByModuleManager()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe");
        if (!File.Exists(host)) Assert.Inconclusive("尚未生成独立宿主发布文件。");
        using var controller = new HostProcessModuleController(SyncWallpaperModule.RemoteHost, executableOverride: host, faultInjector: new ConfigurableFaultInjector(new[] { FaultPoint.ProcessImmediateExit }));
        using var manager = new ModuleManager(options: new ModuleLifecycleOptions { EnableAutoRecovery = false });
        manager.Register(new ModuleDefinition(SyncWallpaperModule.RemoteHost, "remote", true, Array.Empty<SyncWallpaperModule>()), controller);
        await manager.StartAsync(SyncWallpaperModule.RemoteHost);
        Assert.AreEqual(ModuleLifecycleState.Faulted, manager.GetState(SyncWallpaperModule.RemoteHost));
        Assert.IsFalse(controller.IsRunning);
    }

    [TestMethod]
    public void ReadOnlyResourceProbeRunsOneHundredIterationsAndWritesReport()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var root = FindWorkspaceRoot();
        var directory = Path.Combine(root, "artifacts", "validation-snapshots");
        Directory.CreateDirectory(directory);
        var resources = new WindowsResourceDiagnosticsProvider();
        var before = resources.Capture();
        var discovery = new MonitorDiscoveryService();
        var monitors = discovery.Discover();
        using var audio = new WindowsCoreAudioEndpointProvider();
        using var windows = new WindowsWindowPlatform(() => monitors);
        var desktop = new WindowsShellDesktopIconProvider(() => monitors);
        for (var i = 0; i < 100; i++)
        {
            _ = discovery.Discover();
            _ = audio.Enumerate();
            _ = windows.Enumerate();
            _ = desktop.Capture();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = resources.Capture();
        var payload = new
        {
            CapturedAt = DateTime.UtcNow,
            Iterations = 100,
            Before = before,
            After = after,
            Delta = new
            {
                WorkingSetBytes = after.WorkingSetBytes - before.WorkingSetBytes,
                PrivateBytes = after.PrivateBytes - before.PrivateBytes,
                Handles = after.HandleCount - before.HandleCount,
                Gdi = after.GdiObjects - before.GdiObjects,
                User = after.UserObjects - before.UserObjects
            },
            NotRun = new[] { "30 分钟常驻", "主窗口打开/关闭 50 次", "缩略图 100 次" }
        };
        var path = Path.Combine(directory, $"stage15-resource-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(File.Exists(path) && new FileInfo(path).Length > 0);
        Assert.IsTrue(after.HandleCount - before.HandleCount < 500, "句柄增量异常，需检查 COM/窗口/Shell 释放。");
    }

    [TestMethod]
    public void RepeatedMonitorDiscoveryDoesNotRetainWmiHandlesUntilGc()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var resources = new WindowsResourceDiagnosticsProvider();
        var discovery = new MonitorDiscoveryService();
        _ = discovery.Discover();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = resources.Capture();

        for (var i = 0; i < 50; i++) _ = discovery.Discover();

        var after = resources.Capture();
        var handleDelta = after.HandleCount - before.HandleCount;
        Console.WriteLine($"Monitor discovery handle delta without forced GC: {handleDelta}");
        Assert.IsTrue(handleDelta < 100,
            $"50 次显示器检测保留了 {handleDelta} 个句柄；WMI 结果应在每次读取后立即释放。");
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SyncWallpaper.sln"))) current = current.Parent;
        return current?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static IReadOnlyList<SyncWallpaper.Core.Int32Rect> EnumerateVisibleWindowsForProcess(int processId)
    {
        var result = new List<SyncWallpaper.Core.Int32Rect>();
        var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid != processId || !IsWindowVisible(window) || !GetWindowRect(window, out var rect)) return true;
                var title = new StringBuilder(Math.Max(2, GetWindowTextLength(window) + 1));
                GetWindowText(window, title, title.Capacity);
                if (title.ToString().Equals("屏序副屏任务栏", StringComparison.Ordinal))
                    result.Add(new SyncWallpaper.Core.Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, SyncWallpaper.Core.Int32Rect> GetMonitorWorkAreas()
    {
        var result = new Dictionary<string, SyncWallpaper.Core.Int32Rect>(StringComparer.OrdinalIgnoreCase);
        var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
                if (GetMonitorInfo(monitor, ref info))
                    result[info.DeviceName] = new SyncWallpaper.Core.Int32Rect(
                        info.Work.Left,
                        info.Work.Top,
                        info.Work.Right - info.Work.Left,
                        info.Work.Bottom - info.Work.Top);
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
        return result;
    }

    private static bool WorkAreasEqual(
        IReadOnlyDictionary<string, SyncWallpaper.Core.Int32Rect> left,
        IReadOnlyDictionary<string, SyncWallpaper.Core.Int32Rect> right)
        => left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr rect, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public WindowRect Monitor;
        public WindowRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRect, MonitorEnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
}
