# F24 Windows acceptance

The 0.23.0 self-contained candidate opened the guide from the Motion library in Simplified Chinese and English. Changing the concept, style and duration updated the brief. Both language passes copied the displayed brief exactly to the Windows clipboard. The fixture profile stayed byte-identical. Clipboard contention is handled by a localized message and text selection. The acceptance run exercised the successful copy path.

The authored six-second Orbit Ignition example follows the brief's export rules. The F24 test imported its shipped MP4 into an isolated profile, bound that copy to two real test apps, previewed the bound copy and exercised Test launch and a Windows shortcut. The preview left the target closed. Each cold launch waited for the video, and warm launch reused its window. The same shortcut kept working after rename and launched normally after removal.

The independent review checked the UI, localization, clipboard handling and sample. The final fixture restores the clipboard only when it still contains the exact test brief, follows the workstation's decimal formatting and tests the same imported asset through preview and both launch routes.

The Release build passed with zero warnings and errors. The installation check verified 885 payload files and the active version selector. The isolated installer lifecycle passed install, failed-upgrade rollback, upgrade from 0.21.0, uninstall and reinstall. The existing shortcuts and profile survived the upgrade.

The unsigned 0.23.0 installer and checksum are under `artifacts/f24/verified-0.23.0/setup`. Test logs are under `artifacts/f24`. See the [verification steps](quickstart.md).
