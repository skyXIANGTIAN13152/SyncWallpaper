# Changelog

## 1.2.0-beta.7 — 2026-10-12

First public release in the 1.2 series: generic wallpaper profiles and the Mystery desktop redesign. The earlier 1.2 beta entries below describe local development iterations included in this release.

- Create any number of generic profiles from an empty layout, with Landscape 1/2/3 and Portrait 1/2/3 slots and no fixed slot limit. Prefer safe, complete specific matches; otherwise require an exact count match for each orientation before using a generic profile.
- Number displays by desktop position within each orientation. Preserve slot gaps and retain the current wallpaper at unassigned positions; incompatible layouts leave every wallpaper unchanged.
- Introduce the Mystery theme with deep forest green, antique gold, warm white, static mist and orbital accents. Redesign all seven pages, profile editing, previews and dialogs; retain the existing optional themes.
- Replace app and tray artwork with transparent hollow display outlines, and distinguish generic matching with its own diamond state marker.
- Keep titles, primary actions, search and filters fixed above independently scrolling content. Restore mouse-wheel routing and correct compact icon clipping and minimum-window navigation layout.
- Strengthen display-topology checks and transactional wallpaper rollback. Render all target images before any desktop changes, recover from non-COM I/O failures, and tolerate corrupt preview images.
- Make uninstall preserve profiles, wallpapers and other user data inside the installation directory as well as the fallback data directory.
- Retain existing profile bindings, wallpaper files and preferences during upgrade. Packages contain application files and documentation only.

## 1.2.0-beta.6 — 2026-10-12

- Keep page titles, primary actions and library search controls fixed while each page's content scrolls independently.
- Keep profile actions above the independently scrolling combination list, and preserve native wheel behavior without changing dropdown selections.
- Keep dialog titles and action buttons visible while long content scrolls within the dialog.
- Correct compact brand icon measurement so both screen outlines remain visible at sidebar and list sizes.
- Preserve existing profile definitions, wallpaper files and matching rules.

## 1.2.0-beta.5 — 2026-10-12

- Apply the approved Mystery visual system throughout the desktop app: deep forest green, antique gold and warm white, with a static vector mist and orbital background.
- Rework all seven main pages, wallpaper profile editing and app dialogs around display layouts, real wallpaper thumbnails and clearer matching information.
- Replace the application and tray marks with transparent hollow display outlines; distinguish specific, generic, unmatched, paused and error states by shape.
- Keep profile and wallpaper storage unchanged. Retain wheel routing, exact generic orientation counts and safe specific-profile priority.

## 1.2.0-beta.4 — 2026-10-12

- Require both landscape and portrait counts to match the entire generic profile; mismatched layouts retain every current wallpaper. Safely identified specific profiles remain first.
- Remove the 32-slot limit and allow generic profiles to start from zero. Keep slot indexes and gaps, with each orientation's highest index defining its required screen count.
- Show required and current counts in the editor, profile list and overview, including an explicit count-mismatch state.
- Validate the complete generic layout before and during wallpaper application; reject stale partial matches and restore changed wallpapers if the layout changes or Explorer fails. Explorer recovery redetects displays before matching.
- Read active paths directly from QueryDisplayConfig so remembered wallpaper entries for disconnected monitors cannot invalidate a current combination; verify live desktop bounds through the wallpaper interface.
- Cover orientation mismatches, missing/extra screens, profiles beyond 32 slots, safe-specific priority and transaction rollback with regression tests.

## 1.2.0-beta.3 — 2026-10-12

- Add checkbox hints and expandable matching-rule explanations to the specific profile editor, including reliable identity, exact compatibility conditions, confirmation requirements and generic fallback.
- Document compatible candidate scoring and its safety limits without changing the matching rules.

## 1.2.0-beta.2 — 2026-10-12

- Restore mouse-wheel page scrolling over the wallpaper profile and runtime log lists.
- Scroll the profile editor when the pointer is over a closed monitor, wallpaper or fit dropdown, preserving its selection; keep normal wheel scrolling inside an open dropdown.
- Add WPF regression coverage for upward/downward page scrolling and open/closed dropdown handling.

## 1.2.0-beta.1 — 2026-10-11

- Add blank generic profiles with Landscape 1/2/3 and Portrait 1/2/3 slots, wallpaper import and per-slot fit settings, without binding hardware identities.
- Prefer complete, safely matched specific profiles during automatic matching, then use enabled generic profiles by priority and stable ID. Explicit manual application can select a generic profile directly.
- Number each orientation left to right, then top to bottom, with ordinal device-path ordering for position ties. Include internal displays and preserve saved slot indexes.
- Apply only configured slots present in the current layout and retain current wallpapers on all other screens. Allow up to 32 slots with indexes 1–32; reject duplicate positions and Span mode.
- Add a purple diamond tray state after successful generic application, while detection, paused and error states retain priority.
- Migrate profiles to schema 6 while preserving existing specific profiles. Add matching, partial-transaction, migration and tray regression coverage; no persistent configuration or wallpaper backups are introduced.

## 1.1.0-beta.2 — 2026-09-06

- Add Aurora technology, Graphite dark and Daylight light themes with instant switching and a persisted appearance preference.
- Replace native mixed-color input, dropdown, checkbox and selection surfaces with shared theme-aware templates; retain text contrast in selected, focused and disabled states.
- Use opaque reading surfaces, restrained accent colors, active navigation and consistent spacing while retaining transparent outer window corners.
- Add automated palette contrast, preference migration and live WPF control-theme regression checks.
- Prevent compact text inputs from applying padding twice; regression checks cover complete Chinese glyphs at 100%, 125%, 150% and 200% layout scales.
- Keep Chinese and English update checks within their own release edition, and publish verified packages through draft releases before public promotion.

- Preserve per-device binding IDs and wallpapers when several monitors share a logical role such as Portrait, including repeated manual identification and changed enumeration order.
- Repair duplicate binding IDs in saved profiles and retain existing file selections when reopening identification or profile editing.
- Reject duplicate target devices before applying, preflight every source image, and verify the complete distinct-monitor set before reporting success.
- Refresh monitor discovery before manual reapply and reject conflicting reliable EDID serials even when a connection path is reused.
- Add regression coverage for all 24 four-screen orderings, duplicate bindings, repeated application, incomplete profiles and final wallpaper verification.

## 1.1.0-beta.1 — 2026-08-25

- Refocused SyncWallpaper exclusively on multi-monitor wallpaper identification, profiles, matching and recovery.
- Removed display-configuration mutation, audio, window management, automation, desktop icons, secondary taskbar, Shell, screen saver, remote and online-provider modules and UI.
- Kept the full read-only display inventory, including EDID/WMI identity, device path, GPU path, connector, resolution, refresh rate, DPI, HDR, orientation and desktop geometry.
- Reduced the main UI to Overview, Monitor Identification, Wallpaper Library, Wallpaper Profiles, Logs, Settings and About.
- Fixed startup application so a saved matching topology begins a wallpaper transaction immediately after sign-in without waiting for a later display event.
- Preserved named wallpaper profiles, blank-profile editing, rename/delete/manual apply and automatic topology matching.
- Removed configuration recovery history, deleted-wallpaper backup storage and retired-feature data files.
- Reduced the published product to the App and read-only Diagnostics executables.
- Added wallpaper-only unit/integration coverage; 80 automated tests pass. Real Windows 11 checks cover successful 3/3 application with external displays and 1/1 cold-start application in laptop-only mode.

## 1.0.0-rc.2 — 2026-08-06

- Configured the public GitHub Releases checker for `skyXIANGTIAN13152/SyncWallpaper`.
- Added named wallpaper combinations with save, apply, rename and delete actions.
- Added wallpaper library refresh and per-display wallpaper status reporting.
- Updated taskbar/tray icon assets and control colors.

## 1.0.0-beta.1 — 2026-08-05

- Added layered monitor identity, topology stabilization, per-display wallpaper transactions, A/B/C identification and the initial WPF interface.
