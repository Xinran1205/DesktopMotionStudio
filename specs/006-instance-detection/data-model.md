# F05 data model

## Launch identity

| Field | Type | Rule |
|---|---|---|
| Kind | `Executable` or `Packaged` | Derived from the F04 typed target. |
| Value | absolute path or AUMID | EXE path is normalized with Windows path rules; AUMID is exact and validated by F04. |

Identity comparison is case-insensitive on Windows and never falls back to display name, title, or basename.

## Process stamp

| Field | Type | Rule |
|---|---|---|
| ProcessId | positive 32-bit integer | Valid only with the creation timestamp and confirmed identity. |
| CreationTime | 64-bit file-time ticks | Prevents a reused PID from being treated as the process that F04 launched. |
| ImagePath | optional normalized path | Required to confirm executable identity. |
| Aumid | optional string | Required to confirm packaged identity. |
| SessionId | unsigned integer | Must equal the coordinator's current interactive session. |

## Window candidate

| Field | Type | Rule |
|---|---|---|
| Handle | `nint` | Revalidate before each activation; never persist it. |
| OwnerProcessId | positive integer | PID of the top-level HWND owner. |
| IdentityProcessId | positive integer | PID whose executable/AUMID confirms the target; F05 currently requires it to equal the HWND owner PID. |
| ProcessCreationTime | 64-bit file-time ticks | Must match the verified process stamp. |
| Visible / Cloaked / Minimized / Owned | booleans | Candidate filtering and deterministic scoring inputs. |
| IsForeground / IsMainWindow | booleans | Scoring signals; `IsMainWindow` is derived from Windows' visible, unowned process-main-window handle and does not read a title. No title or content field exists. |

## Result states

- `ActivatedExisting`: an exact existing window was selected and `SetForegroundWindow` succeeded.
- `ForegroundDenied`: the target is confirmed, focus was denied, and one notification was issued.
- `WindowReady`: a window for the newly dispatched process identity appeared before the deadline.
- `NoUsableWindow`: a matching process is present but no eligible main window can be selected.
- `AmbiguousWindow`: multiple top candidates have the same best score.
- `TimedOut`: no matching window appeared within 15 seconds; target remains alive.
- `ProcessExited` / `IdentityChanged`: the correlated process ended or no longer matches its original stamp.
- `InvalidTarget` / `IdentityUnavailable` / `LaunchFailed` / `GateTimedOut` / `Canceled`: structured safe failures. Same-AppId gate wait is capped at 30 seconds.

## State transitions

`Requested -> GateAcquired -> ExistingMatch -> Activate -> Completed`

`Requested -> GateAcquired -> NoMatch -> F04Dispatch -> WaitingForWindow -> WindowReady | TimedOut | ProcessExited | IdentityChanged | Canceled`

All exits release the session semaphore. A timeout/cancel does not stop the dispatched process.
