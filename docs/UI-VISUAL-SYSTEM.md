# UI visual system

The application visual system separates the internal observing-eye artwork from Windows iconography. The detailed eye appears only inside the WPF application; taskbar and tray assets use a monitor-topology symbol that remains readable at small sizes.

## Resources

- `src/SyncWallpaper.App/MainWindow.xaml`: glass window shell, state cards and internal eye artwork.
- `EyeGradient`: cyan-to-blue-violet gradient for the internal UI.
- `assets/syncwallpaper-icon.svg` and `assets/AppIcon.ico`: taskbar application icon resources.
- `assets/TrayIcons/tray_normal.*`, `tray_paused.*`, `tray_recognizing.*` and `tray_error.*`: tray states.
- `TrayIconRenderer` and `TrayIconState`: runtime state mapping and static ICO rendering.

The taskbar icon uses a cyan observation ring, desktop monitor, laptop and status point. It contains no eye, nebula, text or HUD scale marks. Tray ICOs include 16, 20, 24, 32, 48 and 64 pixel frames.

## Tray states

| State | Visual signal | Trigger |
|---|---|---|
| Normal monitoring | Complete cyan ring, monitor/laptop and status point | Background monitor is healthy |
| Automatic switching paused | Gray-blue topology with pause bars | Auto-match disabled, verification mode or safe mode |
| Recognizing | Bright cyan scanning arc and point | Stable topology is being matched/applied |
| Ambiguous or error | Red broken ring, uncertain display and warning triangle | Ambiguous/no match or failed transaction |

Color is not the only signal: pause uses two vertical bars, recognition uses a scanning arc and errors use a dashed display plus warning triangle.

## Performance and DPI

Windows chooses the nearest tray frame for 100%, 125%, 150% and 200% scaling. The taskbar icon includes a 256 pixel resource. Icons are regenerated only when state changes; no animation thread or continuous high-frame-rate renderer remains after the window is hidden.
