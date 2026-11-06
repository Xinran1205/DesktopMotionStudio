# F10 Validation Guide

Use the SDK in global.json and a normal Windows 11 x64 session. On the development workstation set DOTNET_ROOT to C:\Users\AAA\.dotnet and use that directory's dotnet.exe. Keep temporary desktop, receipts and state isolated.

1. Run the native ownership proof in the Core checks, covering occupied destination, file ID, edits, hard links, symlinks, and verified delete.
2. Build Core checks, Studio checks and Studio Release. Run the F10 contract checks and complete existing regressions.
3. Publish Studio to a fresh win-x64 framework-dependent directory. Verify Launcher and MotionHost dependency files and Scenes. Use isolated state and desktop fixtures. In a disposable Windows user session, exercise the production known-folder defaults where available.
4. Use the keyboard to create an entry from an enabled binding. Repeat, close Studio, activate the actual .lnk, observe target and renderer, replace the binding, retest and then disable motion and retest native launch.
5. Exercise missing-file repair, same-name collision, edited-file refusal and owned removal. Compare state/backup and unrelated file bytes, and record process cleanup.
6. Record exact commands, environment, PASS/FAIL/SKIP counts and unverified limitations in evidence.md.
