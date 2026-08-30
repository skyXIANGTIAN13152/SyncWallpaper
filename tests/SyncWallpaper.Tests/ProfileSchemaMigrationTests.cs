using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class ProfileSchemaMigrationTests
{
    [TestMethod]
    public void DuplicateRoleIds_AreRepairedWithoutChangingLogicalRoles()
    {
        var duplicate = "same-role-id";
        var profile = new WallpaperProfile
        {
            Name = "Two landscape monitors",
            Roles = new()
            {
                new() { RoleId = duplicate, Role = "Landscape" },
                new() { RoleId = duplicate, Role = "Landscape" }
            }
        };

        var migrated = ProfileSchemaMigrator.Migrate(new ProfilesDocument { Profiles = new() { profile } });

        Assert.AreEqual("Landscape", migrated.Profiles[0].Roles[0].Role);
        Assert.AreEqual("Landscape", migrated.Profiles[0].Roles[1].Role);
        Assert.AreNotEqual(migrated.Profiles[0].Roles[0].RoleId, migrated.Profiles[0].Roles[1].RoleId);
    }

    [TestMethod]
    public void VersionOneProfileMigratesWithoutDroppingRoleOrWallpaperAsset()
    {
        var fingerprint = new MonitorIdentity { MonitorDevicePath = "PATH-A", ManufacturerName = "AOC", ProductCodeId = "B426", EdidSerialNumber = "SER-A" };
        var profile = new WallpaperProfile
        {
            SchemaVersion = 1,
            Name = "Legacy configuration",
            AllowCompatibleMatch = false,
            Roles = new List<MonitorRoleBinding>
            {
                new() { SchemaVersion = 1, Role = "Landscape", WallpaperAssetId = "asset-1", Fingerprint = fingerprint }
            }
        };
        var document = ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 1, Profiles = new() { profile } });

        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, profile.SchemaVersion);
        Assert.AreEqual(1, profile.ExpectedMonitorCount);
        Assert.AreEqual("asset-1", profile.Roles[0].WallpaperAssetId);
        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, profile.Roles[0].SchemaVersion);
        Assert.IsFalse(string.IsNullOrWhiteSpace(profile.Roles[0].RoleId));
        Assert.AreEqual("PATH-A", profile.Roles[0].LastKnownMonitorDevicePath);
        Assert.AreEqual(MonitorIdentitySource.EdidSerial, profile.Roles[0].Fingerprint.StableIdSource);
    }

    [TestMethod]
    public void MigratingTwiceIsIdempotent()
    {
        var document = new ProfilesDocument { SchemaVersion = 1, Profiles = new() { new WallpaperProfile { SchemaVersion = 1, Roles = new() { new MonitorRoleBinding() } } } };
        var first = ProfileSchemaMigrator.Migrate(document);
        var roleId = first.Profiles[0].Roles[0].RoleId;
        var second = ProfileSchemaMigrator.Migrate(first);

        Assert.AreSame(first, second);
        Assert.AreEqual(roleId, second.Profiles[0].Roles[0].RoleId);
        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, second.SchemaVersion);
    }

    [TestMethod]
    public void VersionTwoSentinelContainerIdentityMigratesToPermanentPath()
    {
        var fingerprint = new MonitorIdentity
        {
            SchemaVersion = 2,
            MonitorDevicePath = "PATH-LAPTOP",
            ContainerId = "{00000000-0000-0000-ffff-ffffffffffff}",
            StableId = "container:{00000000-0000-0000-FFFF-FFFFFFFFFFFF}",
            StableIdSource = MonitorIdentitySource.ContainerId
        };
        var profile = new WallpaperProfile
        {
            SchemaVersion = 2,
            Roles = new() { new MonitorRoleBinding { SchemaVersion = 2, Role = "Laptop", Fingerprint = fingerprint } }
        };

        var document = ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 2, Profiles = new() { profile } });

        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(MonitorIdentitySource.MonitorDevicePath, fingerprint.StableIdSource);
        StringAssert.StartsWith(fingerprint.StableId, "path:");
    }

    [TestMethod]
    public void VersionThreeCompleteProfileRepairsStaleAutoApplyFlag()
    {
        var profile = new WallpaperProfile
        {
            SchemaVersion = 3,
            AutoApply = false,
            ExpectedMonitorCount = 1,
            Roles = new()
            {
                new MonitorRoleBinding
                {
                    Role = "Laptop",
                    WallpaperPath = @"D:\Wallpapers\laptop.jpg",
                    Fingerprint = new MonitorIdentity
                    {
                        MonitorDevicePath = "PATH-LAPTOP",
                        StableId = "path:PATH-LAPTOP",
                        StableIdSource = MonitorIdentitySource.MonitorDevicePath
                    }
                }
            }
        };

        ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 3, Profiles = new() { profile } });

        Assert.IsTrue(profile.AutoApply);
        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, profile.SchemaVersion);
    }

    [TestMethod]
    public void VersionThreeIncompleteProfileRemainsNonAutomatic()
    {
        var profile = new WallpaperProfile
        {
            SchemaVersion = 3,
            AutoApply = false,
            ExpectedMonitorCount = 1,
            Roles = new()
            {
                new MonitorRoleBinding
                {
                    Role = "Laptop",
                    Fingerprint = new MonitorIdentity { MonitorDevicePath = "PATH-LAPTOP" }
                }
            }
        };

        ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 3, Profiles = new() { profile } });

        Assert.IsFalse(profile.AutoApply);
    }
}
