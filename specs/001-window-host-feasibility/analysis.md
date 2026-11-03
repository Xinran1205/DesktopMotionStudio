# Spec–Plan–Tasks consistency check

**Date:** 2026-11-03 · **Status:** Converged for bounded F00 POC

| Spec requirement | Planned design | Task | Result |
|---|---|---|---|
| FR-001 mode/monitor parsing | CLI selection and work-area geometry | T003 | Covered |
| FR-002 visual and exit | Prototype form with Esc/button | T004, T006 | Covered |
| FR-003 ordinary-user top-level | Windows Forms TopMost; no hooks | T004, T008 | Covered |
| FR-004 transparent region | TransparencyKey; interactive observation | T004, T006–T007 | Pass on current two-screen desktop |
| FR-005 cleanup | Dispose form/process exit | T004, T006 | Pass for Esc and Close paths |
| FR-006 evidence distinction | Separate build/desktop results | T005–T008 | Covered |

## Independent review pass

- Read the implementation against every F00 requirement after runtime checks. Corrected two issues before convergence: primary fallback now uses `Screen.PrimaryScreen`; card layout is recalculated after the form is shown so high DPI does not clip the title.
- No global hook, elevation, target-app mutation, persistence, network call, or unmanaged resource handle is present in the prototype.
- `ShowInTaskbar = true` is deliberate for POC observability. Production MotionHost taskbar behavior must be decided in F06.
- The current `Screen.AllScreens` index is an ephemeral selector suitable only for this POC. A production contract should use a stable monitor identity or target-window monitor.
- 100% and 150% DPI, device removal, GPU rendering, and real target-window focus remain outside F00's bounded feasibility gate and must be tracked by later acceptance.

**Convergence:** The spec, plan, tasks, implementation, build, and available real-desktop evidence agree. F00 may close as a feasibility spike; this does not prove a shippable renderer.
