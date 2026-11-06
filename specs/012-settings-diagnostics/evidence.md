# F11 Verification Evidence

## Preferences Core, 2026-11-06

The new preference checks first failed at build because the contract was absent. After implementation, Core Release built with zero warnings and errors and `--f11-settings-checks` reported 12 PASS. Cases include defaults without writes, explicit dark/mute and light/unmute changes, exact no-op and conflict preservation, unchanged application and binding records, legacy migration with the original backup, future/malformed state and structured Busy.

## Fluent platform proof

Run `dotnet run --project specs/012-settings-diagnostics/probes/theme -c Release`. On .NET Windows Desktop 10.0.14, the same WPF TextBlock switched foreground from #E4000000 (Light) to #FFFFFFFF (Dark), then #E4000000 (System on this desktop) and #FF000000 (explicit contrast palette). All four dictionaries exposed the required semantic brush and control styles. This is a live resource lookup and switch proof. Actual OS theme toggles are tracked in T010.

## T004 — Studio preferences and palette

The Settings dialog applies the saved theme to existing windows, persists audio preference and preserves bytes on cancel. Three WPF checks passed for dark/mute and live resource change, exact cancel behavior, and system/unmute reopening. Existing Studio views use Fluent semantic resources.

The first actual F08 run could not open Add with Alt+A. The pinned WPF AccentButtonStyle does not recognize string access keys by default. Studio supplies an AccessText content template for accent buttons, preserving their content and Fluent styling. The unchanged real Windows test then passed. [Pinned WPF template source](https://raw.githubusercontent.com/dotnet/wpf/v10.0.14/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/Button.xaml)

Final settings increment ran 407 Core PASS, one documented optional packaged-instance SKIP, 60 Studio PASS, and published actual F08/F09/F10 UI 16/15/12 PASS. Zero failures. Release publish and affected builds passed. Final system preference toggles and diagnostics remain T010.
