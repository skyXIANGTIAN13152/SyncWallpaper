# Fault-injection testing

Production uses `NoFaultInjector` and never creates faults intentionally. Tests use `ConfigurableFaultInjector` to verify configuration-write failures, malformed configuration, unwritable logs, wallpaper transaction failures, rollback failures and cancellation.

Fault injection exists only in automated tests. It never changes real display modes, audio, windows, taskbar or Explorer. Wallpaper failure tests verify a clear terminal transaction state and ensure ambiguous or incomplete profiles cannot be applied automatically.
