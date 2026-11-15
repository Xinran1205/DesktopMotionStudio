# F22 storage model

| Item | Fields and rules |
| --- | --- |
| Draft | Generated `user-mp4-<32 lowercase hexadecimal digits>` ID and private staging directory. Readable local source copied as `clip.mp4`, up to 64 MiB. A successful hidden inspection supplies the source duration. Drafts never appear in catalog results. |
| Published work | Folder name matches its generated ID. Contains only managed `clip.mp4` and `work.json`. Metadata schema 1 contains ID, revision, display name, source duration in milliseconds, selected duration in milliseconds, fit and sound. Source path stays out of the metadata. |
| Name | Trimmed, 1 to 80 characters, no control characters. Preserve case and user-entered language. Initialize from a sanitized source base name or a localized generic name. Equal display names are allowed. |
| Fit and audio | Fit is `complete` or `fill`. Default `complete`. Sound is Boolean and defaults to false. The existing global mute and Windows animation preference still apply. |
| Duration | Integer milliseconds, from 1,000 to the smaller of 10,000 and verified source duration. Use that upper bound by default. Preserve the verified source duration when editing playback options. |
| Revision | Positive integer, incremented once per committed edit. Read unknown schema versions as unavailable and preserve their bytes. |
| Recovery | A private ownership marker records the generated ID and expected identities for unpublished files. Foreign content or reparse points are left alone and are never served as works. |

Draft → inspected → published is the normal flow. Cancellation leaves no published item. A committed removal takes the item out of the catalog before cleanup. Existing app profiles and shortcut receipts continue to reference the generated ID.
