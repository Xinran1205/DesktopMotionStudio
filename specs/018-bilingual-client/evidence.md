# F17 verification — Windows candidate 0.17.0

Verified on Windows 11 Home x64, build 26200, using isolated application profiles and package registration. The release artifact is the unsigned current-user Setup in `artifacts/f17/candidate-0.17.0/setup`. The local artifact folder also contains its checksum. SHA-256 `1A714AA9414E31E6C3C2634C0BF9164483EFD18C5E5BC6CE5E78A195AABB5C17`.

| Acceptance | Result |
|---|---|
| SC-001 · GitHub README paths | English occupies `README.md`. Both headings have reciprocal relative links. Both README files and their repository-local link targets resolved. |
| SC-002 · Two language directions on Windows | The self-contained Studio payload opened with the Chinese fallback. Settings saved English and then Chinese across restarts. The English home and library showed Zero Boundary, and the actual preview exposed a keyboard-focusable English Stop button. A saved app also named 零界 kept its exact name. |
| SC-003 · Preference and existing data | Missing, invalid and unreadable preferences fell back to Chinese. Cancel preserved the existing file. Profile and backup bytes stayed identical after both language changes and preview. Isolated upgrade preserved an existing Shell Link byte for byte. |

## Reproduce the language check

Set the paths to the self-contained Studio payload and the Windows test target, then run the dedicated Windows check.

```powershell
$env:DMS_F08_STUDIO_PATH = (Resolve-Path artifacts/f17/candidate-0.17.0/payload/DesktopMotionStudio.Studio.exe).Path
$env:DMS_F06_TARGET_PATH = (Resolve-Path src/DesktopMotionStudio.WindowHost.Poc/bin/Release/net10.0-windows/win-x64/DesktopMotionStudio.WindowHost.Poc.exe).Path
dotnet run --project tests/DesktopMotionStudio.Studio.Checks -c Release -- --f17-language-ui
```

## Regression coverage

- 357 matching bilingual resource keys, 328 resolved static references and identical formatting placeholders.
- Core 550, Studio 81, MotionHost 22, Chinese preview 13 and Windows launch overlay 27 checks passed. Logs are in the local `artifacts/f17` directory.
- The bilingual desktop check passed three consecutive runs on the final payload after the fixture was adjusted to wait for the preview window before inspecting its owner.
- Seven isolated 0.16.0 → 0.17.0 package lifecycle checks passed, covering upgrade, rollback, shortcut and profile preservation, uninstall and reinstall.
- The optional packaged-target scenario needed an AUMID fixture. The separate desktop reduced-motion handoff scenario needed a different Windows animation setting. Both were skipped in this run. MotionHost drawing checks exercised reduced-motion rendering.

The independent review identified fixed-language accessibility names and the ordering of language save relative to theme application. The renderer names now follow the selected locale, with their private window identifiers intact. Settings saves the language independently of applying the theme, and reports any write failure. The review found no changes to shortcut ownership or the launch and playback data contracts.
