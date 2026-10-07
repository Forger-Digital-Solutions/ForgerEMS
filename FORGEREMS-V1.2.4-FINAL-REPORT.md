# ForgerEMS v1.2.4 Final Report

Date: 2026-10-07

## Executive Result

Production release closure is **BLOCKED**. The engineering candidate is substantially
complete, but authorized production signing and a usable isolated Windows installer
environment are unavailable. Clean install, previous-version upgrade and uninstall
are not proved. No publication, push, host driver replacement or host installation
was performed.

This pass actively fixed signing entrypoints/uninstaller handling, packaged notices
and documentation, confirmed stale links, startup diagnostic isolation, and keyboard
navigation. It preserved the completed modernization rather than restarting it.
Repeated tests and portable execution are evidence, not production certification.

The final artifact build follows the commit containing this report. Its exact HEAD,
hashes, sizes, signatures and commands are recorded in the final evidence files
identified below. This avoids a self-referential report hash and prevents treating
validation-candidate hashes as final artifact hashes.

## Repository State

- Repository: `I:\ForgerEMS_App\repo`.
- Branch: `release/v1.2.4-modernization`; origin:
  `https://github.com/Forger-Digital-Solutions/ForgerEMS.git`.
- Implementation checkpoint: `5e77cf630550bac7f439f4cfa5867ce65e22e8f3`.
- Final artifact source HEAD: read `release/release.json` and
  `final-build-attestation.txt` under `.verify/v1.2.4-final-certification`.
- Require source HEAD equal the final repository HEAD and dirty count zero;
  a mismatch invalidates final-artifact provenance.
- Evidence and old outputs are ignored, preserved, and not silently substituted
  for fresh results. No unrelated working changes were reverted.

## Continuation Baseline

Direct baseline verification found clean branch HEAD
`bf917f5c2af2133e3b613741b40fdcc9cdb1e109`,
message `Record v1.2.4 candidate evidence and partial certification`.
There were zero staged/modified/untracked files. Ignored artifacts were present.
`evidence/git-baseline.txt` records the branch, commit, remotes and ignored sample.

The preceding candidate was built from
`e8820b8953c2da27c20f1dd6c7bac57eada4d5f8`; the intervening baseline commit
changed the report only. Artifact versions, metadata, signatures and hashes were
rechecked, not accepted on the strength of the prior report.
Evidence: `evidence/artifact-provenance.txt`.

## Previous Partial Gates

Closed available engineering gaps: LocalMachine signing-store selection, explicit
unsigned entrypoint guards, verified uninstaller capture, callback quoting,
fail-closed Git provenance, missing packaged documentation/licensing guidance,
confirmed broken URL replacements, and inaccessible keyboard navigation.

Still open: trusted signing, isolated installer lifecycle, complete UI/DPI coverage,
historical crash attribution, all-link verification, some resource adapters, and
redistribution/relinking legal questions. Advisory driver behavior is a deliberate
safety boundary, not an unimplemented promise of automatic installation.

## CLR Abort Investigation

The saved original output records 699 passes, 29 assertion failures and an internal
CLR `0x80131506` abort. The original TRX was subsequently overwritten and cannot
establish crash-time test ordering.

Windows telemetry independently identifies testhost.exe faulting in CoreCLR 8.0.31,
exception `0xc0000005`, offset `0x1064f0`, PID 27688, WER bucket
1642125279669840107. Preserved dump: `evidence/testhost.exe.27688.dmp`,
29,289,100 bytes, SHA256:
`164CF17C519D5B9469168942CF302880271B8B28C2458FE1E1DEA2957AA104C`.

dotnet-dump 8.0.547301 loaded the dump and produced thread/managed-stack output.
Concurrent stacks include intelligence/sensor orchestration, overlay and download
tests. They do not identify the faulting native instruction or prove causality.
`eestack` is unsupported by this tool; the final command exits 1 and is not
represented as a successful native debugger diagnosis.

The process-global sensor lifecycle race is a credible mechanism addressed by
serialization, not a proven historical cause. WinVerifyTrust layout, native memory
ownership/state-close ordering and flags were reviewed. WUA COM runs in a separate
PowerShell process. Neither is blamed without discriminating evidence.

Classification: historical native runtime/test-process crash, root cause unknown,
not reproduced in the recorded later complete runs. Non-reproduction does not
establish a non-product cause. Evidence: `evidence/CLR-lead-investigation.md`,
`evidence/CLR-dump-lead-review.md`, `evidence/historical-dump-analysis.txt`,
event-log/ WER captures and the separate TRX/logs.

## Kyra Final Audit

Runtime, UI, SDK, gateway, configuration and package surfaces remain removed.
No obsolete test infrastructure was restored. Historical user files are not
unnecessarily deleted and are no longer read by the retired assistant.
The validated ZIP has zero Kyra-named entries; final inventory is recorded
separately with final artifact evidence.

`evidence/final-source-residuals.txt` classifies meaningful remaining categories:
historical docs/release notes/audits; negative removal tests; current statements
that the feature was removed; local-host rejection tests/policy; placeholder
rejection; original license prose; and names such as Process Hacker.
`autodownload` also matches the broad case-insensitive TODO substring search.
Actual remaining TODOs include future port-power deep sensors and unenforced
Pro-license verification; neither is a completed v1.2.4 capability.

## Version Consistency

`VERSION` is authoritative: 1.2.4. Validated app file version is 1.2.4.0 and
product version 1.2.4; installer uses the same version authority.
Backend date-version is separately 2026.10.06.1 and the packaged self-test verifies
frontend/backend alignment. Candidate channel `preview` and unsigned flags do not
change the semantic app version to an obsolete preview number.

## Published Release Truth

Official release evidence identifies `v1.2.3-preview.1`, prerelease, published
2026-07-02T22:48:18Z. No published stable release or public v1.2.4 was found.
Assets: prior installer, portable ZIP, CHECKSUMS.sha256, release.json and download
instructions. Remote tag points to `c98feeb769380db7b1c3b9eb1518a78f1dbf9351`;
local historical tags differ. Evidence:
`evidence/github-release-v1.2.3-preview.1.json` and prior remote-tag capture.
Public release state is independent of the local candidate.

## Updater Security

Highest eligible semantic version wins; drafts, malformed metadata, wrong
architecture and untrusted URLs are excluded. Stable-only is the default;
persisted explicit prerelease opt-in remains supported. Older releases do not
authorize a downgrade.

Expected SHA256 originates from trusted same-release metadata/checksums, not a
digest calculated from the downloaded bytes and compared to itself. Metadata
requests are bounded; authentication stays on the GitHub API host.
Executable update installers require chain verification and exact publisher
SimpleName equality. Cached-only revocation fails closed; native DLL resolution
is pinned to System32. Provider flags are 0x1180.

Tests cover prior expiry/schema fail-open cases, exact publisher versus
prefix/suffix/substring, missing/mismatched hashes, redirects, corrupt metadata,
cancellation/offline failures and unsafe extraction. ZIP bytes do not themselves
carry Authenticode. Installation/restart/rollback remain manual.

## Managed Resource Discovery

Frozen live evidence: `evidence/resource-probe-final.json`, source HEAD
134ec32deddb915e6aa305a3750518e0e1c1ffa9, policy SHA256
`9EEB285E900AEDCEEE9034FC26F08EC27A1D6890C3E2C98B465CBC43CD5DDBAC`.
Later signing/report changes do not alter the resolver/policy; use this capture,
not a re-derived network answer. Scope: real resolver classes and packaged policy,
live metadata followed by controlled offline cache fixtures, no payload installation.

All rows target x64 in this probe. Installed versions are unknown/not detected;
no per-tool installed-version capability is manufactured. LTS rows use LTS;
other dynamic rows use stable. Resolved metadata includes expected SHA256, but
artifact download/signature verification was not executed by this probe.

| Dynamic Resource | Official Metadata Source | Captured Version / Result |
| --- | --- | --- |
| Rescuezilla | GitHub rescuezilla/rescuezilla | RequiresUserAction: four matching flavors |
| Ventoy | GitHub ventoy/Ventoy | 1.1.17, ResolvedMetadata |
| Ubuntu desktop | changelogs.ubuntu.com/meta-release-lts | UnableToVerify: two checksum matches |
| Kali | cdimage.kali.org/current/SHA256SUMS | 2026.2, ResolvedMetadata |
| Angry IP Scanner | GitHub angryip/ipscan | 3.10.0, ResolvedMetadata |
| Driver Store Explorer | GitHub lostindark/DriverStoreExplorer | 1.0.26, ResolvedMetadata |
| RustDesk | GitHub rustdesk/rustdesk | 1.5.0, ResolvedMetadata |
| Rufus | GitHub pbatard/rufus | UnableToVerify: ambiguous stable version |
| Etcher | GitHub balena-io/etcher | 2.1.7, ResolvedMetadata |
| Notepad++ | GitHub notepad-plus-plus/notepad-plus-plus | 8.9.8.1, ResolvedMetadata |
| System Informer | GitHub winsiderss/systeminformer | UnableToVerify: asset pattern mismatch |
| PuTTY | the.earth.li/~sgtatham/putty/latest/sha256sums | 0.85, ResolvedMetadata |
| Ubuntu server | changelogs.ubuntu.com/meta-release-lts | UnableToVerify: two checksum matches |
| Debian netinst | cdimage.debian.org/debian-cd/current | UnableToVerify: 20s timeout |
| Debian GNOME | cdimage.debian.org/debian-cd/current-live | UnableToVerify: 20s timeout |
| Debian KDE | cdimage.debian.org/debian-cd/current-live | UnableToVerify: 20s timeout |
| Debian XFCE | cdimage.debian.org/debian-cd/current-live | UnableToVerify: 20s timeout |
| KeePassXC | GitHub keepassxreboot/keepassxc | 2.7.12, ResolvedMetadata |
| TestDisk | cgsecurity.org/testdisk_sha256.txt | 7.2, ResolvedMetadata |
| PowerToys | GitHub microsoft/PowerToys | 0.101.2362.0, ResolvedMetadata |

Of 50 descriptors, live states are 11 ResolvedMetadata, 29 RequiresUserAction,
8 UnableToVerify, 2 Unsupported. There are 20 dynamic descriptors and 30 explicit
official-page exceptions, not 50 automatically updated tools.
Fresh offline cache preserves 11 metadata resolutions; expired-cache fixture
returns those 11 as CachedStale with download eligibility false.

Official-page exceptions requiring user action: SystemRescue, GParted, Clonezilla,
Memtest86+, Linux Mint, CrystalDiskInfo, BlueScreenView, Alpine, VeraCrypt,
Wireshark, Proxmox VE/Backup, Fedora Server/Workstation, FreeBSD, OpenBSD,
Rocky minimal/DVD, Alma minimal/DVD, NetBSD, openSUSE, Arch, Xubuntu, Lubuntu,
Kubuntu, TrueNAS and Parrot. FreeDOS live/USB are Unsupported for requested x64.
Per-row official URLs, failure, cache, integrity, compatibility, install/rollback
and reboot fields remain in the frozen JSON. Unknown fields stay unknown.

Ambiguous matching, changed asset schemas and timeouts remain safe failures, not
silent last-wins choices. Current external resolution coverage is partial.

## Windows Integration

The maintenance probe reports OS edition/version/build/architecture, service
states, reboot indicators, visible policy, driver bindings and WUA offers/history.
It performs read-only search, not update download/install, policy bypass, repair
or a forced feature upgrade. A successful search is not proof of servicing health,
support entitlement, absence of safeguards or a fully current machine.
Prior real-host capture is retained; fresh packaged GUI invocation of the inner
maintenance control was unavailable through the UI Automation client.

## Driver Store

Structured inventory correlates installed device bindings and published INF names.
Prior captured host evidence contains 126 third-party packages and 227 binding
rows; these are dated observations, not fresh measurements or equal scopes.
Bound, boot-critical, critical-class and uncertain packages are protected.
Possible superseded/rollback labels are advisory. RemovalPermitted remains false.
Inventory signer text is not cryptographic verification of a package.

## Driver Applicability Boundary

Bindings expose hardware/compatible IDs, device name, provider, version/date, INF,
reported signed state and package information where available.
Vendor portals are informational. Exact-ID matches against WUA offers identify
candidates, not independently verified OEM suitability, rank, OS/architecture,
signature, recovery or rollback. InstallationPermitted remains false.
No arbitrary vendor driver installation or Driver Store removal is implemented.

## Supply Chain

Transfers bind expected hashes and sizes, restrict HTTPS redirects, use partial
staging and promote only verified data. Extraction rejects traversal, rooted/UNC
paths, ADS, reserved names, symlinks/reparse roots and inflated-size violations.
Backend checksums/version alignment are validated separately.
Portable QA exercised no destructive USB workflow or host-driver mutation.

## Signing

No authorized code-signing certificate/private key is available in accessible
CurrentUser/LocalMachine stores; signing thumbprint configuration is unset.
Windows SDK signtool exists. See `evidence/phase4-env-check.txt`.
Unsigned app and installer are NotSigned, with no signer/timestamp to report.
Self-signed certificates were not fabricated.

Production defaults fail closed; unsigned builds require explicit -UnsignedCandidate
and emit signed=false, unsignedCandidate=true, productionEligible=false.
Helper gates include private key, validity, code-signing EKU, SHA256/RFC3161,
signtool verification, exact publisher and timestamp presence.
LocalMachine selection adds /sm. Callback values reject injection metacharacters.

Upstream Inno 6.7.3 source proves callback-mode signed uninstallers are temporary
uninst.e32.tmp files, not persistent *.dat caches. The helper captures verified bytes
before Inno deletes the temporary file; postcompile verification requires the
captured copy. Existing files are never overwritten for that capture.
Inno's $f is already quoted; harmless argument-capture execution proves paths
with spaces survive the callback. It is not a substitute for real signing.
Evidence: upstream compiler-source capture, callback-stub output and regressions.
The credentialed signing/uninstaller path remains unexecuted and blocked.

## Dependency / Vulnerability Review

Genuine official NuGet restore/cache and clean build inputs were used. Earlier
fabricated-cache results remain disqualified. Product package references did not
change during closure; prior captured advisory query reports zero known vulnerable
packages, not a guarantee of no vulnerability. Compatibility-pinned sensor
dependencies and test-only xUnit v2 deprecation are documented rather than
blindly upgraded. .NET runtime is 8.0.31, SDK 8.0.425; .NET 8 support ends
2026-11-10 and migration planning remains necessary.
Prior detailed graph/advisory evidence is under
`.verify/v1.2.4-continuation/packages-*-final.json` and
`runtime-dependency-inventory.json`.

## Third-Party Licensing

Packaged notices include MPL LibreHardwareMonitor/dependencies, HidSharp Apache,
Mono upstream text, Microsoft runtime notices and the original Windows SDK RTF.
LHM 0.9.6 is a loose replaceable assembly; pinned upstream commit is
3d331e3370efb858411f19511373eff65a218701.

Embedded PawnIO 0.1.6 modules have LGPL-2.1-or-later notice/license and corresponding
source ZIP, SHA256
`647BF55985837302B00AD2C05FC3FB700F140AF2E34693F390FF2DB47B608867`.
The source archive includes upstream compiler binary/source RPMs as data.
No PawnIO kernel driver/installer is bundled. The new replacement guide explains
both DLL copies, rebuild sources and the difference between ForgerEMS's lack of
a DLL authenticity gate and upstream PawnIO trust restrictions.
No claim that arbitrary modified modules work with a signed upstream driver is made.

Windows SDK RTF is 246,945 original bytes, SHA256
`DD07EB178E00C6BBA4148457FC00FF77CD4887EB521D504186FE59C9EC8BBE62`;
the validated packaged copy matches. Native Mono.Posix helpers and WinRT/SDK
assemblies are included in the dependency-scope review, not silently omitted.
Exact Mono/SDK redistribution scope and PawnIO relinking/trust implications still
require qualified review. Technical notice/source packaging is not attorney
clearance. Third-party rights are not overridden by proprietary terms.

## Link Audit

Broader production-source capture includes runtime CS/XAML, manifests and current
docs; historical/developer/test URI scopes are distinguished. Its old state is not
rewritten to pretend later replacements were already verified.
Confirmed repairs include EndeavourOS, Emsisoft, Surface driver guidance,
Windows 2000 lifecycle index, SanDisk, TrueNAS, Slackware, DDU and MSI support.
MSI category 404s were replaced with the functioning support root.
SanDisk portal notes explicitly do not claim WD HDD coverage.

`evidence/changed-urls-recheck.json` records seven of nine canonical destinations
returning HTTP 200; MSI and SlackDocs rejected the raw checker with 403 but worked
in recorded real-browser checks. TrueNAS/DDU canonical redirects were captured.
Challenge-blocked vendor destinations, rejected policies and URI templates remain
unverified/manual; an HTTP 403 is not counted as success. No arbitrary mirror
was substituted. Full all-link closure remains partial.
Evidence: `production-links-current.json`, browser batches/replacements and
changed-URL recheck under `evidence/`.

## About / FAQ / Privacy / Terms

Current documentation accurately describes local diagnostics, settings-controlled
GitHub/vendor requests and user-triggered WUA against configured Microsoft/WSUS
services. Support bundles are not automatically uploaded; redaction requires
review. Administrator rights, optional sensor behavior and manual/vendor download
boundaries are disclosed. Stable-channel preference does not imply artifact signing.

Published documentation paths now agree across csproj, release builder and Inno:
21 current docs plus root SECURITY.md and sensor replacement guidance are packaged.
Relative links are regression-tested; obsolete Kyra QA checklists are excluded.
README/developer-only references are explicitly source-checkout-only.
About/FAQ/Legal content is reviewed in source and tests, but those actual packaged
dialogs were not invoked through the available UI Automation client.

## Visual QA

Real owned-window screenshots of the freshly extracted validation candidate cover
consent, welcome dismissal, five navigation screens, minimum size and relaunch.
Consent was confined to an isolated profile and persisted across relaunch.
App processes closed gracefully. Screens are under `qa-validated/screens`;
the earlier capture set is preserved under `qa-gui/screens`.

Visible navigation/version/driver-advisory copy and the focus cue were inspected.
This is not complete dialog/error-state/maintenance interaction QA. Inner page
controls were unavailable through the automation client. Only 96 DPI (100%) was
captured; 125%/150% and multiple supported Windows configurations remain untested.
No redesign or destructive device workflow was used to generate screenshots.

## Accessibility

A confirmed defect was fixed: sidebar Focusable=False prevented keyboard access.
All five navigation buttons now have focusable tab stops, glyph-free automation
names and an IsKeyboardFocused border cue. Static XAML and real STA-window tests
verify the contract. UIA shell navigation and the rendered focus cue are recorded.
Complete keyboard activation, tab order, screen-reader behavior and inner-control
accessibility remain partial; inaccessible automation is not silently treated as
proof that every control works.

## Automated Tests

Fresh results, not copied baseline numbers:

| Run | Source | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| Baseline Release 1 | bf917f5 | 1627 | 0 | 0 |
| Baseline Release 2 | bf917f5 | 1627 | 0 | 0 |
| Baseline Release 3 | bf917f5 | 1627 | 0 | 0 |
| Phase 4 Release 1 | d26b37d | 1666 | 0 | 0 |
| Phase 4 Release 2 | d26b37d | 1666 | 0 | 0 |
| Phase 4 Release 3 | d26b37d | 1666 | 0 | 0 |
| Phase 5 full Release | 134ec32 | 1666 | 0 | 0 |
| Final signing-capture/doc focused | pre-5e77cf6 working delta | 36 | 0 | 0 |

Final-HEAD full-suite result is captured after this report's commit in
`test-results/phase6-final.trx` and `phase6-final.log`. Use its exact counters for
the final count; the added capture regression changes the count, not the prior
captured results. Do not sum overlapping runs into a unique test count.

The intervening phase-3 full run had 1663 passes and one stale Windows 2000 URL
expectation failure, since corrected. It was not a CLR abort.
Evidence for failed attempts is retained rather than erased.
Analyzer warnings remain; the build is not claimed warning-free.

## Stress / Repeat Tests

Three consecutive post-navigation-fix Release runs use --blame-crash and separate
TRX/logs, each exit 0. Durations including first rebuild: 99.5s, 69.9s, 71.6s.
SDK 8.0.425, runtime 8.0.31, VSTest 17.11.1 x64.
`phase4-run-meta.json` records exact commands/times/source. Further phase-5 full
run also passes. No repeat abort was observed. This remains non-reproduction
evidence, not definitive attribution of the historical native crash.

## Clean Build

Validated-candidate build succeeded from clean committed 134ec32. The subsequent
signing-capture fix is locally committed and focused-tested. Final unsigned build
is deliberately performed after this report commit from a clean tree.
Final restore/build command, exit status and provenance are recorded in
`final-build.log` and `final-build-attestation.txt`.
No prior artifact is silently promoted to final status.

## Portable ZIP Validation

Independent clean extraction of the validation candidate launched without installer
state. --self-test exits 0; bundled backend is found and version/checksums align;
required scripts exist, deep sensor mode is Off and unsafe capabilities false.
Writes/consent remain in process-isolated LOCALAPPDATA/TEMP/TMP.
Evidence: `qa-validated/localappdata/ForgerEMS/Runtime/diagnostics/published-self-test.txt`.
Final exact-HEAD extraction/self-test evidence is separately under `qa-final/`.
The safe extraction regressions are not a claim that hostile archives were executed.

## Installer Validation

Actual Inno compiler guard cases reject missing/conflicting signing modes and
accept explicit unsigned candidate mode. Candidate installer compilation succeeds.
No usable disposable Windows environment is accessible: Hyper-V enumeration is
permission-denied, Windows Sandbox executable and VirtualBox are unavailable.
Linux WSL distributions are not a substitute for Windows installer lifecycle QA.
See `evidence/vm-availability.txt` and `evidence/phase4-env-check.txt`.

Clean install, registration, shortcuts, first installed launch and installed
uninstaller signature remain **BLOCKED**, not inferred from compilation.
No host installer was executed to work around this boundary.

## Upgrade Validation

Prior supported public candidate is v1.2.3-preview.1, with legitimate installer
asset metadata preserved. Actual install-old/create-state/upgrade/new-launch,
settings preservation, duplicate-registration and stale Kyra-component cleanup
remain **BLOCKED** by unavailable Windows isolation.
Reinstall/repair are unproved. Downgrade is documented unsupported; the updater
rejects older releases, but no claim that old published installers block downgrade
is made.

## Uninstall Validation

Actual uninstall and residue inspection remain **BLOCKED**. Intended policy retains
user settings, consent, logs/reports outside the installation folder. This is
documented intent, not observed lifecycle evidence. Binaries, shortcuts, registry,
services/tasks and deliberate retained data require isolated before/after testing.

## Artifact Hashes

Final unsigned candidates:
`.verify/v1.2.4-final-certification/release/ForgerEMS-v1.2.4.zip` and
`.verify/v1.2.4-final-certification/release/ForgerEMS-Setup-v1.2.4.exe`.
Final SHA256 values are in `release/CHECKSUMS.sha256`; exact sizes/source HEAD,
architecture, versions, signatures and commands are in `release/release.json`
and `final-build-attestation.txt`. These are generated from the final report
commit, not the earlier source checkpoint.

The following values belong ONLY to the retained validation run at 134ec32:

| Validation Artifact | Bytes | SHA256 |
| --- | ---: | --- |
| validated-candidate/release/ForgerEMS-Setup-v1.2.4.exe | 61778568 | F50641E1A3CC4EE2656D1DB01226994DCCCF63D71087D3D8B81870B21D6B742B |
| validated-candidate/release/ForgerEMS-v1.2.4.zip | 81741359 | 17EDCEA800421C8D3F3A4FB33251056BE800E229162B80E0D10C5856511EAFE3 |

Validation metadata: win-x64, Release, preview, unsignedCandidate=true,
productionEligible=false, sourceDirtyFileCount=0. App/installer are NotSigned.
Its ZIP contains 80 entries, no Kyra-named entries and no .sys/.inf/.cat entries.
Final package inventory must be checked independently against final evidence.
Hashes detect byte changes; they are not production publisher authentication.

## Local Security Scan

Local Windows Defender is available with real-time protection enabled.
Final artifact-only custom scan uses -DisableRemediation and records command,
stdout/stderr and exit in `final-defender-scan.log`.
No proprietary binary was uploaded to an arbitrary reputation service.
A no-threat local scan is not proof that software is vulnerability-free.

## Commits

Local continuation commits:
- b4984f0: signing, packaged notices, resource links and diagnostic isolation.
- d26b37d: keyboard navigation/focus and corrected lifecycle URL test expectation.
- dd4b24c: verified candidate release notes.
- 134ec32: executable-only signature wording.
- 5e77cf6: correct callback-mode signed-uninstaller capture.
- Final report commit: exact SHA recorded by final artifact provenance.

No branch/tag push, GitHub release or production update-feed modification occurred.

## Remaining Issues

1. Authorized production signing certificate/private-key access and exact publisher
   identity, followed by real credentialed app/installer/uninstaller verification.
2. Accessible disposable Windows environment for clean install, prior-version
   upgrade, reinstall/downgrade policy and uninstall/residue evidence.
3. Native-debugger-level historical CLR attribution or convincing non-product
   evidence; repeated non-reproduction alone does not establish causality.
4. Complete dialogs/error states, maintenance interactions, keyboard/screen reader,
   125%/150% DPI and supported-system visual coverage.
5. Resource ambiguity/schema/timeouts and remaining challenge/template link review.
6. Qualified redistribution/relinking review for PawnIO, Mono.Posix and SDK/WinRT.

## Release Certification

PASS for candidate packaging is not PASS for signed production release or installer
lifecycle. Evidence paths below are relative to the certification root unless
explicitly marked continuation. Final artifact rows require the associated final
build attestation and matching clean source HEAD; this report alone is not an
artifact attestation.

| Gate | Status | Evidence | Blocking? |
| --- | --- | --- | --- |
| Repository integrity | PASS | git-baseline, local commits; final attestation requires clean matching HEAD | No |
| Kyra removal | PASS | residual classification, negative tests, ZIP inventory | No |
| v1.2.4 consistency | PASS | VERSION, version tests, PE metadata, packaged self-test | No |
| Genuine dependencies | PASS | genuine NuGet cache/restore and build logs; placeholder evidence disqualified | No |
| Updater correctness | PASS | security regressions, exact publisher/flags/expiry/hash/redirect review | No |
| Artifact integrity | PASS | validation hashes; final CHECKSUMS and source attestation | No |
| Signing | BLOCKED | no authorized code-signing credential; unsigned NotSigned artifacts | Yes |
| Resource discovery | PARTIAL | frozen 50-row live/cache capture, 11 resolved metadata rows | Yes |
| Windows integration | PARTIAL | read-only backend evidence/tests; packaged control invocation incomplete | Yes |
| Driver Store | PASS | conservative inventory/protection regressions and dated host capture | No |
| Driver safety | PASS | InstallationPermitted=false; RemovalPermitted=false; advisory documentation | No |
| Links | PARTIAL | confirmed fixes/browser evidence; remaining challenges/templates | Yes |
| Legal/license notices | PARTIAL | shipped licenses/source/replacement aid; qualified scope review outstanding | Yes |
| Automated tests | PASS | three 1666/0/0 runs, phase5 and focused fixes; final phase6 TRX | No |
| CLR stability | PARTIAL | repeated clean runs and dump review; native cause unknown | Yes |
| Visual QA | PARTIAL | fresh real screenshots; inner dialogs/DPI coverage incomplete | Yes |
| Portable ZIP | PASS | clean extract, isolated launch/relaunch/self-test; final qa-final evidence | No |
| Clean install | BLOCKED | no accessible disposable Windows environment | Yes |
| Upgrade | BLOCKED | previous-version lifecycle not executed | Yes |
| Uninstall | BLOCKED | uninstall/residue lifecycle not executed | Yes |
| Final packaging | PARTIAL | clean unsigned candidate build/attestation; production signing unavailable | Yes |

External mandatory prerequisites prevent production closure. Engineering progress
and candidate validation are retained; no production-ready claim is made.

FORGEREMS_V1.2.4_BLOCKED
