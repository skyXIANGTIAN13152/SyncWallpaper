namespace SyncWallpaper.Core;

public enum ConfigurationTransactionStage
{
    Prepare,
    CaptureCurrentState,
    ValidateTarget,
    Apply,
    Verify,
    Commit,
    Rollback,
    VerifyRollback,
    RecordResult
}

public sealed record ConfigurationTransactionOutcome(
    bool Success,
    bool RolledBack,
    bool RollbackVerified,
    bool Cancelled,
    ConfigurationTransactionStage LastStage,
    IReadOnlyList<string> Steps,
    string Message,
    Exception? Error = null);

/// <summary>
/// Shared bounded transaction skeleton for display, audio, window and desktop
/// adapters. Adapters supply the read/validate/apply/verify/rollback operations;
/// this class owns ordering, cancellation, rollback verification and result
/// recording so a new module cannot accidentally add an unbounded retry loop.
/// </summary>
public sealed class ConfigurationTransactionPipeline<TTarget, TSnapshot>
{
    public async Task<ConfigurationTransactionOutcome> ExecuteAsync(
        TTarget target,
        Func<TTarget, CancellationToken, Task>? prepare,
        Func<CancellationToken, Task<TSnapshot>> capture,
        Func<TTarget, TSnapshot, CancellationToken, Task<bool>> validate,
        Func<TTarget, CancellationToken, Task> apply,
        Func<TTarget, CancellationToken, Task<bool>> verify,
        Func<TSnapshot, CancellationToken, Task> rollback,
        Func<TSnapshot, CancellationToken, Task<bool>> verifyRollback,
        Action<ConfigurationTransactionOutcome>? record = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<string>();
        var stage = ConfigurationTransactionStage.Prepare;
        TSnapshot? snapshot = default;
        var hasSnapshot = false;
        var applied = false;
        try
        {
            if (prepare is not null) await prepare(target, cancellationToken).ConfigureAwait(false);
            steps.Add(nameof(ConfigurationTransactionStage.Prepare));
            stage = ConfigurationTransactionStage.CaptureCurrentState;
            snapshot = await capture(cancellationToken).ConfigureAwait(false); hasSnapshot = true;
            steps.Add(nameof(ConfigurationTransactionStage.CaptureCurrentState));
            stage = ConfigurationTransactionStage.ValidateTarget;
            if (!await validate(target, snapshot, cancellationToken).ConfigureAwait(false))
                return Finish(new(false, false, false, false, stage, steps, "目标验证失败。"));
            steps.Add(nameof(ConfigurationTransactionStage.ValidateTarget));
            stage = ConfigurationTransactionStage.Apply;
            cancellationToken.ThrowIfCancellationRequested();
            // Mark before the external operation: a native call can partially
            // mutate state and then throw/observe cancellation.
            applied = true;
            await apply(target, cancellationToken).ConfigureAwait(false);
            steps.Add(nameof(ConfigurationTransactionStage.Apply));
            stage = ConfigurationTransactionStage.Verify;
            if (!await verify(target, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("应用后验证失败。");
            steps.Add(nameof(ConfigurationTransactionStage.Verify));
            stage = ConfigurationTransactionStage.Commit;
            steps.Add(nameof(ConfigurationTransactionStage.Commit));
            return Finish(new(true, false, false, false, stage, steps, "事务已提交。"));
        }
        catch (OperationCanceledException ex)
        {
            var rollbackResult = await TryRollbackAsync(snapshot, hasSnapshot, applied, rollback, verifyRollback, steps).ConfigureAwait(false);
            return Finish(new(false, rollbackResult.Attempted, rollbackResult.Verified, true, ConfigurationTransactionStage.RecordResult, steps, "事务已取消并完成有界回滚。", ex));
        }
        catch (Exception ex)
        {
            var rollbackResult = await TryRollbackAsync(snapshot, hasSnapshot, applied, rollback, verifyRollback, steps).ConfigureAwait(false);
            return Finish(new(false, rollbackResult.Attempted, rollbackResult.Verified, false, ConfigurationTransactionStage.RecordResult, steps, ex.Message, ex));
        }

        ConfigurationTransactionOutcome Finish(ConfigurationTransactionOutcome result)
        {
            try { record?.Invoke(result); } catch { /* diagnostics recording must not reopen a committed transaction */ }
            return result;
        }
    }

    private static async Task<(bool Attempted, bool Verified)> TryRollbackAsync(
        TSnapshot? snapshot, bool hasSnapshot, bool applied,
        Func<TSnapshot, CancellationToken, Task> rollback,
        Func<TSnapshot, CancellationToken, Task<bool>> verifyRollback,
        List<string> steps)
    {
        if (!hasSnapshot || !applied || snapshot is null) return (false, false);
        try
        {
            steps.Add(nameof(ConfigurationTransactionStage.Rollback));
            await rollback(snapshot, CancellationToken.None).ConfigureAwait(false);
            steps.Add(nameof(ConfigurationTransactionStage.VerifyRollback));
            var verified = await verifyRollback(snapshot, CancellationToken.None).ConfigureAwait(false);
            return (true, verified);
        }
        catch (Exception ex)
        {
            steps.Add($"回滚失败：{ex.Message}");
            return (true, false);
        }
    }
}
