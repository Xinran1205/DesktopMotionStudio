# F15 Specification Analysis

All five functional requirements map to T001 and the home integration in T002. T003 verifies the three success criteria through the same Launcher path used by a managed link. The plan preserves the existing profile and IPC versions. Review before implementation should focus on process ownership, state feedback after selecting another app, and proving no shortcut is created as a side effect.

The installed shim waits for the child only for the dedicated result mode, keeping F12 image verification and the existing Shell forwarding behavior. Both result modes enter the same dispatch pipeline. Studio never makes another dispatch after an unknown or late result.
