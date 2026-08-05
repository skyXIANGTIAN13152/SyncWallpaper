using SyncWallpaper.Core;

namespace SyncWallpaper.AudioEngine;

public sealed class AudioConfigurationEngine : IAudioConfigurationEngine
{
    private readonly IAudioEndpointProvider _provider;
    private readonly IStage1Logger _logger;
    private readonly IFaultInjector _faultInjector;

    public AudioConfigurationEngine(IAudioEndpointProvider provider, IStage1Logger logger, IFaultInjector? faultInjector = null)
    {
        _provider = provider; _logger = logger; _faultInjector = faultInjector ?? NoFaultInjector.Instance;
    }

    public async Task<AudioConfigurationResult> ApplyAsync(AudioProfile profile, AudioStepMode overallMode = AudioStepMode.Optional, CancellationToken cancellationToken = default)
    {
        var steps = new List<string> { "Prepare：音频事务开始" };
        if (overallMode == AudioStepMode.Disabled)
            return new() { Success = true, Message = "音频步骤已禁用。", Steps = new[] { "已跳过音频配置" } };

        IReadOnlyList<AudioEndpointReference> endpoints;
        IReadOnlyDictionary<AudioEndpointRole, AudioEndpointReference?> before = new Dictionary<AudioEndpointRole, AudioEndpointReference?>();
        var captured = false;
        var changedRoles = new List<AudioEndpointRole>();
        try
        {
            endpoints = _provider.Enumerate();
            steps.Add("CaptureCurrentState：已读取端点和默认角色");
            before = profile.Assignments.Select(x => x.Role).Distinct().ToDictionary(x => x, x => _provider.GetDefault(x));
            captured = true;
            var requiredFailure = false;
            foreach (var assignment in profile.Assignments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mode = assignment.Mode == AudioStepMode.Disabled ? overallMode : assignment.Mode;
                if (mode == AudioStepMode.Disabled) { steps.Add($"{assignment.Role}：已禁用"); continue; }

                if (_faultInjector.IsRequested(FaultPoint.AudioDeviceDisappearance))
                {
                    _faultInjector.ThrowIfRequested(FaultPoint.AudioDeviceDisappearance);
                }
                var endpoint = endpoints.FirstOrDefault(x => x.DeviceId.Equals(assignment.Endpoint.DeviceId, StringComparison.OrdinalIgnoreCase));
                if (endpoint is null || endpoint.State != AudioEndpointState.Active)
                {
                    var message = endpoint is null ? $"{assignment.Role} 设备不存在：{assignment.Endpoint.FriendlyName}" : $"{assignment.Role} 设备未启用：{endpoint.FriendlyName}";
                    steps.Add(message); _logger.Warn("Audio", message);
                    requiredFailure |= mode == AudioStepMode.Required || overallMode == AudioStepMode.Required;
                    if (requiredFailure) break;
                    continue;
                }

                try
                {
                    _faultInjector.ThrowIfRequested(FaultPoint.AudioApply);
                    // Mark before the external call: the provider may have
                    // changed the default and then observed cancellation.
                    if (!changedRoles.Contains(assignment.Role)) changedRoles.Add(assignment.Role);
                    await _provider.SetDefaultAsync(endpoint, assignment.Role, cancellationToken).ConfigureAwait(false);
                    var actual = _provider.GetDefault(assignment.Role);
                    if (actual is null || !actual.DeviceId.Equals(endpoint.DeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{assignment.Role} 默认设备回读不匹配。");
                    steps.Add($"Apply/Verify：{assignment.Role}：{endpoint.FriendlyName}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A native provider can change the default and then throw.
                    // Re-read once: only remove the role when we can prove it
                    // is still the captured endpoint; otherwise retain it for
                    // rollback so partial mutations are not lost.
                    try
                    {
                        var beforeEndpoint = before.TryGetValue(assignment.Role, out var capturedEndpoint) ? capturedEndpoint : null;
                        var currentEndpoint = _provider.GetDefault(assignment.Role);
                        if (beforeEndpoint is not null && currentEndpoint is not null
                            && currentEndpoint.DeviceId.Equals(beforeEndpoint.DeviceId, StringComparison.OrdinalIgnoreCase))
                            changedRoles.Remove(assignment.Role);
                    }
                    catch { /* keep the role marked when the state cannot be read */ }
                    var message = $"{assignment.Role} 设置失败：{ex.Message}";
                    steps.Add(message); _logger.Warn("Audio", message);
                    requiredFailure |= mode == AudioStepMode.Required || overallMode == AudioStepMode.Required;
                    if (requiredFailure) break;
                }
            }

            if (requiredFailure || steps.Any(x => x.Contains("失败", StringComparison.Ordinal)))
            {
                var rollback = await RestoreAsync(before, changedRoles, CancellationToken.None).ConfigureAwait(false);
                steps.Add($"Rollback/VerifyRollback：{rollback}");
                return new()
                {
                    Success = !requiredFailure,
                    RequiredFailure = requiredFailure,
                    Message = requiredFailure ? "必需音频设备未能应用，已恢复原默认设备。" : "可选音频设备部分失败，已恢复已修改的设备。",
                    RollbackAttempted = true,
                    RollbackSucceeded = rollback,
                    Steps = steps
                };
            }

            steps.Add("Commit：音频配置已提交");
            _logger.Info("Audio", $"音频配置已应用：{profile.Name}");
            return new() { Success = true, Message = "音频配置已应用并验证。", Steps = steps };
        }
        catch (OperationCanceledException)
        {
            // Cancellation must never reuse the cancelled token for rollback.
            // Read the snapshot again only if it was captured successfully.
            var rollback = false;
            if (captured) try { rollback = await RestoreAsync(before, changedRoles, CancellationToken.None).ConfigureAwait(false); } catch { }
            steps.Add($"Cancelled：Rollback/VerifyRollback={rollback}");
            _logger.Warn("Audio", "音频事务已取消并执行有界回滚。");
            return new() { Success = false, RequiredFailure = true, Cancelled = true, Message = "音频事务已取消。", RollbackAttempted = changedRoles.Count > 0, RollbackSucceeded = rollback, Steps = steps };
        }
        catch (Exception ex)
        {
            _logger.Error("Audio", ex.ToString());
            return new() { Success = false, RequiredFailure = true, Message = ex.Message, RollbackAttempted = changedRoles.Count > 0, RollbackSucceeded = false, Steps = steps };
        }
    }

    private async Task<bool> RestoreAsync(IReadOnlyDictionary<AudioEndpointRole, AudioEndpointReference?> before, IReadOnlyList<AudioEndpointRole> changedRoles, CancellationToken cancellationToken)
    {
        var ok = true;
        foreach (var role in changedRoles.Reverse())
        {
            var endpoint = before.TryGetValue(role, out var value) ? value : null;
            if (endpoint is null) continue;
            try
            {
                await _provider.SetDefaultAsync(endpoint, role, cancellationToken).ConfigureAwait(false);
                var actual = _provider.GetDefault(role);
                if (actual is null || !actual.DeviceId.Equals(endpoint.DeviceId, StringComparison.OrdinalIgnoreCase)) ok = false;
            }
            catch (Exception ex) { ok = false; _logger.Error("Audio", $"恢复 {role} 失败：{ex.Message}"); }
        }
        return ok;
    }
}

public sealed class UnavailableAudioEndpointProvider : IAudioEndpointProvider
{
    public event EventHandler? DevicesChanged;
    public event EventHandler? DefaultsChanged;
    public IReadOnlyList<AudioEndpointReference> Enumerate() => Array.Empty<AudioEndpointReference>();
    public AudioEndpointReference? GetDefault(AudioEndpointRole role) => null;
    public Task SetDefaultAsync(AudioEndpointReference endpoint, AudioEndpointRole role, CancellationToken cancellationToken)
        => Task.FromException(new PlatformNotSupportedException("Windows Core Audio 不可用。"));
    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseDefaultsChanged() => DefaultsChanged?.Invoke(this, EventArgs.Empty);
}
