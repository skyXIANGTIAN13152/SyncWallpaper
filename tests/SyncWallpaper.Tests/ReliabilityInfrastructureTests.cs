using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class ReliabilityInfrastructureTests
{
    [TestMethod]
    public void FaultInjectionIsDisabledByDefault()
    {
        var injector = NoFaultInjector.Instance;
        injector.ThrowIfRequested(FaultPoint.ConfigurationWrite);
        Assert.IsFalse(injector.Enabled);
    }

    [TestMethod]
    public void FaultInjectionIsRepeatableAndCounted()
    {
        var injector = new ConfigurableFaultInjector(new[] { FaultPoint.ConfigurationWrite }, occurrences: 2);
        Assert.ThrowsException<InjectedFaultException>(() => injector.ThrowIfRequested(FaultPoint.ConfigurationWrite));
        Assert.ThrowsException<InjectedFaultException>(() => injector.ThrowIfRequested(FaultPoint.ConfigurationWrite));
        Assert.IsFalse(injector.IsRequested(FaultPoint.ConfigurationWrite));
    }

    [TestMethod]
    public void ConfigurationCorruptionFallsBackAndWriteFaultIsBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperReliability", Guid.NewGuid().ToString("N"));
        try
        {
            var corrupt = new ConfigurableFaultInjector(new[] { FaultPoint.ConfigurationCorrupt });
            var store = new ConfigurationStore(new DataPaths(root), corrupt);
            var fallback = new AppSettings();
            Assert.AreSame(fallback, store.Load("settings.json", fallback));
            var writeStore = new ConfigurationStore(new DataPaths(Path.Combine(root, "write")), new ConfigurableFaultInjector(new[] { FaultPoint.ConfigurationWrite }));
            Assert.ThrowsException<InjectedFaultException>(() => writeStore.Save("settings.json", new AppSettings()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void UnwritableLogDoesNotEscapeToWallpaperCore()
    {
        var root = new DataPaths(Path.Combine(Path.GetTempPath(), "SyncWallpaperReliability", Guid.NewGuid().ToString("N")));
        try
        {
            var log = new LogService(root, new ConfigurableFaultInjector(new[] { FaultPoint.LogUnwritable }));
            log.Info("test", "message");
            Assert.AreEqual(1, log.Recent.Count);
        }
        finally { if (Directory.Exists(root.Root)) Directory.Delete(root.Root, true); }
    }
}
