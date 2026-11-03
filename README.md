# Desktop Motion Studio

Windows 11 x64 desktop application for binding built-in launch animations to local apps. The repository is in early feature-by-feature development; there is no installable product yet.

## Development status

The original [PRD](Desktop_Motion_Studio_PRD_V1.0.md) and [technical design](Desktop_Motion_Studio_Technical_Design_V1.0.md) remain source baselines. [Feature inventory](FEATURES.md), [Spec Kit constitution](.specify/memory/constitution.md), [decisions](docs/DECISIONS.md), and independent specifications under `specs/` track the executable plan and refinements.

Spec Kit 1.2.3 is initialized with Codex skills in `.agents/skills/` and PowerShell scripts in `.specify/scripts/powershell/`. On a machine with the CLI installed, `specify version` checks the installation. See the [official installation guide](https://github.com/github/spec-kit/blob/main/docs/installation.md) for setting up another machine.

## First feature

F00 validates the Windows animation host in isolation. Once the .NET 10 SDK is installed:

```powershell
dotnet build .\src\DesktopMotionStudio.WindowHost.Poc\DesktopMotionStudio.WindowHost.Poc.csproj -c Release
dotnet run --project .\src\DesktopMotionStudio.WindowHost.Poc -- --mode centered
dotnet run --project .\src\DesktopMotionStudio.WindowHost.Poc -- --mode full --monitor 0
```

Press Esc or click **Close** to exit. The [F00 evidence log](specs/001-window-host-feasibility/evidence.md) distinguishes compilation from real Windows desktop observations.
