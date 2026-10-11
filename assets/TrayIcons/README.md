# Hollow display icon set

The notification-area and application icons share the approved landscape/portrait display silhouette. Backgrounds and screen interiors are transparent: no ring, backing plate, halo, or continuous animation. Small frames simplify the overlap so both screens remain readable at 16 px.

The palette uses antique gold `#D2B47A`, warm white `#F3EBDC`, quiet sage `#A5B5A9`, and soft copper `#D5A085`, designed alongside the ink-green `#0D1C19` interface.

- `tray_normal`: antique-gold screens and a check mark for specific matching.
- `tray_generic`: antique-gold screens and a hollow warm-white diamond for generic matching.
- `tray_unmatched`: sage screens and a horizontal dash for no applicable match.
- `tray_paused`: sage screens and two vertical pause strokes.
- `tray_error`: soft-copper screens and an exclamation mark.
- `tray_recognizing`: antique-gold screens and three static dots during detection.

The runtime renderer and generated tray resources use 16/20/24/32/48/64 px frames. AppIcon.ico also includes a 256 px frame; the larger application artwork follows `../syncwallpaper-icon.svg`, while its small frames simplify the overlapping outlines.

Regenerate the complete icon set:

```powershell
./tools/BuildTrayIconAssets.ps1
```

Regenerate only selected tray resources:

```powershell
./tools/BuildTrayIconAssets.ps1 -States generic,unmatched -SkipTaskbarAssets
```

The generator renders native vector geometry and does not read or modify legacy taskbar-reference bitmaps. SVG tray files are regenerated with the same geometry; raster frames enforce a minimum 1.1 px stroke for small-size readability.
