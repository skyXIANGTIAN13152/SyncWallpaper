# Wallpaper profile matching

Each profile stores a name, expected monitor count, priority, logical roles, stable monitor fingerprints, wallpaper assets and fit modes. Roles may be Laptop, Landscape, Portrait or a custom name.

## Automatic matching

1. Filter profiles by the number of active monitors.
2. Build a one-to-one assignment between profile roles and physical monitors.
3. Score EDID serials, device paths, instance names and hardware topology in layers.
4. Mark a profile **Matched** and allow automatic application only when the assignment is unique, identity evidence is reliable and every role has a valid wallpaper.
5. Mark it **Unmatched** when monitor count, identity, roles or wallpapers are incomplete, evidence is weak or assignments tie.

Blank profiles can be created and edited at any time, but they do not participate in automatic matching. **Apply selected profile** runs that profile's wallpaper transaction only; it does not change profile priority, edit selection, modification time or the current automatic result.

Multiple profiles may describe the same topology. Priority and stable ordering choose one profile; matching never relabels another profile as current or enabled.

## Topology changes

Sign-in, HDMI/DP/USB-C hot-plug, orientation/layout changes, sleep/wake and Explorer recovery trigger detection. Once the topology is stable, existing profiles are matched and a complete match is applied immediately.
