using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class DisplayTopologyStabilizerTests
{
    [TestMethod]
    public async Task EmitsOnlyAfterTwoEqualSamplesAndDeduplicates()
    {
        var emitted = new List<string>();
        var calls = 0;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = calls++ < 1 ? "a" : "b" } } },
            (snapshot, _) => { emitted.Add(snapshot.Signature); completed.TrySetResult(true); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1));
        stabilizer.Signal();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, emitted.Count);
        stabilizer.Signal();
        await Task.Delay(80);
        Assert.AreEqual(1, emitted.Count);
    }

    [TestMethod]
    public async Task NewSignalCancelsOlderRun()
    {
        var emitted = 0;
        using var stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = "stable" } } },
            (_, _) => { Interlocked.Increment(ref emitted); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        stabilizer.Signal();
        await Task.Delay(3);
        stabilizer.Signal();
        await Task.Delay(150);
        Assert.AreEqual(1, emitted);
    }

    [TestMethod]
    public async Task TenThousandSignalsRemainBounded()
    {
        var emitted = 0;
        using var stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = new() { new MonitorIdentity { StableId = "bounded" } } },
            (_, _) => { Interlocked.Increment(ref emitted); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1));
        for (var i = 0; i < 10_000; i++) stabilizer.Signal();
        await Task.Delay(120);
        Assert.AreEqual(1, emitted);
    }
}
