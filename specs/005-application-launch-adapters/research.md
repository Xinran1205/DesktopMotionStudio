# F04 research decisions

**Date:** 2026-11-04

## EXE startup and arguments

- **Decision:** Use `ProcessStartInfo` with `UseShellExecute=false` and `ArgumentList` for direct EXE targets.
- **Rationale:** The .NET API exposes each argument separately, avoiding hand-built command line quoting. Shell execution is not needed for a direct executable.
- **Source:** [Microsoft ProcessStartInfo API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo)

## Shortcut resolution

- **Decision:** Resolve `.lnk` through Windows Shell Link COM (`IShellLinkW` and `IPersistFile`), then accept only an executable target. Do not blindly shell-open the shortcut because it may point to a document, URL, or folder.
- **Rationale:** Windows Shell Links can store target, working directory, and arguments and support resolution when a target moved.
- **Source:** [Microsoft Shell Links overview](https://learn.microsoft.com/en-us/windows/win32/shell/links), [IShellLinkW::GetPath](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath)

## Packaged application activation

- **Decision:** Use `IApplicationActivationManager::ActivateApplication` for a confirmed AUMID and represent its failing HRESULT as structured failure data.
- **Rationale:** Microsoft's API is the generic Windows Store app launch contract and returns a process ID through its out parameter.
- **Validation:** Accept the package-family-name / app-ID form, reject embedded control characters and enforce the Windows maximum of 129 content characters (the documented 130-character constant includes the terminating NUL).
- **Source:** [Microsoft ActivateApplication API](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication), [IApplicationActivationManager interface](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iapplicationactivationmanager), [Windows identity constants](https://learn.microsoft.com/en-us/windows/desktop/appxpkg/identity-constants), [Package identity overview](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview)

## Known boundary

Successful activation or process creation does not prove the app's main window is ready. F05 owns process/window identity and bounded readiness checks. F04 must not wait indefinitely for a UI window.
