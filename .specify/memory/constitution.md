# Project Constitution — Desktop Motion Studio

**Version:** 1.0.0 · **Ratified:** 2026-11-03

## Principles

1. **The target application wins.** Animation is optional decoration. Launch requests are never cancelled by skip, rendering failure, timeout, or handoff failure; fail open to native launch and clean up the overlay.
2. **Least privilege and local first.** No elevation, injection, global hooks, drivers, network listeners, telemetry, or modification of target application files/data. Persist only what the feature needs under the current user's profile.
3. **Explicit ownership.** Manage only artifacts created by this product and verified as still owned by it. Never silently replace a user's shortcut or overwrite newer/unknown configuration.
4. **Stable boundaries.** Keep Studio, launcher, renderer, app adapters, scene definitions, persistence, and shell integration behind testable contracts. A scene never selects or hard-codes its target application.
5. **Capability-based compatibility.** Detect runtime/API capabilities and app identity; do not infer compatibility from product names or build success. Unsupported enhancement falls back to a documented safe path.
6. **Small, reviewable increments.** Each feature has its own spec, plan, tasks, acceptance checks, and convergence review. Do not implement dependent features before their contracts are settled.
7. **Quality is part of delivery.** Use nullable reference types, analyzers, deterministic dependency versions, structured error codes, bounded/redacted logs, and automated tests at the narrowest practical layer. Never weaken tests to make a change pass.
8. **Version every contract.** Product, persisted configuration, scene manifest, and IPC protocol have independent versions. Unknown newer data is preserved and rejected safely; migrations are deterministic, idempotent, and backed up.
9. **Reproducible dependencies and licensing.** Prefer supported stable dependencies, pin versions centrally, review transitive/native dependencies, and track code and media rights separately.
10. **Git is the recovery path.** Keep changes focused, preserve user changes, use descriptive commits at verified milestones, and never rewrite shared history without explicit instruction.

## Architecture constraints

- Windows 11 x64 is the initial target; exact supported OS builds must be validated against current Microsoft lifecycle and real test evidence before release claims.
- Studio is not required to remain running for managed launch shortcuts to work.
- Launcher responsibilities remain small and fail-open; rendering runs out of process.
- No arbitrary user-provided scene code is loaded in V1.
- UI and OS-specific behavior sit behind interfaces so core rules can be tested without a desktop session.

## Testing strategy

- Unit tests cover domain rules, serialization, migrations, shortcut ownership decisions, and launch-state decisions.
- Contract/integration tests cover manifest and IPC versioning, persistence atomicity, adapter results, and process coordination.
- Windows interactive acceptance tests cover focus, DPI, multi-monitor placement, skip, renderer crashes, and handoff. Headless CI is not evidence for desktop composition behavior.
- Every feature's spec acceptance scenarios map to tests or an explicitly recorded manual evidence item.

## Definition of Done

- Spec, plan, tasks, and implementation agree; acceptance criteria have evidence.
- Build, formatting/static analysis, relevant automated tests, and platform checks pass; failures are fixed rather than hidden.
- Error paths fail safely, logs are bounded and do not capture screen content or secrets, and compatibility behavior is explicit.
- User data and unrelated shortcuts remain untouched; docs, version history, and licensing are updated where needed.
- Changes are reviewed and committed at a verified milestone; known gaps are listed honestly.

## Amendment history

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2026-11-03 | Initial constitution, grounded in PRD/TDD V1.0 and current empty-codebase state. |
