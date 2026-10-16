# MicNeverDown 1.3.0 validation

Checked on October 16, 2026. By **arahizok**.

## Results

- **30 core tests passed.** Fake audio endpoints cover volume enforcement, hardware rounding, boundaries, pause/resume, exact device selection, reconnect timing, errors, mute preservation, legacy XML compatibility, validation, DTD rejection, size limits, atomic writes and concurrent storage access.
- **13 isolated localization scenarios / 105 checks passed.** These cover the English default, English and Russian preferences, malformed and invalid preferences, safe fallback, persistence before change notification, failed-write behavior, legacy microphone settings, live settings-window updates, tray labels and checkmarks, unsaved edits, accessibility and numeric precision. Pending decimal text is parsed using the previous language before it is reformatted, including comma-to-dot and dot-to-comma transitions.
- **7 CLI integration checks passed.** Background launch with a missing fake endpoint, one-instance enforcement, pause/resume, rejection of configuration changes while running, clean stop, and unchanged real startup state.
- **Two builds matched byte-for-byte.** SHA-256 for the EXE is in `SHA256SUMS.txt`; input hashes and tool versions are in `build-info.json`.
- **The installed copy was updated successfully.** It reached `Locked` with matching target and actual levels. Its microphone settings and startup shortcut remained byte-for-byte unchanged.
- **The English settings screenshot was rendered from the built application and visually checked.** It uses the sample device label `Headset Microphone`; it does not contain the developer's desktop, account, device identifier or real microphone name. It is an interface preview, not evidence of a hardware test.

The core and localization tests use fake providers or read-only enumeration with stub save callbacks. They do not record sound or change the real microphone volume or startup shortcut. The installed guard continues to enforce the user's previously configured level.

## Package checks

The source and release packages are checked for unexpected runtime settings, private paths and stale release names. They contain no `data` directory, `settings.xml`, `ui.xml`, `status.xml`, error log, Windows shortcut, debugging symbols or personal backup. The source archive has its files at the root, ready for a new GitHub repository.

Public documentation and release prose are in English. Russian strings remain in the source code and localization tests because the application supports Russian. Names and messages supplied by Windows or a device driver can follow the operating system's language.

## Limits

Physical USB disconnection/reconnection, Windows reboot, testing on a separate high-DPI monitor and antivirus scanning of this new EXE were not performed. The EXE is unsigned. Earlier VirusTotal results belong to different files and do not apply to this build. Checksums and deterministic builds establish file identity and build provenance; they do not guarantee that software is free of bugs or malicious behavior.
