using System.Text.Json;
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
        injector.ThrowIfRequested(FaultPoint.ProcessStart);
        Assert.IsFalse(injector.Enabled);
        Assert.IsFalse(injector.IsRequested(FaultPoint.ProcessStart));
    }

    [TestMethod]
    public void FaultInjectionIsRepeatableAndCounted()
    {
        var injector = new ConfigurableFaultInjector(new[] { FaultPoint.IpcTimeout }, occurrences: 2);
        Assert.IsTrue(injector.IsRequested(FaultPoint.IpcTimeout));
        Assert.ThrowsException<InjectedFaultException>(() => injector.ThrowIfRequested(FaultPoint.IpcTimeout));
        Assert.IsTrue(injector.IsRequested(FaultPoint.IpcTimeout));
        Assert.ThrowsException<InjectedFaultException>(() => injector.ThrowIfRequested(FaultPoint.IpcTimeout));
        Assert.IsFalse(injector.IsRequested(FaultPoint.IpcTimeout));
    }

    [TestMethod]
    public void IpcMessagesRoundTripWithVersionRequestAndInstance()
    {
        using var document = JsonDocument.Parse("{\"action\":\"ping\"}");
        var message = new ModuleIpcMessage(ModuleIpcProtocol.Version, "request-1", "instance-1", "ping", document.RootElement.Clone());
        var json = ModuleIpcJson.Serialize(message);
        Assert.IsTrue(ModuleIpcJson.TryDeserialize(json, out var parsed));
        Assert.AreEqual("request-1", parsed!.RequestId);
        Assert.AreEqual("instance-1", parsed.ModuleInstanceId);
        Assert.AreEqual("1", parsed.ProtocolVersion);
    }

    [TestMethod]
    public void CorruptIpcMessagesAreRejectedWithoutThrowing()
    {
        Assert.IsFalse(ModuleIpcJson.TryDeserialize("not-json", out _));
        Assert.IsFalse(ModuleIpcJson.TryDeserializeResponse("{\"protocolVersion\":", out _));
    }

    [TestMethod]
    public void RuntimeDocumentDoesNotContainProcessId()
    {
        var state = new ModuleRuntimeState { Enabled = true, LastRecoveryPoint = "running", CrashCount = 1 };
        var json = JsonSerializer.Serialize(state);
        Assert.IsFalse(json.Contains("processId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("pid", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ConfigurationCorruptionFallsBackAndWriteFaultIsBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperReliability", Guid.NewGuid().ToString("N"));
        try
        {
            var corrupt = new ConfigurableFaultInjector(new[] { FaultPoint.ConfigurationCorrupt });
            var store = new ConfigurationStore(new DataPaths(root), corrupt);
            var fallback = new ModuleRuntimeDocument();
            Assert.AreSame(fallback, store.Load("runtime.json", fallback));
            var writeFault = new ConfigurableFaultInjector(new[] { FaultPoint.ConfigurationWrite });
            var writeStore = new ConfigurationStore(new DataPaths(Path.Combine(root, "write")), writeFault);
            Assert.ThrowsException<InjectedFaultException>(() => writeStore.Save("runtime.json", new ModuleRuntimeDocument()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void UnwritableLogDoesNotEscapeToCore()
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
