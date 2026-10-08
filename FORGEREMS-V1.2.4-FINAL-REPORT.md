# ForgerEMS v1.2.4 Final Release Report

Date: 2026-10-08. Evidence root: `.verify/v1.2.4-closure/`.
This is the single authoritative certification report. Earlier artifacts and
failed campaigns remain preserved; they are not silently substituted for final evidence.

## Executive Result

**Production certification remains BLOCKED.** Authorized production signing
credentials are unavailable, and isolated clean install, upgrade and uninstall
have not been demonstrated. The code candidate passes five consecutive broad
Release runs, native verification stress and portable execution.

Confirmed defects repaired during closure include provider version/asset parsing,
bounded metadata transport, CI signing-key provisioning, report-path isolation,
cached Driver Hub wording and test synchronization. No release gate is weakened.
Nothing is pushed, tagged publicly, published or installed on the development host.
No host driver is installed, removed or replaced.

## Repository State

- Repository: `I:\ForgerEMS_App\repo`; branch `release/v1.2.4-modernization`.
- Starting HEAD: `c08167df8a7b84f92f7612c97a8fe65f0df21919`, clean.
- Engineering commits: `b4e926216912c131fe12649f0f3fee1def3cafd4`,
  `62faece3ac818537c7515cfe75d167c8cd53bfbd`,
  `335262a4a564d1c38a49f4238703c470e2fa286b` and
  `c883e3f985a3148068cdb14ac93f2f085bb2bee8`.
- Latest fully tested code HEAD: `c883e3f985a3148068cdb14ac93f2f085bb2bee8`.
- Final artifact build follows the report-only commit. Its exact source HEAD,
  dirty count, sizes, hashes and signature results are recorded in
  `final-build-attestation.txt` and `release/release.json`.
- Final provenance requires sourceHead equal repository HEAD, dirty count zero
  and independently checked file hashes. A mismatch invalidates final packaging.

## Starting Certification Baseline

Direct checks established version `1.2.4`, clean source and matching starting
release metadata. App FileVersion is `1.2.4.0`, ProductVersion `1.2.4`;
backend version is separately `2026.10.06.1`. `VERSION` remains authoritative.

Starting installer: 61,780,931 bytes, SHA-256
`A0C0848FAE1EE0C6938DDAC639E7FBB1255CC56679E7ED88430D884C3ACD6614`.
Starting ZIP: 81,740,240 bytes, SHA-256
`8A7572A5B0BDDB6B943F7CF468EAE61A152C41BE0BD4EF101616A4A2A67F5B06`.
Both are unsigned and non-production. These are baseline, not final hashes.
Evidence: `evidence/lead-findings.md`.

## Signing Infrastructure Discovery

Read-only discovery inspected CurrentUser/My (eight certificates),
LocalMachine/My (one), EKUs, private-key availability, cryptographic providers,
enrollment configuration, environment variable names, repository references,
SDK signtool, release workflows and GitHub configuration.
No authorized Code Signing identity, hardware/enterprise identity or configured
Azure/Trusted Signing/Key Vault/HSM/PFX path was established.
Development, EFS and localhost ServerAuth identities are not production signers.
No key is exported and no secret value is printed.

GitHub discovery found zero self-hosted runners and no configured repository
environments. The old unused external Kyra secret/variable names are not runtime
references; they were not deleted through a side-effecting API.
Evidence: `evidence/environment-discovery.txt`, `evidence/lead-findings.md`;
the final refresh is separately retained under `evidence/`.

## Historical Release Signature Analysis

Official release discovery identifies `v1.2.3-preview.1`, prerelease, published
2026-07-02; remote tag `c98feeb769380db7b1c3b9eb1518a78f1dbf9351`.
The authentic previous installer is 65,967,832 bytes, SHA-256
`E3597E2657789BB359DFA7BE93156BB3C0C6BEF90690EAEE189489EF90B1B4D0`,
matching the independent GitHub API digest.
Authenticode is NotSigned; signtool verification exits 1 with no signature.
Signer, issuer, certificate chain, expiration and timestamp digests are not
applicable. No historical signing identity can be inferred.
Evidence: `evidence/previous-release-inspection.txt`.

## Signing Result

**BLOCKED: legitimate authorized credentials must be provisioned by the owner.**
Production packaging defaults to fail closed; unsigned builds require explicit
`-UnsignedCandidate` and record `productionEligible:false`.
The hosted-runner initializer now validates owner-supplied identity, thumbprint,
validity, private key, Code Signing EKU and exact publisher before importing
in memory. Protected environment and secrets still require owner provisioning.
No production import/signing operation or self-signed substitute is performed.

Signing uses SHA-256, RFC3161 timestamping, correct certificate-store selection,
signtool verification, exact publisher and timestamp certificate checks.
Inno 6.7.3 compiler-source review establishes callback uninstallers are temporary
files. Verified bytes are captured before cleanup; existing capture paths are
not overwritten. Failure-path/argument regression tests are passing.
Actual credentialed app, installer and embedded-uninstaller signing is unexecuted,
not a PASS inferred from stubs or source inspection.

## Windows Isolation Discovery

Sandbox is unavailable. Hyper-V inventory is authorization-denied. No existing
registered VirtualBox Windows guest or available CI execution path was found.
New local-only lifecycle workflow and guest harness are prepared but not invoked
through an unapproved push or workflow dispatch.

VirtualBox 7.2.12 starts an owned VM but its Windows boot trials stall in EFI.
Existing WSL QEMU 8.2.2/OVMF provides a second path: an owned q35/TCG/2CPU/4GB
guest with private TPM helper, no networking/host sharing and an owned QCOW2.
Official Windows 11 Enterprise LTSC 2024 evaluation ISO is 5,112,850,432 bytes,
SHA-256 `67CEC5865EAA037A72DDC633A717A10A2BED50778862267223DDB9C60EF5DA68`,
matching Microsoft's independent hash PDF, page 5.

Guest Windows kernel execution is observed, but setup never produced a ready
marker. The last recorded long observation showed zero new block IO over about
53 minutes with the guest still executing; this does not identify the stall's
cause. On 2026-10-08 the refresh found QEMU/TPM no longer running after external
WSL shutdown: signal 15 from systemd-shutdown, not established as a guest crash.
Owned disk, firmware variables, TPM state and serial logs remain on disk.
No unchanged TCG reboot is treated as progress. KVM device access is unavailable
to the WSL user. Elevated use was requested but not approved or attempted.
Evidence: `evidence/qemu/`, `evidence/windows-media-integrity.json`,
final isolation refresh under `evidence/`.

## Clean Install

**BLOCKED / NOT EXECUTED in a ready Windows guest.** Installer compilation,
firmware/kernel boot and portable launch do not prove install paths, registration,
shortcuts, consent, installed UI or installed self-test behavior.

## Previous-Version Upgrade

**BLOCKED / NOT EXECUTED.** Authentic prior fixture is available and byte-verified.
The prepared harness checks prior settings, inert legacy configuration, version,
single product registration and migration. Preparation is not upgrade proof.

## Uninstall

**BLOCKED / NOT EXECUTED.** Installer-owned file/shortcut/registration removal and
intentional user-data preservation require actual isolated execution.
No installer or uninstaller is run on the development host.

## Reinstall / Repair

Same-version reinstall and install-after-uninstall remain unexecuted.
No distinct repair mode is implemented; repair is NOT APPLICABLE.

## CoreCLR Crash Investigation

The historical crash is confirmed, but its root cause remains unproved.
Process-global LibreHardwareMonitor lifecycle serialization addresses a credible
native lifetime hazard, not a demonstrated historical trigger.
No reproducible product-side native crash occurred in the completed campaigns.
Residual attribution risk remains PARTIAL, not erased by later passing tests.

## Dump Findings

Preserved dump SHA-256:
`164CF17C519D5B9469168942CF302880271B8B28C2458FE1E1DEA2957AA104C4`.
The prior report omitted the last hexadecimal character; direct bytes correct it.
Exception is read access violation `0xc0000005`, address `0x0000019182840000`,
OS thread 12544, managed thread 29, CoreCLR 8.0.31 offset `0x1064F0`.
Exact Microsoft symbols resolve `GCHandleStore::CreateHandleOfType+0xC0`.
SOS reports ExecutionEngineException / `0x80131506`; the initiating caller is
not recovered. Handle-table walking fails. verifyheap checks zero objects:
its zero-error output is NOT evidence that captured memory is intact.
Evidence: `crash-context.json`, `fault-symbol.txt`, `gc-analysis.txt`,
`fault-full-stack.txt` and the preserved original dump.

## Native Interop Audit

WinVerifyTrust SDK review covers LONG/DWORD widths, pointer alignment, action GUID,
file union, UI-none, VERIFY/CLOSE, flags `0x1180`, string/pointer lifetimes and
single cleanup. Regression tests pin x64 file-info size 32/data size 80 and offsets.
Inventory uses OS CIM/PnPUtil/DISM, not new custom SetupAPI/Driver Store P/Invoke.
WUA runs synchronously in a separate PowerShell process with no manual RCW
over-release or install action. Volume/power scalar layouts are checked.
Deep sensor/driver activation is not used to modify host drivers.

## Native Stress Campaign

Three completed catalog-fixture runs total 30,000 verifier calls and 60,000
GCHandle cycles, zero errors; catalog-signed cmd.exe is not an embedded positive.
Three completed embedded-SDK runs total another 30,000 calls and 60,000 cycles:
6,000 valid signatures, 6,000 wrong-publisher rejections, 18,000
unsigned/malformed/missing rejections, zero errors.
An incomplete attempt with no captured exit/result is excluded.
Finite handle/memory samples do not establish an unlimited-lifetime leak guarantee.
Evidence: `evidence/native-stress*.json` and separate command/exit logs.

## Full-Suite Stability

At clean `c883e3f`, FIVE consecutive broad Release runs with `--blame-crash`
each pass **1703 passed / 0 failed / 0 skipped**, exit 0.
Every TRX counter was independently read. Evidence:
`test-results/closure-latest-stability-1..5.trx` and matching command/head logs.
Guest CPUs are paused only for controlled campaigns and resumed afterward.

Earlier failures remain recorded: whole-PowerShell 15s performance assertion
and a five-second benchmark re-enable wait. The performance collection now
runs separately from competing tests, without increasing its deadline.
The benchmark test previously blocked the captured test context with SpinWait;
it now subscribes before completion and asynchronously awaits the same five-second
bound. All token/cancellation assertions and production command code are unchanged.
This repairs a synchronization hazard, not proof of the precise earlier scheduling.
Focused benchmark/command verification passes 22/22.

## Dynamic Resource Coverage

Frozen final-policy probe: **19 of 20 dynamic descriptors resolve metadata**,
up from 11; 50 total descriptors have 19 ResolvedMetadata, 29 RequiresUserAction
and 2 Unsupported. Thirty descriptors remain explicit manual/unsupported exceptions.
This is metadata evidence, not downloaded-payload/signature/applicability certification.
Probe source was dirty during implementation; its exact policy bytes are retained,
SHA-256 `E493BFB00AC8CCB9E795FB47773AD9C1DFD7DBA13A534E4A05A768284C5A70B8`.

| Previously Unresolved Resource | Official Source | Final Result / Action |
| --- | --- | --- |
| Rufus | GitHub pbatard/rufus | 4.15; strict two-part numeric tag handling |
| System Informer | GitHub winsiderss/systeminformer | 4.0.26241.138; anchored current asset naming |
| Ubuntu desktop/server | Ubuntu LTS metadata/checksums | 26.04.1; latest same-family point release; equal normalized versions rejected |
| Debian netinst/GNOME/KDE/XFCE | cdimage.debian.org | 13.7.0; diagnosed ~21s response, bounded 45s metadata budget |
| Kali | kali.download official checksums | 2026.2; canonical vendor endpoint, no random mirror allowlist expansion |
| Rescuezilla | GitHub rescuezilla/rescuezilla | RequiresUserAction / AmbiguousSelection; no arbitrary flavor choice |

No hash, ambiguity, architecture or applicability gate is relaxed.
Offline/stale state and failure categories remain distinct.
Evidence: `evidence/resource-probe-policy-final.json`,
`evidence/metadata-transport-serial.json`.

## Link Validation

Current-source audit records 209 HTTP-reachable URLs, 33 unable to verify,
two broken backend verification examples (not production links), 16 original
upstream-license HTTP references, four namespaces, two schemas, one scheme
literal and seven templates. Browser follow-up has three loaded pages, five
bot-filtered and 25 unable to verify. Bot challenges are not successful checks.
No confirmed additional production 404 is established. Coverage remains PARTIAL.
Evidence: `evidence/current-links.json`, `evidence/browser-current-links.json`.

## Driver Safety Boundary

Driver Store is read-only inventory and device correlation. Update guidance remains
advisory: no automatic install/removal or firmware flashing. A saved snapshot may
be stale and does not prove version applicability. Report helpers now use the
injected runtime root; four regression tests prevent cross-profile report access.
Latest portable screenshot shows "No local device snapshot loaded", not the old
host snapshot. No host driver change occurred.

## Windows Update Validation

Read-only live OS/WUA evidence is preserved in
`evidence/windows-maintenance-checkupdates.txt`; host build is 26300, UBR 9457.
No updates are installed or host OS upgrade forced. Policy/support/servicing
uncertainty remains explicit. WUA observations are not guest installer evidence.

## Visual / DPI QA

Five primary tabs, minimum-size layout and relaunch were captured in earlier
candidate QA. Latest Driver Hub screenshot was directly inspected and confirms
the empty snapshot state. Its owned window reports DPI 120: actual **125%**.
100%/150% multi-environment visual coverage is not established.
Occluded terms captures are excluded. Old automation-name mismatches are recorded;
no missing control is fabricated as passing.
Evidence: `qa-gui-validation/screens/`, `latest-qa/screens/driverhub-latest.png`.

## Accessibility

Primary navigation permits keyboard focus, visible focus indication and readable
automation names. Icon-only overflow names are added. Focused regressions and
the broad suite pass. Full traversal/dialog/high-DPI certification remains PARTIAL;
some card content is absent from the exposed UI Automation tree.

## Third-Party Licensing

Measured validation PE inventory has 459 bundle entries and maps 458 binaries
to 14 identities, zero unmapped. This is technical inventory, not legal clearance.
System.CodeDom's separate MIT notice is now included. Microsoft runtime/SDK,
LibreHardwareMonitor/MPL, HidSharp/Apache, Mono and PawnIO notices ship.
PawnIO LGPL source ZIP SHA-256:
`647BF55985837302B00AD2C05FC3FB700F140AF2E34693F390FF2DB47B608867`.
No PawnIO kernel driver is bundled; replacement/source instructions are packaged.
SDK/WinRT and Mono redistribution scope and PawnIO relinking/trust implications
remain counsel-review qualifications. No mandatory counsel-signoff policy was found.
Evidence: `evidence/validation-binary-inventory.json`, packaged
`providers/sensors/THIRD-PARTY-NOTICES.txt` and `providers/sensors/LICENSES/`.

## Vulnerability Review

Official NuGet advisory query includes transitive product/test packages.
The 2026-10-08 refresh exits 0 with no vulnerable-package entries for either
project: `evidence/dotnet-list-vulnerable-closure-final.json` and matching log.
This is known-advisory evidence, not
a guarantee of no vulnerability. .NET 8 migration remains necessary before
support ends 2026-11-10. No casual major upgrade occurs during closure.

## Final Build / Artifact Manifest / Artifact Hashes

Final destination: `.verify/v1.2.4-closure/release/`, fresh and non-overwriting.
Build from the clean report commit, Release/win-x64, explicit unsigned candidate.
Exact final source HEAD, filenames, sizes and SHA-256 values are recorded in
`final-build-attestation.txt`, `release/release.json`, `release/CHECKSUMS.sha256`
and final machine-readable artifact attestation. These are final-build evidence,
not reused hashes from `validation-release/` or `latest-release/`.
Portable extraction/self-test must verify the actual final ZIP, numeric owned
process exit and package contents. Final output is accepted only after these checks.

## Artifact Signatures / Defender Scan

Production signatures are BLOCKED. App and installer remain NotSigned;
ZIP Authenticode is not applicable. All manifests identify unsigned/non-production.
The local Defender final scan uses MpCmdRun custom-file scan with
`-DisableRemediation`; command, timestamp, exit and result are retained in
`evidence/final-defender-scan.txt`. No public malware-analysis upload occurs.
Final attestation records whether this exact-artifact gate passed; earlier
candidate no-threat results alone do not establish a final scan.

## Remaining Risk

Owner action: provision an authorized production Code Signing identity and protected
release environment; execute real signing, timestamp and uninstaller verification.
Provide a usable sanctioned disposable Windows guest/runner, or approve the narrowly
requested KVM permission, then run actual clean install/upgrade/uninstall/reinstall.
KVM approval would permit an attempt, not guarantee successful setup.
Historical CLR attribution, wider DPI/accessibility, bot-filtered links and legal
qualifications remain explicit. Driver applicability stays advisory by design.

## Certification Matrix

| Gate | Status | Evidence | Blocking? |
| --- | --- | --- | --- |
| Repository integrity | PASS | clean tested HEAD; final source attestation required | No |
| Kyra removal | PASS | removal/package regressions; no active restored feature | No |
| Version consistency | PASS | VERSION, metadata, bundled self-test | No |
| Updater | PASS | security regression suite | No |
| Expected hashes | PASS | vendor/release-bound metadata; integrity regressions | No |
| Redirect security | PASS | resolver/updater policy tests | No |
| Signature verification code | PASS | SDK layouts; embedded positive/negative stress | No |
| Production artifact signing | BLOCKED | no authorized Code Signing identity | Yes |
| Inno uninstaller signing | BLOCKED | pipeline reviewed/tested; real signing unexecuted | Yes |
| CoreCLR stability | PARTIAL | five clean 1703-test runs; 60000 native calls; cause unknown | Residual risk |
| Windows isolation | BLOCKED | guest kernel runs; no ready Windows setup | Yes |
| Clean install | BLOCKED | not executed | Yes |
| Previous-version upgrade | BLOCKED | authentic fixture available; not executed | Yes |
| Uninstall | BLOCKED | not executed | Yes |
| Portable package | PASS | clean candidate self-test/GUI; final attestation required | No |
| Resource resolution | PARTIAL | 19/20 dynamic; Rescuezilla ambiguous; manual exceptions | No |
| Windows Update integration | PASS | live read-only WUA evidence and regressions | No |
| Driver Store diagnostics | PASS | read-only inventory/correlation and regressions | No |
| Driver applicability boundary | PASS | advisory only; no automatic install/removal | No |
| Link audit | PARTIAL | current HTTP/browser evidence; unverified destinations | No |
| Visual QA | PARTIAL | actual 125%; 100/150% not established | No |
| Accessibility | PARTIAL | navigation fixed; broader UIA/traversal gaps | No |
| Licensing/notices | PARTIAL | inventory/notices/source present; qualified interpretations | No |
| Vulnerability review | PASS | official transitive advisory output; final refresh evidence | No |
| Test suite | PASS | five consecutive 1703/0/0 Release/crash-enabled runs | No |
| Final packaging | PASS | clean candidate proven; exact-final-HEAD attestation required | No |
| Defender scan | PARTIAL | candidate no threats; exact final scan disposition in attestation | No |

## Final Certification

Unsigned candidate engineering is validated, not authenticated production distribution.
Final artifacts require the linked post-report build evidence to be accepted.
Mandatory signing and actual isolated installer lifecycle gates remain unsatisfied.
No publication is authorized by this report.

FORGEREMS_V1.2.4_BLOCKED
