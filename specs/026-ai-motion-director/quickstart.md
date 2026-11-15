# Try AI Motion Director locally

From the repository on Windows 11 x64, use the SDK in `global.json`.

```powershell
dotnet build src/DesktopMotionStudio.Studio -c Release
& .\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\DesktopMotionStudio.Studio.exe
```

1. Open Motion Library → AI Motion Director. Enter `Docker` and leave the idea blank. Click Generate instruction. Verify that the result contains the complete A–E contract and a default idea requesting an original app-specific concept.
2. Add `一只可爱的机械鲸鱼从未来数字海洋中跃起，最后组成 Docker Logo，要科技感很强也有趣。` and generate again. Check that the idea appears in full in the final data block. Try `微信` and `原神` to check Unicode names.
3. Use Copy instruction or Download instruction. A canceled save leaves the destination empty. Compare the displayed text with clipboard contents and with the saved Markdown.
4. Review the handoff with Docker alone, Docker and the whale brief, and a different app such as 微信. Expect B to name the verified source, asset and actual input, C to match the selected route, and E to require three visual checks. For a text-only video tool, expect separate asset compositing. In a browsing-disabled session, expect an explicit draft and the exact missing reference.
5. To evaluate video quality, use an external AI with browsing for the brief, then supply the actual B reference file to the specified video input or editor. Paste C into the video generator. Compare the first full subject view, climax and final mark with the source after export.

Studio Checks accepts `--f24-director` for the composer and `--f24-director-ui` for the Windows UI. Set `DMS_F08_STUDIO_PATH` to the freshly built Studio executable before running the UI check. Use `--f22-import-ui <fixtures>` and `--f24-sample-launch <delayed-target.exe>` for the focused media regression. See the [F21 quickstart](../022-mp4-playback/quickstart.md) for fixture paths.
