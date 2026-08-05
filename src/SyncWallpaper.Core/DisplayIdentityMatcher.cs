namespace SyncWallpaper.Core;

public enum DisplayIdentityMatchStatus
{
    ExactMatch,
    StrongMatch,
    ProbableMatch,
    Ambiguous,
    Unknown
}

public sealed class DisplayIdentityMatchResult
{
    public DisplayIdentityMatchStatus Status { get; init; }
    public MonitorIdentity? Monitor { get; init; }
    public int Score { get; init; }
    public bool CanAutoApply { get; init; }
    public string Basis { get; init; } = string.Empty;
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ConflictingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MonitorIdentity> TiedCandidates { get; init; } = Array.Empty<MonitorIdentity>();
}

/// <summary>
/// Compares monitor identities without using DISPLAY1/2/3 or array order.
/// The list overload is intentionally conservative: equal top scores become
/// Ambiguous even when a geometry hint would make one choice look convenient.
/// </summary>
public sealed class DisplayIdentityMatcher
{
    public DisplayIdentityMatchResult Match(MonitorIdentity expected, MonitorIdentity actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var evaluation = Evaluate(expected, actual);
        return ToResult(evaluation, actual, Array.Empty<MonitorIdentity>(), ambiguous: false);
    }

    public DisplayIdentityMatchResult Match(MonitorIdentity expected, IReadOnlyList<MonitorIdentity> candidates)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (candidates is null || candidates.Count == 0)
            return new() { Status = DisplayIdentityMatchStatus.Unknown, Basis = "没有当前显示器候选", Reasons = new[] { "候选列表为空" } };

        var evaluations = candidates.Select(candidate => (Monitor: candidate, Evaluation: Evaluate(expected, candidate)))
            .OrderByDescending(x => x.Evaluation.Score).ToArray();
        var best = evaluations[0];
        if (best.Evaluation.Score <= 0)
            return ToResult(best.Evaluation, best.Monitor, Array.Empty<MonitorIdentity>(), ambiguous: false);

        var tied = evaluations.Where(x => x.Evaluation.Score == best.Evaluation.Score).Select(x => x.Monitor).ToArray();
        if (tied.Length > 1)
        {
            var result = ToResult(best.Evaluation, null, tied, ambiguous: true);
            return new DisplayIdentityMatchResult
            {
                Status = DisplayIdentityMatchStatus.Ambiguous,
                Monitor = null,
                Score = result.Score,
                CanAutoApply = false,
                Basis = result.Basis,
                Reasons = result.Reasons.Concat(new[] { "多个候选具有相同最高匹配分数" }).ToArray(),
                ConflictingFields = result.ConflictingFields,
                TiedCandidates = tied
            };
        }
        return ToResult(best.Evaluation, best.Monitor, Array.Empty<MonitorIdentity>(), ambiguous: false);
    }

    private static DisplayIdentityMatchResult ToResult(Evaluation evaluation, MonitorIdentity? monitor, IReadOnlyList<MonitorIdentity> tied, bool ambiguous)
    {
        var status = ambiguous ? DisplayIdentityMatchStatus.Ambiguous : evaluation.Status;
        return new DisplayIdentityMatchResult
        {
            Status = status,
            Monitor = monitor,
            Score = evaluation.Score,
            CanAutoApply = !ambiguous && status is DisplayIdentityMatchStatus.ExactMatch or DisplayIdentityMatchStatus.StrongMatch,
            Basis = evaluation.Basis,
            Reasons = evaluation.Reasons,
            ConflictingFields = evaluation.ConflictingFields,
            TiedCandidates = tied
        };
    }

    private static Evaluation Evaluate(MonitorIdentity expected, MonitorIdentity actual)
    {
        var reasons = new List<string>();
        var conflicts = FindConflicts(expected, actual);

        if (!string.IsNullOrWhiteSpace(expected.StableId)
            && !string.IsNullOrWhiteSpace(actual.StableId)
            && string.Equals(expected.StableId, actual.StableId, StringComparison.OrdinalIgnoreCase)
            && actual.StableIdSource != MonitorIdentitySource.Ambiguous)
        {
            var exact = actual.StableIdSource == MonitorIdentitySource.EdidSerial;
            reasons.Add($"稳定 ID（{actual.StableIdSource}）一致");
            return new(exact ? DisplayIdentityMatchStatus.ExactMatch : DisplayIdentityMatchStatus.StrongMatch,
                exact ? 1200 : 1050, "StableId", reasons, conflicts);
        }

        if (expected.HasUsableSerial && actual.HasUsableSerial && string.Equals(expected.SerialKey, actual.SerialKey, StringComparison.OrdinalIgnoreCase))
            return new(DisplayIdentityMatchStatus.ExactMatch, 1200, "厂商 + 产品代码 + EDID 序列号", new[] { "EDID 序列号一致" }, conflicts);

        if (SameNonEmpty(expected.ContainerId, actual.ContainerId))
            return new(DisplayIdentityMatchStatus.StrongMatch, 1050, "Container ID", new[] { "Container ID 一致" }, conflicts);

        if (HasPermanentPath(expected) && string.Equals(expected.MonitorDevicePath, actual.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
            return new(DisplayIdentityMatchStatus.StrongMatch, 900, "monitorDevicePath", new[] { "monitorDevicePath 一致" }, conflicts);

        if (SameNonEmpty(expected.InstanceName, actual.InstanceName))
            return new(DisplayIdentityMatchStatus.StrongMatch, 820, "WmiMonitorID InstanceName", new[] { "InstanceName 一致" }, conflicts);

        if (SameHardware(expected, actual))
            return new(DisplayIdentityMatchStatus.StrongMatch, 760, "Adapter LUID + Target ID + 接口", new[] { "显卡适配器、Target ID、接口类型和 connectorInstance 一致" }, conflicts);

        if (SameModel(expected, actual) && SameConnection(expected, actual) && SameGeometry(expected, actual))
            return new(DisplayIdentityMatchStatus.ProbableMatch, 520, "厂商 + 产品代码 + 接口 + 几何", new[] { "型号、接口、分辨率、方向和桌面位置一致" }, conflicts);

        if (SameModel(expected, actual) && SameGeometry(expected, actual))
            return new(DisplayIdentityMatchStatus.ProbableMatch, 360, "厂商 + 产品代码 + 几何", new[] { "型号和几何信息一致" }, conflicts);

        if (SameGeometry(expected, actual))
            return new(DisplayIdentityMatchStatus.ProbableMatch, 120, "分辨率 + 方向 + 桌面位置", new[] { "仅几何信息一致，不能单独安全应用" }, conflicts);

        return new(DisplayIdentityMatchStatus.Unknown, 0, string.Empty, reasons.Count == 0 ? new[] { "没有可靠身份依据" } : reasons, conflicts);
    }

    private static bool SameNonEmpty(string left, string right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasPermanentPath(MonitorIdentity monitor)
        => !string.IsNullOrWhiteSpace(monitor.MonitorDevicePath)
            && !monitor.MonitorDevicePath.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)
            && !monitor.MonitorDevicePath.StartsWith("fallback://", StringComparison.OrdinalIgnoreCase);

    private static bool SameModel(MonitorIdentity left, MonitorIdentity right)
        => left.ModelKey.Length > 1 && string.Equals(left.ModelKey, right.ModelKey, StringComparison.OrdinalIgnoreCase);

    private static bool SameHardware(MonitorIdentity left, MonitorIdentity right)
        => SameNonEmpty(left.AdapterId, right.AdapterId)
            && left.TargetId == right.TargetId
            && left.OutputTechnology == right.OutputTechnology
            && left.ConnectorInstance == right.ConnectorInstance;

    private static bool SameConnection(MonitorIdentity left, MonitorIdentity right)
        => left.OutputTechnology == right.OutputTechnology && left.ConnectorInstance == right.ConnectorInstance;

    private static bool SameGeometry(MonitorIdentity left, MonitorIdentity right)
        => left.Width == right.Width && left.Height == right.Height && left.Rotation == right.Rotation
            && left.DesktopX == right.DesktopX && left.DesktopY == right.DesktopY;

    private static IReadOnlyList<string> FindConflicts(MonitorIdentity expected, MonitorIdentity actual)
    {
        var conflicts = new List<string>();
        if (expected.HasUsableSerial && actual.HasUsableSerial && !string.Equals(expected.SerialKey, actual.SerialKey, StringComparison.OrdinalIgnoreCase)) conflicts.Add("EDID 序列号");
        if (!string.IsNullOrWhiteSpace(expected.ContainerId) && !string.IsNullOrWhiteSpace(actual.ContainerId) && !SameNonEmpty(expected.ContainerId, actual.ContainerId)) conflicts.Add("Container ID");
        if (HasPermanentPath(expected) && HasPermanentPath(actual) && !string.Equals(expected.MonitorDevicePath, actual.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) conflicts.Add("monitorDevicePath");
        if (!string.IsNullOrWhiteSpace(expected.InstanceName) && !string.IsNullOrWhiteSpace(actual.InstanceName) && !SameNonEmpty(expected.InstanceName, actual.InstanceName)) conflicts.Add("InstanceName");
        if (expected.Width > 0 && actual.Width > 0 && expected.Width != actual.Width) conflicts.Add("分辨率宽度");
        if (expected.Height > 0 && actual.Height > 0 && expected.Height != actual.Height) conflicts.Add("分辨率高度");
        if (expected.Rotation > 0 && actual.Rotation > 0 && expected.Rotation != actual.Rotation) conflicts.Add("旋转方向");
        return conflicts;
    }

    private sealed record Evaluation(DisplayIdentityMatchStatus Status, int Score, string Basis, IReadOnlyList<string> Reasons, IReadOnlyList<string> ConflictingFields);
}

public static class MonitorIdentityBuilder
{
    public static IReadOnlyList<MonitorIdentity> AssignStableIds(IEnumerable<MonitorIdentity> monitors)
    {
        var list = monitors.Where(m => m is not null).ToList();
        var serialCounts = CountBy(list, x => x.HasUsableSerial ? x.SerialKey : string.Empty);
        var containerCounts = CountBy(list, x => x.ContainerId);
        var pathCounts = CountBy(list, x => HasPermanentPath(x) ? x.MonitorDevicePath : string.Empty);
        var instanceCounts = CountBy(list, x => x.InstanceName);
        var hardwareCounts = CountBy(list, x => HasHardware(x) ? x.HardwareKey : string.Empty);
        foreach (var monitor in list)
        {
            if (monitor.HasUsableSerial && IsUnique(serialCounts, monitor.SerialKey))
            {
                monitor.StableId = "edid:" + Canonical(monitor.SerialKey);
                monitor.StableIdSource = MonitorIdentitySource.EdidSerial;
            }
            else if (IsUniqueNonEmpty(containerCounts, monitor.ContainerId))
            {
                monitor.StableId = "container:" + Canonical(monitor.ContainerId);
                monitor.StableIdSource = MonitorIdentitySource.ContainerId;
            }
            else if (HasPermanentPath(monitor) && IsUnique(pathCounts, monitor.MonitorDevicePath))
            {
                monitor.StableId = "path:" + Canonical(monitor.MonitorDevicePath);
                monitor.StableIdSource = MonitorIdentitySource.MonitorDevicePath;
            }
            else if (IsUniqueNonEmpty(instanceCounts, monitor.InstanceName))
            {
                monitor.StableId = "instance:" + Canonical(monitor.InstanceName);
                monitor.StableIdSource = MonitorIdentitySource.InstanceName;
            }
            else if (HasHardware(monitor) && IsUnique(hardwareCounts, monitor.HardwareKey))
            {
                monitor.StableId = "topology:" + Canonical(monitor.HardwareKey);
                monitor.StableIdSource = MonitorIdentitySource.HardwareTopology;
            }
            else if (HasGeometry(monitor) && list.Count(x => GeometryKey(x) == GeometryKey(monitor)) == 1)
            {
                monitor.StableId = "geometry:" + Canonical(GeometryKey(monitor));
                monitor.StableIdSource = MonitorIdentitySource.Geometry;
            }
            else
            {
                monitor.StableId = string.Empty;
                monitor.StableIdSource = MonitorIdentitySource.Ambiguous;
            }
        }
        return list;
    }

    private static Dictionary<string, int> CountBy(IEnumerable<MonitorIdentity> monitors, Func<MonitorIdentity, string> key)
        => monitors.Select(key).Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
    private static bool IsUnique(IReadOnlyDictionary<string, int> counts, string key) => !string.IsNullOrWhiteSpace(key) && counts.TryGetValue(key, out var count) && count == 1;
    private static bool IsUniqueNonEmpty(IReadOnlyDictionary<string, int> counts, string key) => IsUnique(counts, key);
    private static bool HasHardware(MonitorIdentity x) => !string.IsNullOrWhiteSpace(x.AdapterId) && x.OutputTechnology != 0 && x.ConnectorInstance != 0;
    private static bool HasGeometry(MonitorIdentity x) => x.Width > 0 && x.Height > 0;
    private static string GeometryKey(MonitorIdentity x) => $"{x.ModelKey}|{x.Width}x{x.Height}|{x.Rotation}|{x.DesktopX},{x.DesktopY}";
    private static string Canonical(string value) => value.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
    private static bool HasPermanentPath(MonitorIdentity x) => !string.IsNullOrWhiteSpace(x.MonitorDevicePath)
        && !x.MonitorDevicePath.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)
        && !x.MonitorDevicePath.StartsWith("fallback://", StringComparison.OrdinalIgnoreCase);
}
