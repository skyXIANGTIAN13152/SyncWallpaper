# Real hardware verification checklist

This checklist is intentionally manual. Do not run it unattended and do not automate Explorer termination, display mode changes, audio switching or sleep.

- [ ] Capture a read-only baseline snapshot and note Windows build, GPU driver and connected ports.
- [ ] Confirm internal panel and each monitor expose monitorDevicePath, adapter/target and geometry.
- [ ] Disconnect all external displays; verify Laptop Only applies without changing the Windows display mode.
- [ ] Connect HDMI, DP and USB-C combinations one at a time; wait for the 2-second debounce and two equal samples.
- [ ] Swap two identical monitors between ports. With serials, identity must follow the monitor; without serials the app must request confirmation.
- [ ] Rotate a portrait display and verify geometry/rotation evidence without permanent number matching.
- [ ] Lock/unlock and suspend/resume manually; compare before/after snapshots and wallpaper paths.
- [ ] Restart Explorer only after saving work and confirming the risk dialog; verify the core host remains alive.
- [ ] Close every optional module and verify process exit, hook/COM cleanup and stable handle count.
- [ ] Export a sanitized diagnostic report and inspect it for personal paths before sharing.

Record each result, timestamp, driver version and whether the step was skipped. Beta claims only include steps actually completed on hardware.
