# F21 Windows verification

Use Windows 11 x64 with desktop animations enabled for the timing check and the SDK pinned in `global.json`. Run the desktop checks sequentially because they exercise focus and Escape on owned windows. They use isolated profiles, catalogs and shortcut directories.

```powershell
$package = Join-Path $PWD 'artifacts/f21/local-check'
$fixtures = Join-Path $PWD 'artifacts/f21/local-fixtures'
& .\installer\Build-Setup.ps1 -Version 0.20.0 -OutputDirectory $package
& .\specs\021-mp4-feasibility\New-Fixtures.ps1 -Ffmpeg $ffmpeg -OutputDirectory $fixtures
& $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=320x180:rate=30:duration=2' `
    -an -c:v mpeg4 -movflags +faststart (Join-Path $fixtures 'unsupported.mp4')
& $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=320x180:rate=2:duration=2' `
    -f lavfi -i 'sine=frequency=440:duration=2' -c:v libx264 -pix_fmt yuv420p -c:a aac (Join-Path $fixtures 'low-rate.mp4')
$env:DMS_F08_STUDIO_PATH = Join-Path $package 'payload/DesktopMotionStudio.Studio.exe'
$worker = Join-Path $package 'payload/MotionHost/DesktopMotionStudio.VideoHost.exe'
$checks = 'tests/DesktopMotionStudio.Studio.Checks'
dotnet run --project $checks -c Release -- --f21-video $fixtures
dotnet run --project $checks -c Release -- --f21-video-timeouts (Join-Path $fixtures 'landscape.mp4')
```

The first command after the build exercises the actual Studio preview and worker. The timeout check uses a controlled stalled worker. The optional sound check uses the six-second AAC test clip and reads Windows playback peak meters on an idle output device. It uses a test tone and does not record audio.

```powershell
dotnet run --project specs/022-mp4-playback/audio-check -c Release -- $worker (Join-Path $fixtures 'landscape.mp4')
```

For changes to the launch path, set `DMS_F06_TARGET_PATH` and `DMS_F10_FIXTURE_LAUNCHER_PATH` as in the [managed shortcuts quickstart](../011-managed-shortcuts/quickstart.md). Build the Launcher fixture as self-contained x64 with runtime 10.0.14 when testing the self-contained payload. Run `--f21-launch-ui <delayed-window-fixture.exe> <fixtures>` in Studio Checks for the real test launch and Shell Link. Use `--f13-preview-ui`, `--f13-launch-ui` and `--f13-legacy-launch-ui` when the shared orchestration changes.

For accessibility checks, turn off **Settings → Accessibility → Visual effects → Animation effects**, run `--f21-reduced-motion <landscape.mp4>`, then restore the previous Windows setting.
