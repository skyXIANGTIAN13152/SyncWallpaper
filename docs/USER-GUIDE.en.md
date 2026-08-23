# SyncWallpaper Beta 1.0.0 user guide

1. Start `SyncWallpaper.App.exe`. Lightweight mode is the default; Taskbar, Shell, Remote and online provider hosts are not started.
2. To use secondary taskbars, enable Taskbar Host under Taskbar & Screen Behaviour. It supports grouped app buttons, per-window actions, pins, DWM previews, a clock and bottom-edge auto-hide. Auto-hide leaves a 2 px reveal edge and never reserves work area. Work-area reservation uses the public AppBar API with exactly one secondary display; multi-secondary layouts safely fall back to overlay mode rather than risk assigning several insets to one monitor. Disabling Taskbar Host removes its windows, AppBar, hooks, timers and process.
3. Import JPG/JPEG/PNG/BMP files in Wallpaper Library.
4. Review the stable identity source in Displays. When identical monitors are ambiguous, choose Identify Displays; each screen briefly shows A/B/C. These letters are temporary prompts, never permanent IDs.
5. In Rules & Matching, enter a name and choose Save Current Wallpaper Combination. Each named profile stores stable monitor identities, logical roles and wallpaper bindings independently; save multiple topologies or multiple wallpaper sets for the same topology, then rename, delete or apply a selected combination. Deleting a combination does not delete wallpaper files.
6. After HDMI/DP/USB-C changes, topology changes or Explorer recovery, SyncWallpaper waits for two stable samples before applying. Ambiguity preserves the current wallpaper.
7. Use Settings → Optional modules to select Lightweight, Standard, Full or Custom. Each module shows the features lost when disabled and current resources.
8. Use Diagnostics for read-only snapshots and sanitized reports. Risky operations are never started in the background.

9. In Settings → Update checks, updates are disabled by default. A manual check only queries GitHub Releases; if a newer release exists, the GitHub Release page is opened for you to download and install. You may opt into one weekly check.

Data is stored in the writable application/project directory (for example `D:\屏序 SyncWallpaper`) under `Wallpapers`, `Config`, `Cache` and `Logs` when possible, and falls back to `%LocalAppData%\SyncWallpaper` only when that directory is not writable. The Settings page shows the active data directory. The first switch to a D: drive directory migrates the legacy library. SyncWallpaper never downloads or installs updates automatically and does not upload wallpapers, monitor identities, configuration or logs.
