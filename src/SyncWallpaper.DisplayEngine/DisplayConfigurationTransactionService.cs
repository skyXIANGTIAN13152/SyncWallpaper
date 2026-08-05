using SyncWallpaper.Core;

namespace SyncWallpaper.DisplayEngine;

public sealed record DisplayModeInfo(int Width, int Height, uint RefreshRateNumerator, uint RefreshRateDenominator, int Rotation);

public interface IDisplayModeCatalog
{
    IReadOnlyList<DisplayModeInfo> GetModes(MonitorIdentity monitor);
}

public sealed class DisplayConfigurationValidator : IDisplayConfigurationValidator
{
    private readonly IDisplayModeCatalog? _modeCatalog;

    public DisplayConfigurationValidator(IDisplayModeCatalog? modeCatalog = null) => _modeCatalog = modeCatalog;

    public DisplayValidationResult Validate(DisplayConfigurationProfile target, DisplayTopologySnapshot current)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var differences = BuildDifferences(target, current.Profile);

        if (target.Displays.Count == 0) errors.Add("显示配置不包含任何显示器。");
        if (target.Displays.GroupBy(x => x.MonitorFingerprint.MonitorDevicePath, StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            errors.Add("显示器配置包含空的或重复的 monitorDevicePath。");
        if (target.Displays.Count(x => x.Enabled && x.IsPrimary) > 1) errors.Add("只能有一个主显示器。");
        if (target.Displays.All(x => !x.Enabled)) errors.Add("至少需要保留一个启用的显示器。");

        foreach (var entry in target.Displays)
        {
            var currentMonitor = FindCurrent(entry, current.Profile);
            if (entry.Enabled && currentMonitor is null)
            {
                errors.Add($"找不到目标显示器：{entry.MonitorFingerprint.DisplayLabel}。");
                continue;
            }

            if (entry.Enabled && (entry.Width <= 0 || entry.Height <= 0))
                errors.Add($"显示器 {entry.MonitorFingerprint.DisplayLabel} 的分辨率无效。");
            if (entry.RefreshRateNumerator == 0 || entry.RefreshRateDenominator == 0)
                errors.Add($"显示器 {entry.MonitorFingerprint.DisplayLabel} 的刷新率无效。");
            if (entry.Rotation is < 1 or > 4)
                errors.Add($"显示器 {entry.MonitorFingerprint.DisplayLabel} 的旋转值无效。");
            if (entry.DpiScale is < 0.5 or > 5.0)
                errors.Add($"显示器 {entry.MonitorFingerprint.DisplayLabel} 的 DPI 缩放无效。");

            if (currentMonitor is not null && _modeCatalog is not null && entry.Enabled)
            {
                var supported = _modeCatalog.GetModes(currentMonitor);
                if (supported.Count > 0 && !supported.Any(m => ((m.Width == entry.Width && m.Height == entry.Height) ||
                        (m.Width == entry.Height && m.Height == entry.Width)) &&
                        m.RefreshRateNumerator * entry.RefreshRateDenominator == entry.RefreshRateNumerator * m.RefreshRateDenominator))
                    errors.Add($"显示器 {entry.MonitorFingerprint.DisplayLabel} 不支持 {entry.Width}×{entry.Height} @{entry.RefreshRateNumerator}/{entry.RefreshRateDenominator}。");
            }

            if (entry.HdrEnabled is not null)
                warnings.Add($"HDR 设置将在重新读取真实状态后确认：{entry.MonitorFingerprint.DisplayLabel}。");
            if (entry.DpiScale != 1.0)
                warnings.Add($"DPI 设置可能需要重新登录：{entry.MonitorFingerprint.DisplayLabel}。");
        }

        if (target.Displays.Any(x => x.Enabled && x.IsPrimary) && target.Displays.Where(x => x.Enabled && x.IsPrimary).Any(x => x.DesktopX != 0 || x.DesktopY != 0))
            warnings.Add("主显示器通常应位于虚拟桌面的 (0,0)，实际位置将由 Windows 校正。");

        return errors.Count == 0
            ? DisplayValidationResult.Valid(warnings, differences)
            : new DisplayValidationResult { IsValid = false, Errors = errors, Warnings = warnings, Differences = differences };
    }

    private static MonitorIdentity? FindCurrent(DisplayConfigurationEntry entry, DisplayConfigurationProfile current)
    {
        var fingerprint = entry.MonitorFingerprint;
        var byPath = current.Displays.FirstOrDefault(x => string.Equals(x.MonitorFingerprint.MonitorDevicePath, fingerprint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
        if (byPath is not null && !string.IsNullOrWhiteSpace(fingerprint.MonitorDevicePath)) return byPath.MonitorFingerprint;
        var bySerial = current.Displays.FirstOrDefault(x => fingerprint.HasUsableSerial && x.MonitorFingerprint.HasUsableSerial &&
            string.Equals(x.MonitorFingerprint.ModelKey, fingerprint.ModelKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.MonitorFingerprint.EdidSerialNumber, fingerprint.EdidSerialNumber, StringComparison.OrdinalIgnoreCase));
        if (bySerial is not null) return bySerial.MonitorFingerprint;
        if (fingerprint.HasUsableSerial) return null;
        return current.Displays.FirstOrDefault(x => x.MonitorFingerprint.AdapterId == fingerprint.AdapterId &&
            x.MonitorFingerprint.TargetId == fingerprint.TargetId &&
            x.MonitorFingerprint.ConnectorInstance == fingerprint.ConnectorInstance)?.MonitorFingerprint;
    }

    public static IReadOnlyList<DisplayConfigurationDiff> BuildDifferences(DisplayConfigurationProfile target, DisplayConfigurationProfile current)
    {
        var differences = new List<DisplayConfigurationDiff>();
        var currentByPath = current.Displays.ToDictionary(x => x.MonitorFingerprint.MonitorDevicePath, StringComparer.OrdinalIgnoreCase);
        foreach (var targetEntry in target.Displays)
        {
            var path = targetEntry.MonitorFingerprint.MonitorDevicePath;
            if (!currentByPath.TryGetValue(path, out var currentEntry))
            {
                differences.Add(new($"显示器 {targetEntry.MonitorFingerprint.DisplayLabel}", "不存在", targetEntry.Enabled ? "启用" : "禁用"));
                continue;
            }
            var label = targetEntry.MonitorFingerprint.DisplayLabel;
            if (currentEntry.IsPrimary != targetEntry.IsPrimary)
                differences.Add(new($"{label} 主显示器", currentEntry.IsPrimary ? "是" : "否", targetEntry.IsPrimary ? "是" : "否"));
            if (currentEntry.Enabled != targetEntry.Enabled)
                differences.Add(new($"{label} 状态", currentEntry.Enabled ? "启用" : "禁用", targetEntry.Enabled ? "启用" : "禁用"));
            if (currentEntry.Width != targetEntry.Width || currentEntry.Height != targetEntry.Height)
                differences.Add(new($"{label} 分辨率", $"{currentEntry.Width}×{currentEntry.Height}", $"{targetEntry.Width}×{targetEntry.Height}"));
            if (currentEntry.RefreshRateNumerator * targetEntry.RefreshRateDenominator != targetEntry.RefreshRateNumerator * currentEntry.RefreshRateDenominator)
                differences.Add(new($"{label} 刷新率", $"{currentEntry.RefreshRateNumerator}/{currentEntry.RefreshRateDenominator}", $"{targetEntry.RefreshRateNumerator}/{targetEntry.RefreshRateDenominator}"));
            if (currentEntry.Rotation != targetEntry.Rotation)
                differences.Add(new($"{label} 方向", currentEntry.Rotation.ToString(), targetEntry.Rotation.ToString()));
            if (currentEntry.DesktopX != targetEntry.DesktopX || currentEntry.DesktopY != targetEntry.DesktopY)
                differences.Add(new($"{label} 桌面位置", $"{currentEntry.DesktopX},{currentEntry.DesktopY}", $"{targetEntry.DesktopX},{targetEntry.DesktopY}"));
        }
        return differences;
    }
}

public sealed class DisplayConfigurationTransactionService
{
    private readonly IDisplayTopologyReader _reader;
    private readonly IDisplayConfigurationValidator _validator;
    private readonly IDisplayConfigurationApplier _applier;
    private readonly IDisplayConfigurationVerifier _verifier;
    private readonly IDisplayConfigurationRollbackService _rollback;
    private readonly IDisplayChangeStabilizer _stabilizer;
    private readonly IDisplayConfirmationService? _confirmation;
    private readonly IStage1Logger _logger;
    private readonly IFaultInjector _faultInjector;

    public DisplayConfigurationTransactionService(
        IDisplayTopologyReader reader,
        IDisplayConfigurationValidator validator,
        IDisplayConfigurationApplier applier,
        IDisplayConfigurationVerifier verifier,
        IDisplayConfigurationRollbackService rollback,
        IDisplayChangeStabilizer stabilizer,
        IStage1Logger logger,
        IDisplayConfirmationService? confirmation = null,
        IFaultInjector? faultInjector = null)
    {
        _reader = reader; _validator = validator; _applier = applier; _verifier = verifier;
        _rollback = rollback; _stabilizer = stabilizer; _logger = logger; _confirmation = confirmation;
        _faultInjector = faultInjector ?? NoFaultInjector.Instance;
    }

    public async Task<DisplayConfigurationApplyResult> ApplyAsync(
        DisplayConfigurationProfile target,
        DisplayConfigurationApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DisplayConfigurationApplyOptions();
        var steps = new List<string>();
        DisplayTopologySnapshot? snapshot = null;
        DisplayValidationResult? validation = null;
        var mutationStarted = false;
        try
        {
            snapshot = _reader.Capture();
            steps.Add("已保存当前显示配置快照");
            validation = _validator.Validate(target, snapshot);
            steps.Add($"预检查完成：{validation.Errors.Count} 个错误，{validation.Warnings.Count} 个警告");
            if (!validation.IsValid)
            {
                _logger.Warn("Display", string.Join("；", validation.Errors));
                return new() { Status = DisplayConfigurationTransactionStatus.PrecheckFailed, Message = "显示配置预检查失败。", Validation = validation, Steps = steps };
            }
            if (options.ValidationOnly)
                return new() { Status = DisplayConfigurationTransactionStatus.Planned, Message = "显示配置预检查通过。", Validation = validation, Steps = steps };

            cancellationToken.ThrowIfCancellationRequested();
            mutationStarted = true;
            _faultInjector.ThrowIfRequested(FaultPoint.DisplayApply);
            if (_applier is IStagedDisplayConfigurationApplier staged)
            {
                await staged.ApplyTopologyAsync(target, cancellationToken);
                steps.Add("已提交显示拓扑和基本模式（阶段 1）");
                await _stabilizer.WaitForStableAsync(cancellationToken);
                steps.Add("显示系统已稳定（阶段 1）");
                await staged.ApplyFinalAsync(target, cancellationToken);
                steps.Add("已提交主显示器、位置、旋转和最终模式（阶段 2）");
            }
            else
            {
                await _applier.ApplyAsync(target, cancellationToken);
                steps.Add("已提交显示拓扑和基本模式（单阶段适配器）");
            }
            await _stabilizer.WaitForStableAsync(cancellationToken);
            steps.Add("显示系统已稳定");
            _faultInjector.ThrowIfRequested(FaultPoint.DisplayVerify);
            var verification = await _verifier.VerifyAsync(target, cancellationToken);
            steps.Add(verification.IsValid ? "真实状态验证通过" : "真实状态验证失败");
            if (!verification.IsValid)
            {
                _logger.Error("Display", "显示配置应用后真实状态不匹配，开始回滚。");
                return await RollbackAsync(snapshot, DisplayConfigurationTransactionStatus.VerificationFailed,
                    "显示配置验证失败，已请求回滚：" + string.Join("；", verification.Errors), steps, validation, CancellationToken.None);
            }

            if (options.RequireConfirmation)
            {
                if (_confirmation is null)
                    return await RollbackAsync(snapshot, DisplayConfigurationTransactionStatus.ConfirmationExpired, "未提供确认界面，已恢复旧显示配置。", steps, validation, CancellationToken.None);
                var keep = await _confirmation.ConfirmAsync(target, verification, options.ConfirmationTimeout, cancellationToken);
                if (!keep && options.RestoreOnConfirmationTimeout)
                    return await RollbackAsync(snapshot, DisplayConfigurationTransactionStatus.ConfirmationExpired, "确认倒计时结束，已恢复旧显示配置。", steps, validation, CancellationToken.None);
            }

            _logger.Info("Display", $"显示配置已提交：{target.Name}");
            return new() { Status = DisplayConfigurationTransactionStatus.Applied, Message = "显示配置已应用并验证。", Validation = verification, Applied = true, Verified = true, Steps = steps };
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Display", "显示配置事务已取消。");
            if (mutationStarted && snapshot is not null)
                return await RollbackAsync(snapshot, DisplayConfigurationTransactionStatus.Cancelled, "显示配置事务取消，已尝试恢复旧配置。", steps, validation ?? DisplayValidationResult.Invalid("事务取消"), CancellationToken.None);
            return new() { Status = DisplayConfigurationTransactionStatus.Cancelled, Message = "显示配置事务已取消。", Steps = steps };
        }
        catch (Exception ex)
        {
            _logger.Error("Display", ex.ToString());
            if (mutationStarted && snapshot is not null)
                return await RollbackAsync(snapshot, DisplayConfigurationTransactionStatus.Failed, "显示配置应用失败，已尝试恢复旧配置：" + ex.Message, steps, validation ?? DisplayValidationResult.Invalid(ex.Message), CancellationToken.None);
            return new() { Status = DisplayConfigurationTransactionStatus.Failed, Message = ex.Message, Steps = steps };
        }
    }

    private async Task<DisplayConfigurationApplyResult> RollbackAsync(
        DisplayTopologySnapshot snapshot,
        DisplayConfigurationTransactionStatus status,
        string message,
        List<string> steps,
        DisplayValidationResult validation,
        CancellationToken cancellationToken)
    {
        try
        {
            _faultInjector.ThrowIfRequested(FaultPoint.DisplayRollback);
            await _rollback.RollbackAsync(snapshot, cancellationToken);
            await _stabilizer.WaitForStableAsync(cancellationToken);
            steps.Add("旧显示配置已恢复");
            var restored = await _verifier.VerifyAsync(snapshot.Profile, cancellationToken);
            if (!restored.IsValid)
            {
                steps.Add("回滚后的真实状态仍不匹配");
                _logger.Error("Display", "显示配置回滚验证失败。");
                return new() { Status = DisplayConfigurationTransactionStatus.RollbackFailed, Message = "显示配置和回滚均未通过验证。", Validation = validation, RollbackAttempted = true, RollbackSucceeded = false, Steps = steps };
            }
            return new() { Status = DisplayConfigurationTransactionStatus.RolledBack, Message = message, Validation = validation, RollbackAttempted = true, RollbackSucceeded = true, Steps = steps };
        }
        catch (Exception ex)
        {
            steps.Add("回滚调用失败：" + ex.Message);
            _logger.Error("Display", "显示配置回滚失败：" + ex);
            return new() { Status = DisplayConfigurationTransactionStatus.RollbackFailed, Message = "显示配置回滚失败，请立即检查系统显示设置。", Validation = validation, RollbackAttempted = true, RollbackSucceeded = false, Steps = steps };
        }
    }
}

public sealed class DisplayProfileRepository : IDisplayProfileRepository
{
    private readonly ConfigurationStore _store;
    private readonly string _fileName;
    private readonly DisplayConfigurationDocument _document;

    public DisplayProfileRepository(ConfigurationStore store, string fileName = "display-configurations.json")
    {
        _store = store; _fileName = fileName;
        _document = _store.Load(_fileName, new DisplayConfigurationDocument());
    }

    public IReadOnlyList<DisplayConfigurationProfile> List() => _document.Profiles.ToArray();

    public void Save(DisplayConfigurationProfile profile)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        var existing = _document.Profiles.FindIndex(x => x.ProfileId.Equals(profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _document.Profiles[existing] = profile;
        else _document.Profiles.Add(profile);
        _store.Save(_fileName, _document);
    }

    public bool Delete(string profileId)
    {
        var removed = _document.Profiles.RemoveAll(x => x.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) _store.Save(_fileName, _document);
        return removed;
    }

    public DisplayConfigurationProfile? Find(string profileId) => _document.Profiles.FirstOrDefault(x => x.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    public DisplayConfigurationProfile Copy(string profileId, string name)
    {
        var source = Find(profileId) ?? throw new InvalidOperationException("找不到显示配置。");
        var clone = System.Text.Json.JsonSerializer.Deserialize<DisplayConfigurationProfile>(
            System.Text.Json.JsonSerializer.Serialize(source)) ?? new DisplayConfigurationProfile();
        clone.ProfileId = Guid.NewGuid().ToString("N"); clone.Name = name; clone.CreatedAt = DateTime.UtcNow; clone.ModifiedAt = DateTime.UtcNow;
        Save(clone);
        return clone;
    }
}
