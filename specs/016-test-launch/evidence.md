# F15 Verification Evidence

**Date** 2026-11-07 · **Host** Windows 11 Home x64, non-elevated workstation session · **Package** 0.15.0, unsigned

The original WPF regression failed because the home action was missing. After implementation, 11 focused WPF checks passed. They cover canonical arguments, duplicate clicks, stale selection, native use with disabled motion, unavailable bindings, bounded waiting, component failure, window closure and unchanged state.

The unpackaged payload and the actual 0.15.0 installation each passed seven native checks. Alt+T opened one test target and handed off the animation. A warm request reused the same target process. Removing the test renderer temporarily opened the target with native feedback, and the exact image was restored. The profile and backup remained byte-for-byte unchanged during each run. The subsequent isolated managed Shell Link produced the same target and animation results and was removed safely.

The installed run started Studio through the real Start menu and used the installed entry shim. The shim forwarded the actual result for the test request. After the test, the registered uninstaller cleared the product slots and the test-only profile and diagnostics were archived. The initial profile baseline was refreshed between runs after its test preferences changed. Each run still verified its own unchanged profile and backup hashes. The synced Desktop was left untouched.

Release regression passed 542 Core, 78 Studio and 10 MotionHost checks with zero failures. The optional packaged-instance check was skipped in the full run. The actual 0.12.8 → 0.15.0 published payload pair passed all seven isolated lifecycle checks, covering rollback, retry, stale installer refusal, link and profile retention, removal and reinstall.

The candidate's SHA256 is `885A18265BD4B63E938E257F6C87BBA232D57684F51DEEB299E0551AAB0D094B`. Local evidence and the candidate are under `artifacts/f15-20261107`.
