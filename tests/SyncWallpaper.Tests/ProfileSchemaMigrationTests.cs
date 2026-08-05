using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class ProfileSchemaMigrationTests
{
    [TestMethod]
    public void VersionOneProfileMigratesWithoutDroppingRoleOrWallpaperAsset()
    {
        var fingerprint = new MonitorIdentity { MonitorDevicePath = "PATH-A", ManufacturerName = "AOC", ProductCodeId = "B426", EdidSerialNumber = "SER-A" };
        var profile = new WallpaperProfile
        {
            SchemaVersion = 1,
            Name = "旧配置",
            AllowCompatibleMatch = false,
            Roles = new List<MonitorRoleBinding>
            {
                new() { SchemaVersion = 1, Role = "Landscape", WallpaperAssetId = "asset-1", Fingerprint = fingerprint }
            }
        };
        var document = ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 1, Profiles = new() { profile } });

        Assert.AreEqual(ProfileSchemaMigrator.CurrentSchemaVersion, document.SchemaVersion);
        Assert.AreEqual(2, profile.SchemaVersion);
        Assert.AreEqual(1, profile.ExpectedMonitorCount);
        Assert.AreEqual("asset-1", profile.Roles[0].WallpaperAssetId);
        Assert.AreEqual(2, profile.Roles[0].SchemaVersion);
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
        Assert.AreEqual(2, second.SchemaVersion);
    }
}
