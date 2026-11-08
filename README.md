# Desktop Motion Studio

**English** · [简体中文](README.zh-CN.md)

Desktop Motion Studio adds short launch animations to apps on Windows 11 x64. Pick a built-in work, preview it, and test a real launch from Studio. Once you like the result, create a managed desktop shortcut that works with Studio closed.

## Getting started

1. Run `DesktopMotionStudio.Setup.exe` and open **Desktop Motion Studio** from the Windows Start menu. The current-user installer includes the runtime. Local candidate builds are unsigned.
2. In **My apps**, add an installed app, local executable, or Windows shortcut. Choose a motion from the library and bind it to the app.
3. Preview the motion, then press **Alt+T** or click **Test launch** to open the real app. Create a desktop shortcut once you are happy with the result.
4. Open **Settings → Interface language** (设置 → 界面语言), choose **English** or **简体中文**, save, and reopen Studio. Saved app names stay exactly as entered.
5. Choose **Settings → Animation display area** to use a work's recommended area, a compact or large centered window, or full screen. This applies from the next playback.

**Zero Boundary** is an original two-second tech sequence with an ice-blue window frame. Studio Pulse is also available as a development work. For a cold launch, the app starts after the animation closes. Press **Esc** to skip the animation and start immediately. An app already running can reuse its existing window, and renderer failures fall back to opening the app normally.

See the [Chinese user guide](docs/USER_GUIDE.zh-CN.md) for installation, shortcuts, and troubleshooting.

## Build from source

Use the .NET SDK pinned in [global.json](global.json) and Windows PowerShell.

```powershell
dotnet build .\src\DesktopMotionStudio.Studio -c Release -r win-x64
& .\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.Studio.exe
```

To build a self-contained Setup, run the following command with a new output directory. [Installer and validation guide](specs/013-installer-upgrade/quickstart.md)

```powershell
& .\installer\Build-Setup.ps1 -Version 0.19.0 -OutputDirectory (Join-Path $PWD 'artifacts/local-0.19.0')
```

## Project development

Spec Kit is configured for Codex with PowerShell scripts. Run `specify version` to check the CLI on your machine. [Official Spec Kit setup](https://github.com/github/spec-kit/blob/main/docs/installation.md)

- [Feature inventory](FEATURES.md) and [Spec Kit constitution](.specify/memory/constitution.md)
- [Original product requirements](Desktop_Motion_Studio_PRD_V1.0.md) and [technical design](Desktop_Motion_Studio_Technical_Design_V1.0.md)
- [Architecture decisions](docs/DECISIONS.md) and [project checkpoint](docs/RESUME.md)
- [Launch animation evidence](specs/014-first-licensed-scene/evidence.md) and [bilingual client evidence](specs/018-bilingual-client/evidence.md)
- [Isolated Studio checks](specs/009-application-management/quickstart.md) and [desktop shortcut checks](specs/011-managed-shortcuts/quickstart.md)
