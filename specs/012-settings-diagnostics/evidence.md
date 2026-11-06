# F11 Verification Evidence

## Preferences Core, 2026-11-06

The new preference checks first failed at build because the contract was absent. After implementation, Core Release built with zero warnings and errors and `--f11-settings-checks` reported 12 PASS. Cases include defaults without writes, explicit dark/mute and light/unmute changes, exact no-op and conflict preservation, unchanged application and binding records, legacy migration with the original backup, future/malformed state and structured Busy.

## Fluent platform proof

Run `dotnet run --project specs/012-settings-diagnostics/probes/theme -c Release`. On .NET Windows Desktop 10.0.14, the same WPF TextBlock switched foreground from #E4000000 (Light) to #FFFFFFFF (Dark), then #E4000000 (System on this desktop) and #FF000000 (explicit contrast palette). All four dictionaries exposed the required semantic brush and control styles. This is a live resource lookup and switch proof. Actual OS theme toggles are tracked in T010.
