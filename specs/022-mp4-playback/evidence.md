# F21 Windows acceptance

**Workstation** Windows 11 x64 build 26200. **Candidate** unsigned 0.20.0. **Scope** packaged player, Studio preview, test launch and managed shortcuts. The test scenes exist only in isolated fixtures. F22 supplies the import UI and F23 supplies the first built-in video work.

| Check | Observed |
| --- | --- |
| 1080p landscape, 10 seconds | Real stage visible after 398 ms. Completed and confirmed process exit 10,191 ms after visibility. |
| 1080p landscape with AAC muted, 6 seconds | Stage visible after 453 ms. Completed and confirmed exit 6,005 ms later. |
| 2 fps with AAC muted | Finished the 2-second clip in 1,992 ms after visibility. |
| Portrait, centered fill, full monitor | Verified the window matched the monitor bounds. Focused Esc skipped and closed the stage. |
| Window size preferences | An isolated preference applied compact, large and full sizes to the actual video worker. Measured widths were 760, 1,120 and 2,560 pixels. |
| Effective sound | Default Windows output was idle. Muted AAC produced no worker audio session and an output peak of 0.00000. Enabled audio produced 0.12998 at the owned session and 0.03399 at the endpoint. The probe read levels only. |
| Studio preview | Discovered the fixture, opened its video preview, and reported Esc. The isolated profile remained byte-identical. |
| Cold start and reuse | Studio Test launch and a real Windows shortcut opened their isolated targets once. The target was absent while the video was visible. A repeated Test launch reused its existing window without replaying. |
| Unsupported and unavailable work | Rejected malformed, missing and unsupported media before showing a stage. Missing player or bad media released native launch. Missing media preserved the existing shortcut and launched without playback. |
| Time limits | Controlled startup stall ended with the owned process gone in 2,469 ms. Controlled playback stall ended in 1,769 ms for the 1-second request. |
| Reduced motion | With Windows animation effects off, the AAC clip used a brief silent still and confirmed exit 346 ms after visibility. The original Windows setting was restored and read back. |
| Installer | Published 879 hash-verified files plus inventory to an inactive Windows install root and verified selector activation. The release worker is self-contained with the shared pinned desktop runtime. |
| Upgrade and uninstall | Installed 0.19.0 in an isolated Windows profile, verified a failed upgrade rollback, then upgraded to 0.20.0. Existing link, profile and backup bytes were preserved. Uninstall and reinstall passed. |
| Native regression | Both 0.19.0 scene directories matched byte for byte. Both works previewed and completed through Test launch and a real shortcut on the candidate. Native renderer source and IPC v5 are unchanged. |
| Automated checks | Core 555 passed, Studio 81 passed, MotionHost 26 passed. Core skipped the optional packaged-app instance case because no fixture AUMID was configured. Release build had zero warnings or errors. |

## Review and convergence

The independent review checked the first-frame event, timeout and exit handling, catalog validation, source profiles and native launch ordering. Follow-up review covered the silent decoder and its memory layout. The fixes gate visibility on painting, propagate unconfirmed worker cleanup, reject unsupported containers and extra tracks, check media progress, use the decoder's display aperture for padded frames, and include the last sample's duration in completion.

Windows Media Engine emitted a brief AAC transient on this workstation even with its mute controls and after deselecting the audio track. Muted AAC now uses a video-only Source Reader. The endpoint probe caught the transient in intermediate builds and verified silence on the candidate.

The tests cover this workstation's active display and default audio endpoint. Windows N without Media Feature Pack, secondary monitors and other hardware were outside the local test matrix. The owner enforces time limits if native decoding stalls. A synchronous decode stall can delay input to the worker window until the native call returns or the owner closes the process.

## Artifacts

- Candidate and checksum are under `artifacts/f21/release-0.20.0/setup`.
- Measured traces are under `artifacts/f21`. Start with `audio-release.log`, `video-release.log`, `video-presets.log`, `launch-release.log`, `timeouts-release.log`, `reduced-release.log`, `stage-release.log`, `installer-lifecycle.log` and `core-release.log`.
- See the [verification quickstart](quickstart.md) for reproduction.
