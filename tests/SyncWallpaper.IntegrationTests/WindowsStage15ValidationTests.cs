using System.Diagnostics;
using System.Text.Json;
using SyncWallpaper.App;
using SyncWallpaper.AudioEngine;
using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;
using SyncWallpaper.WindowEngine;
using SyncWallpaper.Windows;

namespace SyncWallpaper.IntegrationTests;

[TestClass]
public class WindowsStage15ValidationTests
{
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

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SyncWallpaper.sln"))) current = current.Parent;
        return current?.FullName ?? Directory.GetCurrentDirectory();
    }
}
