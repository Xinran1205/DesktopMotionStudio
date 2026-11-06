# F11 Verification Evidence

**Date** 2026-11-06 · **Host** Windows 11 x64 build 26200, local NTFS, actual DPI120 (125%) · **SDK** 10.0.403, Desktop runtime 10.0.14

| Release suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Core with real WindowHost target and published MotionHost | 425 | 0 | 1 |
| Studio WPF and deterministic UI | 67 | 0 | 0 |
| F11 published Studio, actual keyboard and Windows Shell | 25 | 0 | 0 |
| F08 published UI regression | 16 | 0 | 0 |
| F09 published UI regression | 15 | 0 | 0 |
| Launcher diagnostic boundary mapping | 3 | 0 | 0 |

The optional packaged-instance case was skipped because DMS_F05_TEST_AUMID was unset. Release builds and analyzers passed with zero warnings and errors. Affected C# project whitespace and Git whitespace checks passed.

## Preferences and accessibility

The real published Studio saved dark and mute by keyboard and retained both on restart. Cancel preserved exact profile and backup bytes. WPF tests exercised semantic palette changes and preference conflict feedback. Core checks covered schema1 backup, schema2 no-op, malformed or future files, and contention.

The initial platform probe followed an actual Windows Light → Dark → Light change and exposed a lost explicit palette on contrast exit. The final probe linked production StudioTheme in two independent processes. The System probe reported text/background #E4000000/#FFF3F3F3 → #FF3D3D3D/#FFFFFAEF → #E4000000/#FFF3F3F3. Explicit Dark reported #FFFFFFFF/#FF202020 → the same Windows contrast palette → #FFFFFFFF/#FF202020. Windows was restored to its original Light mode with no contrast theme.

## Diagnostics and recovery

Real Shell activation ran one target and an owned renderer where applicable. A missing host, disabled animation, missing binding, held diagnostic gate, unknown log schema and throwing log sink exercised distinct results. Each completed activation checked that its isolated renderer had exited. Core exercised the 128-entry and 64 KiB committed bounds, interrupted handover, contention and blocked reparse traversal.

Published Studio opened diagnostics through Alt+I, distinguished renderer fallback and missing binding, and used Alt+R to repair the selected missing entry while preserving the profile and foreign link. WPF tests covered conflict, removed selection, copy, clipboard error and guidance. The copy test captured the same formatter's output through an injected clipboard callback. Fixture strings were absent from raw records and the copied summary.

Shell acceptance compiles the production Launcher entry source with an isolated profile provider and trace hook. The shipping apphost separately rejects an unexpected switch with exit 2. The keyboard harness initially encountered Windows foreground restrictions and passed after explicit activation of identified fixture windows. The actual user profile, desktop entries and clipboard were outside test scope.

The framework-dependent publish is retained at artifacts/studio-f11 with hashes and logs in artifacts/checkpoints/f11-20261106. F12 owns install, upgrade and uninstall acceptance.
