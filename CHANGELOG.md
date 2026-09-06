# Changelog

## 1.1.0-beta.2 — 2026-09-06

- Add Aurora technology, Graphite dark and Daylight light themes with instant switching and a persisted appearance preference.
- Replace native mixed-color input, dropdown, checkbox and selection surfaces with shared theme-aware templates; retain text contrast in selected, focused and disabled states.
- Use opaque reading surfaces, restrained accent colors, active navigation and consistent spacing while retaining transparent outer window corners.
- Add automated palette contrast, preference migration and live WPF control-theme regression checks.
- Prevent compact text inputs from applying padding twice; regression checks cover complete Chinese glyphs at 100%, 125%, 150% and 200% layout scales.
- Keep Chinese and English update checks within their own release edition, and publish verified packages through draft releases before public promotion.

- Preserve per-device binding IDs and wallpapers when several monitors share a logical role such as Portrait, including repeated manual identification and changed enumeration order.
- Repair duplicate binding IDs in saved profiles and retain existing file selections when reopening identification or profile editing.
- Reject duplicate target devices before applying, preflight every source image, and verify the complete distinct-monitor set before reporting success.
- Refresh monitor discovery before manual reapply and reject conflicting reliable EDID serials even when a connection path is reused.
- Add regression coverage for all 24 four-screen orderings, duplicate bindings, repeated application, incomplete profiles and final wallpaper verification.

## 1.1.0-beta.1 — 2026-08-25

- Refocused SyncWallpaper exclusively on multi-monitor wallpaper identification, profiles, matching and recovery.
- Removed display-configuration mutation, audio, window management, automation, desktop icons, secondary taskbar, Shell, screen saver, remote and online-provider modules and UI.
- Kept the full read-only display inventory, including EDID/WMI identity, device path, GPU path, connector, resolution, refresh rate, DPI, HDR, orientation and desktop geometry.
- Reduced the main UI to Overview, Monitor Identification, Wallpaper Library, Wallpaper Profiles, Logs, Settings and About.
- Fixed startup application so a saved matching topology begins a wallpaper transaction immediately after sign-in without waiting for a later display event.
- Preserved named wallpaper profiles, blank-profile editing, rename/delete/manual apply and automatic topology matching.
- Removed configuration recovery history, deleted-wallpaper backup storage and retired-feature data files.
- Reduced the published product to the App and read-only Diagnostics executables.
- Added wallpaper-only unit/integration coverage; 80 automated tests pass. Real Windows 11 checks cover successful 3/3 application with external displays and 1/1 cold-start application in laptop-only mode.

## 1.0.0-rc.2 — 2026-08-06

- Configured the public GitHub Releases checker for `skyXIANGTIAN13152/SyncWallpaper`.
- Added named wallpaper combinations with save, apply, rename and delete actions.
- Added wallpaper library refresh and per-display wallpaper status reporting.
- Updated taskbar/tray icon assets and control colors.

## 1.0.0-beta.1 — 2026-08-05

- Added layered monitor identity, topology stabilization, per-display wallpaper transactions, A/B/C identification and the initial WPF interface.
