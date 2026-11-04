# Feature Specification: My applications

**Feature Branch**: `codex/f08-application-management`
**Created**: 2026-11-03 · **Revised**: 2026-11-04
**Status**: Specified · **Version**: 0.2.0
**Input**: PRD My applications, TDD application discovery/management, F02/F04/F07.

## Goal

Provide a usable Windows Studio home where the owner adds supported applications without editing configuration, searches saved applications, inspects their current binding status, repairs targets, and removes registrations safely. Supply F07 with explicit typed launch targets from persistent application metadata.

## User Scenarios & Testing

### User Story 1 - Add an application without code (Priority: P1)

The owner selects a local executable/shortcut or a discovered packaged application, checks its identity, and adds it to My applications.

**Independent Test**: On an isolated user-state path, add a test executable using the UI, restart Studio, and verify the saved name/typed target and stable app ID.

**Acceptance Scenarios**:

1. **Given** an empty profile, **When** a supported executable or shortcut is selected and confirmed, **Then** validation succeeds before saving and the application appears once with a stable ID and readable name.
2. **Given** a discovered packaged entry, **When** selected, **Then** its explicit package/application identity is saved as that target type, without interpreting a command string.
3. **Given** the same resolved launch definition is already added, **When** added again, **Then** the existing registration is identified without duplicate state or modified bindings. Same display name alone is not a duplicate.
4. **Given** the picker is cancelled or target is invalid, **When** the add flow ends, **Then** no registration is saved and no target is launched.

### User Story 2 - Discover and search locally (Priority: P1)

The owner searches locally discovered launch entries to add one and separately filters the already saved home list.

**Independent Test**: Inject a known discovery set containing duplicate names, invalid entries and errors; verify selection, disambiguation, cancellation and saved-only home behavior.

**Acceptance Scenarios**:

1. **Given** installed launch entries are available, **When** Add application opens, **Then** names are searchable and ambiguous names show enough target identity to choose the intended entry.
2. **Given** discovery fails or returns no entries, **When** the result is displayed, **Then** the owner receives a recoverable status and can still choose a local file.
3. **Given** saved applications and unadded discovery entries, **When** home is searched, **Then** only saved registrations are shown and search changes no data.

### User Story 3 - Inspect, repair, rename, and remove (Priority: P1)

The owner sees current app/binding status, renames or repairs an entry, and removes an obsolete registration.

**Independent Test**: Seed two registrations and bindings, repair/rename one through UI, remove it with confirmation, and reload to verify the other entry is untouched.

**Acceptance Scenarios**:

1. **Given** a saved entry, **When** inspected, **Then** name, icon or fallback, target kind/details, binding scene/status and current target validation status agree with stored data and F07 resolution.
2. **Given** a renamed or repaired entry, **When** saved, **Then** appId and bindingId remain stable and future binding resolution uses the new explicit target.
3. **Given** a removal is confirmed, **When** committed, **Then** only that app and its binding are removed atomically; cancelling does nothing. The target application and its files remain untouched.
4. **Given** an enabled binding, **When** disabled from home, **Then** F07 handles the change and the refreshed status agrees with disk; enabling an unavailable binding displays the rejection and preserves state.

### User Story 4 - Preserve existing configuration (Priority: P1)

Existing schema-1 profiles remain recoverable when typed target metadata is introduced.

**Independent Test**: Load legacy fixtures, migrate on an explicit mutation with backup, restart, and compare IDs, bindings, settings and legacy target text. Test corrupt/future files and transaction contention.

**Acceptance Scenarios**:

1. **Given** a valid older profile with untyped target text, **When** loaded, **Then** registrations, bindings, settings and raw text remain intact; the entry is marked for explicit target confirmation instead of guessing its type.
2. **Given** the owner confirms the target type or edits the profile, **When** the new format is written, **Then** migration is deterministic, backed up and repeatable, preserving stable IDs.
3. **Given** unsupported newer/corrupt/unreadable configuration or Busy storage, **When** editing is attempted, **Then** a clear error is shown and existing bytes are preserved; UI does not display a false success.

### Edge Cases

- Duplicate names, duplicate resolved definitions, missing icons, removed executable/shortcut, structurally valid but uninstalled AUMID.
- Target arguments and working directory belong to explicit executable metadata, never a concatenated shell command. Advanced values stay out of the ordinary add flow.
- Discovery is bounded, cancellable, local-only and does not enumerate arbitrary user documents or launch discovered applications.
- Stale UI edits must reread latest state in a transaction; concurrent unrelated app/binding edits must survive.
- Home remains keyboard operable under system scaling; loading, empty, error and repair states are visible and recoverable.

## Requirements

### Functional Requirements

- **FR-001**: Deliver a Windows desktop Studio with My applications as the default home, showing only saved registrations; add/search/details/repair/rename/remove are available without code or JSON editing.
- **FR-002**: Add supports explicit executable, shortcut and discovered packaged target types through existing F04 validation. Adding does not launch the target or create shortcuts.
- **FR-003**: Persist typed target metadata and provide F07's target-provider boundary using the supplied current profile; never infer target type from legacy text or scene content.
- **FR-004**: Determine duplicate registrations from resolved launch identity plus executable argument vector/working directory, or exact packaged identity. Display names do not define identity; repeated add is a no-op.
- **FR-005**: Local discovery has a bounded completion/cancellation policy, recoverable errors, searchable names, and explicit identity for ambiguous selections; manual file selection remains available.
- **FR-006**: Show a usable icon fallback and current binding/target status. Do not claim managed-shortcut health before an actual shortcut record/check exists. Scene preview/replacement and shortcut creation remain F09/F10.
- **FR-007**: Preserve appId and bindingId on rename/repair. Remove app and binding atomically after confirmation; never uninstall or alter third-party software.
- **FR-008**: Use F07 for binding enable/disable and availability. Display rejections accurately; no optimistic success survives a failed save.
- **FR-009**: Version new target metadata and migrate older valid state with backups, stable IDs and idempotence. Preserve unresolved legacy target text for explicit repair; unknown future versions remain protected.
- **FR-010**: Every mutation uses F07's shared transaction; stale views cannot overwrite unrelated changes. Storage failures retain the prior valid file/backup and map to recoverable UI status.
- **FR-011**: Operate with ordinary user rights and no network/elevation/injection/global hooks. Inputs are validated and no user text is evaluated as a shell command.
- **FR-012**: UI supports keyboard navigation, scalable layout and clear loading/empty/error states. Configuration failures leave a readable recovery view.

### Key Entities and I/O

- **Registered application**: stable appId, display name, explicit typed launch definition, legacy repair metadata when necessary.
- **Discovery candidate**: display name, explicit target definition, source and identity for disambiguation; not persistent until selected.
- **Application view**: saved profile, target/binding status, icon/fallback, recoverable action state.
- **Inputs**: user selections, trusted local discovery results, versioned state, F04 validation and F07 binding operations.
- **Outputs**: saved application mutations and refreshed UI; discovery/home search are read-only.

## Success Criteria

- **SC-001**: A normal user can add a valid local test executable from the UI and see it persist after restart without editing files or launching it.
- **SC-002**: Saved-only search, duplicate prevention, typed identity preservation, repair/rename ID stability and atomic remove each have automated contract or UI acceptance evidence.
- **SC-003**: Legacy migration preserves all IDs/bindings/settings/raw target text; corrupt/future/Busy/I/O failure fixtures preserve bytes and show failed rather than successful edits.
- **SC-004**: Logged-in Windows acceptance covers keyboard add/details/remove flows and readable home at 100%, 125% and 150% scaling; unsupported display cases are recorded explicitly.
- **SC-005**: F00–F07 regression checks continue to pass, including real same-scene/two-target handoff.

## Assumptions and Boundaries

- F08 supplies application registration and the first Studio management surface. Full scene preview/library UI is F09; owned shortcuts and installed launcher integration are F10; themes/settings/diagnostics are F11; installer is F12.
- Packaged identity validation is not proof of installation or successful activation. Status wording preserves this F04 boundary.
- Framework choice, discovery APIs, persistence shape/migration details and numeric discovery bounds are planning decisions; no code is implemented before Plan/Tasks/Analyze.

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline. |
| 0.2.0 | 2026-11-04 | Define saved-only Studio home, explicit target registration/discovery, repair/removal, F07 integration and versioned legacy preservation. |
