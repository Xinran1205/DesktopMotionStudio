# F05 research decisions

**Date:** 2026-11-04

## Window enumeration and process ownership

- **Decision:** Enumerate top-level windows with `EnumWindows`, then associate each handle with the creator PID using `GetWindowThreadProcessId`. Never classify by a title or basename.
- **Rationale:** These documented Win32 APIs expose desktop windows and their process ownership. Candidate activation still revalidates the handle/PID because windows and process IDs can disappear or be reused.
- **Sources:** [EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows), [GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)

## Exact process identity

- **Decision:** For EXE targets, open matching process handles with `PROCESS_QUERY_LIMITED_INFORMATION` and obtain the full image path through `QueryFullProcessImageNameW`; compare canonical full paths case-insensitively. For packaged targets, use `GetApplicationUserModelId` and compare the exact AUMID case-insensitively. Capture process creation time when a request is correlated to a PID.
- **Rationale:** A basename or PID alone can match an unrelated app or a later process that reused the PID. The process APIs support least-privilege query access and return an explicit error when identity is unavailable; unknown identity must not cause activation.
- **Sources:** [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew), [GetApplicationUserModelId](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid)

## Visible window candidate

- **Decision:** `EnumWindows` supplies top-level candidates. Require `IsWindowVisible` and a successful `DwmGetWindowAttribute(DWMWA_CLOAKED)` result showing the window is not cloaked; reject owned and tool windows. Use a deterministic score that prefers the current foreground window, then the process main-window handle, then a non-minimized unowned window. A tied score is `AmbiguousWindow`. Revalidate identity and structural visibility immediately before activation.
- **Rationale:** `IsWindowVisible` only reflects the `WS_VISIBLE` style and can be true when the window is obscured, so it is only one signal. DWM exposes cloaking state. The feature never reads title text or pixels.
- **Sources:** [IsWindowVisible](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowvisible), [DwmGetWindowAttribute](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute), [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- **Main-window source:** [.NET `Process.Windows.cs`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs) obtains `MainWindowHandle` through the process window finder; F05 reads only this handle and never accesses `MainWindowTitle`.

## Restore and foreground behavior

- **Decision:** Use `ShowWindowAsync(SW_RESTORE)` for a minimized confirmed window, then attempt `SetForegroundWindow` once. If the OS declines, call `FlashWindowEx` once and return `ForegroundDenied`.
- **Rationale:** `ShowWindowAsync` posts the restore request without waiting for an unresponsive target. Windows restricts foreground changes and may deny them even when documented conditions are met; the app must not bypass those rules or loop focus requests. `FlashWindowEx` notifies without changing active state.
- **Sources:** [ShowWindowAsync](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync), [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow), [Window Features: foreground rules](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), [FlashWindowEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-flashwindowex)

## AUMID and packaged app behavior

- **Decision:** Match the exact process AUMID and require the top-level HWND owner PID to be that confirmed process PID. If a packaged app delegates its UI to a different host process, F05 fails closed until that host independently exposes the same confirmed AUMID; it never infers a parent/child relationship as identity.
- **Rationale:** Microsoft documents that an AUMID identifies a packaged app, while app instance behavior varies: Windows App SDK desktop apps are multi-instance by default unless the app implements instance redirection. The feature can find an existing confirmed window but cannot impose another app's instance policy.
- **Sources:** [Package identity overview](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview), [Windows App SDK multi-instance apps](https://learn.microsoft.com/en-us/windows/apps/develop/launch/multi-instance-apps), [UWP app lifecycle](https://learn.microsoft.com/en-us/windows/uwp/launch-resume/app-lifecycle)

## Same-AppId request serialization

- **Decision:** Use a session-local named semaphore whose name contains only a SHA-256 digest of the stable AppId. Acquire it before scanning; wait no more than 30 seconds; rescan after acquisition; release it after ready/timeout. Different AppIds use independent gates.
- **Rationale:** `Semaphore` permits release after asynchronous waits and a session-local name coordinates separate Launcher processes for the same interactive Windows session without exposing the AppId in the object name. Requests do not hold machine-global locks or modify target state.

## High-risk validation before implementation

- **Proof completed:** [`poc/WindowIdentityProbe.ps1`](poc/WindowIdentityProbe.ps1) launched the existing benign WinForms host, matched its visible HWND to the exact full image path/PID, minimized and restored it, and successfully brought it to the foreground on this desktop. The probe then activated installed Paint by AUMID and confirmed the returned PID's exact AUMID and top-level HWND ownership. It observed one visible and four hidden top-level windows for the package process, confirming that visibility filtering is required. Both test applications were closed after observation; details are in [`poc/evidence.md`](poc/evidence.md).
- **Alternatives rejected:** Using `Process.MainWindowHandle` alone misses other candidate windows and stale snapshots, so it is only a scoring signal alongside full top-level enumeration. Window title matching can activate unrelated apps and exposes user content; thread/input hooks or forced input are prohibited by the constitution. A generic unowned splash window cannot be classified without a title, content, or app-specific signal, so `WindowReady` is structural rather than a claim that app content is loaded.
