using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

internal static class TestData
{
    public static MonitorIdentity Monitor(string path = "PATH-A", string serial = "SERIAL-A", int x = 0)
        => new()
        {
            MonitorDevicePath = path,
            InstanceName = "DISPLAY\\ACME123\\" + serial,
            ManufacturerName = "ACME",
            ProductCodeId = "MODEL",
            EdidManufactureId = "ACME",
            EdidProductCodeId = "MODEL",
            EdidSerialNumber = serial,
            AdapterId = "adapter-1",
            TargetId = (uint)Math.Max(0, x / 1920),
            Width = 1920,
            Height = 1080,
            DesktopX = x,
            Rotation = 1,
            StableId = "edid:ACME|MODEL|" + serial,
            StableIdSource = MonitorIdentitySource.EdidSerial
        };
}
