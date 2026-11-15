# F00 verification evidence

**Status:** F00 feasibility checks passed on the available Windows desktop. This is not a release compatibility certification.

| Check | Result | Evidence / notes |
|---|---|---|
| .NET 10 Release build | Pass | SDK 10.0.403; `dotnet build -c Release` yielded 0 warnings, 0 errors. Self-contained win-x64 single-file publish succeeded. |
| Format/static check | Pass | `dotnet format ... --verify-no-changes --no-restore` exited 0; analyzers and warnings-as-errors enabled. |
| Invalid arguments | Pass | `--mode invalid` printed usage and exited 2 without a window. |
| Centered, Esc exit | Pass | [Centered screenshot](screenshots/centered.png); Esc removed the window from the desktop window list and the process exited. |
| Full, button exit | Pass | [Full screenshot](screenshots/full.png); the desktop remained visible through the transparent region; Close removed the window and process. |
| Secondary monitor placement | Pass | [Secondary screenshot](screenshots/secondary-centered.png); centered card and transparent background appeared on `DISPLAY2`; Esc closed it. |
| Invalid monitor fallback | Pass | `--monitor 99` logged fallback to primary screen; primary window measured at left 540, top 321, size 360×210. |
| DPI coverage | Pass for F00 gate | `GetDpiForWindow` measured primary DPI 192 (200%) and secondary DPI 120 (125%). Secondary window rect: left 855, top -768, size 576×336; contained within the secondary display. 100% and 150% remain in the later release matrix. |

## Environment and limits

- Windows 11 x64 build `26200`; two connected displays: primary 1440×900 and secondary 2048×1152 as enumerated by Windows Forms in PowerShell. The displays have different DPI scales.
- Screenshots came from Windows Graphics Capture through the Computer Use tool. The full-mode capture confirms transparent pixels reveal the underlying desktop. The centered window capture may render transparent pixels black because it captures the window surface, so transparency was judged from the full and secondary captures together.
- The prototype does not validate video, Win2D, GPU device loss, application handoff, or 100%/150% DPI. These are later feature/release gates.
