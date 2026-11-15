# F10 Research Decisions

## R1 — Shortcut authority and file scope

**Decision** Use Shell Link with an exact binding-ID command. Treat DesktopDirectory as the current user desktop, then enforce local fixed NTFS and canonical paths. Open each child relative to its retained parent with NtCreateFile, OBJ_DONT_REPARSE and FILE_OPEN_REPARSE_POINT. Reject reparse directory handles and keep ancestry pinned. Publish a retained stage using a simple same-parent filename and NtSetInformationFile with replacement disabled. A write-attributes-only handle can convert a pinned empty directory into a junction, so absolute child opens are unsafe even with read-only sharing. Avoid Shell Link Resolve and stop on unsupported capabilities.

**Rationale** Known folders can be redirected. Link publication and removal must remain within the validated folder even when another process changes paths. Restricting the filesystem avoids unverified identity and rename behavior.

**Alternatives considered** Fixed user-profile Desktop path, public Desktop, unguarded path checks, recursively locating relocated links.

**Sources** [Microsoft known folders](https://learn.microsoft.com/windows/win32/shell/known-folders), [User-mode NtCreateFile](https://learn.microsoft.com/windows/win32/api/winternl/nf-winternl-ntcreatefile), [Object attributes](https://learn.microsoft.com/windows/win32/api/ntdef/ns-ntdef-_object_attributes), [Same-directory rename](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information), [Shell Link persistence](https://learn.microsoft.com/windows/win32/shell/links).

## R2 — Ownership, publication and repair

**Decision** Record content digest, volume and file identity, and path. Include a random marker in generated link bytes. Windows can restore the cached creation time for a recently removed filename during rename, so the receipt does not require that metadata. Open exact candidate without reparse following. Refuse directories, reparse points and multiple hard links. Hold a read/delete handle without write or delete sharing through content verification and deletion. Stage new link as a non-clickable sibling, persist proof, then rename without replacing. Preserve both proofs while repairing. Distinguish healthy current/pending proof, missing, and an occupied path with foreign bytes or identity.

**Rationale** Exact content catches edits. File identity catches a copy of the same bytes. Holding the object through mutation avoids a check-then-delete-by-path race. Receipt-first publication covers interruption before, during and after publishing. A new foreign entry appearing after deletion still defeats publication.

**Alternatives considered** Filename/comment match, digest alone, File.Replace on desktop, after-the-fact ownership receipt, Shell COM overwriting an existing .lnk, transactional NTFS.

**Sources** [SetFileInformationByHandle](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle), [GetFileTime and cached creation timestamps](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfiletime).

## R3 — Independent runtime

**Decision** Make the Launcher a small executable beside the other bundled components. Reuse F02 snapshot read, F04 target resolution, F05 single-dispatch coordinator, F06 renderer lifecycle and the F09 playback definition. Resolve bound application identity and reject recursion into product components. Choose the native path when animation is disabled or unavailable. F02 accepts legacy opaque IDs, so use a bounded canonical Base64Url token. Settings.Muted is an audio preference.

**Rationale** The independent runtime can process the current binding after Studio exits. Existing native launch and animation cleanup policies have real process evidence. A receipt is authority for filesystem mutations only.

**Alternatives considered** A shortcut pointing at Studio, writing target commands into the .lnk, hard-coding the selected scene in arguments, or using F07 Resolve alone, which intentionally rejects disabled/unavailable animation before native fallback.

## R4 — Separate durable records

**Decision** Use independently versioned per-binding receipt data. Take a bounded cross-process lock scoped to the user/profile. Preserve verified prior bytes under a previous name, then publish the flushed primary with no replacement. Both relocations act on retained handles. An outside editor racing the commit cannot have a new primary overwritten. F10 holds the profile's existing write lock during final publication and returns the same snapshot, preserving profile and backup bytes.

**Rationale** A separate receipt preserves profile2 compatibility and limits the blast radius of corruption. A pending proof protects ownership across interruption without cross-volume atomicity requirements.

**Alternatives considered** Adding shortcut fields to profile2, bumping profile schema for unrelated records, or treating filesystem contents as the only receipt.

## Verification gate

Phase 0 research is followed by T001, which must capture real isolated Windows evidence before relying on these primitives in production.
