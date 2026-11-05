# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding`

F08 is committed and converged at `90b70c9`. F09 Specify/Clarify/Plan/Tasks/Analyze are complete:3 user stories,12 FRs,5 SCs,24 tasks,16/16 requirements-quality items. No owner clarification is pending. Research and planning-review decisions are recorded in research/contracts and ADR-004.

T001/T002 are complete with isolated code, actual desktop render observations,7 drawing checks and6 ownership checks. Read evidence.md before making any completion claim. These experiments do not implement the production renderer/session or Studio library UI.

Next execute T003 contract tests, then T004/T005 safety/readers. Preserve scene1/state2, optional presentation fallback and explicit playback/runtime support. Production ownership must use creation-time job-list association, retain/resume native initial-thread handles safely, and enforce a parent non-UI deadline; the POC intentionally never resumes its atomic worker. Entry257 fails the entire library instead of truncating.

Follow dependency gates in tasks.md. No F10 work begins until F09 finishes actual shared launch/preview integration, failures/cleanup, reduced-motion/scaling evidence, independent review and convergence. Use the pinned SDK `C:\Users\AAA\.dotnet\dotnet.exe` with `DOTNET_ROOT=C:\Users\AAA\.dotnet`. All desktop acceptance uses isolated state/catalog/target fixtures.
