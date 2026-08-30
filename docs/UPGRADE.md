# Upgrade

SyncWallpaper only checks GitHub Releases. It does not download, close, replace, install or roll back versions automatically.

1. Download the desired ZIP from the official Release and verify its SHA-256 hash.
2. Exit SyncWallpaper from the tray.
3. Replace only the `App` and `Diagnostics` folders.
4. Keep `Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs`.
5. Start the new version and confirm the Settings page shows the expected data folder and startup state.

`upgrade.ps1` performs an in-place program replacement through the same installer logic and preserves runtime data. No update staging, update backup or configuration history is created.
