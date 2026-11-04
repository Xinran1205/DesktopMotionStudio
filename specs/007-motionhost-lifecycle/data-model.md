# F06 Data Model

F06 stores no persistent state. All records below live for one launch request only.

## MotionHostStartRequest

| Field | Type | Rule |
|---|---|---|
| `RequestId` | GUID | Random per request; never reused in the process lifetime. |
| `SceneId` | string | Must pass F01 scene ID validation and refer to an available F03 built-in entry. |
| `SceneVersion` | string | Exact semantic version from the validated manifest. |
| `DisplayMode` | `Full` / `Centered` | Only F01-defined values are accepted. |
| `MaximumVisibleMilliseconds` | integer | 1–15,000; monotonic watchdog begins when the host first becomes visible. |
| `ProtocolVersion` | integer | Current independent MotionHost IPC version is 1. |

The host never receives `AppId`, `LaunchTarget`, target PID, target HWND, launch arguments, asset paths, or the target's window contents.

## MotionHostFrame

| Field | Type | Rule |
|---|---|---|
| `ProtocolVersion` | integer | Must equal the supported protocol version. |
| `RequestId` | GUID | Must equal the request ID supplied at process launch. |
| `Kind` | enum | Must be valid for the current direction and lifecycle state. |
| `Start` | optional start payload | Present only on the launcher's `Start` frame. |
| `Error` | optional stable error code | Present only on `Failed`; native error code is optional. |

Frame is encoded as a 4-byte little-endian payload length followed by exactly that many UTF-8 JSON bytes. Length must be 1–8192. A frame is not accepted until its complete payload has been read and validated.

## Host lifecycle outcomes

`NotStarted`, `Skipped`, `HandedOff`, `TimedOut`, `Failed`, `Exited`.

- `NotStarted` covers existing-instance activation, where the visual process is bypassed.
- Invalid/missing scene inputs return `Failed` with an error while F05 still attempts the target launch once.
- `Skipped` is terminal for the visual request and has no effect on F05's target result.
- `HandedOff` means MotionHost closed its overlay after an F05 `WindowReady` result; it does not claim the target content is loaded.
- `TimedOut` and `Failed` do not imply target failure.

`Starting`, `Visible`, `HandoffRequested`, `Closing`, and `Closed` are protocol states, not lifecycle outcomes. A user Skip accepted while handoff is pending remains the terminal `Skipped` result.

## F05 extension result

`InstanceResult.LaunchPreparationErrorCode` is nullable and only populated if the optional `beforeDispatch` callback throws on the new-launch path. This is an advisory visual/setup error; `Outcome`, target PID/window identity, and F04 dispatch remain authoritative. Existing callers retain the same method behavior when the optional callback is omitted.

## State transitions

```text
Launcher: ExistingFound -> ActivateExisting -> Completed (MotionHost absent)
Launcher: NotRunning -> StartHostProcess -> DispatchTarget -> WaitForWindow
Host:     ProcessStarted -> PipeConnected -> Ready -> StartReceived -> Visible
Host:     Visible -> SkipRequested -> Close
Host:     Visible -> HandoffRequested -> Fade (<=300 ms) -> Close
Host:     HandoffRequested -> SkipRequested -> Close (concurrent user input wins)
Host:     Visible -> Deadline/Failure/Disconnect -> Close
Launcher: WindowReady -> RequestHandoff; any other terminal F05 result -> RequestShutdown
```

Any invalid frame/state closes the host only. No MotionHost transition changes the target launch token or can issue another F04 dispatch.
