# F20 Windows evidence

Verified on Windows 11 Home x64, build 26200, using Media Foundation Media Engine in frame-server mode. The probe built with zero errors and zero warnings. The clips are generated test patterns. Their video uses 8-bit H.264 at 30 fps and the six-second landscape clip also includes AAC.

| Case | First decoded frame | Window visible | Transferred frames | Result |
|---|---:|---:|---:|---|
| 1920×1080, six seconds | 460 ms | 5,884 ms | 178 | Full aspect ratio and fade at both ends. |
| 720×1280, fit | 289 ms | 5,878 ms | 178 | 303.75×540 frame in a 960×540 stage. |
| 720×1280, fill | 273 ms | 5,877 ms | 178 | 960×1706.67 scaled frame clipped by the stage. |
| 1920×1080, ten seconds | 300 ms | 9,880 ms | 298 | Source reached its 10-second end and the stage closed. |

Each successful case transferred a contrast-bearing frame before the window showed. The first submitted paint used 2.5–5% video opacity and the middle reached full opacity. The last 200 ms included submitted paints below 7% opacity. These values describe decoded data and submitted paints.

The separate verification process observed no target process with the player window visible. All seven cases opened exactly one fixture target after player exit. The Escape key event posted to the owned window led to a skip and the stage closed 20 ms after its Shown event. Corrupt MP4 produced a media error without showing the window. The three-second deadline stopped a deliberately stalled test process and released target startup. The stall was injected in the test worker. The Media Engine detected the AAC track and reported mute enabled.

The installed app remained 0.19.0. A Git comparison against `31b3cc8` found no changes under `src`, `tests`, or `installer`, including both code-work packages. F21 still needs the packaged playback path, measured audio output, broader decoder coverage and explicit media-clock alignment at the first visible frame. The WIC result applies to this workstation.

## Reproduce

Use a local FFmpeg executable that supports `libx264` and AAC. The verified run extracted the executable from the Windows x64 `imageio-ffmpeg` 0.8.1 wheel. The generator and binary were used for fixtures only. [Wheel source](https://pypi.org/project/imageio-ffmpeg/0.8.1/)

```powershell
& .\specs\021-mp4-feasibility\New-Fixtures.ps1 -Ffmpeg $ffmpeg -OutputDirectory artifacts/f20/repro-fixtures
dotnet build .\specs\021-mp4-feasibility\probe -c Release
dotnet build .\src\DesktopMotionStudio.WindowHost.Poc -c Release -r win-x64
$probe = '.\specs\021-mp4-feasibility\probe\bin\Release\net10.0-windows\Mp4MotionProbe.exe'
$fixtures = (Resolve-Path artifacts/f20/repro-fixtures).Path
$target = (Resolve-Path .\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe).Path
& $probe --verify $fixtures $target
```

The final local records are `artifacts/f20/build-final.log` and `artifacts/f20/verify-reviewed.log`.
