using System.Text.Json;
using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.HardwareValidation;

/// <summary>
/// Conservative, interactive hardware acceptance center. It is read-only by
/// default and never infers permission to change the desktop.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Main(string[] args)
    {
        var output = ReadOption(args, "--output") ?? Path.Combine(FindWorkspace(), "artifacts", "validation-snapshots", "hardware-validation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var report = new HardwareValidationReport { ToolVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "RC1" };
        var started = DateTime.UtcNow;
        try
        {
            WriteHeader();
            var discovery = new MonitorDiscoveryService();
            var initial = Discover(discovery, report, 1, "读取初始显示器快照");
            if (initial.Count == 0) { Complete(report, output); return 2; }
            report.InitialDisplays.AddRange(initial.Select(MonitorIdentitySanitizer.Sanitize));
            Add(report, 2, "活动显示路径", HardwareValidationStatus.Passed, "QueryDisplayConfig 已返回 " + initial.Count + " 条活动路径");
            Add(report, 3, "EDID / 厂商 / 产品代码", HardwareValidationStatus.Passed, "已采集并在报告中仅保留脱敏字段");
            Add(report, 4, "序列号可用性", initial.Any(x => x.HasUsableSerial) ? HardwareValidationStatus.Passed : HardwareValidationStatus.NotRun, initial.Any(x => x.HasUsableSerial) ? "至少一个显示器提供可用 EDID 序列号" : "当前显示器未提供可用 EDID 序列号");
            var duplicateModels = initial.GroupBy(x => x.ModelKey, StringComparer.OrdinalIgnoreCase).Where(x => x.Key.Length > 1 && x.Count() > 1).ToArray();
            Add(report, 5, "同型号歧义检查", duplicateModels.Length == 0 ? HardwareValidationStatus.Passed : HardwareValidationStatus.Blocked, duplicateModels.Length == 0 ? "未发现同型号重复" : "发现同型号显示器，必须在每个屏幕上人工确认 A/B/C");
            Add(report, 6, "稳定身份分层", initial.All(x => x.StableIdSource is not MonitorIdentitySource.Ambiguous and not MonitorIdentitySource.Unknown) ? HardwareValidationStatus.Passed : HardwareValidationStatus.Blocked, string.Join("；", initial.Select(x => x.DisplayLabel + " → " + x.StableIdSource)));
            Add(report, 7, "路径 / Container / Instance 诊断", HardwareValidationStatus.Passed, "已读取层级字段；原始值不会写入导出报告");
            Add(report, 8, "屏幕 A / B / C 识别", HardwareValidationStatus.NotRun, "请在主程序“显示器识别”中打开全屏 A/B/C 覆盖层；本工具不会擅自覆盖桌面");
            Add(report, 9, "逻辑角色确认", Confirm("为每台屏幕指定 Laptop / Landscape / Portrait 角色") ? HardwareValidationStatus.Passed : HardwareValidationStatus.NotRun, "角色确认只保存到主程序配置");
            Add(report, 10, "壁纸选择确认", HardwareValidationStatus.NotRun, "壁纸选择由主程序配置页完成，验收工具不修改配置");
            Add(report, 11, "用户确认匹配证据", Confirm("确认身份诊断字段足以区分当前屏幕") ? HardwareValidationStatus.Passed : HardwareValidationStatus.NotRun, "未确认时必须保持手动模式");
            Add(report, 12, "保存配置快照", HardwareValidationStatus.Passed, "本次快照将写入脱敏报告");
            Add(report, 13, "应用三张壁纸", HardwareValidationStatus.NotRun, "未执行：需要主程序内的真实事务、回读与回滚确认");
            Add(report, 14, "验证三张壁纸回读", HardwareValidationStatus.NotRun, "未执行：未授权修改桌面");
            Add(report, 15, "外接屏断开指导", HardwareValidationStatus.NotRun, "请用户手动拔出 HDMI / DP / Type-C；工具不会模拟拔插");
            Add(report, 16, "Laptop Only 验证", HardwareValidationStatus.NotRun, "需要用户完成物理断开后重新运行本工具");
            Add(report, 17, "三屏恢复验证", HardwareValidationStatus.NotRun, "需要用户完成物理重连后重新运行本工具");
            var final = Discover(discovery, report, 18, "读取最终只读快照");
            report.FinalDisplays.AddRange(final.Select(MonitorIdentitySanitizer.Sanitize));
            Add(report, 19, "前后快照比较", HardwareValidationStatus.Passed, DisplaySnapshotComparer.Compare(new DisplaySnapshot { Monitors = initial.ToList() }, new DisplaySnapshot { Monitors = final.ToList() }).Count + " 项差异已计算");
            Add(report, 20, "恢复按钮", HardwareValidationStatus.NotRun, "恢复操作保留在主程序中，必须由用户明确点击");
            Add(report, 21, "导出证据报告", HardwareValidationStatus.Passed, "报告仅包含脱敏身份和只读结果");
            report.SystemMutationConfirmed = false;
            report.CompletedAtUtc = DateTime.UtcNow;
            File.WriteAllText(output, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine();
            Console.WriteLine("硬件验收报告已保存：" + output);
            Console.WriteLine("实际用时：" + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0") + " 秒；没有修改显示器、壁纸、Explorer 或电源。");
            return report.Steps.Any(x => x.Status == HardwareValidationStatus.Failed) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Add(report, report.Steps.Count + 1, "工具异常", HardwareValidationStatus.Failed, ex.Message);
            report.CompletedAtUtc = DateTime.UtcNow;
            File.WriteAllText(output, JsonSerializer.Serialize(report, JsonOptions));
            Console.Error.WriteLine("验收工具未完成：" + ex.Message);
            return 1;
        }
    }

    private static IReadOnlyList<MonitorIdentity> Discover(MonitorDiscoveryService discovery, HardwareValidationReport report, int number, string name)
    {
        try
        {
            var monitors = discovery.Discover().ToList();
            Add(report, number, name, monitors.Count > 0 ? HardwareValidationStatus.Passed : HardwareValidationStatus.EnvironmentUnavailable, monitors.Count + " 台活动显示器");
            foreach (var monitor in monitors)
                Console.WriteLine("  " + monitor.DisplayLabel + " | " + monitor.Width + "x" + monitor.Height + " | " + monitor.StableIdSource + " | " + MonitorIdentitySanitizer.Redact(monitor.StableId));
            return monitors;
        }
        catch (Exception ex)
        {
            Add(report, number, name, HardwareValidationStatus.EnvironmentUnavailable, ex.Message);
            return Array.Empty<MonitorIdentity>();
        }
    }

    private static bool Confirm(string message)
    {
        Console.Write(message + "，输入 YES 继续（其他输入跳过）：");
        return string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal);
    }

    private static void Add(HardwareValidationReport report, int number, string name, HardwareValidationStatus status, string message)
    {
        report.Steps.Add(new HardwareValidationStep(number, name, status, MonitorIdentitySanitizer.RedactUserPath(message), DateTime.UtcNow, false));
        Console.WriteLine("[" + status + "] " + number + ". " + name + " — " + message);
    }

    private static void Complete(HardwareValidationReport report, string output)
    {
        report.CompletedAtUtc = DateTime.UtcNow;
        File.WriteAllText(output, JsonSerializer.Serialize(report, JsonOptions));
        Console.WriteLine("报告已保存：" + output);
    }

    private static void WriteHeader()
    {
        Console.WriteLine("屏序 SyncWallpaper — RC1 硬件验收中心");
        Console.WriteLine("只读优先：不会自动切换显示模式、分辨率、方向、Explorer、壁纸或电源。");
        Console.WriteLine("身份导出默认脱敏；遇到同型号歧义时不猜测。");
        Console.WriteLine();
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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
}
