# 1.1.0-beta.1-en release acceptance

## Product scope

This English release contains multi-monitor discovery and identity, the wallpaper library, named profiles, automatic matching/application, tray integration, logs, startup, read-only diagnostics and user-initiated GitHub update checks. It retains complete read-only resolution, refresh rate, HDR, DPI, orientation, connector and hardware identity data, but has no display-setting write capability.

## Verification

- Release build: 0 warnings and 0 errors.
- Automated tests: 80 passed (67 core/UI tests, 9 update tests and 4 Windows integration tests).
- The package contains only the App and read-only Diagnostics executables plus documentation and installation scripts.
- Both ZIPs were generated from the same isolated English worktree and checked with SHA-256.

## Packages

- `SyncWallpaper-1.1.0-beta.1-en-win-x64.zip` — `13d8aad461ad9ed770847c5834a0591eef8321ea1e22b43f7ffaba3212eeac30`
- `SyncWallpaper-1.1.0-beta.1-en-win-x64-selfcontained.zip` — `c5231a230ac4aed88d161d4d7aa18ee5c04318e7882bc76d45a005bd5e3952b1`

No user configuration, wallpapers, logs, credentials, developer paths, updater or configuration backup is included in the release packages.
