# Monitor identity

## Collection

1. `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` enumerates active display paths.
2. `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_TARGET_DEVICE_NAME)` reads monitor device path, EDID manufacturer/product codes, `outputTechnology`, `connectorInstance` and `targetId`.
3. Source/target mode data supplies a temporary display label, source/target IDs, resolution, refresh rate, orientation and desktop coordinates.
4. WMI `WmiMonitorID` adds `InstanceName`, `SerialNumberID`, `ManufacturerName`, `ProductCodeID` and the friendly name.
5. SetupAPI Container ID, native resolution, DPI, HDR/color, primary status and internal status are additional read-only evidence.

## Priority

1. Manufacturer + product code + valid EDID serial.
2. Monitor device path.
3. WMI `InstanceName`.
4. Adapter ID + target ID + output technology + connector instance.
5. Container ID as supporting evidence.
6. Resolution + orientation + desktop position as final geometric evidence.

Serials take precedence over connectors. If identical displays swap connectors, valid serials keep identity with the physical display. Without serials, unresolved ambiguity requires A/B/C confirmation.

`\\.\DISPLAY1`, `DISPLAY2` and similar Windows labels are temporary session hints and are never saved as permanent identity.

## Orientation and flip

Raw rotation comes from `DISPLAYCONFIG_PATH_TARGET_INFO.rotation`. The UI follows Windows Settings semantics:

- 1: Landscape · Normal
- 2: Portrait · Normal
- 3: Landscape · Flipped
- 4: Portrait · Flipped

The internal numeric value is not shown in the UI.

## Read-only guarantee

The wallpaper-only edition still displays complete monitor information. Resolution, refresh rate, HDR, DPI and orientation remain available for matching explanations; only the ability to modify those settings was removed.
