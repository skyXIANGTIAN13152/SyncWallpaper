# Changelog

## 1.0.0-rc.2 — 2026-08-06

- Configured the public GitHub Releases checker for `skyXIANGTIAN13152/SyncWallpaper`.
- Added named wallpaper combinations with save, apply, rename and delete actions; deleting a profile never deletes wallpaper assets.
- Added wallpaper library refresh and per-display wallpaper status reporting, with missing-file and ambiguous-identity safeguards.
- Updated the taskbar/tray icon assets and accessibility-oriented control colors.
- Replaced the unused automatic-install update design with an opt-in GitHub Releases checker; release packages explicitly exclude `SyncWallpaper.Updater.exe`.
- Kept ordinary configuration backups and profile migration behavior unchanged.

## Unreleased

- Future changes will be listed here before the next tagged release.

## 1.0.0-rc.1 — 2026-08-05

- Added formal Git baseline and release/1.0.0-rc1 branch.
- Added explicit wallpaper transaction states, generation IDs, rollback-failure stop and latest-state topology coordination.
- Added the standalone read-only HardwareValidation center with 21-step sanitized reports.
- Added five configuration recovery points, size/depth/path validation, suspend/resume state model and mixed-DPI layout validation.
- Added x64 framework-dependent/self-contained/portable package metadata, SHA256 files and current-user install/upgrade/uninstall scripts.
- Release Candidate status remains unverified for physical hotplug, sleep/wake, Explorer restart, Windows 10, mixed-DPI hardware and 12-hour real-time soak.

## 1.0.0-beta.1 — 2026-08-05

- Added Monitor Identity V2 fields and explainable stable ID sources.
- Added QueryDisplayConfig source/target names, optional SetupAPI ContainerId and WMI identity enrichment.
- Added Exact/Strong/Probable/Ambiguous/Unknown layered matching and no-guess behavior.
- Added profile schema migration, logical role bindings and Laptop/Three Monitor/Custom templates.
- Added event-driven topology stabilizer with native Windows messages, cancellation and duplicate suppression.
- Added wallpaper transaction snapshots, bounded retries, verification, rollback, Span mode and bounded render cache.
- Added A/B/C display identification overlay and expanded read-only UI/diagnostics.
- Added Beta architecture, identity, matching, transaction, privacy, performance and manual verification documentation.

Known limitations are documented in `docs/KNOWN-LIMITATIONS.md`; this release does not claim full DisplayFusion Pro parity.
