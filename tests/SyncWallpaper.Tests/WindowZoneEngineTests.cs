using SyncWallpaper.Core;
using SyncWallpaper.WindowEngine;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class WindowZoneEngineTests
{
    [TestMethod]
    public void PresetsGenerateDeterministicNonOverlappingZones()
    {
        var monitor = StrongMonitor(width: 2560, height: 1440);
        foreach (var preset in Enum.GetValues<WindowZonePreset>())
        {
            var layout = WindowZoneLayoutFactory.Create(string.Empty, monitor, preset);
            Assert.IsTrue(WindowZoneLayoutValidator.Validate(layout).IsValid, preset.ToString());
            Assert.AreEqual(preset is WindowZonePreset.TwoColumns or WindowZonePreset.TwoRows ? 2 : preset == WindowZonePreset.Grid2X2 ? 4 : 3, layout.Zones.Count);
        }
    }

    [TestMethod]
    public void PrimaryAndStackAdaptsToPortraitMonitor()
    {
        var layout = WindowZoneLayoutFactory.Create("portrait", StrongMonitor(width: 1440, height: 2560), WindowZonePreset.PrimaryAndStack);
        Assert.AreEqual(1d, layout.Zones[0].Width, 0.0001);
        Assert.AreEqual(2d / 3, layout.Zones[0].Height, 0.0001);
        Assert.AreEqual(.5, layout.Zones[1].Width, 0.0001);
    }

    [TestMethod]
    public void InvalidOrOverlappingZonesAreRejected()
    {
        var monitor = StrongMonitor();
        var layout = new WindowZoneLayout
        {
            TargetMonitor = monitor,
            Zones = new List<WindowZone>
            {
                new() { Left = 0, Top = 0, Width = .75, Height = 1 },
                new() { Left = .5, Top = 0, Width = .6, Height = 1 }
            }
        };
        var result = WindowZoneLayoutValidator.Validate(layout);
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(x => x.Contains("重叠", StringComparison.Ordinal)));
        Assert.IsTrue(result.Errors.Any(x => x.Contains("边界", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WeakMonitorIdentityCannotBePersistedForAutomaticZones()
    {
        var monitor = TestData.Monitor();
        monitor.StableId = "geometry:test";
        monitor.StableIdSource = MonitorIdentitySource.Geometry;
        Assert.ThrowsException<InvalidOperationException>(() => WindowZoneLayoutFactory.Create("unsafe", monitor, WindowZonePreset.TwoColumns));
    }

    [TestMethod]
    public void NormalizedZoneSurvivesResolutionAndNegativeDesktopCoordinates()
    {
        var saved = StrongMonitor(serial: "SERIAL-A", width: 1920, height: 1080);
        var current = StrongMonitor(serial: "SERIAL-A", x: -3840, y: -200, width: 3840, height: 2160);
        var layout = WindowZoneLayoutFactory.Create("halves", saved, WindowZonePreset.TwoColumns);
        var document = new WindowZoneLayoutsDocument { GapPixels = 12, Layouts = new() { layout } };
        var platform = new FakeZonePlatform(Window(new IntPtr(7)));
        var result = new WindowZoneSnapService(platform).TrySnap(new IntPtr(7), new Int32Point(-100, 500), document, new[] { current });
        Assert.AreEqual(WindowZoneSnapStatus.Applied, result.Status);
        Assert.AreEqual(new Int32Rect(-1914, -194, 1908, 2148), platform.Applied.Single());
    }

    [TestMethod]
    public void ElevatedWindowIsNeverMoved()
    {
        var monitor = StrongMonitor();
        var document = Document(monitor);
        var platform = new FakeZonePlatform(Window(new IntPtr(8), elevated: true));
        var result = new WindowZoneSnapService(platform).TrySnap(new IntPtr(8), new Int32Point(100, 100), document, new[] { monitor });
        Assert.AreEqual(WindowZoneSnapStatus.ElevatedWindow, result.Status);
        Assert.AreEqual(0, platform.Applied.Count);
    }

    [TestMethod]
    public void SamePathOnTwoDisplaysIsAmbiguousAndNeverGuessed()
    {
        var expected = StrongMonitor(path: "PATH-X", serial: "SERIAL-A");
        expected.EdidSerialNumber = "0";
        expected.StableId = "path:PATH-X";
        expected.StableIdSource = MonitorIdentitySource.MonitorDevicePath;
        var first = TestData.Monitor("PATH-X", "0", x: 0);
        first.AdapterId = "GPU-A";
        var second = TestData.Monitor("PATH-X", "0", x: 1920);
        second.AdapterId = "GPU-B";
        MonitorIdentityBuilder.AssignStableIds(new[] { first, second });
        var layout = new WindowZoneLayout { TargetMonitor = expected, Zones = WindowZoneLayoutFactory.Create("x", StrongMonitor(), WindowZonePreset.TwoColumns).Zones };
        var platform = new FakeZonePlatform(Window(new IntPtr(9)));
        var result = new WindowZoneSnapService(platform).TrySnap(new IntPtr(9), new Int32Point(100, 100), new WindowZoneLayoutsDocument { Layouts = new() { layout } }, new[] { first, second });
        Assert.AreEqual(WindowZoneSnapStatus.AmbiguousMonitor, result.Status);
        Assert.AreEqual(0, platform.Applied.Count);
    }

    [TestMethod]
    public void ShiftObservedDuringDragSnapsOnlyAtMoveEnd()
    {
        var monitor = StrongMonitor();
        var source = new FakeWindowEventSource();
        var platform = new FakeZonePlatform(Window(new IntPtr(10))) { Pointer = new Int32Point(120, 120), ShiftPressed = true };
        WindowZoneSnapResult? completed = null;
        using var controller = new WindowZoneSnapController(source, platform, () => Document(monitor), () => new[] { monitor }, x => completed = x);

        source.Raise(0x000A, new IntPtr(10));
        platform.ShiftPressed = false;
        source.Raise(0x800B, new IntPtr(10));
        Assert.AreEqual(0, platform.Applied.Count);
        source.Raise(0x000B, new IntPtr(10));

        Assert.AreEqual(1, platform.Applied.Count);
        Assert.AreEqual(WindowZoneSnapStatus.Applied, completed?.Status);
    }

    [TestMethod]
    public void DragWithoutShiftDoesNotSnapAndDisposeUnsubscribes()
    {
        var monitor = StrongMonitor();
        var source = new FakeWindowEventSource();
        var platform = new FakeZonePlatform(Window(new IntPtr(11))) { Pointer = new Int32Point(120, 120) };
        var controller = new WindowZoneSnapController(source, platform, () => Document(monitor), () => new[] { monitor });
        source.Raise(0x000A, new IntPtr(11));
        source.Raise(0x000B, new IntPtr(11));
        Assert.AreEqual(0, platform.Applied.Count);

        platform.ShiftPressed = true;
        controller.Dispose();
        source.Raise(0x000A, new IntPtr(11));
        source.Raise(0x000B, new IntPtr(11));
        Assert.AreEqual(0, platform.Applied.Count);
        Assert.AreEqual(0, source.SubscriberCount);
    }

    [TestMethod]
    public void DisabledDocumentDoesNotMoveWindow()
    {
        var monitor = StrongMonitor();
        var document = Document(monitor);
        document.ShiftDragEnabled = false;
        var platform = new FakeZonePlatform(Window(new IntPtr(12)));
        var result = new WindowZoneSnapService(platform).TrySnap(new IntPtr(12), new Int32Point(100, 100), document, new[] { monitor });
        Assert.AreEqual(WindowZoneSnapStatus.Disabled, result.Status);
        Assert.AreEqual(0, platform.Applied.Count);
    }

    private static WindowZoneLayoutsDocument Document(MonitorIdentity monitor)
        => new() { GapPixels = 12, Layouts = new() { WindowZoneLayoutFactory.Create("halves", monitor, WindowZonePreset.TwoColumns) } };

    private static WindowPositionSnapshot Window(IntPtr handle, bool elevated = false)
        => new()
        {
            Handle = handle,
            Identity = new WindowIdentity { ExecutablePath = @"C:\Apps\editor.exe", WindowTitle = "Editor", IsElevated = elevated },
            PhysicalBounds = new Int32Rect(20, 20, 800, 600),
            Dpi = 96
        };

    private static MonitorIdentity StrongMonitor(string path = "PATH-A", string serial = "SERIAL-A", int x = 0, int y = 0, int width = 1920, int height = 1080)
    {
        var monitor = TestData.Monitor(path, serial, x, y, width, height);
        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });
        return monitor;
    }

    private sealed class FakeZonePlatform : IWindowZonePlatform
    {
        private readonly WindowPositionSnapshot _window;
        public FakeZonePlatform(WindowPositionSnapshot window) => _window = window;
        public bool ShiftPressed { get; set; }
        public Int32Point? Pointer { get; set; } = new Int32Point(100, 100);
        public bool MoveResult { get; set; } = true;
        public List<Int32Rect> Applied { get; } = new();
        public WindowPositionSnapshot? TryGetWindow(IntPtr handle) => handle == _window.Handle ? _window : null;
        public Int32Point? GetCursorPosition() => Pointer;
        public bool IsShiftPressed() => ShiftPressed;
        public bool TrySetPosition(WindowPositionSnapshot window, Int32Rect physicalBounds, bool maximize)
        {
            if (MoveResult) Applied.Add(physicalBounds);
            return MoveResult;
        }
    }

    private sealed class FakeWindowEventSource : IWindowEventSource
    {
        private EventHandler<WindowEvent>? _eventReceived;
        public event EventHandler<WindowEvent>? EventReceived
        {
            add => _eventReceived += value;
            remove => _eventReceived -= value;
        }
        public bool IsActive => true;
        public int SubscriberCount => _eventReceived?.GetInvocationList().Length ?? 0;
        public void Raise(uint type, IntPtr handle) => _eventReceived?.Invoke(this, new WindowEvent(type, handle, 0, 0, 0));
        public void Dispose() => _eventReceived = null;
    }
}
