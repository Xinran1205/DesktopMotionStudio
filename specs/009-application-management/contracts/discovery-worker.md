# Discovery worker contract

Studio accepts one internal `--discovery-worker` invocation with no user text. Its STA worker reads current-user/shared Start Menu launch links and explicit packaged AppsFolder metadata, emits one bounded UTF-8 JSON result, then exits. Normal UI process never executes discovered entries.

Parent creates only its own executable using discrete arguments, captures stdout/stderr without presenting raw diagnostics, enforces 5s/1MiB/2000-candidate limits, validates kind/shape/name/target fields and handles cancellation. Worker timeout/cancellation terminates only the owned worker. Result is failed rather than accepting truncated/unsupported output. Manual file selection remains available for all failures.

UI refresh cancels an earlier discovery request and discards its stale result. Closing Add cancels discovery. No stdout path/title/content unrelated to launch entries is persisted in diagnostics.
