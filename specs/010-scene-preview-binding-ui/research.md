# F09 Research Decisions

**Date:** 2026-11-05. Two independent read-only research agents examined current F03/F06/F07/F08 boundaries and Microsoft documentation. No production implementation preceded this design.

## Metadata and capability

**Decision:** Retain manifest1/state2; use optional presentation1 and required playback1 sidecars, with independent diagnostics and explicit supported runtime capability.
**Reason:** F03 treats every manifest asset as required. Optional covers must not be included in that required set; required playback may be included. Missing/future presentation can honestly fall back, while unknown playback must not show a generic animation as if it were the selected work.
**Alternative:** Manifest2 migration couples unrelated identity/presentation changes; one sidecar couples optional-image errors to required execution. Per-scene compiled ID switches prevent compatible content from remaining data-only.

## Shared execution and truthful preview

**Decision:** Pure elapsed-time evaluator in Core, one WinForms vector drawing surface. Begin with a bounded generic vector-orbit runtime and an original neutral development work. Stopwatch drives time; Timer only invalidates the viewport. Reduced motion freezes transforms while retaining the finite lifecycle.
**Reason:** Existing MotionHost displays a lifecycle status card and does not execute scene content. Both preview and launch must use the same definition/evaluator/paint; real launch may interrupt at target Ready without waiting for full preview.
**Alternative:** Embedding a second WPF renderer risks divergent playback; introducing GPU/video dependencies before this narrow spike adds unverified risk. New media capabilities can follow with explicit versions.
**Sources:** [Microsoft double buffering](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/how-to-reduce-graphics-flicker-with-double-buffering-for-forms-and-controls), [Timer interval limitations](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/limitations-of-the-timer-component-interval-property), [client-area animation preference](https://learn.microsoft.com/en-us/windows/win32/winauto/client-area-animation).

## IPC and ownership

**Decision:** Explicit unified IPC v2 for immutable validated playback snapshots, Preview/Launch mode and natural Preview Completed. Retain the 8 KiB encoded-frame bound. Version1 packets are safely rejected by v2 peers; bundle mismatch remains fail-open to native launch. Existing lifecycle-only Launch requests remain available for F06 diagnostics, but Preview must contain a playable definition.
**Reason:** v1 has no mode/definition or natural completion transition, and rejects unknown fields. One v2 state machine avoids maintaining separate evolving scene transports. A research alternative retained launch v1 with independent Preview IPC and fixed compiled scene identity resolution; that is smaller for one hard-coded work but less suitable for reusable declarative content. The isolated POC does not implement either production transport.

**Decision:** An owned host enters an unnamed non-inherited KILL_ON_JOB_CLOSE job before Start/show. Live-parent Stop is graceful for 1s then forcibly cleans only that host within another 1s. Pipe EOF plus UI Timer alone cannot handle both parent death and a hung host UI. Job assignment failure cleans the owned child and reports unavailable preview.
**Sources:** [Windows job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [AssignProcessToJobObject restrictions](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject).

**Planning review correction:** A successful post-start Assign POC proves only already-associated children. Production must create directly into the job with STARTUPINFOEX/PROC_THREAD_ATTRIBUTE_JOB_LIST to remove the create-to-assign orphan window; suspended-then-assign is insufficient. An early-parent-death suspended-worker POC supplements the first proof. The live parent also needs an independent deadline to kill a hung host; Stopwatch/UI Timer alone cannot enforce that bound. [Microsoft creation-time job association](https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812/), [UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute).

## Library and binding safety

**Decision:** Separate catalog integrity, presentation fallback and IsPlayable. F07 accepts an optional eligibility predicate with a compatible default; Studio validates current playback in fresh transaction-time catalog scans. Preserve existing unavailable bindings and same-binding no-op behavior, never re-save a stale whole state.
**Reason:** UI-only eligibility can race with removed resources. The current shared transaction is the mutation authority. Preview has neither an application profile nor any dispatcher interface.

**Overflow decision:** Probe only up to a 257th directory, then fail the whole library as TooManyScenes without partial content/sidecar reads. This prevents both unbounded enumeration/sorting and hidden duplicate IDs past the cap.

**Decision:** Cover bytes and dimensions are bounded; validate local ancestry/relative containment/reparse before reads. Studio uses OnLoad caching then freezes the image and closes the stream. Failures are nonfatal to playback.
**Source:** [Microsoft BitmapImage.StreamSource](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.streamsource?view=windowsdesktop-10.0).

## Resolved assumptions and gates

No owner clarification is required: technical choices above are delegated, and product journeys follow the PRD/TDD. Shared playback POC proves only drawing/evaluation and actual bounded window life; production IPC, actual F06 target handoff, single-preview concurrency, jobs, crash/parent exit and system reduced-motion/scaling remain independent tasks with evidence gates.
