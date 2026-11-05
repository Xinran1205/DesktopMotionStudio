# Desktop Motion Studio

Windows 11 x64 desktop application for binding built-in launch animations to local apps. The repository is in early feature-by-feature development; there is no installable product yet.

## Development status

The original [PRD](Desktop_Motion_Studio_PRD_V1.0.md) and [technical design](Desktop_Motion_Studio_Technical_Design_V1.0.md) remain source baselines. [Feature inventory](FEATURES.md), [Spec Kit constitution](.specify/memory/constitution.md), [decisions](docs/DECISIONS.md), and independent specifications under `specs/` track the executable plan and refinements.

Spec Kit 1.2.3 is initialized with Codex skills in `.agents/skills/` and PowerShell scripts in `.specify/scripts/powershell/`. On a machine with the CLI installed, `specify version` checks the installation. See the [official installation guide](https://github.com/github/spec-kit/blob/main/docs/installation.md) for setting up another machine.

F00–F08 are verified. The Studio development build now provides **My applications**: add local EXE/LNK or discovered packaged entries, search saved applications, rename/repair/remove and toggle existing bindings. It preserves legacy state through an explicit, backed-up migration. Scene-library/preview/binding UI is the next feature; managed shortcuts, installer and licensed scene delivery remain later work.

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
