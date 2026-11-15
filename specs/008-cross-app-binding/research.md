# F07 Research and Decisions

## Repository concurrency

**Decision:** Add a synchronous Update transaction and path-scoped named mutex shared with Save.
**Rationale:** Existing static SaveGate protects only one process and cannot protect a read performed before Save. Atomic file replacement alone prevents partial JSON, not lost read-modify-write updates.
**Alternatives:** A database/new dependency is excessive for bounded local state; optimistic retries would need persistent revision fields and migration. Save remains explicit snapshot replacement.

## Target persistence boundary

**Decision:** Retain schema 1 and require an explicit typed target provider based on the current ApplicationProfile. F08 owns typed target persistence.
**Rationale:** F07 only changes existing binding fields. Inferring target kinds from a free-form string is unsafe and prematurely migrating unrelated app metadata would expand scope.
**Alternatives:** schema 2 now (unnecessary coupling); guessing .lnk/! conventions (rejected).

## Eligibility versus installed applications

**Decision:** Use F04 validation and document Ready as a current eligibility snapshot. AUMID validation is structural; activation remains authoritative.
**Rationale:** Existing F04 resolver does not establish that a packaged app is installed. No additional compatibility promise is introduced.

## Planning review

The Spec Kit planning research agent independently inspected the repository/F03/F04 boundaries and agreed with these choices. It highlighted stale Save replacement, mutable list inputs, typed provider freshness, AUMID eligibility, stable BindingId, and preserving unavailable references; the plan/contracts include all six constraints.
