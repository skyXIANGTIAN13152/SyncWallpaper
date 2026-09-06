# SyncWallpaper 1.1 English user guide

1. Start `App\SyncWallpaper.App.exe`. Closing the window leaves the lightweight tray monitor running.
2. Monitor Identification shows manufacturer, product, serial/device path, connector, resolution, refresh rate, DPI, HDR, orientation/flip and desktop coordinates. These values are read-only.
3. If identical displays cannot be distinguished safely, show the temporary A/B/C overlay and bind each physical display to a logical role. The letters are not permanent IDs.
4. Import JPG/JPEG/PNG/BMP files in Wallpaper Library. Refresh hides entries whose managed files were deleted.
5. Save the current topology or create a blank profile and edit it later. Profiles can be named, renamed, deleted and manually applied.
6. Assign a wallpaper and fit mode to every role. Green **Matched** means the hardware topology is uniquely and completely configured; red **Unmatched** means it is not safe to apply automatically.
7. At sign-in and after HDMI/DP/USB-C, layout, resume or Explorer changes, SyncWallpaper detects and applies the matching profile automatically.
8. Settings controls automatic matching, startup, low-resource mode and opt-in update checks.

The formal D: deployment stores data in `Config`, `Wallpapers`, `Cache`, `Thumbnails` and `Logs` beside the application. No configuration history or deleted-wallpaper backup is created.
