# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding` · **Status:** Complete

F09 has31/31 tasks completed with evidence. Requirements checklist16/16 passes. Analyze maps12 FR+5 SC keys with100% coverage; independent reviews and Converge report no outstanding required gaps. See review.md and evidence.md for scope.

Verified:321 Core checks,53 Studio checks,10 production drawing checks; F08 external UI16; final published F09 UI16 and shipped-catalog2. One optional packaged-instance integration is explicitly skipped. Actual100/125/150% and OS animation Off/On passed; original125%/On restored. Latest real owned-thread crash/hang suite15 passed after a test-only ownership correction. Actual scene handoff240ms and hung-UI cleanup1518ms.

Delivered library/cover/details, target-free shared-renderer preview/Stop, latest-state binding/picker/replacement/home, safe process ownership/deadlines and UI intent ordering. State2/manifest1 retained. Development content is labelled honestly. Framework-dependent publish runs with its own bundled MotionHost; installer and licensed release content remain later features.

Next feature: F10, `specs/011-managed-shortcuts`. Refine its independent spec/plan/tasks and validate shortcut ownership risks before implementation. Do not infer shortcut ownership from filenames or overwrite user shortcuts. Use the pinned SDK and isolated fixture paths.

**User-requested pause — 2026-11-06:** F09 implementation committed as `8182ecf`. F10 has not started. Read [the resume checkpoint](../../docs/RESUME.md) before continuing. A hash-verified local publish and64 verification logs are retained under ignored `artifacts/` paths documented there.
