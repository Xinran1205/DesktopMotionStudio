# F12 Spec Kit Analysis

The requirements checklist passes 16/16. All 7 FR, 9 AC and 3 SC keys are traced to the 12 tasks. Setup owns isolated program generations and registration. F10 remains the authority for desktop ownership. No configuration migration or new IPC dependency is introduced.

| Analysis item | Disposition |
|---|---|
| A successful Studio self-contained publish may conceal a framework-dependent Launcher | T002 proves all three apphosts independently. T003 assembles only independently published inputs. |
| Per-user MSI rollback did not restore registration | T001 reproduced it. T004 gates new setup on an isolated generation-selector and recovery proof. T006 checks the real package. |
| Uninstall cleanup during upgrade would break AC-04 | T008 guards ordinary removal and covers the upgrade skip. |
| F10 Remove clears some missing receipts | T007 adds a narrower verified cleanup path and preserves unresolved evidence. |
| This workstation has a filtered administrator token | Treat local proof separately. T009–T011 remain clean-account release gates. |

The analysis allows the payload build and the new transaction proof to proceed. Production setup is gated by both.
