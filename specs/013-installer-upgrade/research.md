# F12 Research Decisions

## Installation engine

**Decision** Use a self-contained setup executable that stages immutable version directories and switches one active selector after validation. Keep a stable current-user uninstall registration and stable launcher entry. Use an owned journal to recover interrupted program and registration transitions. The selector and crash recovery are a distinct proof before product setup.

**Rationale** The native MSI proof used an embedded cabinet, per-user scope and RemoveExistingProducts inside the transaction. A real post-InstallExecute failure restored the old marker bytes and HKCU marker value but reported access denied while restoring Windows Installer's registration. This violates AC-05. Keep program selection and Installed Apps registration within the user's writable namespace. Stage new binaries separately so an interrupted write cannot overwrite the running version. WiX 7.0.1 targets the same Windows Installer engine. Inno Setup and NSIS can supply installer UI, but the required version-selection transaction would still belong to the product.

**Sources** [Windows Installer per-user context](https://learn.microsoft.com/windows/win32/msi/installation-context), [rollback](https://learn.microsoft.com/windows/win32/msi/rollback-installation), [upgrade sequencing](https://learn.microsoft.com/windows/win32/msi/removeexistingproducts-action), [WiX terms](https://docs.firegiant.com/wix/osmf/), [WiX releases](https://github.com/wixtoolset/wix/releases), [Inno FAQ](https://jrsoftware.org/isfaq.php).

## Deployment boundary

**Decision** Use a fixed install location under the current user's LocalApplicationData\Programs\DesktopMotionStudio and the existing Launcher child path. Separately publish Studio, Launcher and MotionHost as self-contained win-x64 applications. Retain profile, diagnostics and receipts under the independent user data root.

**Rationale** The isolated current Studio-only self-contained publish placed 246 files at its root and 219 in MotionHost, but Launcher contained only five files and neither hostfxr.dll nor coreclr.dll. Separate publishes let each executable resolve its own bundled runtime. Compare local apphost traces. Validate ordinary-account behavior in a clean VM.

**Source** [Microsoft .NET deployment modes](https://learn.microsoft.com/dotnet/core/deploying/).

## Ownership and commit

**Decision** Enumerate a bounded number of top-level leaf names through the pinned receipt directory. Validate schema, receipt key and binding ID independently of profile availability. Reacquire the per-receipt lock and verify the same current desktop and installed Launcher before deleting. Keep evidence for absent, relocated, edited, replaced, unverified or busy paths. Run the helper only after a committed ordinary uninstall. A cleanup failure is retained as a separate outcome.

**Rationale** Existing single-entry Remove clears a receipt if the recorded path is missing. Uninstall retains proof because the link may have moved. Setup checks the payload inventory and F10 handles desktop identities. Discovery is read-only. Completed program removal is independent of partial user cleanup.

**Sources** [Windows directory handle enumeration](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_full_dir_info), [Windows Installer commit custom actions](https://learn.microsoft.com/windows/win32/msi/commit-custom-actions).

## Verification host

This workstation is Windows 11 Home x64 build 26200. The current token is non-elevated and belongs to an administrator account with a filtered token. Windows Sandbox is unavailable and the Desktop is redirected. Use only uniquely scoped fixtures here. Full acceptance requires a disposable Windows 11 x64 VM with an actual standard-user account, a runtime inventory and offline execution. [Windows Sandbox prerequisites](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/) · [UAC filtered tokens](https://learn.microsoft.com/windows/security/application-security/application-control/user-account-control/how-it-works).
