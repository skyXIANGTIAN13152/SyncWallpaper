# Known limitations

- ContainerId and WMI availability depends on the display driver and session. SetupAPI is optional and gracefully degrades.
- A fallback geometry path is used only when QueryDisplayConfig is unavailable; it is marked as fallback and must not be treated as a hardware serial.
- `IDesktopWallpaper` has a process-wide position setting. Per-monitor rendering is used for all modes; Span is best-effort and should be verified on the target Windows build.
- The current release contains a process-isolated host boundary, not a complete Explorer replacement taskbar, title-bar hook, Alt+Tab shell or screen saver product.
- Audio, display mode, Explorer restart and power actions are not automatically exercised by CI.
- Windows 10, Windows 11, mixed-DPI, sleep/wake and identical-monitor behavior require the manual checklist for Verified status.
- Online wallpaper providers and RemoteHost are intentionally disabled in the default install and have no network transport in Beta.
