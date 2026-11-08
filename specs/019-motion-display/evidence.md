# F18 verification

The Windows 11 x64 fixture saved all three explicit sizes without restarting Studio. Native windows measured 760×420, 1120×630, and the full 2560×1440 monitor. Both windowed layouts were centered within the work area. Esc closed each preview. Cancel, restart, an invalid token and a separate profile retained their expected settings.

The Launcher entry implementation used the saved full-screen placement with Studio closed and an isolated profile. Profile and backup bytes were identical. IPC v4 round-tripped all three effective placements and rejected mismatched protocol versions. `artifacts/f18` holds the local logs.

Independent review found that percentage-based large placement could become smaller than the capped compact placement on a narrow monitor. Compact now scales from the available large placement before applying its cap. Geometry checks cover 800×600 and an offset 720×1280 work area.
