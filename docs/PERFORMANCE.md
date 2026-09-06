# Performance

SyncWallpaper is a single-process, event-driven wallpaper monitor. When displays do not change it does not poll topology and does not create taskbar, Shell, remote, online-provider or other feature processes.

Normal display events wait about two seconds and require two identical snapshots, with a ten-second stability limit. Duplicate signatures do not trigger another application. Sign-in uses one immediate detection.

Low-resource mode limits the render cache to 128 MiB; normal mode allows 512 MiB. The cache is trimmed by recent use. Hidden windows do not rebuild the UI, and the tray icon is redrawn only when its state changes.

`SyncWallpaper.Diagnostics monitor-soak` repeats read-only discovery and reports handle growth, CPU and memory deltas. It never changes displays or wallpapers.
