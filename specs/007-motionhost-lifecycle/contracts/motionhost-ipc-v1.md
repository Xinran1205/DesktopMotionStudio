# MotionHost IPC v1

Historical F06 baseline. Superseded in production by explicit F09 IPC v2 (`../../010-scene-preview-binding-ui/contracts/playback-ipc.md`); retained packet examples and15s limit describe the original release checkpoint, not the current wire contract or10s visual cap.

**Owner:** Core / MotionHost · **Transport:** one request-scoped duplex Windows named pipe · **Trust:** same interactive user only

## Process arguments

The installed host accepts exactly:

```text
DesktopMotionStudio.MotionHost.exe --pipe <pipe-name> --request-id <guid>
```

Unknown, duplicate, missing, overlong, or invalid arguments exit before creating a window. The pipe name is generated from a random request GUID and is not reused. The child receives no target-application identity or filesystem path.

## Framing

Each frame is a four-byte unsigned little-endian length, then a UTF-8 JSON object of 1–8192 bytes. A sender writes a complete frame before sending another. A receiver reads the length exactly, rejects out-of-range values before allocating the payload, reads the full frame, and then validates JSON, protocol version, request ID, message kind, and allowed state transition. The pipe is byte-mode, one server instance, asynchronous, and `CurrentUserOnly` on both server and client.

## Frame shape

```json
{
  "protocolVersion": 1,
  "requestId": "e7a52415-1495-43b1-a2dc-cd401faf9c5a",
  "kind": "Start",
  "start": {
    "sceneId": "dragon-girl-portal",
    "sceneVersion": "1.0.0",
    "displayMode": "Centered",
    "maximumVisibleMilliseconds": 15000
  },
  "error": null
}
```

Optional members must be absent or null except on their designated message. Unknown frame kinds and versions fail closed. JSON strings are bounded by the overall 8 KiB frame; the individual identity fields have the F01 limits.

## Messages and direction

| Kind | Direction | Allowed state | Meaning |
|---|---|---|---|
| `Ready` | Host → Launcher | Connected, before `Start` | Host process parsed args and is waiting for one start command. |
| `Start` | Launcher → Host | After `Ready` | Supplies the validated built-in scene ID/version, display mode, and deadline. |
| `Started` | Host → Launcher | After valid `Start` and visible window | Lifecycle surface is visible. |
| `SkipRequested` | Host → Launcher | Visible or HandoffRequested | Esc or accessible Skip requested close. Host closes immediately, including when the launcher has just requested handoff. |
| `Handoff` | Launcher → Host | Started/Visible | Target window is ready; enter `HandoffRequested`, fade out if allowed, then close. |
| `Finished` | Host → Launcher | HandoffRequested or Closing | Host surface has closed. |
| `TimedOut` | Host → Launcher | Visible | Visual watchdog expired; host closes. |
| `Failed` | Either direction | Any pre-close state | Stable error code; host closes or is ignored by launcher. |
| `Shutdown` | Launcher → Host | Any live state | Close immediately without affecting the target. |

## Failure behavior

- Pipe connection must finish within 1 second after process start; failure terminates only the MotionHost child.
- Parent disconnect or EOF closes the host window within 1 second.
- Host exit or malformed IPC is observed without cancelling F05.
- Host window watchdog is 15 seconds from visible time. F05's own target-window wait remains a separate 15-second deadline.
- Launcher requests a 300 ms maximum fade. Reduced motion or any fade error makes the close immediate.
- Launcher requests graceful close, waits at most 1 second, then may terminate its own MotionHost process only.

## Protocol evolution

Protocol version is independent of scene-manifest and persisted-state versions. A future incompatible message shape increments the protocol version. Version 1 peers reject unknown newer versions rather than guessing; the launcher still dispatches the target through F04/F05 when the host rejects the request.
