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

Before production preview, prove owned-job cleanup with a hung owned worker and terminated owning parent; verify no target process joins the job. Contract checks then exercise strict sidecars, safety limits/fallback, interpolation/reduced motion and explicit IPC v2. Keep old manifest/state formats and F00–F08 regressions protected.

Final acceptance: browse two fixtures; preview without an app/target dispatcher or profile writes; bind one work to two app fixtures; replace one with stable IDs and unrelated state preserved; cancel and exercise removed app/scene, Busy/I/O and post-save reload failure. Prove single-preview/stale/Stop/crash/parent exit behavior with actual host PIDs/HWNDs. Repeat keyboard/visual checks at actual100/125/150% and both system motion preferences, restoring all starting settings. Do not infer these results from unit tests or a successful build.
