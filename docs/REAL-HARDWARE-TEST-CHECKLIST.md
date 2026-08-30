# Physical hardware test checklist

- [ ] Confirm monitor count, model, resolution, refresh rate, DPI, HDR, orientation/flip and connector after startup.
- [ ] Confirm the internal display and every external display have an explainable stable identity source.
- [ ] Connect only the laptop panel and confirm the Laptop profile matches and applies automatically.
- [ ] Connect HDMI, DP and USB-C combinations one at a time, wait for topology stability and inspect wallpapers.
- [ ] Swap connectors on identical monitors: valid serials must follow the physical monitor; unresolved serial-less ambiguity must request confirmation.
- [ ] Rotate a portrait display and confirm the UI follows Windows Normal/Flipped semantics.
- [ ] Manually apply a profile and confirm automatic selection and profile metadata do not change.
- [ ] Sign in again and confirm the matching profile applies without another hot-plug event.
- [ ] Sleep/wake and lock/unlock the system; confirm monitoring and wallpapers recover.
- [ ] Restart Explorer while preserving work; confirm the process survives and verifies wallpapers again.
- [ ] After repeated hot-plug cycles, confirm Working Set, Private Bytes, handles and CPU do not grow continuously.
- [ ] Export a redacted report and review it manually before sharing.

All steps observe or apply wallpapers only; they do not modify Windows display parameters.
