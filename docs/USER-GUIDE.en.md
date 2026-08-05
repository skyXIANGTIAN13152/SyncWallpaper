# SyncWallpaper Beta 1.0.0 user guide

1. Start `SyncWallpaper.App.exe`. Lightweight mode is the default; Taskbar, Shell, Remote and online provider hosts are not started.
2. Import JPG/JPEG/PNG/BMP files in Wallpaper Library.
3. Review the stable identity source in Displays. When identical monitors are ambiguous, choose Identify Displays; each screen briefly shows A/B/C. These letters are temporary prompts, never permanent IDs.
4. Bind each stable identity to Laptop, Landscape, Portrait or a custom role and choose a wallpaper.
5. After HDMI/DP/USB-C changes, topology changes or Explorer recovery, SyncWallpaper waits for two stable samples before applying. Ambiguity preserves the current wallpaper.
6. Use Settings → Optional modules to select Lightweight, Standard, Full or Custom. Each module shows the features lost when disabled and current resources.
7. Use Diagnostics for read-only snapshots and sanitized reports. Risky operations are never started in the background.

Data is stored under `%LocalAppData%\SyncWallpaper`. SyncWallpaper is offline by design; it does not upload wallpapers, monitor identities or logs.
