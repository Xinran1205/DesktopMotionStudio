# Rebuild and check Orbit Ignition

Use Windows with the SDK in `global.json` and an FFmpeg executable that provides libx264. The authoring program writes one 1920×1080, 30 fps H.264 video and a PNG from frame 82. Use a new output directory for each render.

```powershell
dotnet run --project specs/024-first-mp4-work/authoring -c Release -- $ffmpeg (Join-Path $PWD 'artifacts/f23/rebuild')
```

Use Studio Checks with the packaged Studio path from the [F21 quickstart](../022-mp4-playback/quickstart.md). Run `--f23-orbit-ui` to inspect and import the shipped bytes and check Chinese and English previews. Run `--f23-orbit-launch <delayed-target.exe>` with the isolated Launcher and target paths to verify a real Test launch and desktop shortcut.
