# F07 Verification Evidence

**Date**: 2026-11-04 · **Status**: Verified and independently reviewed; final convergence passed.

## Environment and checks

- Windows 11 x64 build 26200; .NET SDK 10.0.403; no new dependencies or persisted schema fields.
- Core, MotionHost win-x64, WindowHost POC win-x64, and check harness Release builds passed with warnings treated as errors: 0 warnings, 0 errors.
- Core/check harness `dotnet format --verify-no-changes --no-restore` passed.
- Complete F00–F07 harness: **134 PASS, 0 FAIL, 1 SKIP**. F07 contributes **38 checks**. The inherited optional real packaged-instance check skipped because `DMS_F05_TEST_AUMID` was not set.
- Full run command used the built check DLL with `DMS_F06_HOST_PATH` and `DMS_F06_TARGET_PATH` set. Machine-local stdout: `%TEMP%/dms-f07-final-contract-checks.log`.

## Domain and persistence evidence

- Two applications share a scene with distinct stable BindingIds. Replacement/toggles retain ID and leave other app/profile/settings/scene content unchanged.
- Assign/disable/remove no-ops preserve state and backup bytes. Read-only resolution uses current catalog metadata, retains unavailable bindings, and exposes no launch context for non-Ready statuses.
- Explicit EXE, shortcut, and packaged target types pass through the provider boundary; unavailable/null/throwing providers and missing/duplicate/asset-invalid catalog entries fail safely.
- Empty/overlong/unsafe IDs reject; 128-character application ID boundary passes.
- Transaction callbacks execute once; callback exception, invalid output, corrupt/future/oversized input, real I/O failure, and a locked-backup atomic replacement failure preserve existing state and backup.
- Two real mutation workers (**PIDs 5856 and 15440**) updated different apps through BindingService; both edits survived reload. Same-app concurrent commands retained one binding.
- Held write lock returned **Busy in 2010 ms**, without invoking the losing callback. A parent-retained mutex handle ensured that killing the owner produced actual abandoned ownership; the next transaction revalidated state and succeeded.
- Independent review found non-file configuration misclassified as empty installation. Its new Load/Resolve/Remove regression failed before the fix and passed after direct file-open error classification. Cleanup review also closed a per-target failure race; each owned process/directory is now cleaned independently and failures contribute to the test result.

## Real cross-application integration

The test copied complete WindowHost POC output to two distinct owned temp paths, registered each, assigned the same `scene-one`, resolved each typed target, and passed it to the existing F06/F05 coordinator.

| App | Scene | Target PID | Target HWND | MotionHost |
|---|---|---:|---|---|
| fixture-0 | scene-one | 14764 | 0xA4011A | HandedOff |
| fixture-1 | scene-one | 14448 | 0xD106CC | HandedOff |

Both target processes/windows were distinct and alive after handoff. The test closed only owned windows/processes and removed its temporary state/catalog/targets. Scene content contained no target identity.

## Compatibility boundaries

F07 does not claim application discovery/UI, managed shortcuts, installed launcher delivery, or licensed scene rendering. The mandatory typed-target provider is the F08 application-registry boundary; free-form legacy target strings are not guessed. Packaged Ready is structural validation, not proof of installation/activation. Whole-state Save remains explicit replacement; read-modify-write clients must use Update.
