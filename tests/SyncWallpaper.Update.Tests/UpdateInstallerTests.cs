using System.Text.Json;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Update.Tests;

[TestClass]
public sealed class UpdateInstallerTests
{
    [TestMethod]
    public async Task SuccessfulInstallReplacesProgramsPreservesUserDataAndDesktopIcon()
    {
        using var fixture = new Fixture();
        var protectedBefore = fixture.ProtectedSnapshot();
        var launcher = new FakeLauncher();
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan, launcher);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("new-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("new-diagnostics", File.ReadAllText(fixture.Installed("Diagnostics", "diagnostic.txt")));
        Assert.AreEqual("dedicated-icon", File.ReadAllText(fixture.Installed("App", "SyncWallpaper-Mystery.ico")));
        CollectionAssert.AreEquivalent(protectedBefore, fixture.ProtectedSnapshot());
        Assert.AreEqual(fixture.Plan.DataRoot, launcher.DataRoots.Single());
        Assert.AreEqual(0, Directory.GetDirectories(fixture.Plan.InstallRoot, ".syncwallpaper-update-*").Length);
    }

    [TestMethod]
    public async Task MissingOptionalDiagnosticsKeepsInstalledDiagnostics()
    {
        using var fixture = new Fixture();
        Directory.Delete(Path.Combine(fixture.Plan.PackageDirectory, "Diagnostics"), true);
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan, new FakeLauncher());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("old-diagnostics", File.ReadAllText(fixture.Installed("Diagnostics", "diagnostic.txt")));
    }

    [TestMethod]
    public async Task FailureBetweenOldAppMoveAndNewAppMoveRestoresOriginal()
    {
        using var fixture = new Fixture();
        var before = fixture.ProtectedSnapshot();
        var launcher = new FakeLauncher();
        var installer = fixture.Installer((source, destination) =>
        {
            if (source.EndsWith(Path.Combine("candidate", "App"))) throw new IOException("simulated locked install destination");
        });
        var result = await installer.ExecuteAsync(fixture.Plan, launcher);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("old-diagnostics", File.ReadAllText(fixture.Installed("Diagnostics", "diagnostic.txt")));
        CollectionAssert.AreEquivalent(before, fixture.ProtectedSnapshot());
        Assert.IsNull(launcher.ReadyPaths.Single());
    }

    [TestMethod]
    public async Task DiagnosticsSwapFailureRestoresAlreadyReplacedApp()
    {
        using var fixture = new Fixture();
        var result = await fixture.Installer((source, _) =>
        {
            if (source.EndsWith(Path.Combine("candidate", "Diagnostics"))) throw new IOException("locked Diagnostics");
        }).ExecuteAsync(fixture.Plan, new FakeLauncher());
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("old-diagnostics", File.ReadAllText(fixture.Installed("Diagnostics", "diagnostic.txt")));
        Assert.AreEqual("old manifest", File.ReadAllText(fixture.Installed("package-manifest.json")));
    }

    [TestMethod]
    public async Task StartupFailureStopsOnlyNewProcessThenRestoresAndRelaunchesOld()
    {
        using var fixture = new Fixture();
        var protectedBefore = fixture.ProtectedSnapshot();
        var launcher = new FakeLauncher { StartupReady = false };
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan, launcher);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual(1, launcher.StopCalls);
        Assert.AreEqual(2, launcher.ReadyPaths.Count);
        Assert.IsNotNull(launcher.ReadyPaths[0]);
        Assert.IsNull(launcher.ReadyPaths[1]);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("old manifest", File.ReadAllText(fixture.Installed("package-manifest.json")));
        CollectionAssert.AreEquivalent(protectedBefore, fixture.ProtectedSnapshot());
    }

    [TestMethod]
    public async Task LaunchFailureRestoresOldApplicationAndOriginalManifest()
    {
        using var fixture = new Fixture();
        var launcher = new FakeLauncher { FailFirstLaunch = true };
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan, launcher);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("old manifest", File.ReadAllText(fixture.Installed("package-manifest.json")));
        Assert.AreEqual(2, launcher.ReadyPaths.Count);
    }

    [TestMethod]
    public async Task UnstoppableNewProcessKeepsOldBackupAndDoesNotOverwriteRunningApp()
    {
        using var fixture = new Fixture();
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan,
            new FakeLauncher { StartupReady = false, CanStop = false });
        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.RolledBack);
        Assert.IsNotNull(result.RecoveryDirectory);
        Assert.AreEqual("old-app", File.ReadAllText(Path.Combine(result.RecoveryDirectory, "backup", "App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("new-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
    }

    [TestMethod]
    public async Task FailedRollbackKeepsBackupAndReportsRecoveryDirectory()
    {
        using var fixture = new Fixture();
        var result = await fixture.Installer((source, _) =>
        {
            if (source.EndsWith(Path.Combine("backup", "App"))) throw new IOException("recovery path locked");
        }).ExecuteAsync(fixture.Plan, new FakeLauncher { StartupReady = false });
        Assert.IsFalse(result.RolledBack);
        Assert.IsNotNull(result.RecoveryDirectory);
        Assert.AreEqual("old-app", File.ReadAllText(Path.Combine(result.RecoveryDirectory, "backup", "App", "SyncWallpaper.App.exe")));
    }

    [TestMethod]
    public async Task ActualLockedProgramCancelsReplacementWithoutLosingOriginal()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing behavior test.");
        using var fixture = new Fixture();
        using var locked = new FileStream(fixture.Installed("App", "SyncWallpaper.App.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var before = fixture.ProtectedSnapshot();
        var result = await fixture.Installer().ExecuteAsync(fixture.Plan, new FakeLauncher());
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        CollectionAssert.AreEquivalent(before, fixture.ProtectedSnapshot());
    }

    [TestMethod]
    public void ProductVersionMismatchFailsBeforeAnyReplacement()
    {
        using var fixture = new Fixture();
        var installer = new UpdateInstaller(new UpdateInstallerOptions { ReadProductVersion = _ => "9.9.9" });
        Assert.ThrowsException<InvalidDataException>(() => installer.Validate(fixture.Plan));
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
    }

    [TestMethod]
    public void UserDataInsideProgramDirectoryRefusesAutomaticUpdate()
    {
        using var fixture = new Fixture();
        fixture.Plan.DataRoot = fixture.Installed("App", "portable-data");
        Assert.ThrowsException<InvalidDataException>(() => fixture.Installer().Validate(fixture.Plan));
    }

    [TestMethod]
    public void ExistingPrivateFolderUnderAppIsNeverDeleted()
    {
        using var fixture = new Fixture();
        var privateFile = fixture.Installed("App", "Config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(privateFile)!);
        File.WriteAllText(privateFile, "private data");
        Assert.ThrowsException<InvalidDataException>(() => fixture.Installer().Validate(fixture.Plan));
        Assert.AreEqual("private data", File.ReadAllText(privateFile));
    }

    [TestMethod]
    public void WorkDirectoryInsideApplicationIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Plan.WorkDirectory = fixture.Installed("App", Guid.NewGuid().ToString("N"));
        fixture.Plan.PackageDirectory = Path.Combine(fixture.Plan.WorkDirectory, "package");
        Assert.ThrowsException<InvalidDataException>(() => fixture.Installer().Validate(fixture.Plan));
    }

    [TestMethod]
    public void PackageOutsideOwnedWorkDirectoryIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Plan.PackageDirectory = Path.GetTempPath();
        Assert.ThrowsException<InvalidDataException>(() => fixture.Installer().Validate(fixture.Plan));
    }

    [TestMethod]
    public void RelativeInstallRootIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Plan.InstallRoot = "..";
        Assert.ThrowsException<InvalidDataException>(() => fixture.Installer().Validate(fixture.Plan));
    }

    [TestMethod]
    public async Task InstallationRootBecomingUnwritableReportsUntouchedOriginal()
    {
        using var fixture = new Fixture();
        var installer = new UpdateInstaller(new UpdateInstallerOptions
        {
            ReadProductVersion = _ => fixture.Plan.Version,
            BeforePreparationDirectoryCreate = _ => throw new UnauthorizedAccessException("install root now read-only")
        });
        var error = await Assert.ThrowsExceptionAsync<UpdatePreparationException>(() => installer.ExecuteAsync(fixture.Plan, new FakeLauncher()));
        Assert.IsNull(error.RecoveryDirectory);
        Assert.IsInstanceOfType(error.InnerException, typeof(UnauthorizedAccessException));
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual(0, Directory.GetDirectories(fixture.Plan.InstallRoot, ".syncwallpaper-update-*").Length);
    }

    [TestMethod]
    public async Task DiskFailureAfterCreatingTransactionMarkerCleansPartialPreparation()
    {
        using var fixture = new Fixture();
        var installer = new UpdateInstaller(new UpdateInstallerOptions
        {
            ReadProductVersion = _ => fixture.Plan.Version,
            BeforePreparationDirectoryCreate = path =>
            {
                if (Path.GetFileName(path) == "candidate") throw new IOException("disk full");
            }
        });
        var error = await Assert.ThrowsExceptionAsync<UpdatePreparationException>(() => installer.ExecuteAsync(fixture.Plan, new FakeLauncher()));
        Assert.IsNull(error.RecoveryDirectory);
        Assert.AreEqual("old-app", File.ReadAllText(fixture.Installed("App", "SyncWallpaper.App.exe")));
        Assert.AreEqual(0, Directory.GetDirectories(fixture.Plan.InstallRoot, ".syncwallpaper-update-*").Length);
    }

    private sealed class FakeLauncher : IUpdateApplicationLauncher
    {
        public bool StartupReady { get; init; } = true;
        public bool CanStop { get; init; } = true;
        public bool FailFirstLaunch { get; init; }
        public int StopCalls { get; private set; }
        public List<string?> ReadyPaths { get; } = [];
        public List<string> DataRoots { get; } = [];
        public Task<IUpdateApplicationProcess> LaunchAsync(string executablePath, string dataRoot, string? readyFilePath, CancellationToken cancellationToken)
        {
            Assert.IsTrue(File.Exists(executablePath));
            ReadyPaths.Add(readyFilePath);
            DataRoots.Add(dataRoot);
            if (FailFirstLaunch && ReadyPaths.Count == 1) throw new IOException("launch denied");
            return Task.FromResult<IUpdateApplicationProcess>(new FakeProcess(this));
        }

        private sealed class FakeProcess(FakeLauncher owner) : IUpdateApplicationProcess
        {
            public Task<bool> WaitUntilReadyAsync(string readyFilePath, TimeSpan timeout, CancellationToken cancellationToken)
                => Task.FromResult(owner.StartupReady);
            public Task<bool> StopForRollbackAsync(CancellationToken cancellationToken)
            { owner.StopCalls++; return Task.FromResult(owner.CanStop); }
            public void Dispose() { }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SyncWallpaper-InstallerTests-" + Guid.NewGuid().ToString("N"));
        public UpdateInstallPlan Plan { get; }
        public Fixture()
        {
            var install = Path.Combine(_root, "installed");
            var work = Path.Combine(_root, "work", Guid.NewGuid().ToString("N"));
            Plan = new UpdateInstallPlan
            {
                InstallRoot = install, WorkDirectory = work, PackageDirectory = Path.Combine(work, "package"),
                DataRoot = install, Version = "1.3.0-beta.1", ParentProcessId = 123, ParentStartTimeUtcTicks = 1
            };
            Write(Installed("App", "SyncWallpaper.App.exe"), "old-app");
            Write(Installed("App", "SyncWallpaper-Mystery.ico"), "dedicated-icon");
            Write(Installed("Diagnostics", "diagnostic.txt"), "old-diagnostics");
            Write(Installed("package-manifest.json"), "old manifest");
            foreach (var folder in new[] { "Config", "Wallpapers", "Cache", "Thumbnails", "Logs", "Backups" })
                Write(Installed(folder, "user-file.txt"), folder + " user content");
            Write(Path.Combine(Plan.PackageDirectory, "App", "SyncWallpaper.App.exe"), "new-app");
            Write(Path.Combine(Plan.PackageDirectory, "Diagnostics", "diagnostic.txt"), "new-diagnostics");
            Write(Path.Combine(Plan.PackageDirectory, "package-manifest.json"), JsonSerializer.Serialize(new
            { product = "屏序 SyncWallpaper", version = Plan.Version, rid = "win-x64", selfContained = true, language = "zh-CN" }));
        }
        public string Installed(params string[] path) => Path.Combine([Plan.InstallRoot, .. path]);
        public UpdateInstaller Installer(Action<string, string>? beforeMove = null) => new(new UpdateInstallerOptions
        { ReadProductVersion = _ => Plan.Version, BeforeDirectoryMove = beforeMove });
        public string[] ProtectedSnapshot() => new[] { "Config", "Wallpapers", "Cache", "Thumbnails", "Logs", "Backups" }
            .Select(folder => folder + ":" + File.ReadAllText(Installed(folder, "user-file.txt"))).ToArray();
        private static void Write(string path, string content)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(_root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("SyncWallpaper-InstallerTests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
