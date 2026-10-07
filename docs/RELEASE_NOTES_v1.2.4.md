# ForgerEMS v1.2.4 Release Notes

These notes describe the current `v1.2.4` candidate for Windows technicians. **The artifacts currently built from this state are unsigned, non-production candidates**: the `1.2.4` version number does not imply production certification, and the updater's stable-only default is a channel-selection policy — it does not mean any artifact is signed or production-authenticated.

ForgerEMS is a local-device support workflow tool, USB toolkit/profile builder, Driver Hub/vendor guidance helper, and port/USB mapping and drive-check workflow helper by Forger Digital Solutions.

## Highlights

- The in-app Kyra assistant (chat, online providers, gateway, slash commands, and related settings/memory surfaces) has been completely removed. Assistant configuration and memory files previously saved on the device are left untouched and are no longer read.
- The update checker discovers releases by semantic version — the highest eligible stable (or explicitly opted-in prerelease) tag/name — rather than publication date. It defaults to stable releases only, ignores drafts, and rejects malformed release metadata. Persisted beta/RC channel preferences are honored.
- The build version is sourced from the repository `VERSION` file as the single version authority; app, installer, and diagnostics display one consistent version.
- Update downloads verify an expected SHA256 taken from trusted same-release metadata and follow only trusted redirects. Executable update installers require exact publisher and chain validation that fails closed; managed ZIP extraction uses safe path validation.
- Driver/vendor resources are discovered through dynamic metadata, with manual official-page exceptions where safe automated verification is unsupported. Resource resolution corrections verified for this candidate: two-part vendor tags (e.g. Rufus `v4.15`) resolve via a resource-provider fallback while the app semantic parser stays strict, Ubuntu image selection now picks the newest unique same-family point release with ambiguity/foreign-family fail-closed behavior, and the System Informer asset pattern matches the current official `-bin.zip` layout.
- Windows Update and Driver Store inventory is read-only. Hardware-ID candidate correlation is advisory only — ForgerEMS does not automatically install or remove drivers.
- `System.Management`, `System.IO.Ports`, and `System.Threading.AccessControl` were updated to 10.0.12.
- The optional sensor stack lifecycle is serialized, and a missing sensor provider degrades neutrally. The historical CLR abort observed during test runs has no proven cause; the defensive changes shipped are hardening, not a claimed fix.
- Production packaging is gated on signing: the app executable, installer, and captured signed uninstaller are each signed and verified during the build, and production fails closed without credentials. An explicit unsigned-candidate mode exists for local builds and marks artifacts non-production.
- The packaged documentation set was expanded: legal notices, third-party/sensor notices (including the transitively resolved `System.CodeDom` 10.0.12), corresponding-source and LGPL-component replacement guides, and updated quickstart/FAQ/release documentation.
- The sidebar navigation is keyboard-focusable with a visible focus indicator, and the Driver Hub overflow toggles now expose a readable automation name ("More driver tool actions").
- Driver Hub now labels its hardware card as a **saved local device snapshot**, notes that it may be stale, and still performs no driver-version comparison. System-intelligence report paths resolve under the app runtime root, so isolated runtime profiles no longer read another profile's reports.
- Signing and installer-lifecycle automation is prepared but **not executed**: a protected-environment release workflow provisions an authorized identity in memory only, and a dispatch-only lifecycle workflow is authored for hosted-runner validation. No signed artifact or installer-run proof exists yet.

## Downloads

Candidate artifacts exist only under the local `.verify/v1.2.4-final-certification` workspace. **No public v1.2.4 release has been published.** Final hashes and provenance ship with each release in `CHECKSUMS.sha256` and `release.json`; verify hashes before use. Never treat or describe unsigned artifacts as production-authenticated.

## Known Limits

- No authorized signing credential is provisioned, so current artifacts remain unsigned candidates.
- Isolated install, upgrade, and uninstall of the packaged installer have not been proven; no disposable VM/sandbox environment is available.
- UI, DPI-scaling, and accessibility coverage is partial: UI Automation exposes only shell-level controls, screenshots were captured at 100% DPI, and 125%/150% scaling is untested.
- The historical CoreCLR `0x80131506` abort seen in earlier test runs has no proven root cause, even though repeated full-suite runs now pass.
- Some resource links remain unresolved because vendor sites challenge automated checkers; they stay marked manual/unverified rather than being replaced with unproven mirrors.
- PawnIO module/driver trust restrictions, LGPL relinking scope, and Mono/SDK licensing details remain under legal review; shipped replacement guidance is technical, not legal certification.

ForgerEMS is not enterprise support software, a magic automatic fixer, a certified repair substitute, attorney-reviewed legal guidance, a complete hardware telemetry suite, or a hardware stress/thermal/fan diagnostic tool. Driver/vendor guidance is informational and vendor-first. Some features rely on internet access, vendor sites, manual downloads, user-supplied files, system permissions, or third-party licenses.

## Issue Reporting

Report issues with exact app version, Windows version, steps to reproduce, expected result, actual result, and redacted logs/support bundles only after review. Do not send secrets, private customer data, product keys, recovery keys, API keys, or private documents.
