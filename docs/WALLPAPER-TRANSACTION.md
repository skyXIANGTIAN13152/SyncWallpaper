# Wallpaper transactions

SyncWallpaper uses Windows `IDesktopWallpaper` to set a static wallpaper on each active monitor. A transaction first captures the current wallpaper paths, then renders at each target size, applies the result and reads the paths back for verification.

Supported formats are JPG/JPEG/PNG/BMP. Fit modes are Fill, Fit, Stretch, Center, Tile and Span. Cache keys include the source hash, target size, fit mode, background color and renderer version, with a bounded capacity.

Rules:

- Ambiguous, unknown, weak-evidence or incomplete matches do not write anything.
- Missing files and targets not present in the active paths keep their current wallpaper.
- A target that already has the requested path is not set again.
- Temporary Explorer COM failures receive bounded retries.
- If read-back fails after a write, the transaction restores its in-memory pre-transaction snapshot in reverse order.
- A black fill or random image is never used as a missing-file substitute.
- Transaction snapshots exist only in memory; no disk rollback backup is generated.
