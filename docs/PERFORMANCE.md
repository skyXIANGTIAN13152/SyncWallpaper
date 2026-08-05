# Performance and resource accounting

Diagnostics records Working Set, Private Bytes, handle count, threads, GDI/USER objects, CPU seconds, module PID and startup time. Resource samples are local and are not uploaded.

The event path is notification driven. There is no high-frequency topology polling: a signal schedules a bounded stabilization window (2 seconds initial delay, 250 ms sample interval, 10 second maximum). Identical signatures are deduplicated.

The repository test suite exercises a 10,000-signal virtual burst and bounded cancellation. `SyncWallpaper.Diagnostics soak` samples the core and a self-created host; it does not change the real desktop. A six-hour soak, 24-hour equivalent and mixed-DPI hardware run remain manual evidence rather than a claim.

Compare Lightweight, Standard, Full and Custom modes using the same login session and monitor topology. Record UI closed and UI open separately; Taskbar/Shell/Remote/Online processes should be absent in Lightweight.

## RC1 measured sample

The final package read-only snapshot observed 3 displays and an Explorer process. The self-contained Diagnostics process itself was 37,654,528 bytes Working Set at that instant.

The final real-time sample ran from 2026-08-05T03:13:49Z to 03:14:50Z: 60.6 seconds of monotonic active time, 13 samples at 5 seconds, 0 seconds excluded for sleep, and qualified12Hour=false. Self Working Set was 34,770,944 / 47,716,825 / 52,678,656 bytes (min/average/max), Private Bytes 9,773,056 / 12,441,127 / 15,167,488 bytes, handles 284 / 350 / 361, CPU seconds 0.08 / 0.37 / 0.66. The test host stayed isolated and no display or wallpaper mutation was performed.

The 100,000-event accelerated run completed in 174.5 ms with one stable emission. It is a concurrency/resource test, not elapsed wall-clock soak evidence. UI-open, mixed-DPI, 2/8/12-hour and real hotplug measurements remain Not Run.
