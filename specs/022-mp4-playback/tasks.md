# Tasks — F21 production MP4 playback

## US1 · Preview and launch a bundled video

- [x] T001 [US1] Specify the versioned video asset/request contract, local trust boundary, completion states and media timing budgets in Core and the plan.
- [x] T002 [US1] Build the independent video worker with first-frame gating, fit/fill, fade, duration and effective mute. Test 6 and 10 seconds, Escape, invalid media and shutdown.
- [x] T003 [US1] Route packaged video preview and pre-launch to the new worker. Keep the existing code-work route and its compatibility tests. Cover per-app gating, already-running reuse and native fallback.
- [x] T004 [US1] Package and verify the player in a real isolated installation. Check audio, Windows reduced motion, startup deadline, visibility timing, both launch routes and code-work regression. Record review and convergence.
