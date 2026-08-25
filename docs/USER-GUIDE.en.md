# SyncWallpaper 1.1 wallpaper-only user guide

1. Start `App\SyncWallpaper.App.exe`. Closing the window keeps the tray monitor running.
2. Monitor Identification shows manufacturer, product, serial/device path, connector, resolution, refresh rate, DPI, HDR, orientation/flip and desktop coordinates. These values are read-only.
3. If identical displays cannot be distinguished safely, show the temporary A/B/C overlays and bind each physical display to a logical role. The letters are not permanent IDs.
4. Import JPG/JPEG/PNG/BMP files in Wallpaper Library. Refresh hides entries whose managed files were deleted.
5. Save the current topology or create an empty profile and edit it later. Profiles can be named, renamed, deleted and manually applied.
6. Assign a wallpaper and fit mode to every role. Green “Matched” means the current hardware topology is uniquely and completely configured; red “Not matched” means it is not safe to apply automatically.
7. At sign-in and after HDMI/DP/USB-C, layout, resume or Explorer changes, SyncWallpaper automatically detects and applies the matching profile.
8. Settings controls automatic matching, startup, low-resource mode and opt-in update checks.

The D: deployment stores data under `D:\屏序 SyncWallpaper\Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs`. No configuration history or deleted-wallpaper backup is created.
