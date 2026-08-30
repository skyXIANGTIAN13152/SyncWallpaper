# Contributing to SyncWallpaper

Thank you for your interest in SyncWallpaper. The project targets multi-monitor wallpaper identification and recovery on Windows 10/11 using .NET 8 and WPF. Before contributing, read `README.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md` and `docs/KNOWN-LIMITATIONS.md`.

## Development setup

1. Install the .NET 8 SDK and the Windows desktop workload.
2. Clone the repository and run `dotnet restore` from the repository root.
3. Build with `dotnet build SyncWallpaper.sln -c Release`.
4. Run tests with `dotnet test SyncWallpaper.sln -c Release`.

Do not commit local data from `Config`, `Wallpapers`, `Logs`, `Cache`, `Thumbnails` or `artifacts`. Device paths, EDID serials, logs and wallpaper files must use redacted examples.

## Changes

- Keep each commit focused on one problem and describe behavior changes in the message.
- New features need unit tests. Changes involving Windows display APIs, Explorer or wallpaper transactions should update the relevant documentation and known limitations.
- Never put real monitor configurations, user wallpapers or personal paths in fixtures, reports or screenshots.
- Features that modify resolution, refresh rate, HDR, DPI, audio, windows, taskbar or Shell behavior are out of scope; display APIs are read-only in this project.

## Pull requests

Describe the motivation, impact, test commands, Windows version and any unverified physical-monitor scenarios.

Do not open a public issue for a security problem. Report it privately according to the repository security policy.
