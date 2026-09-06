# Installation

Release packages provide framework-dependent x64 and self-contained x64 ZIPs. The first requires the .NET 8 Desktop Runtime; the second includes the runtime.

Portable use: extract the ZIP and run `App\SyncWallpaper.App.exe`.

Per-user installation:

```powershell
.\install.ps1 -PackagePath . -CreateShortcuts -StartWithWindows
```

The installer defaults to `%LocalAppData%\Programs\SyncWallpaper` and does not need administrator rights. `-StartWithWindows` creates only the current user's SyncWallpaper Run entry.

The formal D: deployment stores application files in `App` and `Diagnostics`, and runtime data in `Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs`. Keep those data folders during an upgrade.

Installation never changes resolution, refresh rate, HDR, DPI, audio, windows, taskbar, Explorer, services, scheduled tasks or power settings.
