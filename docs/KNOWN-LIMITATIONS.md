# Known limitations

- ContainerId and WMI availability depends on the display driver and session. SetupAPI is optional and gracefully degrades.
- A fallback geometry path is used only when QueryDisplayConfig is unavailable; it is marked as fallback and must not be treated as a hardware serial.
- `IDesktopWallpaper` has a process-wide position setting. Per-monitor rendering is used for all modes; Span is best-effort and should be verified on the target Windows build.
- The current release has a process-isolated secondary taskbar with grouping, pins, DWM previews, clock and bottom-edge auto-hide, but it is not a complete Explorer replacement. AppBar work-area reservation is enabled only for one secondary display; multiple secondary displays use a safe overlay fallback because current Shell behavior can assign several insets to one monitor.
- Windows exposes no stable public API for mirroring every third-party notification-area icon. SyncWallpaper does not inject into Explorer or enumerate undocumented internal toolbars, so full notification-area mirroring remains unavailable.
- Audio, display mode, Explorer restart and power actions are not automatically exercised by CI.
- Windows 10, Windows 11, mixed-DPI, sleep/wake and identical-monitor behavior require the manual checklist for Verified status.
- Online wallpaper providers and RemoteHost are intentionally disabled in the default install and have no network transport in Beta.
