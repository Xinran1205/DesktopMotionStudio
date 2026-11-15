# F09 Validation Guide

F08 milestone is `90b70c9`. Use the pinned .NET 10 SDK and `DOTNET_ROOT` setup from F08 quickstart. Never run acceptance against real user state: Studio must receive unique absolute `--state-file`/`--catalog-root` fixture paths.

First build/run `poc/SharedPlaybackPoc.csproj --checks`, then run Preview and Launch-simulation windows. Observe real changing output, normal exit and Esc. Simulation is only a shared-renderer feasibility test, not F06 production handoff proof. Keep T001 open until actual evidence is recorded.

```powershell
dotnet build .\specs\010-scene-preview-binding-ui\poc\SharedPlaybackPoc.csproj -c Release -r win-x64
$poc = '.\specs\010-scene-preview-binding-ui\poc\bin\Release\net10.0-windows\win-x64\SharedPlaybackPoc.exe'
& $poc --checks
& $poc --owned-job-checks
& $poc --mode preview
& $poc --mode launch-simulation
& $poc --mode preview --reduced
```

The GUI experiment exits after8s or Esc/Stop; launch it visibly for inspection. A Shown event alone does not prove an actual painted window. `--reduced` injects a still branch and is not evidence of actual OS-preference acceptance. Job checks terminate only owned fixture processes and verify an unassigned fixture survives; the atomic case deliberately keeps the initial thread suspended to test early parent death.

Reader/timeline checks now run through Core Checks:

```powershell
dotnet build .\tests\DesktopMotionStudio.Core.Checks -c Release
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f09-checks
dotnet build .\src\DesktopMotionStudio.Studio -c Release -r win-x64
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f09-catalog-check "$PWD\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\win-x64\Scenes"
```

The last mode verifies the actual copied original development work. It can also inspect a publish output's Scenes directory; it neither starts playback nor writes configuration.

Before production preview, prove owned-job cleanup with a hung owned worker and terminated owning parent; verify no target process joins the job. Contract checks then exercise strict sidecars, safety limits/fallback, interpolation/reduced motion and explicit IPC v2. Keep old manifest/state formats and F00–F08 regressions protected.

Final acceptance: browse two fixtures; preview without an app/target dispatcher or profile writes; bind one work to two app fixtures; replace one with stable IDs and unrelated state preserved; cancel and exercise removed app/scene, Busy/I/O and post-save reload failure. Prove single-preview/stale/Stop/crash/parent exit behavior with actual host PIDs/HWNDs. Repeat keyboard/visual checks at actual100/125/150% and both system motion preferences, restoring all starting settings. Do not infer these results from unit tests or a successful build.

Production ownership/surface checks:

```powershell
dotnet build .\tests\DesktopMotionStudio.MotionHost.Checks -c Release
dotnet .\tests\DesktopMotionStudio.MotionHost.Checks\bin\Release\net10.0-windows\DesktopMotionStudio.MotionHost.Checks.dll
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f09-ownership-checks
$env:DMS_F06_HOST_PATH = "$PWD\src\DesktopMotionStudio.MotionHost\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.MotionHost.exe"
$env:DMS_F06_TARGET_PATH = "$PWD\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe"
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f09-playback-process-checks
```

Build the host/target Release win-x64 outputs first. Core's no-argument run includes these checks; optional actual integrations report explicit SKIP when their fixture environment is absent. The new drawing checks instantiate the production window/control, compare fixed-time pixels and inject both motion policies; actual system preferences/scaling remain separate desktop acceptance.

Controlled preview races run with Studio Checks `--preview-races`; the normal Studio Checks run includes them and existing WPF/discovery regressions. Core's `--f09-playback-process-checks` now additionally creates/replaces/stops three real coordinator-owned hosts. These gates precede Studio UI wiring; they do not exercise a library or binding picker yet.

Final external UI mode: build Studio/Studio.Checks, set the same isolated target variables, then run Studio.Checks `--f09-ui`. It creates and deletes only owned temporary state/catalog/target fixtures. `DMS_F09_WAIT_FOR_FOCUS=1` provides a45s activation gate when Windows foreground policy requires external activation; `DMS_F09_PAUSE=library` holds45s for visual inspection. `DMS_F09_EXPECT_ANIMATIONS=0|1` asserts the actual read-only OS preference. Tests never alter system settings; actual DPI/motion acceptance uses native Settings separately and restores starting values.

Repeat against a fresh `dotnet publish ... --self-contained false -o <owned-temp-output>` by pointing DMS_F08_STUDIO_PATH at that output. Studio includes its MotionHost subdirectory; no separate host-path override is needed. Core `--f09-playback-process-checks` includes exact owned real renderer crash/UI-thread-hang checks. Full evidence and the explicit optional packaged-instance skip are recorded in evidence.md.
