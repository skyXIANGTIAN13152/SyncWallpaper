using SyncWallpaper.Core;
using SyncWallpaper.DisplayEngine;

namespace SyncWallpaper.Tests;

[TestClass]
public class DisplayEngineStage1Tests
{
    [TestMethod]
    public void ValidatorAcceptsNormalProfile() { var adapter = new FakeDisplayAdapter(); var result = new DisplayConfigurationValidator(adapter).Validate(adapter.Current, adapter.Capture()); Assert.IsTrue(result.IsValid); }

    [TestMethod]
    public void InvalidResolutionIsRejected()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current); target.Displays[0].Width = 0;
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid); StringAssert.Contains(result.Errors[0], "分辨率");
    }

    [TestMethod]
    public void UnsupportedRefreshRateIsRejected()
    {
        var adapter = new FakeDisplayAdapter();
        adapter.Modes["PATH-A"] = new[] { new DisplayModeInfo(1920, 1080, 60, 1, 1) };
        var target = TestData.Clone(adapter.Current); target.Displays[0].RefreshRateNumerator = 165;
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid); StringAssert.Contains(string.Join("|", result.Errors), "不支持");
    }

    [TestMethod]
    public void MissingMonitorIsRejected()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current);
        target.Displays[0].MonitorFingerprint.MonitorDevicePath = "MISSING";
        target.Displays[0].MonitorFingerprint.EdidSerialNumber = "MISSING-SERIAL";
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void DuplicateMonitorPathsAreRejected()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current);
        target.Displays.Add(TestData.Clone(target).Displays[0]);
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void MultiplePrimaryDisplaysAreRejected()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current);
        target.Displays.Add(new DisplayConfigurationEntry { MonitorFingerprint = TestData.Monitor("PATH-B", "SERIAL-B", 1920), AdapterLuid = "GPU", SourceId = 1, TargetId = 2, Width = 1920, Height = 1080, IsPrimary = true });
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void AllDisabledDisplaysAreRejected()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current); target.Displays[0].Enabled = false;
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void DpiAndHdrProduceWarnings()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current);
        target.Displays[0].DpiScale = 1.25; target.Displays[0].HdrEnabled = true;
        var result = new DisplayConfigurationValidator(adapter).Validate(target, adapter.Capture());
        Assert.IsTrue(result.IsValid); Assert.AreEqual(2, result.Warnings.Count);
    }

    [TestMethod]
    public void DifferencesContainResolutionAndPosition()
    {
        var adapter = new FakeDisplayAdapter(); var target = TestData.Clone(adapter.Current);
        target.Displays[0].Width = 1280; target.Displays[0].DesktopX = 100;
        var differences = DisplayConfigurationValidator.BuildDifferences(target, adapter.Current);
        Assert.IsTrue(differences.Any(x => x.Subject.Contains("分辨率")));
        Assert.IsTrue(differences.Any(x => x.Subject.Contains("桌面位置")));
    }

    [TestMethod]
    public async Task ApplyPlanRunsPrecheckApplyStabilizeAndVerify()
    {
        var adapter = new FakeDisplayAdapter(); var stabilizer = new FakeStabilizer(); var confirmation = new FakeConfirmation();
        var service = Create(adapter, stabilizer, confirmation);
        var result = await service.ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = true });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.Applied, result.Status);
        Assert.AreEqual(2, adapter.ApplyCalls); Assert.AreEqual(1, adapter.TopologyCalls); Assert.AreEqual(1, adapter.FinalCalls);
        Assert.AreEqual(2, stabilizer.Calls); Assert.AreEqual(1, confirmation.Calls);
    }

    [TestMethod]
    public async Task VerificationFailureTriggersRollback()
    {
        var adapter = new FakeDisplayAdapter { VerifyMatchesTarget = false }; var service = Create(adapter, new FakeStabilizer(), new FakeConfirmation());
        var result = await service.ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RolledBack, result.Status);
        Assert.IsTrue(result.RollbackSucceeded); Assert.AreEqual(1, adapter.RollbackCalls);
    }

    [TestMethod]
    public async Task RollbackFailureIsReported()
    {
        var adapter = new FakeDisplayAdapter { VerifyMatchesTarget = false, FailRollback = true }; var service = Create(adapter, new FakeStabilizer(), null);
        var result = await service.ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RollbackFailed, result.Status);
        Assert.IsFalse(result.RollbackSucceeded);
    }

    [TestMethod]
    public async Task ConfirmationTimeoutRollsBack()
    {
        var adapter = new FakeDisplayAdapter(); var confirmation = new FakeConfirmation { Result = false };
        var result = await Create(adapter, new FakeStabilizer(), confirmation).ApplyAsync(TestData.Clone(adapter.Current));
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RolledBack, result.Status);
        Assert.IsTrue(result.RollbackAttempted);
    }

    [TestMethod]
    public async Task ValidationOnlyDoesNotApply()
    {
        var adapter = new FakeDisplayAdapter(); var result = await Create(adapter, new FakeStabilizer(), null).ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { ValidationOnly = true, RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.Planned, result.Status); Assert.AreEqual(0, adapter.ApplyCalls);
    }

    [TestMethod]
    public async Task CancellationStopsBeforeApply()
    {
        var adapter = new FakeDisplayAdapter(); using var source = new CancellationTokenSource(); source.Cancel();
        var result = await Create(adapter, new FakeStabilizer(), null).ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false }, source.Token);
        Assert.AreEqual(DisplayConfigurationTransactionStatus.Cancelled, result.Status); Assert.AreEqual(0, adapter.ApplyCalls);
    }

    [TestMethod]
    public async Task CancellationAfterApplyTriggersRollback()
    {
        var adapter = new FakeDisplayAdapter();
        var result = await Create(adapter, new FakeStabilizer { ThrowOnCall = 1 }, null)
            .ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RolledBack, result.Status);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsTrue(result.RollbackSucceeded);
    }

    [TestMethod]
    public void RepositorySavesCopiesAndDeletes()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperStage1", Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new DisplayProfileRepository(new ConfigurationStore(new DataPaths(root)));
            var profile = TestData.DisplayProfile("one"); repo.Save(profile);
            var copy = repo.Copy(profile.ProfileId, "two");
            Assert.AreEqual(2, repo.List().Count); Assert.IsTrue(repo.Delete(profile.ProfileId)); Assert.AreEqual(1, repo.List().Count);
            Assert.AreEqual("two", copy.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ApplyFailureIsReportedWithoutInfiniteRetry()
    {
        var adapter = new FakeDisplayAdapter { FailApply = true }; var result = await Create(adapter, new FakeStabilizer(), null).ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RolledBack, result.Status); Assert.AreEqual(1, adapter.ApplyCalls); Assert.AreEqual(1, adapter.RollbackCalls);
    }

    [TestMethod]
    public async Task InjectedDisplayApplyFailureIsBoundedAndRollsBack()
    {
        var adapter = new FakeDisplayAdapter();
        var injector = new ConfigurableFaultInjector(new[] { FaultPoint.DisplayApply });
        var result = await Create(adapter, new FakeStabilizer(), null, injector).ApplyAsync(TestData.Clone(adapter.Current), new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.RolledBack, result.Status);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.AreEqual(0, adapter.ApplyCalls);
    }

    private static DisplayConfigurationTransactionService Create(FakeDisplayAdapter adapter, FakeStabilizer stabilizer, IDisplayConfirmationService? confirmation, IFaultInjector? injector = null)
        => new(adapter, new DisplayConfigurationValidator(adapter), adapter, adapter, adapter, stabilizer, new TestLogger(), confirmation, injector);
}
