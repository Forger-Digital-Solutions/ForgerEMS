# ForgerEMS v1.2.4 Final Release Report

Date: 2026-10-08. Evidence root: `.verify/v1.2.4-closure/`.
This is the single authoritative certification report. Earlier artifacts and
failed campaigns remain preserved; they are not silently substituted for final evidence.

## Latest Authorized KVM And Production-Gate Closure Round

Observed 2026-10-08. **FORGEREMS_V1.2.4_BLOCKED**.
KVM acceleration is now proven and a cloned Windows guest reached its desktop.
The build/signing-only production-eligibility defect is corrected. Installer
certification remains unexecuted: the guest's evaluation grace period is expired,
and no owner-authorized production signing identity has been supplied.
These are external prerequisites, not evidence of a defective installer.

### Repository And Artifact Provenance

Starting repository HEAD: `a8985ec8f64b1d18a07b77900de374c8c1b19659`, clean.
The only committed delta from artifact source
`fc54150844b36c2e6d6a4fd18d6ea9dd065fd4fb` at entry was this report.
This round changes release tools, workflows, regression tests and this report,
not shipped application/installer/backend/resource/legal content. No candidate
rebuild, signing, host installation, driver replacement, push, tag or publication
occurs. The final local commit and clean-tree state are recorded after commit in
`.verify/v1.2.4-closure/kvm-production-gate-20261008T114613Z/certification-attestation.json`.

The preserved unsigned artifacts still correspond to **fc541508**, not the
new release-tool revision. Fresh integrity evidence:
`.verify/v1.2.4-closure/final-artifact-integrity.json`.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `.verify/v1.2.4-closure/release/ForgerEMS-Setup-v1.2.4.exe` | 61780805 | F7C3541860015A533D437CE80B8D0D8B70DD78F0DE9CF47091353C1175B8A9BB |
| `.verify/v1.2.4-closure/release/ForgerEMS-v1.2.4.zip` | 81742749 | 61A000203FD3E866C10221856322AD318A38562B5CE9E122626205997373B137 |

App and installer remain **NotSigned**, with no signer or timestamp.
The retained exact-artifact Defender result remains no threats / exit 0;
it was not rerun. Historical attestations and candidate metadata are preserved.

### KVM Capability Result

Ubuntu WSL2, kernel `6.6.114.1-microsoft-standard-WSL2`.
`/dev/kvm` exists, mode `0660`, owner root, group kvm (gid 993).
The ordinary uid 1000 remains outside that group and cannot access the device.
The approved probe ran as uid/gid 0: API **12**, USER_MEMORY, IRQCHIP,
SET_TSS_ADDR, EXT_CPUID and PIT2 all **1**. KVM_CREATE_VM returned a VM fd,
which was immediately closed. Result **PASS**, not device-presence inference.
Two immediate probe executions were captured; no further probes or permission
changes followed. No guest disk/firmware/TPM was touched by the probe.
Evidence: `kvm-production-gate-20261008T114613Z/probe-result.json`,
`probe-run.log` and `probe-context.txt`, relative to the closure evidence root.

### Disposable Windows Environment

QEMU uses KVM only, q35/SMM, host CPU, two CPUs, 4096 MB, standard VGA,
private Unix QMP/VNC/TPM sockets and **no guest networking or host shares**.
The old disk-recreating launcher was never used. A full disk clone and copied
firmware/TPM/auxiliary media live in
`/root/forgerems-kvm-clone-20261008T120805Z/`.
Original source `/home/daddy_fds/forgerems-closure-qemu-e86b6629/` remains
unchanged: all six captured source-state hashes match before and after.

The guest emitted FORGEREMS_GUEST_READY at 12:30:22 UTC and was visually
confirmed at a normal desktop with elevated PowerShell:
Windows 11 Enterprise LTSC Evaluation, **10.0.26100**, x64,
UUID `e86b6629-1d60-44b3-88c1-32d6a74bdb21`.
An observed WSL restart at approximately 13:32 UTC interrupted baseline
collection. Its cause is unproved; it is not a ForgerEMS CLR crash.
The same owned clone subsequently booted back to its desktop.

`slmgr.vbs /dli`, relayed through guest COM1, proves **EnterpriseSEval /
TIMEBASED_EVAL**, **Notification**, **0xC004F009 (grace time expired)**.
Installer testing was held. No activation, rearm, clock/key change or network
change occurred. Direct SoftwareLicensingProduct CIM output was not obtained;
the serial slmgr output, not a guessed numeric LicenseStatus, is the evidence.

The guest was stopped through QMP system_powerdown; QEMU exited and its TPM
helper stopped. A private, hashed disk/firmware/TPM checkpoint is preserved at
`/root/forgerems-kvm-checkpoint-preactivation-20261008T1430Z/`.
This is **not a clean lifecycle baseline**. No installer phase executed.
Before resumption, refresh the campaign ISOs with the final reviewed tools:
the preserved ISOs contain an earlier audit-tool revision.

Evidence under `kvm-clone-20261008T120805Z/`: `isolation-receipt.json`,
`source-inventory.txt`, `source-inventory-final.txt`, `frame-wake.png`,
`frame-now3.png`, `serial-ready.log`, `serial-at-wsl-death.log`,
`licensing-serial-proof.txt`, `serial-final.log`, `checkpoint-hashes.txt`
and `final-runtime-inventory.txt`. Exact QEMU configurations are preserved
in `kvm-clone-launch.sh` and `kvm-clone-relaunch.sh` at the closure root.
The historical TCG stall's cause remains unproved; fixing the cloned guest's
DVD key prompt does not establish the cause of the historical kernel stall.

### Production Eligibility Gate Fix And Lifecycle Evidence Binding

`build-release.ps1` always writes **productionEligible:false**, with schema 2,
a build ID and UnsignedValidationCandidate/SignedValidationCandidate labels.
It emits a separate candidate manifest after final signing, packaging and hashes.
The temporary signed-uninstaller capture/verification and directory/no-overwrite
safeguards remain intact. No old-format eligibility boolean is authority.

`Test-ForgerEMSReleaseCertification.ps1` independently checks clean committed
source, allowed tool/report-only deltas, artifact hashes/sizes, pinned publisher
signatures/timestamps, signtool verification and the portable frontend identity.
All mandatory gate receipts must bind source HEAD, version, architecture,
build ID, manifest SHA-256 and exact installer filename/SHA-256.
Missing, stale, wrong-artifact, wrong-HEAD, failed, duplicate or malformed
receipts fail closed. Lifecycle runs must postdate the final candidate manifest.

Every receipt requires a detached SHA-256 CMS signature from a caller-pinned,
approved, chain-trusted code-signing identity. Online revocation and Code Signing
application policy are mandatory. Evidence leaves are independently hashed.
Fixture facts can exercise the pure policy core, not bypass the production
endpoint. Raw guest records always remain engineering-only and untrusted.

Build/Test/Integrity, Signing, Lifecycle and Security/Portable/Policy gate groups
jointly determine production eligibility. Lifecycle includes clean install,
upgrade, uninstall, residue, reinstall, driver/service/task audit and GUI evidence.
The staging workflow cannot publish. The dispatch-only promotion workflow
downloads already-built artifacts and signed receipts; it never rebuilds or
re-signs after lifecycle testing. Actual authorized signing and the production
endpoint's real-credential happy path remain unexecuted.

### Tests, Focused Policy Checks And Revised Certification Matrix

New broad Release execution: **1735 passed / 0 failed / 0 skipped**, exit 0,
`full-suite-round/full-suite.trx` and `run.log`.
The combined focused tooling run passed **80/0/0** (`certification-focused-7.trx`);
after final audit/clean-CI fixes, the promotion suite passed **35/0/0**, exit 0
(`certification-focused-8.trx`). The broad result predates these final narrow
fixes; it is not relabeled as a second final-HEAD broad run.
No current CLR crash occurred in these tests. Historical root cause is unproved.

Focused production-surface search finds no active Kyra runtime/SDK/gateway/
navigation or `1.2.4-preview.4`/`1.2.4-preview.5`; remaining tool references are
negative checks and intentionally seeded legacy upgrade fixtures.
VERSION remains 1.2.4. The retained resource result is **19/20**, not the
older 11/20 figure; no new network probe or resolver modification occurred.
The published previous-release state was not queried anew without network
approval. The authentic retained v1.2.3-preview.1 fixture remains available.

| Gate | Status | Exact Evidence (closure root unless stated) | Blocking? |
| --- | --- | --- | --- |
| Repository integrity | PASS | Starting git status; post-commit certification-attestation.json | No |
| Product source | PASS | fc541508 provenance; final-artifact-integrity.json; git delta | No |
| Kyra removal | PASS | Focused production-surface git search; retained package evidence | No |
| Version consistency | PASS | VERSION; retained release.json; focused/broad tests | No |
| Tests | PASS | full-suite-round/full-suite.trx; certification-tests/certification-focused-7.trx and -8.trx | No |
| CLR stability | PARTIAL | New clean run; historical dump/root cause remains unproved | Historical risk requires release-policy review |
| Portable package | PASS | Retained final-qa/latest-qa.log; unchanged ZIP hash | No |
| KVM capability | PASS | kvm-production-gate-20261008T114613Z/probe-result.json | No |
| Disposable Windows | PARTIAL | Desktop/serial proof; licensing-serial-proof.txt; checkpoint | Yes: expired evaluation; baseline not complete |
| Clean install | BLOCKED | No phase executed | Yes |
| Upgrade | BLOCKED | No phase executed; authentic previous fixture retained | Yes |
| Uninstall | BLOCKED | No phase executed | Yes |
| Reinstall | BLOCKED | No phase executed | Yes |
| Residue audit | BLOCKED | No completed installed/uninstalled baseline | Yes |
| Driver/service/task audit | BLOCKED | Baseline collection interrupted; read-only tooling prepared | Yes |
| Signing pipeline | PARTIAL | Reviewed tooling; focused regression receipts/callback tests | Real identity/provider operation unproved |
| Authorized signing identity | BLOCKED | No owner-authorized identity supplied; prior discovery retained | Yes |
| Final artifact signing | BLOCKED | final-artifact-integrity.json: NotSigned | Yes |
| Lifecycle-artifact binding | PASS | Schema/core/endpoint regression tests; no real production receipts | Receipts still required |
| Production eligibility | BLOCKED | Builder false; authenticated endpoint gates; current attestation | Yes |
| Defender | PASS | Retained evidence/final-defender-scan.txt, exact unchanged hashes | No |

### Signing Status And Remaining Owner Action

No signing occurred; no signer, issuer or timestamp exists for these artifacts.
Preferred acquisition remains an owner-authorized publicly trusted token/HSM
identity exposed through the Windows certificate store and selected by thumbprint.
Supply provider, validated publisher identity, public thumbprint, approved
token/account access and authorization for ForgerEMS; never provide keys/PINs/
passwords in source or chat. The protected receipt-author pin also requires
owner approval; the same approved identity may sign reviewed receipts if policy
permits. No speculative remote-provider adapter was introduced.

The Windows blocker is now precise, not presumed missing acceleration:
provide/authorize legitimate licensing or evaluation renewal for the preserved
guest, or supply a valid licensed disposable Windows VM. Guest activation would
require an explicitly approved network/licensing path; approval alone is not
proof that activation can resolve the expired evaluation.
Then validate a clean, rebootable/revertible baseline and run lifecycle phases.
The unsigned candidate can provide engineering evidence only; final signed bytes
must ultimately receive their own exact-artifact lifecycle/security receipts.

Updated owner actions:
`.verify/v1.2.4-closure/unblock-round-20261008/OWNER-ACTIONS.md`.
Current certification status: **FORGEREMS_V1.2.4_BLOCKED**.

## Previous Production-Certification Unblock Round

Observed 2026-10-08 at 11:17-11:19 UTC. **FORGEREMS_V1.2.4_BLOCKED**.
Neither required external prerequisite became available. No signing,
installer execution, unchanged TCG replay, host installation, elevation,
permission change, push, public release or production-eligibility change occurred.

### Repository And Preserved Candidate

Starting HEAD was `fc54150844b36c2e6d6a4fd18d6ea9dd065fd4fb`, branch
`release/v1.2.4-modernization`, working tree clean. This round changes only this
certification report; no runtime, installer, manifest, workflow, dependency or
test source changes. Its documentation-only commit is the final repository HEAD,
recorded after commit in
`.verify/v1.2.4-closure/unblock-round-20261008/unblock-attestation.json`.

The unchanged release-candidate source remains **fc54150844b36c2e6d6a4fd18d6ea9dd065fd4fb**.
No new production artifact is generated from the report-only commit. The
preserved candidate's sourceHead/dirty-zero evidence is not rewritten to name
the newer documentation commit. Earlier exact-repository-HEAD predicates below
describe their original build checkpoint, not a new production certification.

| Preserved Unsigned Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `.verify/v1.2.4-closure/release/ForgerEMS-Setup-v1.2.4.exe` | 61780805 | F7C3541860015A533D437CE80B8D0D8B70DD78F0DE9CF47091353C1175B8A9BB |
| `.verify/v1.2.4-closure/release/ForgerEMS-v1.2.4.zip` | 81742749 | 61A000203FD3E866C10221856322AD318A38562B5CE9E122626205997373B137 |

Both sizes/hashes were independently rechecked against current files before
editing. App and installer Authenticode were directly rechecked: **NotSigned**,
no signer/timestamp certificate. ZIP Authenticode is not applicable.
The existing `final-artifact-attestation.json`, checksums and artifacts are
preserved unchanged. `productionEligible:false` remains unchanged.

### Signing Boundary

Fresh read-only evidence: `unblock-round-20261008/signing-discovery.txt` and
`gh-ci-inventory.txt`, relative to the evidence root.
CurrentUser/My: eight certificates, zero Code Signing EKU identities.
LocalMachine/My: one certificate, zero Code Signing EKU identities.
No signing/certificate/PFX/Trusted Signing/Key Vault/Azure/HSM environment
variable names or authorized production-material references were established.
Repository references are configuration, not an available authorized identity.

All four read-only GitHub queries exit 0: repository environments empty,
zero self-hosted runners, no signing secrets/variables. Only the previously
recorded unrelated obsolete Kyra secret/variable names remain; no values are
printed and no configuration is deleted. No approved remote signing service
or owner-authorized PFX/HSM mechanism is established.

SDK signtool and existing signing tooling are available. Inspection confirms
SHA-256/RFC3161, Code Signing EKU/private-key/validity checks, exact publisher,
signtool plus Authenticode verification, and temporary-uninstaller capture.
This is tooling readiness, not an executed authorized signing operation.
Signer subject, issuer, thumbprint, validity and chain are NOT APPLICABLE to
the unsigned product; no ambiguous/developer identity is substituted.

Required owner action: provision an authorized ForgerEMS production identity
through the existing certificate-store path, or explicitly provision the
protected `production-release` environment and its
`FORGEREMS_SIGNING_CERT_THUMBPRINT`, `FORGEREMS_SIGNING_PFX_BASE64` and
`FORGEREMS_SIGNING_PFX_PASSWORD` secrets. Obtain credentials securely; do not
place key material in source, reports or chat. Merely finding a private key
would not establish authorization. No public workflow is invoked by this round.

### Windows Isolation Boundary

Fresh evidence: `isolation-host.txt`, `vbox-vminfo.txt` and `wsl-state.txt`
under `unblock-round-20261008/`.
Sandbox executables remain absent. Hyper-V module/service/hypervisor are present,
but non-elevated Get-VM is authorization-denied. The existing VirtualBox
capability-trial VM is powered off and is the same EFI-stalled setup, not a ready
Windows test system. No new sanctioned VMware/cloud/test-runner path is established.

WSL user is uid 1000, not in kvm; `/dev/kvm` is root:kvm mode 0660 and neither
readable nor writable. No QEMU/TPM process is running. The prior external WSL
shutdown and zero ready-marker result remain unchanged.
Owned QCOW2, firmware variables, TPM data and serial evidence are retained;
there is no saved running-memory snapshot or completed Windows installation.
The staged obsolete payload ISO is not attached or promoted to current input.
No material configuration/permission change justifies replaying unchanged TCG.

Required external action: supply an approved ready disposable Windows VM/runner
with administrative guest access and revert/reboot/evidence-transfer capability,
or authorize the narrowly scoped KVM access needed to attempt accelerated QEMU.
Approval alone is not lifecycle proof. Guest acceptance must precede execution:
OS/build/architecture, identity, clean baseline, disk/network/security state,
reset capability and evidence transfer. Existing authenticated Microsoft LTSC
evaluation media and the authentic v1.2.3-preview.1 fixture remain available.

### Explicit Release Answers And Gate Disposition

| Question / Gate | Current Answer |
| --- | --- |
| A: authorized production signing | NO; BLOCKED, no qualifying identity |
| B: exact installer clean-installed on disposable Windows | NO; BLOCKED / NOT EXECUTED |
| C: authentic previous-release upgrade | NO; BLOCKED / NOT EXECUTED |
| D: uninstall, reboot and residue | NO; BLOCKED / NOT EXECUTED; no residue classification claimed |
| E: driver changes | None performed by this round; installer driver-store/service/task deltas remain unexecuted |
| F: Defender | Preserved exact unsigned artifacts PASS, no threats, exit 0 at 11:04:35 UTC; no signed-artifact scan exists |
| G: release-candidate commit | fc54150844b36c2e6d6a4fd18d6ea9dd065fd4fb; report-only repository commit recorded separately |
| H: final candidate hashes | Independently rechecked; table above; no signed replacement exists |
| I: production eligibility | FALSE; unchanged, not set by this report |
| J: public-release blockers | Authorized production signing and usable Windows lifecycle execution |
| Reinstall / lifecycle integrity | BLOCKED / NOT EXECUTED |
| Secret/security-sensitive residue audit | No guest install exists to inspect; not claimed PASS |
| SmartScreen reputation | NOT TESTED; signature validity would not establish reputation |

The already-proven Release suite at fc541508 passes **1703/0/0**; five
consecutive code-stability runs pass **1703/0/0** each. Exact portable self-test
and GUI close exit 0, final ZIP inspection and unsigned Defender evidence remain
valid for the unchanged candidate. No fresh campaign, full suite, live resource
probe, vulnerability derivation or artifact rebuild is represented as executed
in this prerequisite-only round. Original command/exit evidence remains linked
through `final-artifact-attestation.json`.

Prior native stress, 19/20 metadata resolution with Rescuezilla fail-safe,
458-binary/14-identity notice inventory and no known NuGet advisory entries are
retained, not reopened. Historical CLR attribution remains unproved at
`GCHandleStore::CreateHandleOfType+0xC0`; no new crash-clearance claim is made.
Driver operations stay read-only/advisory. Wider DPI/accessibility and qualified
SDK/WinRT, Mono and PawnIO legal limitations remain as recorded below.

No current signed production build or completed installer lifecycle exists.
The new RELEASED terminal status cannot be justified; no gate is relaxed.
The following sections retain the preceding closure history and its evidence.

FORGEREMS_V1.2.4_BLOCKED

## Previous Closure Record

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
