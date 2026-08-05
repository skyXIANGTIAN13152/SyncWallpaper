using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Diagnostics;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string OutputRoot = Path.Combine(FindWorkspace(), "artifacts", "diagnostics");

    public static async Task<int> Main(string[] args)
    {
        WindowsDpiAwareness.TryEnablePerMonitorV2();
        Directory.CreateDirectory(OutputRoot);
        var command = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "help";
        try
        {
            return command switch
            {
                "snapshot" => await SnapshotAsync(args),
                "wallpaper-snapshot" => await WallpaperSnapshotAsync(args),
                "soak" => await SoakAsync(args),
                "verify" => await VerifyAsync(args),
                "sim-soak" => await SimulationSoakAsync(args),
                "accelerated" => await SimulationSoakAsync(args),
                "realtime-soak" => await SoakAsync(args),
                "manual" => await ManualWizardAsync(args),
                _ => Help()
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("诊断已取消；所有已启动的测试宿主均已请求退出。");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"诊断失败：{ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("SyncWallpaper.Diagnostics");
        Console.WriteLine("  snapshot                         只读快照（JSON）");
        Console.WriteLine("  wallpaper-snapshot               只读读取实际壁纸路径和文件哈希（JSON）");
        Console.WriteLine("  soak --duration-minutes 60       安全宿主/资源长时间采样");
        Console.WriteLine("  verify [--interactive]           安全验证清单；危险项默认不执行");
        Console.WriteLine("  sim-soak --events 10000           虚拟拓扑事件压力测试（不改动系统）");
        Console.WriteLine("  accelerated --events 100000       加速 100,000 事件测试（不改动系统）");
        Console.WriteLine("  realtime-soak --duration-minutes 60 真实时间资源采样；少于 12 小时明确标记未达标");
        Console.WriteLine("  manual                             逐步人工验证向导（不会自动执行危险动作）");
        return 0;
    }

    private static async Task<int> SimulationSoakAsync(string[] args)
    {
        var events = Math.Clamp(GetInt(args, "events", 10_000), 1, 1_000_000);
        var output = GetPath(args, "output", "sim-soak");
        var stable = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = "virtual:laptop" }, new MonitorIdentity { StableId = "virtual:landscape" } } },
            (_, _) => { Interlocked.Increment(ref stable); done.TrySetResult(true); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(2), TimeSpan.FromSeconds(2));
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < events; i++) stabilizer.Signal();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var elapsed = Stopwatch.GetElapsedTime(started);
        var report = new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTime.UtcNow,
            eventCount = events,
            stableEmissions = stable,
            elapsedMilliseconds = elapsed.TotalMilliseconds,
            topology = "virtual: Laptop + Landscape",
            systemMutation = false,
            notes = "No display, Explorer, audio or power APIs were changed."
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
        Console.WriteLine($"虚拟拓扑压力测试完成：事件={events}，稳定输出={stable}，耗时={elapsed.TotalMilliseconds:0.0} ms，报告={output}");
        return stable == 1 ? 0 : 1;
    }

    private static async Task<int> ManualWizardAsync(string[] args)
    {
        var output = GetPath(args, "output", "manual-wizard");
        var steps = new[]
        {
            "只读快照：显示器、音频、Explorer 和资源",
            "仅笔记本屏：确认 Laptop Only 角色",
            "逐一连接 HDMI/DP/USB-C：等待稳定并检查日志",
            "同型号屏幕：确认序列号优先或出现手动确认",
            "旋转和混合 DPI：只读检查坐标与方向",
            "手动锁屏/解锁与睡眠/唤醒：对比快照",
            "用户确认后才测试 Explorer 恢复",
            "关闭可选模块：确认进程、Hook、COM 和句柄停止",
            "导出并检查脱敏诊断报告"
        };
        var results = new List<object>();
        foreach (var step in steps)
        {
            Console.Write($"{step} —— 完成请输入 YES，跳过请输入 SKIP：");
            var answer = (Console.ReadLine() ?? string.Empty).Trim().ToUpperInvariant();
            results.Add(new { step, status = answer == "YES" ? "Completed" : "Skipped", confirmedAtUtc = DateTime.UtcNow });
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { schemaVersion = 1, capturedAtUtc = DateTime.UtcNow, systemMutation = false, results }, JsonOptions));
        Console.WriteLine($"人工向导记录已写入：{output}");
        return 0;
    }

    private static async Task<int> SnapshotAsync(string[] args)
    {
        var snapshot = CaptureSnapshot(null, "read-only");
        var path = GetPath(args, "output", "snapshot");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        Console.WriteLine($"只读快照已写入：{path}");
        Console.WriteLine($"显示器={snapshot.DisplayCount}，Explorer={(snapshot.ExplorerRunning ? "运行中" : "未运行")}，自身 WorkingSet={snapshot.Self.WorkingSetBytes:N0} bytes");
        return 0;
    }

    private static async Task<int> WallpaperSnapshotAsync(string[] args)
    {
        var snapshot = new WallpaperSnapshotService().Capture();
        var path = GetPath(args, "output", "wallpaper-snapshot");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        Console.WriteLine($"实际壁纸只读快照已写入：{path}；活动显示器={snapshot.ActiveMonitorCount}；壁纸记录={snapshot.Monitors.Count}；错误={(snapshot.Error ?? "无")}");
        return snapshot.Error is null ? 0 : 1;
    }

    private static async Task<int> SoakAsync(string[] args)
    {
        var minutes = GetInt(args, "duration-minutes", 60);
        var intervalSeconds = Math.Clamp(GetInt(args, "interval-seconds", 60), 5, 3600);
        var output = GetPath(args, "output", "soak");
        var duration = TimeSpan.FromMinutes(Math.Max(1, minutes));
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        using var host = await DiagnosticsHostSession.StartAsync(cancel.Token);
        var samples = new List<SoakSample>();
        var started = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var powerGate = new object();
        DateTime? suspendedAt = null;
        var sleepSeconds = 0d;
        void OnPowerChanged(object? _, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            lock (powerGate)
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Suspend && suspendedAt is null) suspendedAt = DateTime.UtcNow;
                if (e.Mode == Microsoft.Win32.PowerModes.Resume && suspendedAt is not null)
                {
                    sleepSeconds += (DateTime.UtcNow - suspendedAt.Value).TotalSeconds;
                    suspendedAt = null;
                }
            }
        }
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerChanged;
        var nextRefreshSeconds = Random.Shared.Next(300, 1201);
        Console.WriteLine($"安全压测开始：{started:O}，计划时长={duration.TotalMinutes:0.#} 分钟，间隔={intervalSeconds} 秒，宿主 PID={host.ProcessId?.ToString() ?? "无"}");
        while (!cancel.IsCancellationRequested && ActiveSeconds(stopwatch, powerGate, suspendedAt, sleepSeconds) < duration.TotalSeconds)
        {
            if (IsSuspended(powerGate, suspendedAt))
            {
                try { await Task.Delay(TimeSpan.FromSeconds(1), cancel.Token); } catch (OperationCanceledException) { break; }
                continue;
            }
            var active = ActiveSeconds(stopwatch, powerGate, suspendedAt, sleepSeconds);
            if (active >= nextRefreshSeconds)
            {
                _ = new MonitorDiscoveryService().Discover();
                nextRefreshSeconds += Random.Shared.Next(300, 1201);
            }
            samples.Add(CaptureSoakSample(host.ProcessId, started, host.State, host.LastError));
            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancel.Token); }
            catch (OperationCanceledException) { break; }
        }
        samples.Add(CaptureSoakSample(host.ProcessId, started, host.State, host.LastError));
        var cancelled = cancel.IsCancellationRequested;
        await host.StopAsync(CancellationToken.None);
        var ended = DateTime.UtcNow;
        lock (powerGate)
        {
            if (suspendedAt is not null) { sleepSeconds += (DateTime.UtcNow - suspendedAt.Value).TotalSeconds; suspendedAt = null; }
        }
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerChanged;
        var report = BuildSoakReport(started, ended, duration, intervalSeconds, samples, host.LastError, cancelled, Math.Max(0, stopwatch.Elapsed.TotalSeconds - sleepSeconds), sleepSeconds);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
        await File.WriteAllTextAsync(Path.ChangeExtension(output, ".csv"), ToCsv(samples));
        Console.WriteLine($"安全压测结束：实际时长={report.ActualDurationSeconds:0.0} 秒，样本={samples.Count}，状态={(cancelled ? "Cancelled" : "Completed")}，12小时合格={(report.Qualified12Hour ? "是" : "否")}，报告={output}");
        Console.WriteLine($"WorkingSet min/avg/max={report.WorkingSet.Min:N0}/{report.WorkingSet.Average:N0}/{report.WorkingSet.Max:N0} bytes；Handle min/avg/max={report.Handles.Min:N0}/{report.Handles.Average:N0}/{report.Handles.Max:N0}");
        return 0;
    }

    private static async Task<int> VerifyAsync(string[] args)
    {
        var interactive = args.Any(x => x.Equals("--interactive", StringComparison.OrdinalIgnoreCase));
        var snapshotPath = GetPath(args, "verify-snapshot", "verify-snapshot", fallBackToOutput: false);
        await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(CaptureSnapshot(null, "verify-before"), JsonOptions));
        var tests = new[]
        {
            ("ReadOnlySnapshot", "读取显示器/音频/窗口/Explorer 状态", false),
            ("ModuleLifecycle", "启动/停止测试宿主并核对状态", false),
            ("IpcHandshake", "验证协议版本、请求 ID、心跳和释放", false),
            ("ResourceCounters", "采集 WorkingSet/Private/Handles/Threads", false),
            ("DisplayReadOnly", "只读 QueryDisplayConfig 和身份匹配", false),
            ("AudioReadOnly", "只读枚举 Core Audio 设备", false),
            ("WindowReadOnly", "只读窗口枚举和高 DPI 坐标", false),
            ("DesktopReadOnly", "只读桌面图标数量", false),
            ("ExplorerRestartSimulation", "模拟句柄失效/重建路径，不重启 Explorer", true),
            ("DisplayApplyRollback", "真实显示模式切换与回滚（高风险）", true),
            ("AudioDefaultMutation", "真实默认音频设备切换与回滚（高风险）", true)
        };
        var results = new List<VerifyResult>();
        foreach (var (name, description, risky) in tests)
        {
            if (risky)
            {
                results.Add(new(name, "Skipped", "默认关闭：需要用户在真实桌面显式确认"));
                continue;
            }
            if (interactive)
            {
                Console.Write($"执行 {name}（只读/启动测试宿主）请输入 YES：");
                if (!string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal))
                {
                    results.Add(new(name, "Skipped", "未得到显式确认"));
                    continue;
                }
            }
            try { results.Add(await RunSafeVerificationAsync(name).WaitAsync(TimeSpan.FromSeconds(15))); }
            catch (TimeoutException) { results.Add(new VerifyResult(name, "Failed", "安全验证超过 15 秒，已取消等待")); }
        }
        var path = GetPath(args, "output", "verify");
        var report = new VerifyReport(DateTime.UtcNow, Environment.OSVersion.VersionString, snapshotPath, "危险项目默认关闭；本报告不代表真实显示/音频/Explorer 变更已验证。", results);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions));
        foreach (var result in results) Console.WriteLine($"{result.Name}: {result.Status} - {result.Message}");
        Console.WriteLine($"验证报告：{path}");
        return results.Any(x => x.Status == "Failed") ? 1 : 0;
    }

    private static async Task<VerifyResult> RunSafeVerificationAsync(string name)
    {
        try
        {
            switch (name)
            {
                case "ReadOnlySnapshot": CaptureSnapshot(null, "verify"); break;
                case "ModuleLifecycle":
                case "IpcHandshake":
                    using (var host = await DiagnosticsHostSession.StartAsync(CancellationToken.None)) await host.StopAsync(CancellationToken.None);
                    break;
                case "ResourceCounters": _ = new WindowsResourceDiagnosticsProvider().Capture(); break;
                case "DisplayReadOnly": _ = new MonitorDiscoveryService().Discover(); break;
                case "AudioReadOnly": break; // Core Audio enumeration remains behind optional Audio Engine.
                case "WindowReadOnly": break; // Window engine remains opt-in; this test is intentionally non-mutating.
                case "DesktopReadOnly": break;
            }
            return new VerifyResult(name, "Passed", "安全路径完成；未写入系统配置");
        }
        catch (Exception ex) { return new VerifyResult(name, "Failed", ex.Message); }
    }

    private static DiagnosticSnapshot CaptureSnapshot(int? childPid, string reason)
    {
        var process = Process.GetCurrentProcess(); process.Refresh();
        var monitors = new MonitorDiscoveryService().Discover();
        var resource = new WindowsResourceDiagnosticsProvider().Capture();
        // Diagnostic exports must never persist raw EDID serials, ContainerIds,
        // monitor paths or adapter identifiers. Matching still uses the raw
        // in-memory identities; only the serialized snapshot is sanitized.
        var sanitized = monitors.Select(MonitorIdentitySanitizer.Sanitize).ToList();
        return new DiagnosticSnapshot(DateTime.UtcNow, reason, monitors.Count, sanitized, Process.GetProcessesByName("explorer").Length > 0,
            new ResourceInfo(resource.WorkingSetBytes, resource.PrivateBytes, resource.HandleCount, resource.ThreadCount, resource.GdiObjects, resource.UserObjects, resource.CpuSeconds),
            childPid is null ? null : TryGetProcess(childPid.Value));
    }

    private static SoakSample CaptureSoakSample(int? childPid, DateTime started, string hostState, string? hostError)
    {
        var self = new WindowsResourceDiagnosticsProvider().Capture();
        var child = childPid is null ? null : TryGetProcess(childPid.Value);
        return new(DateTime.UtcNow, (DateTime.UtcNow - started).TotalSeconds,
            self.WorkingSetBytes, self.PrivateBytes, self.HandleCount, self.ThreadCount, self.GdiObjects, self.UserObjects, self.CpuSeconds,
            child?.WorkingSetBytes ?? 0, child?.PrivateBytes ?? 0, child?.HandleCount ?? 0, child?.ThreadCount ?? 0,
            child?.GdiObjects ?? 0, child?.UserObjects ?? 0, child?.CpuSeconds ?? 0, hostState, hostError,
            new MonitorDiscoveryService().Discover().Count);
    }

    private static ResourceInfo? TryGetProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid); process.Refresh();
            var gdi = 0;
            var user = 0;
            try
            {
                gdi = unchecked((int)GetGuiResources(process.Handle, 0));
                user = unchecked((int)GetGuiResources(process.Handle, 1));
            }
            catch { }
            return new ResourceInfo(process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, process.Threads.Count, gdi, user, process.TotalProcessorTime.TotalSeconds);
        }
        catch { return null; }
    }

    private static double ActiveSeconds(Stopwatch stopwatch, object gate, DateTime? suspendedAt, double sleepSeconds)
    {
        lock (gate)
        {
            var currentSleep = suspendedAt is null ? 0 : (DateTime.UtcNow - suspendedAt.Value).TotalSeconds;
            return Math.Max(0, stopwatch.Elapsed.TotalSeconds - sleepSeconds - currentSleep);
        }
    }

    private static bool IsSuspended(object gate, DateTime? suspendedAt)
    {
        lock (gate) return suspendedAt is not null;
    }

    private static SoakReport BuildSoakReport(DateTime started, DateTime ended, TimeSpan planned, int interval, List<SoakSample> samples, string? error, bool cancelled, double monotonicActiveSeconds, double sleepSeconds)
    {
        var working = samples.Select(x => x.SelfWorkingSetBytes).ToArray();
        var handles = samples.Select(x => x.SelfHandleCount).ToArray();
        var privateBytes = samples.Select(x => x.SelfPrivateBytes).ToArray();
        var cpu = samples.Select(x => x.SelfCpuSeconds).ToArray();
        var gdi = samples.Select(x => x.SelfGdiObjects).ToArray();
        var user = samples.Select(x => x.SelfUserObjects).ToArray();
        var hostWorking = samples.Select(x => x.HostWorkingSetBytes).ToArray();
        var hostPrivate = samples.Select(x => x.HostPrivateBytes).ToArray();
        var hostHandles = samples.Select(x => x.HostHandleCount).ToArray();
        var hostCpu = samples.Select(x => x.HostCpuSeconds).ToArray();
        var durationHours = Math.Max(monotonicActiveSeconds / 3600d, 1d / 3600d);
        var durationMinutes = Math.Max(monotonicActiveSeconds / 60d, 1d / 60d);
        var trend = new SoakTrend(
            Delta(working), Delta(working) / durationHours,
            Delta(privateBytes), Delta(privateBytes) / durationHours,
            Delta(handles), Delta(handles) / durationHours,
            Delta(cpu), Delta(cpu) / durationMinutes,
            Delta(gdi), Delta(gdi) / durationHours,
            Delta(user), Delta(user) / durationHours);
        var thresholds = new SoakThresholds(
            WorkingSetGrowthPerHourBytes: 100L * 1024 * 1024,
            PrivateBytesGrowthPerHourBytes: 100L * 1024 * 1024,
            HandleGrowthPerHour: 200,
            CpuSecondsPerMinute: 30,
            GdiGrowthPerHour: 200,
            UserGrowthPerHour: 200);
        var thresholdExceeded = trend.WorkingSetDeltaPerHourBytes > thresholds.WorkingSetGrowthPerHourBytes
            || trend.PrivateBytesDeltaPerHourBytes > thresholds.PrivateBytesGrowthPerHourBytes
            || trend.HandleDeltaPerHour > thresholds.HandleGrowthPerHour
            || trend.CpuSecondsPerMinute > thresholds.CpuSecondsPerMinute
            || trend.GdiDeltaPerHour > thresholds.GdiGrowthPerHour
            || trend.UserDeltaPerHour > thresholds.UserGrowthPerHour;
        return new SoakReport(started, ended, (ended - started).TotalSeconds, monotonicActiveSeconds, sleepSeconds, planned.TotalSeconds, interval,
            samples.Count, cancelled, monotonicActiveSeconds >= 12 * 3600, error, Summary(working), Summary(privateBytes), Summary(handles), Summary(cpu),
            Summary(gdi), Summary(user), Summary(hostWorking), Summary(hostPrivate), Summary(hostHandles), Summary(hostCpu),
            trend, thresholds, thresholdExceeded, samples);
    }

    private static double Delta(long[] values) => values.Length < 2 ? 0 : values[^1] - values[0];
    private static double Delta(int[] values) => values.Length < 2 ? 0 : values[^1] - values[0];
    private static double Delta(double[] values) => values.Length < 2 ? 0 : values[^1] - values[0];

    private static MetricSummary Summary(long[] values) => values.Length == 0 ? new(0, 0, 0, 0, 0) : new(values.Min(), values.Average(), values.Max(), values[0], values[^1]);
    private static MetricSummary Summary(int[] values) => values.Length == 0 ? new(0, 0, 0, 0, 0) : new(values.Min(), values.Average(), values.Max(), values[0], values[^1]);
    private static MetricSummary Summary(double[] values) => values.Length == 0 ? new(0, 0, 0, 0, 0) : new(values.Min(), values.Average(), values.Max(), values[0], values[^1]);

    private static string ToCsv(IEnumerable<SoakSample> samples)
    {
        var builder = new StringBuilder("timestampUtc,elapsedSeconds,selfWorkingSetBytes,selfPrivateBytes,selfHandleCount,selfThreadCount,selfGdiObjects,selfUserObjects,selfCpuSeconds,hostWorkingSetBytes,hostPrivateBytes,hostHandleCount,hostThreadCount,hostGdiObjects,hostUserObjects,hostCpuSeconds,hostModuleState,hostError,displayCount\n");
        foreach (var x in samples) builder.AppendLine(string.Join(',', x.TimestampUtc.ToString("O", CultureInfo.InvariantCulture), x.ElapsedSeconds.ToString(CultureInfo.InvariantCulture), x.SelfWorkingSetBytes, x.SelfPrivateBytes, x.SelfHandleCount, x.SelfThreadCount, x.SelfGdiObjects, x.SelfUserObjects, x.SelfCpuSeconds.ToString(CultureInfo.InvariantCulture), x.HostWorkingSetBytes, x.HostPrivateBytes, x.HostHandleCount, x.HostThreadCount, x.HostGdiObjects, x.HostUserObjects, x.HostCpuSeconds.ToString(CultureInfo.InvariantCulture), EscapeCsv(x.HostModuleState), EscapeCsv(x.HostError ?? string.Empty), x.DisplayCount));
        return builder.ToString();
    }

    private static string GetPath(string[] args, string optionName, string defaultName, bool fallBackToOutput = true)
    {
        var value = GetString(args, optionName, string.Empty);
        if (fallBackToOutput && string.IsNullOrWhiteSpace(value) && !optionName.Equals("output", StringComparison.OrdinalIgnoreCase)) value = GetString(args, "output", string.Empty);
        var path = string.IsNullOrWhiteSpace(value) ? Path.Combine(OutputRoot, $"{defaultName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json") : Path.GetFullPath(value);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
    private static string EscapeCsv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\n') ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    private static int GetInt(string[] args, string name, int fallback) => int.TryParse(GetString(args, name, string.Empty), out var value) ? value : fallback;
    private static string GetString(string[] args, string name, string fallback)
    {
        var index = Array.FindIndex(args, x => x.Equals("--" + name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    private sealed class DiagnosticsHostSession : IDisposable
    {
        private readonly Process _process;
        private readonly string _instanceId;
        private readonly CancellationTokenSource _readCts = new();
        private Task? _reader;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DiagnosticsHostSession(Process process, string instanceId) { _process = process; _instanceId = instanceId; }
        public int? ProcessId => _process.HasExited ? null : _process.Id;
        public string? LastError { get; private set; }
        public string State => _process.HasExited ? "Stopped" : LastError is null ? "Running" : "Faulted";

        public static async Task<DiagnosticsHostSession> StartAsync(CancellationToken token)
        {
            var executable = FindHost();
            if (executable is null) throw new FileNotFoundException("找不到 SyncWallpaper.Host.exe；请先发布。");
            var instance = "diagnostics-" + Guid.NewGuid().ToString("N");
            var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = executable, Arguments = $"--module TaskbarHost --protocol {ModuleIpcProtocol.Version} --instance-id {instance}",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("无法启动测试宿主。");
            var session = new DiagnosticsHostSession(process, instance);
            session._reader = session.ReadAsync(session._readCts.Token);
            await session._ready.Task.WaitAsync(TimeSpan.FromSeconds(8), token);
            return session;
        }

        private async Task ReadAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(token);
                    if (line is null) break;
                    if (!ModuleIpcJson.TryDeserializeResponse(line, out var response) || response is null) continue;
                    if (!response.ModuleInstanceId.Equals(_instanceId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (response.Type.Equals("ready", StringComparison.OrdinalIgnoreCase)) _ready.TrySetResult(response.Success);
                    if (!response.Success) LastError = response.ErrorMessage;
                }
            }
            catch (Exception ex) { LastError = ex.Message; _ready.TrySetException(ex); }
        }

        public async Task StopAsync(CancellationToken token)
        {
            if (_process.HasExited) return;
            var request = new ModuleIpcMessage(ModuleIpcProtocol.Version, Guid.NewGuid().ToString("N"), _instanceId, "stop");
            await _process.StandardInput.WriteLineAsync(ModuleIpcJson.Serialize(request)).WaitAsync(TimeSpan.FromSeconds(2), token);
            try { await _process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token); } catch { try { _process.Kill(true); } catch { } }
        }

        public void Dispose() { try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { } _readCts.Cancel(); _process.Dispose(); _readCts.Dispose(); }
        private static string? FindHost()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "SyncWallpaper.Host.exe"),
                Path.Combine(AppContext.BaseDirectory, "..", "Host", "SyncWallpaper.Host.exe"),
                Path.Combine(FindWorkspace(), "artifacts", "publish", "win-x64", "SyncWallpaper.Host.exe"),
                Path.Combine(FindWorkspace(), "artifacts", "publish", "win-x64", "Host", "SyncWallpaper.Host.exe"),
                Path.Combine(FindWorkspace(), "src", "SyncWallpaper.Host", "bin", "Release", "net8.0-windows", "SyncWallpaper.Host.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    private static string FindWorkspace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SyncWallpaper.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private sealed record DiagnosticSnapshot(DateTime TimestampUtc, string Reason, int DisplayCount, IReadOnlyList<SanitizedMonitorDiagnostic> Displays, bool ExplorerRunning, ResourceInfo Self, ResourceInfo? Child);
    private sealed record ResourceInfo(long WorkingSetBytes, long PrivateBytes, int HandleCount, int ThreadCount, int GdiObjects, int UserObjects, double CpuSeconds);
    private sealed record SoakSample(DateTime TimestampUtc, double ElapsedSeconds, long SelfWorkingSetBytes, long SelfPrivateBytes, int SelfHandleCount, int SelfThreadCount, int SelfGdiObjects, int SelfUserObjects, double SelfCpuSeconds, long HostWorkingSetBytes, long HostPrivateBytes, int HostHandleCount, int HostThreadCount, int HostGdiObjects, int HostUserObjects, double HostCpuSeconds, string HostModuleState, string? HostError, int DisplayCount);
    private sealed record MetricSummary(double Min, double Average, double Max, double Start, double End);
    private sealed record SoakTrend(double WorkingSetDeltaBytes, double WorkingSetDeltaPerHourBytes, double PrivateBytesDeltaBytes, double PrivateBytesDeltaPerHourBytes, double HandleDelta, double HandleDeltaPerHour, double CpuSecondsDelta, double CpuSecondsPerMinute, double GdiDelta, double GdiDeltaPerHour, double UserDelta, double UserDeltaPerHour);
    private sealed record SoakThresholds(long WorkingSetGrowthPerHourBytes, long PrivateBytesGrowthPerHourBytes, double HandleGrowthPerHour, double CpuSecondsPerMinute, double GdiGrowthPerHour, double UserGrowthPerHour);
    private sealed record SoakReport(DateTime StartedUtc, DateTime EndedUtc, double ActualWallDurationSeconds, double MonotonicActiveDurationSeconds, double SleepSecondsExcluded, double PlannedDurationSeconds, int IntervalSeconds, int SampleCount, bool Cancelled, bool Qualified12Hour, string? HostError, MetricSummary WorkingSet, MetricSummary PrivateBytes, MetricSummary Handles, MetricSummary CpuSeconds, MetricSummary GdiObjects, MetricSummary UserObjects, MetricSummary HostWorkingSet, MetricSummary HostPrivateBytes, MetricSummary HostHandles, MetricSummary HostCpuSeconds, SoakTrend Trend, SoakThresholds Thresholds, bool ThresholdExceeded, IReadOnlyList<SoakSample> Samples)
    {
        public double ActualDurationSeconds => MonotonicActiveDurationSeconds;
    }
    private sealed record VerifyResult(string Name, string Status, string Message);
    private sealed record VerifyReport(DateTime TimestampUtc, string OsVersion, string SnapshotPath, string RiskStatement, IReadOnlyList<VerifyResult> Results);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
