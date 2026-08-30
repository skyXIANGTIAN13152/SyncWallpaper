# SyncWallpaper

SyncWallpaper is an open-source Windows application dedicated to wallpapers for multi-monitor desktops. It discovers the physical monitors and topology currently connected, matches a saved wallpaper profile, and restores each monitor's wallpaper after sign-in, hot-plug events, sleep/wake, or Explorer recovery.

This English release is `1.1.0-beta.1-en`. The product is wallpaper-only: it does not provide a secondary taskbar, window management, audio switching, screen saver, remote control, online wallpaper provider, or display-setting changes.

## Features

- Enumerates active display paths with `QueryDisplayConfig` and `DisplayConfigGetDeviceInfo`.
- Supplements identity with WMI `WmiMonitorID` (manufacturer, product code, serial, instance name and friendly name).
- Preserves read-only display information: monitor device path, EDID fields, adapter/source/target, connector type and instance, resolution, native resolution, refresh rate, DPI/scale, HDR/color, orientation/flip, desktop coordinates, primary status and internal/external status.
- Uses layered identity matching: manufacturer + product code + EDID serial, monitor device path, WMI instance name, adapter/target/connector topology, then geometry as supporting evidence.
- Never guesses when identical displays cannot be distinguished safely. A/B/C overlays let the user bind physical displays to Laptop, Landscape, Portrait or custom roles.
- Imports and manages JPG/JPEG/PNG/BMP wallpapers. Refresh hides deleted files and restores entries when the original file returns.
- Supports named wallpaper profiles: create blank, edit, rename, delete and manually apply. Multiple profiles can describe the same monitor topology.
- Re-matches after every stable topology change and applies the matching profile automatically. Manual application never changes the selected profile or profile metadata.
- Applies wallpapers per monitor through `IDesktopWallpaper`, then reads them back for verification. Ambiguous matches, missing assets and weak identity evidence leave the existing wallpapers unchanged.
- Optional current-user startup, low-resource rendering cache, read-only diagnostics and user-initiated GitHub Release checks.

## User interface

The main window contains seven pages:

1. Overview
2. Monitor Identification
3. Wallpaper Library
4. Wallpaper Profiles
5. Activity Log
6. Settings
7. About

The complex observing-eye artwork is used only inside the application. Windows taskbar and tray assets use the monitor-topology icon so they remain legible at 16–24 pixels.

## Matching and safety

Windows display numbers such as `\\.\DISPLAY1` are temporary session labels, not permanent identities. Profile matching uses physical identity first and geometry last. A profile is marked **Matched** only when the current topology has one unique assignment, reliable identity evidence and a complete wallpaper binding for every role. It is marked **Unmatched** when the topology, identity, role assignment or wallpaper is incomplete or ambiguous.

Resolution, refresh rate, HDR, DPI, orientation and coordinates are retained for identification and explanation only. SyncWallpaper does not call `SetDisplayConfig` or `ChangeDisplaySettingsEx` and never changes Windows display settings.

## Data layout

The formal D: deployment keeps runtime data beside the application:

```text
<SyncWallpaper folder>/
├─ App/                    application files
├─ Diagnostics/            read-only diagnostic tool
├─ Config/                 settings, profiles and wallpaper index
├─ Wallpapers/             managed wallpaper originals
├─ Cache/Rendered/         per-monitor render cache
├─ Thumbnails/             thumbnail cache
└─ Logs/                   local diagnostic logs
```

No configuration history, deleted-wallpaper backup or update staging is created. Configuration writes use an atomic temporary-file replacement. If the application directory is not writable, storage falls back to `%LocalAppData%\SyncWallpaper`; `SYNCWALLPAPER_DATA_ROOT` can override the location.

## Build and test

Requires Windows 10/11 and the .NET 8 SDK:

```powershell
.\build.ps1
.\run.ps1
.\publish.ps1
.\publish-selfcontained.ps1
```

The solution contains only wallpaper-related projects: Core, Windows, App, Diagnostics, Update.Core and their tests. The automated suite covers identity matching, identical-display ambiguity, topology stabilization, profile editing, wallpaper transactions, library refresh, startup recovery, tray states and update checks.

Published executables are:

```text
artifacts\publish\win-x64\App\SyncWallpaper.App.exe
artifacts\publish\win-x64\Diagnostics\SyncWallpaper.Diagnostics.exe
```

Release ZIPs contain the license, documentation and installation scripts, but no user configuration, wallpapers, logs, credentials, developer paths, extra host processes or updater.

## Read-only diagnostics

```powershell
SyncWallpaper.Diagnostics.exe snapshot
SyncWallpaper.Diagnostics.exe wallpaper-snapshot
SyncWallpaper.Diagnostics.exe monitor-soak --iterations 1000
```

Diagnostics read monitor state, current wallpaper paths and their own resource usage. They do not change display settings or wallpapers.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [User guide](docs/USER-GUIDE.en.md)
- [Monitor identity](docs/MONITOR-IDENTITY.md)
- [Profile matching](docs/PROFILE-MATCHING.md)
- [Wallpaper transactions](docs/WALLPAPER-TRANSACTION.md)
- [Hardware test checklist](docs/REAL-HARDWARE-TEST-CHECKLIST.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Privacy](docs/PRIVACY.md)
- [Security](docs/SECURITY.md)
- [Performance](docs/PERFORMANCE.md)
- [Installation](docs/INSTALLATION.md)

## Open source and updates

Licensed under [LICENSE](LICENSE). Source repository: [skyXIANGTIAN13152/SyncWallpaper](https://github.com/skyXIANGTIAN13152/SyncWallpaper).

Update checks are disabled by default. When requested by the user, the application reads the official GitHub Releases API and opens a validated Release page. It never downloads, replaces or executes update assets.

## Known limitations

- Windows 10/11 only.
- EDID, WMI, Container ID and connector fields depend on the monitor, adapter and graphics driver.
- Identical displays without reliable serials may require A/B/C confirmation again.
- `IDesktopWallpaper` positioning is a global Windows setting; SyncWallpaper pre-renders at target dimensions to reduce aspect-ratio differences.
- Span is best effort and should be validated on the target Windows build and layout.
- The application is unsigned; Windows SmartScreen may show a first-run warning.
