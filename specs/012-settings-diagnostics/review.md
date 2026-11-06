# F11 Independent Review and Convergence

**Date** 2026-11-06

| Finding | Disposition |
|---|---|
| Malformed managed invocation lacked diagnostics | Capture typed Request/InvalidRequest before reading the profile, retain exit 2, and exclude input text. |
| Renderer cleanup failure and timeout could lose their reason | Preserve successful target/handoff with a separate cleanup reason. Map typed timeout and assert isolated renderer exit in Shell acceptance. |
| A read racing handover could hide a busy writer | Read under the existing exclusive gate and return Busy when held. Compare history after release. |
| App ID case variant hid valid recovery | Match with OrdinalIgnoreCase, verified with app ID app and binding reference APP. |
| Diagnostic window size and recovery hints needed refinement | Clamp to the working area and provide local guidance for typed failures. The copy check verifies guidance stays local. |
| Direct Fluent dictionary replacement could lose the correct palette after contrast | Centralize WPF ThemeMode and verify actual System and Dark contrast transitions with the production controller. Document the scoped framework API exception in research. |

Independent Studio and Launcher/storage reviewers checked the fixes. All material findings are closed. An intentional animation skip does not produce a renderer troubleshooting hint.

| Specification | Evidence |
|---|---|
| FR-001–FR-003, AC-01–AC-03, SC-001 | Preference transactions and byte preservation, WPF semantic palette and conflict, published keyboard/restart/cancel, Windows contrast transitions |
| FR-004–FR-006, AC-04–AC-06, SC-002 | Closed diagnostic types, retention/failure boundaries, privacy, guidance, runtime and protocol status |
| FR-007, AC-07–AC-08, SC-003 | Explicit selected F10 repair, conflict and stale selection, empty/copy/error states, Shell fail-open, F08–F10 regressions |

Convergence covered 7 functional requirements, 8 acceptance criteria, 3 success criteria and the Constitution. The 11 tasks cover implementation and verification. F11 retains profile2 and IPC2, with a separately versioned diagnostic format. The optional packaged-instance skip and isolated Launcher fixture are recorded in [evidence](evidence.md).
