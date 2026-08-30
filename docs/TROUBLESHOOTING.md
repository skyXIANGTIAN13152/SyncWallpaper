# Troubleshooting

## A matched profile did not apply after sign-in

Ensure automatic matching is enabled and inspect `Match` and `Wallpaper` entries in Activity Log. The 1.1 startup path detects immediately and starts a transaction without waiting for another display event. Use **Reapply matched wallpapers** from the tray or Overview to verify again.

## The profile is Unmatched

Check monitor count, every role's wallpaper, file existence and the identity source. Identical displays without reliable serials require the Monitor Identification page and A/B/C confirmation.

## A wallpaper says File missing

Open Wallpaper Library and click **Refresh library**. If a file was removed from `Wallpapers`, import it again and select it in the profile editor. SyncWallpaper never substitutes a black or random image for a missing file.

## Nothing changes immediately after hot-plug

SyncWallpaper waits about two seconds and reads two identical topology snapshots so Windows can finish enumerating HDMI/DP/USB-C paths. Use **Detect monitors** for an immediate read.

## Explorer is temporarily unavailable

The application performs bounded backoff retries and keeps the current wallpapers. It detects again after Explorer recovers; inspect `Logs` if the issue persists.

## Configuration is damaged

SyncWallpaper does not keep historical backups. Exit the application, inspect `Config\settings.json`, `profiles.json` and `library.json`, and move an unreadable file out of the folder before restarting. Existing files in `Wallpapers` can then be imported again.
