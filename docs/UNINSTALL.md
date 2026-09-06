# Uninstall

```powershell
.\uninstall.ps1
```

The script stops the installed SyncWallpaper process, removes program files, the current-user startup entry and SyncWallpaper shortcuts. Runtime data is kept by default to avoid accidental wallpaper deletion. After confirming the data folder, remove `Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs` manually if desired.

Uninstall does not change Windows display parameters or the current wallpapers.
