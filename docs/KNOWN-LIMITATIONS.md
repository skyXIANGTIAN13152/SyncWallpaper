# Known limitations

- Windows 10/11 only.
- EDID, WMI, Container ID and connector fields depend on the monitor, adapter and graphics driver.
- Identical displays without reliable serials may require A/B/C confirmation again.
- `IDesktopWallpaper` positioning is a global Windows setting; SyncWallpaper pre-renders at target dimensions to reduce aspect-ratio differences.
- Span is best effort and should be validated on the target Windows build and layout.
- More Windows 10, mixed-DPI, sleep/wake and forced-Explorer-restart hardware scenarios still need validation.
- The application is unsigned; Windows SmartScreen may show a first-run warning.
