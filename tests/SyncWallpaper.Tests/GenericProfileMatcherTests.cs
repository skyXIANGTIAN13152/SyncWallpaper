using System.Text.Json;
using System.Text.Json.Serialization;
using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class GenericProfileMatcherTests
{
    [TestMethod]
    public void CompleteSpecificCombinationAlwaysPrecedesHigherPriorityGeneric()
    {
        var monitor = Screen("known", 0);
        var specific = Specific(monitor, "specific");
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        generic.Priority = int.MaxValue;

        var result = Match(new[] { monitor }, generic, specific);

        Assert.AreSame(specific, result.Profile);
        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.IsFalse(result.IsGeneric);
    }

    [TestMethod]
    public void IncompleteHighPrioritySpecificDoesNotHideCompleteSpecific()
    {
        var monitor = Screen("known", 0);
        var complete = Specific(monitor, "complete");
        var incomplete = Specific(monitor, "");
        incomplete.Priority = 1000;
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1));

        var result = Match(new[] { monitor }, incomplete, generic, complete);

        Assert.AreSame(complete, result.Profile);
        Assert.AreEqual(MatchStatus.Exact, result.Status);
    }

    [TestMethod]
    public void HigherScoringUnsafeSpecificCannotHideSafeCompleteSpecific()
    {
        var monitors = new[] { Screen("A", 0), Screen("B", 1920), Screen("C", 3840) };
        var safe = new WallpaperProfile
        {
            Name = "完整硬件拓扑",
            Roles = monitors.Select(monitor =>
            {
                var fingerprint = monitor.Clone();
                fingerprint.EdidSerialNumber = string.Empty;
                fingerprint.MonitorDevicePath = string.Empty;
                fingerprint.InstanceName = string.Empty;
                fingerprint.StableId = string.Empty;
                fingerprint.StableIdSource = MonitorIdentitySource.Unknown;
                return new MonitorRoleBinding { Fingerprint = fingerprint, WallpaperAssetId = "safe" };
            }).ToList()
        };
        var unsafeProfile = new WallpaperProfile
        {
            Name = "两台强身份加一台冲突",
            Roles = new List<MonitorRoleBinding>
            {
                new() { Fingerprint = monitors[0].Clone(), WallpaperAssetId = "unsafe-a" },
                new() { Fingerprint = monitors[1].Clone(), WallpaperAssetId = "unsafe-b" },
                new() { Fingerprint = Screen("old-C", 3840), WallpaperAssetId = "unsafe-c" }
            }
        };
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1),
            Slot(GenericMonitorOrientation.Landscape, 2), Slot(GenericMonitorOrientation.Landscape, 3));
        Assert.IsTrue(Match(monitors, safe).CanAutoApply);

        var result = Match(monitors, unsafeProfile, safe, generic);

        Assert.AreSame(safe, result.Profile);
        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual(80, result.Confidence);
    }

    [TestMethod]
    public void PreviouslyUnseenDevicesUseGenericWithoutIdentityConfidence()
    {
        var specific = Specific(Screen("old", 0), "specific");
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        var actual = Screen("new", 0);
        actual.ManufacturerName = actual.EdidManufactureId = "NEW";
        actual.ProductCodeId = actual.EdidProductCodeId = "MODEL-NEW";
        actual.AdapterId = "NEW-GPU";

        var result = Match(new[] { actual }, specific, generic);

        Assert.AreEqual(MatchStatus.Generic, result.Status);
        Assert.AreSame(generic, result.Profile);
        Assert.IsTrue(result.IsGeneric);
        Assert.IsTrue(result.CanAutoApply);
        Assert.AreEqual(0, result.Confidence);
        Assert.AreEqual(DisplayIdentityMatchStatus.Unknown, result.IdentityStatus);
        Assert.AreEqual(0, result.UnassignedMonitorCount);
        Assert.AreSame(actual, result.RoleMatches[generic.Roles[0].RoleId]);
    }

    [TestMethod]
    public void OrientationAndPositionAreStableAcrossEveryEnumerationPermutation()
    {
        var laptop = Screen("laptop", -100, y: 200);
        laptop.IsInternal = true;
        var upper = Screen("upper", -100, y: -500);
        var portraitRight = Screen("portrait-right", 2000, portrait: true);
        var portraitLeft = Screen("portrait-left", -2000, portrait: true);
        // Width/height determine the effective orientation even if rotation is
        // stale or describes the panel's native mounting.
        portraitLeft.Rotation = 1;
        var screens = new[] { laptop, upper, portraitRight, portraitLeft };
        var generic = Generic(
            Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Landscape, 2),
            Slot(GenericMonitorOrientation.Portrait, 1), Slot(GenericMonitorOrientation.Portrait, 2));

        foreach (var permutation in Permutations(screens))
        {
            var result = Match(permutation, generic);
            Assert.AreSame(upper, result.RoleMatches[generic.Roles[0].RoleId]);
            Assert.AreSame(laptop, result.RoleMatches[generic.Roles[1].RoleId]);
            Assert.AreSame(portraitLeft, result.RoleMatches[generic.Roles[2].RoleId]);
            Assert.AreSame(portraitRight, result.RoleMatches[generic.Roles[3].RoleId]);
        }
    }

    [TestMethod]
    public void PositionTiesUseOrdinalDevicePathAndSquareScreensAreLandscape()
    {
        var lowerCase = Screen("a", 0);
        var upperCase = Screen("Z", 0);
        lowerCase.Height = lowerCase.Width;
        upperCase.Height = upperCase.Width;
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Landscape, 2));

        foreach (var screens in new[] { new[] { lowerCase, upperCase }, new[] { upperCase, lowerCase } })
        {
            var result = Match(screens, generic);
            Assert.AreSame(upperCase, result.RoleMatches[generic.Roles[0].RoleId]);
            Assert.AreSame(lowerCase, result.RoleMatches[generic.Roles[1].RoleId]);
        }
    }

    [TestMethod]
    public void MissingSlotsPreserveWallpaperWithoutCompressingRequiredCountsOrIndexes()
    {
        var screens = new[] { Screen("one", 0), Screen("two", 1000), Screen("three", 2000) };
        var blank = Slot(GenericMonitorOrientation.Landscape, 1, "");
        var third = Slot(GenericMonitorOrientation.Landscape, 3);
        var generic = Generic(blank, third);

        var result = Match(screens, generic);

        Assert.AreEqual(MatchStatus.Generic, result.Status);
        Assert.AreEqual(1, result.RoleMatches.Count);
        Assert.AreSame(screens[2], result.RoleMatches[third.RoleId]);
        Assert.IsFalse(result.RoleMatches.ContainsKey(blank.RoleId));
        Assert.AreEqual(2, result.UnassignedMonitorCount);
        Assert.AreEqual(2, generic.Roles.Count);
        Assert.AreEqual(3, generic.Roles[1].GenericIndex);
        Assert.AreEqual(3, GenericProfileMatcher.GetRequiredMonitorCount(generic.Roles, GenericMonitorOrientation.Landscape));
        Assert.AreEqual(0, GenericProfileMatcher.GetRequiredMonitorCount(generic.Roles, GenericMonitorOrientation.Portrait));
        Assert.AreEqual(3, result.GenericLayoutMonitors.Count);
        Assert.AreNotSame(screens[0], result.GenericLayoutMonitors[0]);
        screens[0].DesktopX = 5000;
        Assert.AreEqual(0, result.GenericLayoutMonitors[0].DesktopX);
        Assert.AreEqual(MatchStatus.NoMatch, Match(screens.Take(2).ToArray(), generic).Status);
    }

    [TestMethod]
    public void UnassignedGenericSlotNeverUsesLegacyRoleLabelLookup()
    {
        var assigned = Slot(GenericMonitorOrientation.Landscape, 1);
        var unassigned = Slot(GenericMonitorOrientation.Landscape, 2, "");
        assigned.RoleId = unassigned.Role;
        var profile = Generic(assigned, unassigned);
        var result = Match(new[] { Screen("one", 0), Screen("two", 2000) }, profile);

        Assert.IsTrue(result.TryGetMonitor(assigned, out _));
        Assert.IsFalse(result.TryGetMonitor(unassigned, out _));
        Assert.AreEqual(1, result.RoleMatches.Count);
        Assert.IsTrue(WallpaperBindingSafety.TrySnapshotTargets(result, out var targets, out _));
        Assert.AreEqual(1, targets.Count);
    }

    [TestMethod]
    public void FewerScreensNeverPartiallyApplyTheSavedFullCombination()
    {
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Landscape, 2),
            Slot(GenericMonitorOrientation.Portrait, 1));
        var monitor = Screen("one", 0);

        var result = Match(new[] { monitor }, generic);

        Assert.AreEqual(MatchStatus.NoMatch, result.Status);
        Assert.IsFalse(result.CanAutoApply);
        Assert.AreEqual(0, result.RoleMatches.Count);
        Assert.AreEqual(3, generic.Roles.Count);
        Assert.AreEqual(1, result.UnassignedMonitorCount);
    }

    [DataTestMethod]
    [DataRow(2, 0, 1, 1)]
    [DataRow(1, 1, 2, 0)]
    [DataRow(1, 0, 2, 0)]
    [DataRow(1, 0, 1, 1)]
    [DataRow(0, 1, 1, 1)]
    [DataRow(2, 2, 1, 1)]
    public void BothOrientationCountsMustMatchExactly(int requiredLandscape, int requiredPortrait,
        int actualLandscape, int actualPortrait)
    {
        var generic = Generic(Enumerable.Range(1, requiredLandscape)
            .Select(index => Slot(GenericMonitorOrientation.Landscape, index))
            .Concat(Enumerable.Range(1, requiredPortrait).Select(index => Slot(GenericMonitorOrientation.Portrait, index))).ToArray());
        var actual = Enumerable.Range(1, actualLandscape).Select(index => Screen("L" + index, index * 1920))
            .Concat(Enumerable.Range(1, actualPortrait).Select(index => Screen("P" + index, index * 1080, portrait: true))).ToArray();

        var result = Match(actual, generic);

        Assert.AreEqual(MatchStatus.NoMatch, result.Status);
        Assert.IsFalse(result.CanAutoApply);
        Assert.AreEqual(0, result.RoleMatches.Count);
    }

    [TestMethod]
    public void BlankWallpaperSlotsStillCountTowardsTheRequiredCombination()
    {
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Portrait, 1, ""));
        var landscape = Screen("L", 0);
        var portrait = Screen("P", 1920, portrait: true);

        Assert.AreEqual(MatchStatus.NoMatch, Match(new[] { landscape }, generic).Status);
        var result = Match(new[] { portrait, landscape }, generic);
        Assert.AreEqual(MatchStatus.Generic, result.Status);
        Assert.AreEqual(1, result.RoleMatches.Count);
        Assert.AreEqual(1, result.UnassignedMonitorCount);
        Assert.AreSame(landscape, result.RoleMatches[generic.Roles[0].RoleId]);
    }

    [TestMethod]
    public void WrongCountsCannotHideLowerPriorityExactGenericCombination()
    {
        var partial = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        partial.Priority = 1000;
        var exact = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Portrait, 1));

        var result = Match(new[] { Screen("L", 0), Screen("P", 1920, portrait: true) }, partial, exact);

        Assert.AreEqual(MatchStatus.Generic, result.Status);
        Assert.AreSame(exact, result.Profile);
        Assert.AreEqual(2, result.RoleMatches.Count);
    }

    [TestMethod]
    public void DisconnectedScreensAreNotNumberedOrCounted()
    {
        var disconnected = Screen("disconnected", -2000);
        disconnected.ConnectionState = "Disconnected";
        var connected = Screen("connected", 0);
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1));

        var result = Match(new[] { disconnected, connected }, generic);

        Assert.AreSame(connected, result.RoleMatches[generic.Roles[0].RoleId]);
        Assert.AreEqual(0, result.UnassignedMonitorCount);
    }

    [TestMethod]
    public void BlankUnconfiguredWrongOrientationAndOutOfRangeSlotsNeverSucceed()
    {
        foreach (var generic in new[]
                 {
                     Generic(), Generic(Slot(GenericMonitorOrientation.Landscape, 1, "")),
                     Generic(Slot(GenericMonitorOrientation.Portrait, 1)),
                     Generic(Slot(GenericMonitorOrientation.Landscape, 2))
                 })
        {
            var result = Match(new[] { Screen("one", 0) }, generic);
            Assert.AreEqual(MatchStatus.NoMatch, result.Status);
            Assert.IsFalse(result.CanAutoApply);
            Assert.AreEqual(0, result.RoleMatches.Count);
        }
    }

    [TestMethod]
    public void DisabledGenericIsIgnored()
    {
        var generic = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        generic.Enabled = false;
        Assert.AreEqual(MatchStatus.NoMatch, Match(new[] { Screen("one", 0) }, generic).Status);
    }

    [TestMethod]
    public void GenericSelectionUsesPriorityThenStableIdAndSkipsInapplicableProfiles()
    {
        var noApplicableSlot = Generic(Slot(GenericMonitorOrientation.Portrait, 1));
        noApplicableSlot.Priority = 1000;
        var a = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        a.Id = "a";
        a.Priority = 10;
        var b = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        b.Id = "b";
        b.Priority = 10;
        var lowerPriority = Generic(Slot(GenericMonitorOrientation.Landscape, 1));
        lowerPriority.Id = "0";
        foreach (var profiles in Permutations(new[] { noApplicableSlot, a, b, lowerPriority }))
            Assert.AreSame(a, Match(new[] { Screen("one", 0) }, profiles).Profile);
    }

    [TestMethod]
    public void DuplicatePositionsOrBindingIdsAreNotApplicable()
    {
        var duplicatePositions = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Landscape, 1));
        var duplicateIds = Generic(Slot(GenericMonitorOrientation.Landscape, 1), Slot(GenericMonitorOrientation.Landscape, 2));
        duplicateIds.Roles[1].RoleId = duplicateIds.Roles[0].RoleId;

        foreach (var profile in new[] { duplicatePositions, duplicateIds })
        {
            Assert.IsFalse(WallpaperProfileApplyPolicy.IsComplete(profile));
            Assert.IsFalse(Match(new[] { Screen("one", 0), Screen("two", 2000) }, profile).CanAutoApply);
        }
    }

    [TestMethod]
    public void GenericCanBeSavedCompletelyBlankThenConfiguredWithoutHardware()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("通用", 20, WallpaperProfileMatchMode.Generic);
        WallpaperProfileEditingService.Apply(profile, Draft());
        Assert.IsTrue(profile.IsGeneric);
        Assert.AreEqual(0, profile.Roles.Count);
        Assert.IsFalse(profile.AutoApply);
        WallpaperProfileEditingService.Apply(profile, Draft(
            DraftSlot(GenericMonitorOrientation.Landscape, 1, ""),
            DraftSlot(GenericMonitorOrientation.Portrait, 3, "portrait")));

        Assert.IsTrue(profile.AutoApply);
        Assert.AreEqual(0, profile.ExpectedMonitorCount);
        Assert.AreEqual("横屏1", profile.Roles[0].DisplayName);
        Assert.AreEqual("竖屏3", profile.Roles[1].DisplayName);
        Assert.IsTrue(profile.Roles.All(role => role.Fingerprint.StableId == "" && role.Fingerprint.MonitorDevicePath == ""));
        Assert.AreEqual(3, profile.Roles[1].GenericIndex);
    }

    [TestMethod]
    public void GenericSaveRejectsDuplicateSlotsAtomicallyAndRepairsDuplicateIds()
    {
        var profile = Generic(Slot(GenericMonitorOrientation.Portrait, 4));
        var previousRole = profile.Roles[0];
        Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile,
            Draft(DraftSlot(GenericMonitorOrientation.Landscape, 1), DraftSlot(GenericMonitorOrientation.Landscape, 1))));
        Assert.AreSame(previousRole, profile.Roles[0]);
        var first = DraftSlot(GenericMonitorOrientation.Landscape, 1);
        var second = DraftSlot(GenericMonitorOrientation.Portrait, 1);
        first.RoleId = second.RoleId = "duplicate";
        WallpaperProfileEditingService.Apply(profile, Draft(first, second));
        Assert.AreNotEqual(profile.Roles[0].RoleId, profile.Roles[1].RoleId);
    }

    [TestMethod]
    public void GenericAllowsMoreThan32SlotsAndIndexesButRejectsNonPositiveIndexes()
    {
        var profile = Generic();
        var slots = Enumerable.Range(1, 128).Select(index => DraftSlot(GenericMonitorOrientation.Landscape, index)).ToArray();
        WallpaperProfileEditingService.Apply(profile, Draft(slots));
        Assert.AreEqual(128, profile.Roles.Count);
        Assert.IsTrue(WallpaperProfileApplyPolicy.IsComplete(profile));
        var screens = Enumerable.Range(1, 128).Select(index => Screen("L" + index, index * 1920)).ToArray();
        var match = Match(screens, profile);
        Assert.AreEqual(MatchStatus.Generic, match.Status);
        Assert.AreEqual(128, match.RoleMatches.Count);
        foreach (var index in new[] { 0, -1, int.MinValue })
            Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile,
                Draft(DraftSlot(GenericMonitorOrientation.Landscape, index))));
        Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile,
            Draft(DraftSlot((GenericMonitorOrientation)99, 1))));
    }

    [TestMethod]
    public void VeryLargeSavedIndexRemainsValidWithoutExpandingMissingSlots()
    {
        var profile = Generic();
        WallpaperProfileEditingService.Apply(profile, Draft(DraftSlot(GenericMonitorOrientation.Landscape, int.MaxValue)));

        Assert.IsTrue(WallpaperProfileApplyPolicy.IsComplete(profile));
        Assert.AreEqual(1, profile.Roles.Count);
        Assert.AreEqual(int.MaxValue, GenericProfileMatcher.GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Landscape));
        Assert.AreEqual(MatchStatus.NoMatch, Match(new[] { Screen("L", 0) }, profile).Status);
        Assert.AreEqual(1, GenericProfileMatcher.GetNextAvailableIndex(new[] { int.MaxValue }));
        Assert.AreEqual(2, GenericProfileMatcher.GetNextAvailableIndex(new[] { int.MaxValue, 1, 1 }));
        Assert.AreEqual(4, GenericProfileMatcher.GetNextAvailableIndex(new[] { 3, 1, 2, 6, 5 }));
    }

    [TestMethod]
    public void EmptyGenericStartsAtZeroScreensAndFirstNewSlotUsesIndexOne()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("通用", 0, WallpaperProfileMatchMode.Generic);

        Assert.AreEqual(0, GenericProfileMatcher.GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Landscape));
        Assert.AreEqual(0, GenericProfileMatcher.GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Portrait));
        Assert.AreEqual(1, GenericProfileMatcher.GetNextAvailableIndex(Array.Empty<int>()));
        Assert.IsFalse(Match(Array.Empty<MonitorIdentity>(), profile).CanAutoApply);
    }

    [TestMethod]
    public void GenericSpanIsRejectedDuringEditingAndAutomaticMatching()
    {
        var profile = Generic();
        var slot = DraftSlot(GenericMonitorOrientation.Landscape, 1);
        slot.FitMode = WallpaperFitMode.Span;
        Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile, Draft(slot)));
        var storedSlot = Slot(GenericMonitorOrientation.Landscape, 1);
        storedSlot.FitMode = WallpaperFitMode.Span;
        profile.Roles.Add(storedSlot);
        Assert.IsFalse(WallpaperProfileApplyPolicy.IsComplete(profile));
        Assert.IsFalse(Match(new[] { Screen("one", 0) }, profile).CanAutoApply);
    }

    [TestMethod]
    public void GenericJsonRoundTripAndMigrationPreserveHolesWallpaperAndNoIdentity()
    {
        var profile = Generic(Slot(GenericMonitorOrientation.Landscape, 3), Slot(GenericMonitorOrientation.Portrait, 7, ""));
        profile.Roles[0].Fingerprint = Screen("should-be-cleared", 0);
        profile.Roles[0].LastKnownMonitorDevicePath = "should-be-cleared";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var json = JsonSerializer.Serialize(new ProfilesDocument { Profiles = new() { profile } }, options);
        var migrated = ProfileSchemaMigrator.Migrate(JsonSerializer.Deserialize<ProfilesDocument>(json, options));
        var first = JsonSerializer.Serialize(migrated, options);
        ProfileSchemaMigrator.Migrate(migrated);

        Assert.AreEqual(first, JsonSerializer.Serialize(migrated, options));
        var restored = migrated.Profiles[0];
        Assert.AreEqual(6, migrated.SchemaVersion);
        Assert.IsTrue(restored.IsGeneric);
        Assert.AreEqual(0, restored.ExpectedMonitorCount);
        CollectionAssert.AreEqual(new[] { 3, 7 }, restored.Roles.Select(role => role.GenericIndex).ToArray());
        Assert.AreEqual("asset", restored.Roles[0].WallpaperAssetId);
        Assert.AreEqual("", restored.Roles[1].WallpaperAssetId);
        Assert.IsTrue(restored.Roles.All(role => role.Fingerprint.StableId == "" && role.Fingerprint.MonitorDevicePath == ""
            && role.LastKnownMonitorDevicePath == "" && role.Fingerprint.StableIdSource == MonitorIdentitySource.Unknown));
        Assert.IsTrue(restored.AutoApply);
        Assert.AreEqual(3, GenericProfileMatcher.GetRequiredMonitorCount(restored.Roles, GenericMonitorOrientation.Landscape));
        Assert.AreEqual(7, GenericProfileMatcher.GetRequiredMonitorCount(restored.Roles, GenericMonitorOrientation.Portrait));
        Assert.AreEqual(MatchStatus.NoMatch, Match(new[] { Screen("one", 0) }, restored).Status);

        var fullSetup = Enumerable.Range(1, 3).Select(index => Screen("L" + index, index * 1920))
            .Concat(Enumerable.Range(1, 7).Select(index => Screen("P" + index, index * 1080, portrait: true))).ToArray();
        var fullMatch = Match(fullSetup, restored);
        Assert.AreEqual(MatchStatus.Generic, fullMatch.Status);
        Assert.AreEqual(1, fullMatch.RoleMatches.Count);
        Assert.AreEqual(9, fullMatch.UnassignedMonitorCount);
    }

    [TestMethod]
    public void VersionFiveProfilesRetainSpecificHardwareSemantics()
    {
        var profile = Specific(Screen("known", 0), "asset");
        profile.SchemaVersion = 5;
        ProfileSchemaMigrator.Migrate(new ProfilesDocument { SchemaVersion = 5, Profiles = new() { profile } });

        Assert.AreEqual(WallpaperProfileMatchMode.Specific, profile.MatchMode);
        Assert.AreEqual(6, profile.SchemaVersion);
        Assert.AreEqual("known", profile.Roles[0].Fingerprint.MonitorDevicePath);
        Assert.IsFalse(string.IsNullOrEmpty(profile.Roles[0].Fingerprint.StableId));
        Assert.AreEqual(MatchStatus.Exact, Match(new[] { Screen("known", 0) }, profile).Status);
    }

    private static MatchResult Match(MonitorIdentity[] monitors, params WallpaperProfile[] profiles)
        => new ProfileMatcher().Match(monitors, profiles);

    private static WallpaperProfile Generic(params MonitorRoleBinding[] slots)
        => new() { Name = "通用", MatchMode = WallpaperProfileMatchMode.Generic, Roles = slots.ToList() };

    private static WallpaperProfile Specific(MonitorIdentity monitor, string asset)
        => new() { Name = "特定", Roles = new() { new() { Fingerprint = monitor.Clone(), WallpaperAssetId = asset } } };

    private static MonitorRoleBinding Slot(GenericMonitorOrientation orientation, int index, string asset = "asset")
        => new()
        {
            Role = GenericProfileMatcher.GetSlotDisplayName(orientation, index),
            DisplayName = GenericProfileMatcher.GetSlotDisplayName(orientation, index),
            GenericOrientation = orientation, GenericIndex = index, WallpaperAssetId = asset
        };

    private static WallpaperProfileEditDraft Draft(params WallpaperRoleEditDraft[] slots)
        => new() { Name = "通用", MatchMode = WallpaperProfileMatchMode.Generic, Roles = slots.ToList() };

    private static WallpaperRoleEditDraft DraftSlot(GenericMonitorOrientation orientation, int index, string asset = "asset")
        => new() { GenericOrientation = orientation, GenericIndex = index, WallpaperAssetId = asset };

    private static MonitorIdentity Screen(string path, int x, int y = 0, bool portrait = false)
    {
        var screen = TestData.Monitor(path, "SERIAL-" + path, x);
        screen.Width = portrait ? 1080 : 1920;
        screen.Height = portrait ? 1920 : 1080;
        screen.DesktopY = y;
        return screen;
    }

    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length == 0) { yield return Array.Empty<T>(); yield break; }
        for (var index = 0; index < items.Length; index++)
            foreach (var rest in Permutations(items.Where((_, current) => index != current).ToArray()))
                yield return new[] { items[index] }.Concat(rest).ToArray();
    }
}
