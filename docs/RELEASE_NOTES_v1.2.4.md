# ForgerEMS v1.2.4 Release Notes (provisional — source tree state)

ForgerEMS `v1.2.4` is a stable-channel build for Windows technicians.

ForgerEMS is a local-device support workflow tool, USB toolkit/profile builder, Driver Hub/vendor guidance helper, and port/USB mapping and drive-check workflow helper by Forger Digital Solutions.

## Highlights

- The in-app Kyra assistant (chat, online providers, gateway, slash commands, and related settings/memory surfaces) has been removed. Assistant configuration and memory files previously saved on the device are left untouched and are no longer read.
- The update checker now selects releases by semantic version (highest eligible tag/name), defaults to stable releases only, ignores drafts, and never offers malformed release metadata. Persisted beta/RC channel preferences are still honored.
- The build version is sourced from the repository `VERSION` file; app, installer, and diagnostics display a single consistent version.
- `System.Management` was updated to 10.0.12.

## Downloads

`release/current` artifacts are not built from this source state yet and remain unsigned candidate artifacts (no code-signing certificate is provisioned); `CHECKSUMS.sha256` will cover whatever is produced. Verify hashes before use.

## Known Limits

ForgerEMS is not enterprise support software, a magic automatic fixer, a certified repair substitute, attorney-reviewed legal guidance, a complete hardware telemetry suite, or a hardware stress/thermal/fan diagnostic tool. Driver/vendor guidance is informational and vendor-first. Some features rely on internet access, vendor sites, manual downloads, user-supplied files, system permissions, or third-party licenses.

## Issue Reporting

Report issues with exact app version, Windows version, steps to reproduce, expected result, actual result, and redacted logs/support bundles only after review. Do not send secrets, private customer data, product keys, recovery keys, API keys, or private documents.
