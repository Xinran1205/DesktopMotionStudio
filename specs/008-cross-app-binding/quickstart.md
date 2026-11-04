# F07 Validation

From repository root in Windows PowerShell with the pinned .NET SDK available:

```powershell
dotnet build .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f07-checks
```

For live cross-app proof, build the F06 MotionHost and WindowHost POC, then set `DMS_F06_HOST_PATH` and `DMS_F06_TARGET_PATH` as documented in the F06 quickstart. The F07 runner copies the complete POC output into two unique temp directories, creates two app fixtures, binds one scene to both, resolves each typed target and launches each through F06/F05. It verifies distinct target PIDs/HWNDs and closes only the owned fixtures. With these variables unset the live case is explicitly skipped; contract and two-process storage checks still run.

The test worker mode is internal to the harness; it never edits real user state. All state/catalog fixtures use unique temporary directories.
