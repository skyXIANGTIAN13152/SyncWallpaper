# Changelog

## 1.0.0-rc.2 — 2026-08-06

- Configured the public GitHub Releases checker for `skyXIANGTIAN13152/SyncWallpaper`.
- Added named wallpaper combinations with save, apply, rename and delete actions; deleting a profile never deletes wallpaper assets.
- Added wallpaper library refresh and per-display wallpaper status reporting, with missing-file and ambiguous-identity safeguards.
- Updated the taskbar/tray icon assets and accessibility-oriented control colors.
- Replaced the unused automatic-install update design with an opt-in GitHub Releases checker; release packages explicitly exclude `SyncWallpaper.Updater.exe`.
- Kept ordinary configuration backups and profile migration behavior unchanged.

## Unreleased

- Fixed startup wallpaper application when a valid matched profile retained a stale `AutoApply=false` value after being completed from a blank profile.
- Complete wallpaper profiles now derive automatic-apply eligibility from their monitor and wallpaper bindings; schema v4 repairs existing profiles while blank or incomplete profiles remain non-applicable.
- Synchronized the displayed startup setting with the actual current-user Windows startup registration.
- Added editable blank wallpaper profiles and completed-profile validation without changing the global automatic-matching safety switch.
- Added per-monitor window-zone layouts with Shift-drag snapping and mixed-DPI-aware placement safeguards.
- Added TaskbarHost v2 application grouping, grouped window actions, standard close requests, DWM live hover thumbnails, persistent local app pins and a secondary-display clock.
- Extended TaskbarHost IPC/status diagnostics with per-bar task, group and pinned counts while preserving the default lightweight no-process mode.
- Added TaskbarHost v3 bottom-edge auto-hide with delayed reveal, live hidden-state diagnostics and complete timer/process cleanup.
- Added documented AppBar work-area negotiation for one secondary monitor and a tested multi-secondary safety fallback that prevents Windows from stacking multiple work-area insets on one display.
- Audited notification-area mirroring and explicitly rejected Explorer injection or undocumented toolbar enumeration; full third-party tray mirroring remains unimplemented.

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
