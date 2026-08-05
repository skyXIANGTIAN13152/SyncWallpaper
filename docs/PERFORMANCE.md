# Performance and resource accounting

Diagnostics records Working Set, Private Bytes, handle count, threads, GDI/USER objects, CPU seconds, module PID and startup time. Resource samples are local and are not uploaded.

The event path is notification driven. There is no high-frequency topology polling: a signal schedules a bounded stabilization window (2 seconds initial delay, 250 ms sample interval, 10 second maximum). Identical signatures are deduplicated.

The repository test suite exercises a 10,000-signal virtual burst and bounded cancellation. `SyncWallpaper.Diagnostics soak` samples the core and a self-created host; it does not change the real desktop. A six-hour soak, 24-hour equivalent and mixed-DPI hardware run remain manual evidence rather than a claim.

Compare Lightweight, Standard, Full and Custom modes using the same login session and monitor topology. Record UI closed and UI open separately; Taskbar/Shell/Remote/Online processes should be absent in Lightweight.
