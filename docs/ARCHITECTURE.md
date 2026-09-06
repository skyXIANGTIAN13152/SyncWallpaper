# Architecture

SyncWallpaper is a single resident process, `SyncWallpaper.App.exe`. It owns the tray, configuration, logs, monitor discovery, topology coordination, wallpaper profiles, matching and wallpaper transactions.

```text
Windows display/device/power/Explorer events
                    │
                    ▼
         DisplayChangeCoordinator
          two stable snapshots + newest event wins
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
 MonitorDiscoveryService   ProfileMatcher
 QueryDisplayConfig/WMI    unique identity and roles
          │                   │
          └─────────┬─────────┘
                    ▼
          WallpaperApplyService
          render → apply → read-back verify
```

## Project boundaries

- `SyncWallpaper.Core`: data models, identity/profile matching, topology stabilization, atomic configuration writes and transaction state.
- `SyncWallpaper.Windows`: read-only monitor discovery, Windows messages, WMI, Explorer recovery, wallpaper library/render/apply, startup and logging.
- `SyncWallpaper.App`: WPF window, tray and wallpaper-focused runtime.
- `SyncWallpaper.Diagnostics`: read-only monitor/wallpaper snapshots and soak tests.
- `SyncWallpaper.Update.Core`: GitHub Release checks only; no download or install.

## Display API boundary

Resolution, refresh rate, DPI, HDR, orientation, desktop coordinates and connector data are retained because they explain identity and matching. They are read only. The solution contains no `SetDisplayConfig`, `ChangeDisplaySettingsEx` or display-setting write service.

## Events and concurrency

`WM_DISPLAYCHANGE`, `WM_DEVICECHANGE`, `WM_POWERBROADCAST`, `WM_DPICHANGED`, `WM_SETTINGCHANGE`, `TaskbarCreated` and `SystemEvents` feed one coordinator. Normal changes wait about two seconds and require two identical snapshots; a newer event cancels older work. Sign-in performs an immediate detection so a saved topology is applied without waiting for another hot-plug event.

## Data and failure isolation

Runtime data is limited to `Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs`. Configuration is written to a temporary file, flushed and atomically replaced. No history or deleted-wallpaper backup is created. Ambiguous identity, weak evidence, missing files or temporary Explorer failures keep the current wallpapers; read-back failures attempt an in-memory rollback of the current transaction.
