# Security boundaries

- Display APIs are read-only. The project does not call `SetDisplayConfig` or `ChangeDisplaySettingsEx`.
- Wallpapers are applied only to active, uniquely matched monitor device paths. Ambiguity, weak identity, missing assets and verification failures leave the current state unchanged.
- Configuration file names reject path traversal. JSON is limited to 10 MiB and depth 32. Saves use a temporary file, flush and atomic replacement.
- No configuration history, deleted-wallpaper backup, update staging or automatic rollback package is generated.
- Networking is disabled by default. GitHub requests use HTTPS, a 15-second timeout, cancellation and a 2 MiB response limit.
- The browser may open only the configured repository's `https://github.com/.../releases/` page. Release notes are displayed as length-limited plain text.
- No plugins, scripts, Explorer injection, extra host processes or administrator rights are used.
- Logs and read-only diagnostics remain local and are never uploaded automatically.

Report security issues privately through GitHub Security Advisories. Do not publicly paste raw device paths, serials, logs or configuration.
