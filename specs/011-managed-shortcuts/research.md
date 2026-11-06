# F10 Research Decisions

## R1 — Shortcut authority and file scope

**Decision** Use Shell Link with an exact binding-ID command. Treat DesktopDirectory as the current user desktop, then enforce local fixed NTFS and canonical paths. Pin directory ancestry with handles opened without delete sharing through the operation. Recheck directory attributes from the handles, including refusal of reparse points. Avoid shell link resolution and stop on unsupported capabilities.

**Rationale** Known folders can be redirected. Link publication and removal must remain within the validated folder even when another process changes paths. Restricting the filesystem avoids unverified identity and rename behavior.

**Alternatives considered** Fixed user-profile Desktop path, public Desktop, unguarded path checks, recursively locating relocated links.

**Sources** [Microsoft known folders](https://learn.microsoft.com/windows/win32/shell/known-folders), [CreateFileW flags and sharing](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew), [IPersistFile](https://learn.microsoft.com/windows/win32/api/objidl/nn-objidl-ipersistfile).

## R2 — Ownership, publication and repair

**Decision** Record content digest, volume and file identity, creation timestamp and path. Open exact candidate without reparse following. Refuse directories, reparse points and multiple hard links. Hold a read/delete handle without write or delete sharing through content verification and deletion. Stage new link as a non-clickable sibling, persist proof, then rename without replacing. Preserve both proofs while repairing. Distinguish healthy current/pending proof, missing, and an occupied path with foreign bytes or identity.

**Rationale** Exact content catches edits. File identity catches a copy of the same bytes. Holding the object through mutation avoids a check-then-delete-by-path race. Receipt-first publication covers interruption before, during and after publishing. A new foreign entry appearing after deletion still defeats publication.

**Alternatives considered** Filename/comment match, digest alone, File.Replace on desktop, after-the-fact ownership receipt, Shell COM overwriting an existing .lnk, transactional NTFS.

**Source** [SetFileInformationByHandle](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle).

## R3 — Independent runtime

**Decision** Make the Launcher a small executable beside the other bundled components. Reuse F02 snapshot read, F04 target resolution, F05 single-dispatch coordinator, F06 renderer lifecycle and the F09 playback definition. Resolve bound application identity and reject recursion into product components. Choose the native path when animation is disabled or unavailable. F02 accepts legacy opaque IDs, so use a bounded canonical Base64Url token. Settings.Muted is an audio preference.

**Rationale** The independent runtime can process the current binding after Studio exits. Existing native launch and animation cleanup policies have real process evidence. A receipt is authority for filesystem mutations only.

**Alternatives considered** A shortcut pointing at Studio, writing target commands into the .lnk, hard-coding the selected scene in arguments, or using F07 Resolve alone, which intentionally rejects disabled/unavailable animation before native fallback.

## R4 — Separate durable records

**Decision** Use independently versioned per-binding receipt data. Take a bounded cross-process lock scoped to the user/profile and validate the current receipt before replacing it. Use flushed staging plus atomic replace within the private receipt directory. The profile and its backup are read-only to F10 shortcut operations.

**Rationale** A separate receipt preserves profile2 compatibility and limits the blast radius of corruption. A pending proof protects ownership across interruption without cross-volume atomicity requirements.

**Alternatives considered** Adding shortcut fields to profile2, bumping profile schema for unrelated records, or treating filesystem contents as the only receipt.

## Verification gate

Phase 0 research is followed by T001, which must capture real isolated Windows evidence before relying on these primitives in production.
