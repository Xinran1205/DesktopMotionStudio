# Research: MotionHost lifecycle and fail-open

**Date:** 2026-11-04 · **Status:** Decisions recorded

## Questions and decisions

### 1. How should Launcher and MotionHost communicate?

**Decision:** One request-scoped Windows named pipe with `PipeOptions.CurrentUserOnly`, asynchronous duplex I/O, a random pipe name, and the request GUID checked in every frame. Use a 4-byte little-endian length prefix and a strict 8 KiB maximum UTF-8 JSON payload. Do not start an HTTP listener or expose a network endpoint.

**Evidence:** .NET 10 documents `CurrentUserOnly` as limiting a named-pipe server to clients created by the same user. The `NamedPipeServerStream` API supports asynchronous operation and bounded server instances. This matches the local, same-user, one-request process boundary without an external package. See [PipeOptions](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0) and [NamedPipeServerStream](https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstream?view=net-10.0).

**Limit:** Same-user malware is already inside the trust boundary; `CurrentUserOnly` prevents cross-user access but is not process identity authentication.

### 2. Can the neutral host fade out and honor reduce-motion settings without a rendering package?

**Decision:** Use the Windows Forms `Form.Opacity` property for a 250 ms maximum fade, with immediate close if the property update fails. Before fading, query `SPI_GETCLIENTAREAANIMATION`; a failed query or a disabled preference means no fade.

**Evidence:** Microsoft documents that `Form.Opacity` supports phase-out effects and depends on the layered-windows API. Windows exposes `SPI_GETCLIENTAREAANIMATION` (`0x1042`) to determine whether client-area animation is enabled. See [Form.Opacity](https://learn.microsoft.com/dotnet/api/system.windows.forms.form.opacity) and [SystemParametersInfo](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfoa).

**Limit:** This proves only the lifecycle shell's standard fade. It does not establish GPU or DWM handoff capability.

### 3. How does the child host receive its connection details?

**Decision:** The launcher starts only the absolute installed MotionHost path, with `UseShellExecute=false`, a random pipe name and request GUID passed as discrete `ArgumentList` values. No shell string composition, target application path, startup arguments, or scene asset paths are passed. The host connects to the named pipe and validates the exact GUID and protocol before showing a window.

**Evidence:** `ProcessStartInfo` supports explicit executable arguments and redirection; setting `UseShellExecute=false` is required when using redirected standard streams. F06 does not redirect stdio, but follows the explicit-process-start model to avoid invoking a command shell. See [ProcessStartInfo](https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo?view=net-10.0).

### 4. How can existing applications avoid a brief animation flash?

**Decision:** Put a synchronous, optional `beforeDispatch` observer inside the existing F05 AppId gate. Invoke it only after the exact existing-instance scan returns no match and immediately before the existing F04 dispatch call. F05 catches callback exceptions, records the non-fatal HRESULT, and dispatches once. MotionHost process creation is synchronous and short; pipe connection and visual initialization happen asynchronously.

**Rationale:** Starting the host before F05 would briefly show animation for already-running applications. Starting it only after `OpenAsync` returns would show it after the target window is already ready. This seam meets both F05's exact-instance behavior and the PRD's concurrent launch animation behavior.

### 5. What does this version render?

**Decision:** A neutral status card with an accessible Skip button, transparent surround, full/centered layout from the validated scene manifest, and no scene asset execution. Video, audio, particle, Win2D/Direct3D effects, performance adaptation, and DWM transition remain separate content/rendering features.

**Rationale:** F06 is a lifecycle and failure-isolation feature, while F13 owns the first licensed production scene. Keeping the host surface neutral provides a real process/window for end-to-end validation without misrepresenting placeholder artwork as a finished scene.

### 6. What must happen when the host or launcher exits?

**Decision:** Host pipe EOF triggers window close; host has a 15-second monotonic watchdog; launcher waits at most one second for graceful host shutdown and then terminates only its own MotionHost child process if necessary. F05 continues independently and never receives the host's cancellation token.

**Rationale:** Process isolation is useful only if stale overlay windows are cleaned up and the target application's lifetime remains separate.

## Rejected approaches

- **Anonymous standard streams:** They are suitable for one-way process I/O, but the host needs an ongoing duplex control channel and explicit parent-disconnect detection. A request-scoped pipe provides a direct local protocol and ACL.
- **Named pipe without `CurrentUserOnly`:** Adds cross-user access without a product need.
- **HTTP/local TCP:** Adds a listener surface and authentication/port management with no benefit for a same-user child process.
- **Host-first startup outside F05:** Can flash an animation when an existing target is activated.
- **Host waits before F04 dispatch:** Couples target availability to render startup and violates fail-open behavior.
- **DWM or captured target window in F06:** Adds capability and compatibility risk before the lifecycle boundary is established; standard fade is sufficient for this feature.
