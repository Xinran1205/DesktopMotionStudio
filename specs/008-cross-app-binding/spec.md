# Feature Specification: Cross-application scene binding

**Feature Branch**: `codex/f07-cross-app-binding`
**Created**: 2026-11-03 · **Revised**: 2026-11-04
**Status**: Complete · **Version**: 1.0.0
**Input**: PRD cross-application reuse, TDD binding rules, F02/F03/F04 contracts.

## Goal

Let a registered application select one complete, available built-in scene, while the same scene can serve multiple registered compatible applications. Resolve each binding to that application's own typed target and current scene; scene content never chooses the target.

## User Scenarios & Testing

### User Story 1 - Bind and replace a scene (Priority: P1)

An application owner assigns the same scene to two applications and later replaces only one application's choice.

**Independent Test**: Register two fixtures, bind both to one scene, resolve each independently, replace one scene and reload.

**Acceptance Scenarios**:

1. **Given** two registered applications with valid targets and one available scene, **When** both are bound, **Then** both persist with separate stable binding IDs, share only the scene ID, and resolve to their respective typed targets.
2. **Given** an existing binding, **When** its scene is replaced, **Then** its binding ID remains and the other binding, profiles, settings, and scene assets are unchanged.
3. **Given** an enabled identical binding, **When** assign is repeated, **Then** it reports unchanged and does not rewrite state or rotate backup.
4. **Given** two edits to different applications from separate processes, **When** they overlap, **Then** every successful edit survives reload.

### User Story 2 - Disable, enable, and remove (Priority: P1)

The owner disables an animation without forgetting the scene, reenables it, or removes the binding.

**Independent Test**: Disable and reload, resolve Disabled, reenable after validation, remove twice, compare unrelated state.

**Acceptance Scenarios**:

1. **Given** a binding, **When** disabled, **Then** its ID and scene remain stored and resolution returns Disabled without a visual launch context.
2. **Given** a disabled binding whose target or scene is unavailable, **When** enabled, **Then** the specific status is returned and persistent bytes remain unchanged.
3. **Given** a registered application, **When** remove is repeated, **Then** the first removal is applied, later removals are unchanged, and no other binding is removed.

### User Story 3 - Diagnose unavailable bindings safely (Priority: P1)

The owner distinguishes no binding, disabled, missing registration, invalid target, unavailable scene, and unreadable configuration.

**Independent Test**: Remove target/scene fixtures after binding, corrupt a separate state fixture, verify typed status and preserved bytes.

**Acceptance Scenarios**:

1. **Given** a target or scene becomes unavailable, **When** resolved, **Then** status is explicit and the stored binding remains available for repair.
2. **Given** unsupported, oversized, or corrupt state, **When** a command runs, **Then** it returns storage error and preserves the original file and backup.
3. **Given** two bindings sharing a scene, **When** their resolved typed targets pass through existing launch/window coordination, **Then** each returns its own process/window identity.

### Edge Cases

- Application and binding IDs use existing case-insensitive identity rules. Empty, whitespace, or IDs over 128 characters are rejected; scene IDs use manifest validation.
- Missing/duplicate/invalid scenes, missing assets, unavailable roots, and provider exceptions return unavailable status.
- Disable/remove work when target or scene is unavailable. Assign and enable require current eligibility.
- Resolution is a current validation snapshot. Packaged-app identity validation does not promise installation or activation; F04 activation remains authoritative.
- Lock acquisition is bounded to two seconds, returning storage Busy on contention; abandoned locks require revalidation.
- Future application removal must remove its binding in the same transaction; F07 never silently discards an invalid reference.

## Requirements

### Functional Requirements

- **FR-001**: Store stable appId/sceneId references, one binding per application, and allow one scene on multiple applications without copying or changing content.
- **FR-002**: A changed assign/replace requires registered application, validated typed upstream target, and one available built-in scene. Replacement retains binding ID; new bindings receive unique IDs.
- **FR-003**: Identical assign, enable/disable, and repeated remove commands report unchanged without rewriting state or backup.
- **FR-004**: Disable preserves selection; enable revalidates target and scene. Remove affects only the selected application.
- **FR-005**: Resolve returns Ready, Unbound, Disabled, ApplicationMissing, TargetUnavailable, SceneUnavailable, InvalidRequest, or StorageError. Only Ready exposes a target-and-scene context.
- **FR-006**: Resolve is read-only; binding commands do not launch processes, create shortcuts, change target applications or scene assets, or access the network.
- **FR-007**: Mutations read current valid state and atomically save in one shared transaction across repository instances/processes, preserving successful unrelated binding edits.
- **FR-008**: Preserve profiles/settings/unrelated bindings. Unsupported/corrupt/oversized state, provider failure, lock contention, and write failure return stable diagnostics and preserve the last valid state.
- **FR-009**: Typed target identity comes from the application registry boundary and existing launch validation. Never infer executable/shortcut/packaged types from free-form strings or scene content.
- **FR-010**: Preserve current state-schema compatibility and F00–F06 behavior; ordinary-user, local-only operation and backup/atomic replacement rules remain in force.

### Key Entities

- **Application binding**: stable binding ID, registered application ID, stable scene ID, enabled flag.
- **Mutation result**: Applied/Unchanged or stable rejection, optional current binding and storage error.
- **Resolution**: status, profile/binding for diagnosis, target/scene context only when Ready.

### Business Rules, Inputs & Outputs

- **Inputs**: application/scene IDs, enabled choice, registered profile, trusted catalog, explicit typed target provider, versioned local state.
- **Outputs**: persistent binding mutation or read-only resolution snapshot with stable status; no process side effects.
- Disabled takes precedence over target/scene unavailability. Resolution uses current manifest name/version under the stable scene ID without rewriting the binding.
- No-op commands are recognized after registration lookup and before external validation; Resolve diagnoses external availability independently.
- Every editing client must use the transaction operation. Whole-state snapshot replacement is explicit; a stale snapshot replacement is not a merge.

## Success Criteria

- **SC-001**: One scene binds to two fixtures, survives reload, and resolves to distinct target/process/window identities through existing coordination.
- **SC-002**: Replacing/disabling/removing one binding preserves other bindings, profiles, settings, and asset bytes.
- **SC-003**: No-op commands preserve state/backup bytes; two process edits both survive reload. Lock acquisition is bounded to two seconds plus scheduling overhead.
- **SC-004**: Every listed rejection/unavailability class has an automated check; F00–F06 checks continue to pass.

## Assumptions and Boundaries

- F07 delivers binding domain and persistence transactions. Discovery/editing UI is F08/F09; shortcut creation and installed-launcher dispatch are F10.
- Application target metadata belongs to the application registry. F07 requires an explicit typed provider; F08 will version its persistent representation. Binding operations add no fields or schema migration.
- F02/F03/F04 are domain dependencies; F05/F06 provide integration evidence. Licensed scene media remains F13/F14.

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline. |
| 0.2.0 | 2026-11-04 | Define binding lifecycle, atomic/no-op semantics, typed target boundary, availability states, and cross-app validation. |
| 1.0.0 | 2026-11-04 | Implement and verify binding lifecycle/transactions; close two independent-review findings; 38 F07 checks and real two-target handoff passed. |

F09 compatibility amendment (2026-11-05): BindingService accepts an optional scene eligibility predicate after its existing parameters. Default behavior retains this F07 contract. Studio uses current validated playback inside transactions for new binding/enable/resolve; existing identical binding no-op and disable/remove preserve their original semantics. No persisted schema changed.
