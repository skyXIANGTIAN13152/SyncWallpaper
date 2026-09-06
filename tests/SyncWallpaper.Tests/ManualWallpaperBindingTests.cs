using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class ManualWallpaperBindingTests
{
    [TestMethod]
    public void FourScreensWithTwoPortraits_KeepDistinctImagesAcrossAllEnumerationOrders()
    {
        var monitors = Enumerable.Range(0, 4).Select(i => TestData.Monitor("PATH-" + i, "SERIAL-" + i, i * 1920)).ToArray();
        var labels = new[] { "Laptop", "Portrait", "Landscape", "Portrait" };
        var profile = ProfileTemplates.Custom("four", labels);
        // Reproduce the first identification save, including duplicate template labels.
        ManualWallpaperBindingService.Apply(profile, monitors.Select((m, i) => new ManualWallpaperAssignment(m, labels[i], $"image-{i}.png")).ToArray());
        Assert.AreEqual(4, profile.Roles.Select(x => x.RoleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var ids = profile.Roles.ToDictionary(x => x.Fingerprint.MonitorDevicePath, x => x.RoleId);

        foreach (var order in Permutations(new[] { 0, 1, 2, 3 }))
        {
            // Reopening identification and leaving fields blank preserves each
            // physical display's saved file, regardless of row order or label.
            ManualWallpaperBindingService.Apply(profile, order.Select(i => new ManualWallpaperAssignment(monitors[i], labels[i], "")).ToArray());
            var match = new ProfileMatcher().Match(order.Select(i => monitors[i].Clone()).ToArray(), new[] { profile });
            Assert.IsTrue(match.CanAutoApply);
            Assert.AreEqual(4, match.RoleMatches.Count);
            foreach (var role in profile.Roles)
            {
                Assert.IsTrue(match.TryGetMonitor(role, out var mapped));
                var index = Array.FindIndex(monitors, m => m.MonitorDevicePath == mapped.MonitorDevicePath);
                Assert.AreEqual($"image-{index}.png", role.WallpaperPath);
                Assert.AreEqual(ids[mapped.MonitorDevicePath], role.RoleId);
            }
        }
    }

    [TestMethod]
    public void RebindingAnAlreadyCorruptProfile_PreservesBothFilesAndRepairsIds()
    {
        var first = TestData.Monitor("PATH-A", "SERIAL-A");
        var second = TestData.Monitor("PATH-B", "SERIAL-B");
        var profile = new WallpaperProfile { Roles = new()
        {
            new() { RoleId = "shared", Role = "Portrait", Fingerprint = first, WallpaperPath = "first.png" },
            new() { RoleId = "shared", Role = "Portrait", Fingerprint = second, WallpaperAssetId = "second-asset", WallpaperPath = "second.png" }
        }};
        ManualWallpaperBindingService.Apply(profile, new[]
        {
            new ManualWallpaperAssignment(second, "Landscape", ""),
            new ManualWallpaperAssignment(first, "Portrait", "")
        });
        Assert.AreEqual("second.png", profile.Roles[0].WallpaperPath);
        Assert.AreEqual("second-asset", profile.Roles[0].WallpaperAssetId);
        Assert.AreEqual("first.png", profile.Roles[1].WallpaperPath);
        Assert.AreNotEqual(profile.Roles[0].RoleId, profile.Roles[1].RoleId);
    }

    [TestMethod]
    public void ANewDisplayDoesNotInheritAnotherDisplaysImageFromItsRoleLabel()
    {
        var first = TestData.Monitor("PATH-A", "SERIAL-A");
        var second = TestData.Monitor("PATH-B", "SERIAL-B");
        var profile = new WallpaperProfile { Roles = new() { new() { Role = "Portrait", Fingerprint = first, WallpaperPath = "first.png" } } };
        ManualWallpaperBindingService.Apply(profile, new[] { new ManualWallpaperAssignment(second, "Portrait", "") });
        Assert.AreEqual(string.Empty, profile.Roles.Single().WallpaperPath);
        Assert.IsFalse(profile.AutoApply);
    }

    [TestMethod]
    public void DuplicateTargetValidationDoesNotPartiallyOverwriteProfile()
    {
        var monitor = TestData.Monitor();
        var profile = new WallpaperProfile { Roles = new() { new() { Fingerprint = monitor, WallpaperPath = "keep.png" } } };
        var original = profile.Roles;
        Assert.ThrowsException<InvalidOperationException>(() => ManualWallpaperBindingService.Apply(profile, new[]
        {
            new ManualWallpaperAssignment(monitor, "Portrait", "new.png"),
            new ManualWallpaperAssignment(monitor, "Landscape", "other.png")
        }));
        Assert.AreSame(original, profile.Roles);
        Assert.AreEqual("keep.png", profile.Roles.Single().WallpaperPath);
    }

    private static IEnumerable<int[]> Permutations(int[] values)
        => values.Length == 0 ? new[] { Array.Empty<int>() }
            : values.SelectMany((value, index) => Permutations(values.Where((_, i) => i != index).ToArray())
                .Select(tail => new[] { value }.Concat(tail).ToArray()));
}
