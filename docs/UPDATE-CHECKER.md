# GitHub Releases update checker

SyncWallpaper's update feature discovers versions and opens a Release page; it is not an installer. The main process never downloads or executes Release assets.

## Components

- `SyncWallpaper.Update.Core`: `GitHubReleaseChecker`, semantic-version parsing, Release comparison, URL validation, note sanitization and weekly scheduling.
- `AppRuntime.CheckForUpdatesAsync`: stores results in local settings and logs; network errors never change wallpaper state.
- Settings page: current version, channel, last check, notes and a validated GitHub Release link.
- Tray menu: **Check for updates** and **Open GitHub repository** only; no download, install, restart or updater menu item.

## Requests and channels

- Stable queries the latest non-draft, non-prerelease SemVer Release.
- Beta queries up to 100 Releases, ignores drafts, allows prereleases and chooses the highest SemVer.
- Requests use the GitHub JSON media type, API version header, a 15-second timeout, cancellation, one shared `HttpClient` and a 2 MiB response limit.
- The current version comes from `AssemblyInformationalVersion`; equal or lower versions are not offered.

Automatic checks are disabled by default. When enabled, at most one check runs every seven days in UTC. Manual checks ignore that interval; only one request runs at a time. Automatic failures are logged silently.

Only `https://github.com` paths belonging to the configured repository's `/releases/` page are opened. API URLs, asset links, HTTP URLs and other repositories are rejected. Release notes are length-limited plain text with no Markdown, HTML, scripts, images or iframes.

There is no updater executable, staging area, update backup, automatic replacement, health check or automatic rollback.
