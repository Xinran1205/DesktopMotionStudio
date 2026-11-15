# Tasks: 本地状态仓库

**Input**: [spec.md](spec.md), [plan.md](plan.md)

- [x] T001 Define immutable state, app profile, binding, and settings models.
- [x] T002 Implement state validation and stable repository result codes.
- [x] T003 Implement default load, schema v1 read, sibling temp write, flush, replace, and backup rotation.
- [x] T004 Add isolated filesystem contract checks for round-trip, bad/future files, reference checks, backup, and failure preservation.
- [x] T005 Run checks/build/format, review and convergence record.
- [x] T006 Add F07 synchronous Update with a shared path-scoped Save/Update mutex, no-op byte/backup preservation, Busy deadline, abandoned-owner recovery, and real cross-process mutation checks; schema remains 1. Evidence is recorded in `../008-cross-app-binding/evidence.md`.
