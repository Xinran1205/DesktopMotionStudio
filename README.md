# Desktop Motion Studio

Windows 11 x64 desktop application for binding built-in launch animations to local apps. The development build is verified through F11. F12 self-contained Windows Setup candidates are awaiting clean-account acceptance.

## Development status

The original [PRD](Desktop_Motion_Studio_PRD_V1.0.md) and [technical design](Desktop_Motion_Studio_Technical_Design_V1.0.md) remain source baselines. [Feature inventory](FEATURES.md), [Spec Kit constitution](.specify/memory/constitution.md), [decisions](docs/DECISIONS.md), and independent specifications under `specs/` track the executable plan and refinements.

Spec Kit 1.2.3 is initialized with Codex skills in `.agents/skills/` and PowerShell scripts in `.specify/scripts/powershell/`. On a machine with the CLI installed, `specify version` checks the installation. See the [official installation guide](https://github.com/github/spec-kit/blob/main/docs/installation.md) for setting up another machine.

F00–F11 are verified. The Studio development build provides **My applications** for local EXE/LNK and discovered packaged entries, legacy state migration with a backup, scene previews and binding. Managed desktop entries run independently through the bundled Launcher. Settings and local diagnostics are available. Installer and licensed scene delivery are separate features.

```powershell
dotnet build .\src\DesktopMotionStudio.Studio -c Release -r win-x64
& .\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.Studio.exe
```

Use the SDK pinned in `global.json`. See [F08 quickstart](specs/009-application-management/quickstart.md) for isolated UI tests, current workstation SDK setup, configuration behavior and keyboard controls, and [verification evidence](specs/009-application-management/evidence.md) for the actual Windows/DPI coverage.

## First feature

F00 validates the Windows animation host in isolation. Once the .NET 10 SDK is installed:

```powershell
dotnet build .\src\DesktopMotionStudio.WindowHost.Poc\DesktopMotionStudio.WindowHost.Poc.csproj -c Release
dotnet run --project .\src\DesktopMotionStudio.WindowHost.Poc -- --mode centered
dotnet run --project .\src\DesktopMotionStudio.WindowHost.Poc -- --mode full --monitor 0
```

Press Esc or click **Close** to exit. The [F00 evidence log](specs/001-window-host-feasibility/evidence.md) distinguishes compilation from real Windows desktop observations.

## Scene library and preview

Open **动效作品库** with Alt+L from home. In the library, use Alt+P to preview the selected work, Alt+B to choose a saved app, and Alt+H to return home. The picker confirms with Alt+S and cancels with Esc. Home also offers Preview/Replace for the selected app. Preview Esc/Stop closes only the animation; browsing and preview do not write configuration or start target apps. Unavailable work remains represented without deleting stored bindings.

Studio build/publish automatically bundles MotionHost and the original neutral development work. The work is labelled development content; licensed release works are F13/F14. A framework-dependent publish needs the .NET10 Windows Desktop x64 runtime. Installer delivery remains F12.

See [F09 validation guide](specs/010-scene-preview-binding-ui/quickstart.md), [evidence](specs/010-scene-preview-binding-ui/evidence.md) and [review](specs/010-scene-preview-binding-ui/review.md). The actual desktop covered100/125/150% and both system motion preferences, restoring original settings.

## Managed desktop entries

Select a saved application with an enabled and available work. In **受管理桌面入口**, use Alt+C to create its desktop entry, Alt+E to repair a missing or stale entry, and Alt+M to remove it. The entry works after Studio closes and follows the current binding. Disabling the work leaves the entry available for native launch. Desktop files that are edited, replaced, or in conflict are preserved and Studio shows their status. This feature requires a supported local NTFS desktop. The development bundle's location must stay available for its entries to launch.

See [F10 validation and isolated fixture commands](specs/011-managed-shortcuts/quickstart.md) and [verification evidence](specs/011-managed-shortcuts/evidence.md).

## Settings and local diagnostics

Open **设置** with Alt+G to choose a system, light or dark theme and save the mute preference. Windows high contrast takes priority. The current development work has no audio track.

Open **本地诊断** with Alt+I to check versions, renderer availability, recent launch outcomes and the selected desktop entry. Copying a summary is explicit. Its typed records stay local and omit user paths, identifiers and arguments. When the selected entry can be repaired, Alt+R calls the managed-entry recovery flow.

See the [F11 validation guide](specs/012-settings-diagnostics/quickstart.md) and [Windows evidence](specs/012-settings-diagnostics/evidence.md).

## Windows Setup validation

`installer/Build-Setup.ps1` produces an unsigned, self-contained Windows x64 Setup and a SHA256 file. Validation candidates install for the current user under LocalAppData and preserve the separate user-data directory during upgrades and uninstall. The isolated Windows lifecycle has passed. Standard-account offline and Installed Apps acceptance are tracked in the [F12 validation guide](specs/013-installer-upgrade/quickstart.md).
