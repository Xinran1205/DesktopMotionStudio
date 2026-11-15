# F12 Spec Kit Analysis

The requirements checklist passes 16/16. All 7 FR, 9 AC and 3 SC keys are traced to the 12 tasks. Setup owns isolated program generations and registration. F10 remains the authority for desktop ownership. No configuration migration or new IPC dependency is introduced.

| Analysis item | Disposition |
|---|---|
| A successful Studio self-contained publish may conceal a framework-dependent Launcher | T002 proves all three apphosts independently. T003 assembles only independently published inputs. |
| Per-user MSI rollback did not restore registration | T001 reproduced it. T004 gates new setup on an isolated generation-selector and recovery proof. T006 checks the real package. |
| Uninstall cleanup during upgrade would break AC-04 | T008 guards ordinary removal and covers the upgrade skip. |
| F10 Remove clears some missing receipts | T007 adds a narrower verified cleanup path and preserves unresolved evidence. |
| This workstation has a filtered administrator token | Record the non-elevated token and runtime actually loaded. Use installed-product and isolated evidence together under the revised acceptance. |

The analysis allows the payload build and the new transaction proof to proceed. Production setup is gated by both.

The review increment traces FR-008 and SC-004 to T013 and FR-009 and SC-005 to T014. The entry and transaction gates have one acquisition order. Maintenance runs after commit and verifies ownership before reclaiming a retired generation.

FR-010 and SC-006 trace to T015. Unpublished failure cleanup checks recorded identities and operates relative to retained handles. Claimed upgrade stages preserve their durable receipt and remain recoverable.

FR-011 and SC-007 trace to T016. The upgrade intent binds both stable-entry digests to the package version and ownership record. The fixed paths and link bytes remain the external contract. Entry work begins after the selector commit. The same Setup can resume an interrupted entry transition. The install transaction pins the verified Launcher directory before changing the selector.

Spec 0.5.0 changes the acceptance environment, with T009–T011 retaining their link to SC-001–SC-003. The workstation link verifies installed dispatch and fallback, while the isolated checks verify shortcut creation and ownership, upgrade faults and retained data. The actual Windows token and environment limits are recorded in the evidence.
