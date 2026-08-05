using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class ConfigurationTransactionPipelineTests
{
    [TestMethod]
    public async Task PipelineRunsPrepareCaptureValidateApplyVerifyCommit()
    {
        var order = new List<string>();
        var pipeline = new ConfigurationTransactionPipeline<string, int>();
        var result = await pipeline.ExecuteAsync("target",
            (target, _) => { order.Add("Prepare"); return Task.CompletedTask; },
            _ => { order.Add("CaptureCurrentState"); return Task.FromResult(1); },
            (target, state, _) => { order.Add("ValidateTarget"); return Task.FromResult(state == 1); },
            (target, _) => { order.Add("Apply"); return Task.CompletedTask; },
            (target, _) => { order.Add("Verify"); return Task.FromResult(true); },
            (state, _) => { order.Add("Rollback"); return Task.CompletedTask; },
            (state, _) => { order.Add("VerifyRollback"); return Task.FromResult(true); });
        CollectionAssert.AreEqual(new[] { "Prepare", "CaptureCurrentState", "ValidateTarget", "Apply", "Verify" }, order);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(ConfigurationTransactionStage.Commit, result.LastStage);
    }

    [TestMethod]
    public async Task PipelineVerifiesRollbackAfterApplyFailureAndDoesNotRetry()
    {
        var rollbackCalls = 0;
        var pipeline = new ConfigurationTransactionPipeline<string, int>();
        var result = await pipeline.ExecuteAsync("target", null,
            _ => Task.FromResult(1),
            (_, _, _) => Task.FromResult(true),
            (_, _) => throw new InvalidOperationException("apply failed"),
            (_, _) => Task.FromResult(false),
            (_, _) => { rollbackCalls++; return Task.CompletedTask; },
            (_, _) => Task.FromResult(true));
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RolledBack);
        Assert.IsTrue(result.RollbackVerified);
        Assert.AreEqual(1, rollbackCalls);
    }

    [TestMethod]
    public async Task PipelineCancellationUsesNonCancelledRollbackToken()
    {
        var rollbackTokenWasCancelled = true;
        var pipeline = new ConfigurationTransactionPipeline<string, int>();
        using var cts = new CancellationTokenSource();
        var result = await pipeline.ExecuteAsync("target", null,
            _ => Task.FromResult(1),
            (_, _, _) => Task.FromResult(true),
            (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            (_, _) => Task.FromResult(true),
            (_, token) => { rollbackTokenWasCancelled = token.IsCancellationRequested; return Task.CompletedTask; },
            (_, token) => Task.FromResult(!token.IsCancellationRequested), cancellationToken: cts.Token);
        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(rollbackTokenWasCancelled);
    }
}
