# F15 Test Launch Verification

1. On a Windows 11 x64 workstation, open Studio from a published bundle or from the installed Start menu entry.
2. Add a benign local test application and bind an included work. Press **Alt+T** in **我的应用**. Confirm the target opens and Studio reports the result. Press it again while the target remains open and verify the same window is reused.
3. On the test copy, close the target and repeat with motion disabled or the renderer unavailable. Check that the target opens and Studio reports the native result. Restore the test renderer after the probe.
4. Compare the profile and backup hashes and check the test Desktop. Only normal launch diagnostics should be added. Compare the target and animation outcomes with a deliberately created managed Shell Link.

Run the automated WPF and process checks with the pinned SDK:

```powershell
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-entry-checks
```

The optional native check takes a verified test fixture directory, a test Studio PID and the expected Studio executable. It requires a test-only profile with one application ID `f15-fixture`, an enabled binding ID `f15-native-test-fixture`, and the test target `DmsF15AcceptanceTarget.exe` inside that fixture. Run it in the ordinary Windows session. A synced Desktop stays outside the test. Explicit Studio profile and catalog overrides hide **测试启动**, so routine UI fixtures cannot open a live user's bindings.
