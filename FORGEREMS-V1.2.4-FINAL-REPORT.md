# ForgerEMS v1.2.4 Final Report

Date: 2026-10-06

## Executive Result

Meaningful implementation, genuine tests, and fresh packaging are complete.
This is an **unsigned, non-production candidate**, not a closed production release.
Installer lifecycle, upgrade, supported-system coverage, full visual QA, driver
selection intelligence, and legal/signing approval remain incomplete.

The candidate was built from clean committed source
`e8820b8953c2da27c20f1dd6c7bac57eada4d5f8`. It is not published or tagged.
The earlier blocked report is preserved as evidence at
`.verify/v1.2.4-continuation/superseded-blocked-report.md`.

## Repository Baseline

| Item | Value |
| --- | --- |
| Repository | `I:\ForgerEMS_App\repo` |
| Origin | `https://github.com/Forger-Digital-Solutions/ForgerEMS.git` |
| Original branch | `main` |
| Working branch | `release/v1.2.4-modernization` |
| Original HEAD | `94c0030c9bd4916bee7cfee89e9dca4cbc083731` |
| Original message | Bump to 1.2.4-preview.5: test fixes for resolver constructor |
| Original commit date | 2026-08-23T09:39:28-04:00 |
| Original tree | `49ab5e0cfd1c3dc07c10b302a4e3c09ff3156be0` |
| Original working source | Clean |
| Candidate source HEAD | `e8820b8953c2da27c20f1dd6c7bac57eada4d5f8` |

Original source evidence remains under `.verify/v1.2.4-certification/`.
The final preservation check covers 729 baseline tracked files: 358 files
outside reviewed intentional changes are checked byte-for-byte, with **0
unexpected changes**. Reviewed delta paths total 421, including added/deleted
paths. Ignored generated outputs are excluded from this assertion.
Evidence: `source-integrity-final.json` in the continuation evidence directory.

The committed working-file SHA-256 inventory is
`committed-source-files-sha256.txt`; its SHA-256 is
`DD56E00332C67D9BCAB2CA0967E4082950B0403D4715546FE8D548D459DA3637`.
The workspace root and separate prerelease checkout were not reset.

## Previous Blockers And Resolution

Network access and confirmed assistant-only deletion were authorized and used.
Genuine NuGet restore succeeded; the previous fabricated dependency cache and
its 91-test result remain **disqualified**. No placeholder packages or DLLs are
used as current evidence. The earlier unauthorized implicit restore request
and cleanup remain documented in the certification evidence.

Signing credentials and a disposable Windows validation environment remain
unavailable. No elevation, host driver replacement/removal, firmware operation,
Windows reset, public release, tag push, or binary upload occurred.

## Network / Restore Evidence

Official NuGet, GitHub, Microsoft, Ubuntu, vendor, and upstream license sources
were used. Genuine cache: `.verify/v1.2.4-continuation/nuget`.
Fresh integration output: `integration-artifacts`; old `dotnet-artifacts`
outputs are not evidence for the final source.

Restore command includes official NuGet source, isolated packages/artifacts,
`--force --no-cache`, and MSBuild `UseArtifactsOutput=true`.
Logs: `restore.log`, `integration-restore.log`, `final-candidate-build.log`.
SDK: 8.0.425; runtime: 8.0.31. Microsoft's captured .NET 8 metadata identifies
LTS maintenance and EOL **2026-11-10**; migration planning is still needed.

## Kyra Removal

Removed runtime initialization, UI/navigation/settings, assistant providers,
SDK projects/references, gateway/npm files, assistant-only workflows, assets,
and tests. Shared diagnostic redaction/context sanitization remains under
neutral service names.

Current source has no active assistant runtime or configuration requirement.
Fresh ZIP inspection finds **0 Kyra-named entries**. Historical settings are
not unnecessarily deleted; unused values are ignored. Historical changelog,
audit, prior-release material, and negative removal tests retain justified
references. They are not advertised as current features or packaged runtime.
Old ignored generated outputs are preserved but not shipped.

## Version Normalization

`VERSION` is the frontend authority: **1.2.4**. MSBuild, UI, diagnostic metadata,
installer inputs, artifact names, and build scripts derive from it.
Assembly/file version: **1.2.4.0**; product version: **1.2.4**.
Backend date-version remains independent: **2026.10.06.1**.

Current legal/install/download examples are updated; historical prerelease
examples remain explicitly historical. The candidate's `channel: preview`
identifies an unsigned candidate, not a `1.2.4-preview.*` application version.
Version regression tests and fresh executable metadata checks pass.

## Published Release Truth

Official GitHub API capture on 2026-10-06 returns one published release:
`v1.2.3-preview.1`, marked prerelease. **No published stable release** is
reported. Remote tag: `c98feeb769380db7b1c3b9eb1518a78f1dbf9351`.
Latest local version-sorted tag remains `v1.2.1-preview.1`.

Published assets include installer, ZIP, `CHECKSUMS.sha256`, `release.json`,
and download instructions; GitHub supplies SHA-256 asset digests.
Evidence: `published-releases-2026-10-06.json`. Local source/version and
candidate artifacts must not be confused with public release state.

## Self-Updater

Highest eligible semantic version wins; publication time only breaks ties.
Drafts, malformed versions, wrong architecture, and untrusted asset URLs are
excluded. New profiles default stable-only; explicit beta choices remain.
Tests cover current/newer/older versions and hypothetical **v1.2.5 discovery**
without changing the shipped discovery algorithm.

Trusted same-release digest/checksum data binds artifact SHA-256. Downloads
without an expected hash are not offered as verified download actions.
Unqualified architecture names require matching release metadata.
Metadata requests are capped and bounded; tokens stay on `api.github.com`.
Timeout, cancellation, offline failure, missing assets, corrupt downloads,
hash mismatch, and signature failure are tested.

Installation/restart/rollback are **not automated**. Downloads are saved for
manual action, never silently executed. This is not proof of every possible
future OS/vendor compatibility condition or a successful production upgrade.

## Dynamic Resource Resolver

Central `resource-policy.json` descriptors and provider adapters replace frozen
payload URLs for eligible managed resources. Supported strategies include
GitHub stable metadata, vendor checksum indexes, and Ubuntu LTS metadata.
Resource IDs bind manifest, policy, and fresh expiring overlays.

Transport enforces trusted HTTPS origins/redirects, caps, bounded attempts, and
expected SHA-256. Cached/stale states are distinguished; stale metadata cannot
authorize downloads. Missing providers do not fall back to frozen archives.
Ventoy uses this pipeline and safe archive extraction.

The remaining official-page entries are explicit **manual exceptions** for
unimplemented or unsafe metadata/integrity/applicability adapters. They are
not represented as automatically verified latest releases. Complete dynamic
OEM/tool coverage remains a partial gate.

## Windows Update

Driver Hub now exposes an explicit read-only Windows maintenance scan and
native Windows Update settings link. The bundled probe uses CIM, registry,
structured PnPUtil/DISM where available, and Windows Update Agent.

Results include OS/build/architecture, service state, reboot indicators,
visible policy, update offers/categories/history, and failures. WUA success
code 2 is **not** treated as proof that no updates exist or Windows is healthy.
Feature/optional/driver offers require user action; no download or install is
requested. Cached and uncertain states remain visible.

There is no automatic update repair/reset or forced feature upgrade. Servicing
support, hidden enterprise controls, safeguards, and OEM currency are not
inferred from registry values or a successful search.

## Driver Store

Read-only structured enumeration and installed-device binding correlation are
implemented. The real captured host reports **126 third-party packages** and
**227 binding rows**. In-box bindings and third-party store inventory are
different scopes; neither equals driver update discovery.

Bound, boot-critical, critical-class, and unknown packages are protected.
Possible superseded/rollback labels are diagnostic hints, not proven removal
safety. **RemovalPermitted is always false.**
Signer names reported by inventory are not cryptographic signature validation.

## Driver Update Applicability

WUA offers can be correlated by exact hardware/compatible IDs. Candidate
matching does not establish OEM suitability, driver rank, package signatures,
or complete architecture/OS compatibility. **InstallationPermitted is always
false.** Generic numeric-version comparison never authorizes installation.

Complete OEM discovery, ranked applicability, protected cleanup decisions,
installation, and rollback are **not implemented/certified**.

## Supply Chain Security

Verified download staging uses expected SHA-256, size checks, private partial
paths, bounded streaming/retries/cancellation, and verified final promotion.
Unsafe paths, ZIP traversal/symlinks/duplicates/reserved names and inflated
size violations are rejected. Managed backend transfers enforce policy-bound
redirects instead of unrestricted fallback download chains.

Installer downloads require WinVerifyTrust chain policy and exact expected
publisher. Revocation uses cached-only verification and fails closed when
unavailable; native DLL lookup is restricted to System32. This is separate
from mere signature presence or a self-computed digest.

Production packaging/signing prerequisites fail closed without credentials.
Explicit `-UnsignedCandidate` produces non-production metadata. Protected
release environment/certificate provisioning is an owner responsibility;
that pipeline has not executed in CI during this pass.

## Dependencies / Vulnerabilities

Final official NuGet CLI vulnerability query reports **0 known vulnerable
packages** in the current app/test graphs. This is advisory coverage, not a
security guarantee. Source/graph isolation was checked against the actual
`integration-artifacts/obj/ForgerEMS.Wpf/project.assets.json`.

`System.Management`, `System.IO.Ports`, and `System.Threading.AccessControl`
are updated to **10.0.12**; CodeDom resolves to the same patch level.
LibreHardwareMonitor 0.9.6 remains pinned. Its BlackSharp 1.0.7,
DiskInfoToolkit 1.1.2, and RAMSPDToolkit-NDD 1.4.2 dependencies remain
compatibility pins rather than blindly forcing newer minor/major APIs.

Official deprecation review covers **43 distinct restored package/version
records**: **5 deprecated, 38 with no deprecation metadata reported, 0 unable
to verify**. The five are xUnit v2 2.9.3 test-only packages; upstream still
maintains security fixes. Deliberate xUnit v3/tooling migration is deferred.
Remaining outdated test tools are documented, not shipped application tools.
No active npm project remains after gateway removal.

Evidence: `packages-vulnerable-final.json`, `packages-outdated-final.json`,
`packages-deprecation-final.json`. The first deprecation capture failed gzip
parsing and is not the final audit. The corrected capture preserves separate
evidence; it does not manufacture absent metadata.

## Links

Final selected-production-source audit: **76 URLs**, **60 reachable**,
**16 unable to verify**, **0 broken**. These are the selected core docs,
Driver Hub catalog, embedded information text, and policy sources, not a
claim that every historical/developer URL was validated.

The obsolete Ubuntu driver link is replaced by the official current desktop
guide. Vendor anti-bot responses, timeouts, and metadata failures remain
explicitly unable to verify; a 403 is not called a successful check.
Evidence: `production-links-final.json`.
Repository search evidence: `source-search-gates.json`; historic audit/test
fixtures, XML namespace HTTP identifiers, license originals, deliberate
compatibility pins, and removal assertions are distinguished from active
downloads. Complete all-link closure remains partial.

## About / FAQ / Documentation

Updated current About, FAQ, legal, privacy, installer/download guides,
changelog, release notes, environment guide, and update documentation.
Current app copy no longer advertises the assistant. Historical documents
retain prior-release context with superseded notices.

Driver Hub contains the read-only Windows/Driver Store controls; Toolkit
Manager contains managed-resource checking. Bindings and real WPF window
construction are covered by affected tests. Full feature-level visual QA on
supported Windows machines is still unproved.

## Privacy / Terms / Licensing

Terms revision: **2026-10-06.v1.2.4**. Network disclosures describe
settings-controlled GitHub/vendor requests and user-triggered WUA queries to
the configured Microsoft/WSUS/enterprise service. Reports/support bundles
are not automatically uploaded; users must review redaction before sharing.
Legacy assistant preferences are not a required configuration surface.

Shipped notices cover MPL sensor dependencies, HidSharp Apache-2.0, Microsoft
runtime licenses/notices, Windows SDK material, and Mono's upstream license
scope. LibreHardwareMonitor is replaceable as a loose assembly. Embedded
PawnIO modules 0.1.6 have LGPL text and exact upstream source ZIP:
`647BF55985837302B00AD2C05FC3FB700F140AF2E34693F390FF2DB47B608867`.
No kernel driver/installer is bundled.

The first SDK license capture incorrectly serialized bytes as decimal lines.
It was rejected and replaced with the exact official RTF bytes before final
packaging: **246945 bytes**,
`DD07EB178E00C6BBA4148457FC00FF77CD4887EB521D504186FE59C9EC8BBE62`.
Both source and final packaged copy match.

`runtime-dependency-inventory.json` identifies **13 package/runtime-pack
records declaring runtime assets**; embedded components need separate
review. LGPL corresponding-source/relinking, exact Mono redistribution, and
Windows SDK/WinRT redistribution require counsel review. No attorney review
or universal license clearance is claimed. Proprietary terms preserve
third-party license rights.

## Test Results

| Run | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| Initial genuine updater-focused run | 115 | 0 | 0 | 115 |
| Integration-focused run | 376 | 0 | 0 | 376 |
| Broad solution gate, Debug | 1621 | 0 | 0 | 1621 |
| Subsequent affected UI/integration suites | 200 | 0 | 0 | 200 |
| Subsequent documentation suites | 35 | 0 | 0 | 35 |
| Final candidate affected suites | 63 | 0 | 0 | 63 |

These overlapping runs must **not** be summed into a unique test count.
The broad gate preceded the final small UI/cache/docs changes; affected
tests and fresh packaging cover those subsequent changes, not another
claimed full-suite run. Broad recorded elapsed time: **104.737 seconds**.

TRX files are under `test-results/`: `continuation-tests.trx`,
`integration-focused-tests.trx`, `broad-gate-final.trx`,
`consolidation-narrow.trx`, `docs-cleanup.trx`,
`final-candidate-focused.trx`. Commands/logs remain in the evidence directory.
Build configuration is Release for the final self-contained package;
automated-suite configurations are recorded in their respective logs.
Analyzer warnings remain; they are not represented as a warning-free build.

An earlier broad attempt aborted with internal CLR error `0x80131506`,
recording 699 passed / 29 failed / 0 skipped / 728 results. Those failures
were investigated, including stale selectors and actual catalog path drift.
Later complete runs did not reproduce the abort. A verified unsafe LHM
process-global native lifecycle was serialized and regression-tested.
**The abort's cause remains unconfirmed**, without discriminating dump
evidence; passing reruns do not establish causality.

## Real Windows Evidence

Read-only host: Windows 11 Pro, x64, build **10.0.26300**, registry release
26H2/UBR 9457; servicing/support status remains unable to verify.
The frozen native probe reports **126 third-party packages**, **227 binding
rows**, and successful WUA search with **3 offers** (one Defender, two
drivers). Nothing was downloaded/installed by that probe.

Final packaged `ForgerEMS.exe --self-test` exits **0** using isolated
process-only LOCALAPPDATA/TEMP/TMP and Deep Sensor Mode Off.
Its report verifies bundled backend availability/alignment, required files,
PowerShell checks, unsafe-drive benchmark refusal, and disabled sensors.
Evidence: `qa-host/localappdata/ForgerEMS/Runtime/diagnostics/published-self-test.txt`.

The packaged process shows a real ForgerEMS top-level window; no Terms
acceptance or feature interaction was performed. The initial screen capture
was occluded by another application and **rejected**. PrintWindow captured
only window chrome, not WPF content. Neither is full visual QA evidence.
Capture limitations: `qa-host/capture-attempts.md`.
Host writes for this observation were confined to the isolated QA store;
known-folder crash-report fallback remains a residual isolation caveat.

## Packaging

Authoritative candidate directory:
`.verify/v1.2.4-continuation/final-candidate/release/`.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `ForgerEMS-Setup-v1.2.4.exe` | 61771610 | `7ED90C7E0F36D1962394E251E559537178C7418BFBF355DC12A5298B311A6B44` |
| `ForgerEMS-v1.2.4.zip` | 81701593 | `E741E443568D9C2B2B3B94F14B5801BB32D9ABED7ADB172442BCD64AC9E5501B` |

Generated UTC: **2026-10-06T13:57:02.5669512Z**.
Source HEAD: `e8820b8953c2da27c20f1dd6c7bac57eada4d5f8`;
source dirty-file count at packaging: **0**.
Metadata explicitly declares `signed=false`, `unsignedCandidate=true`,
`productionEligible=false`. App and installer signatures are **NotSigned**.

Fresh self-contained win-x64 output, portable ZIP, and Inno Setup 6.7.3
installer are real artifacts. ZIP inspection: **65 entries, 0 Kyra-named
entries, 0 .sys/.inf/.cat entries**. New policy/probe/checksums, current docs,
loose LHM DLL, licenses, and source ZIP are staged.
Loose LHM SHA-256:
`C2274F91322207EE86C344C76E791E7FDF6507ACE95546ADE2C5DF3ABE274246`.

Command: `tools/build-release.ps1 -UnsignedCandidate` with isolated absolute
output/release roots, genuine NuGet cache, and verified Inno compiler.
Evidence: `final-candidate-build.log`, package `release.json` and
`CHECKSUMS.sha256`. Earlier candidates and the interrupted first final build
are preserved but **superseded**, not current release evidence.

## Installer Validation

Compilation, version metadata, staging, and checksum evidence pass.
**Clean installation, installed launch, uninstall, and registration behavior
are not validated.** No host installation was substituted for disposable
testing. Hyper-V enumeration lacks permission; VBoxManage and Windows
Sandbox are unavailable. No elevation was requested.

## Upgrade Validation

**Not performed.** Latest public prior build is `v1.2.3-preview.1`, not a
published stable release. Settings/data preservation and removal of stale
installed assistant artifacts must be observed in an isolated upgrade test.
Source reasoning/unit checks are not equivalent to actual upgrade proof.

## Security Review

Fixed expected-hash omission, token-forwarding/default-header risk,
unrestricted resolved-resource redirects, fail-open overlay expiry/binding,
incorrect WinVerifyTrust revocation flags, unsafe archive paths, stale
selectors, malformed snapshot handling, and native sensor lifecycle overlap.
GitHub Actions pins and protected signing prerequisites are implemented.

Residual risks include unconfirmed earlier CLR abort, incomplete OEM/rank/
signature applicability, downstream vendor semantics, counsel review,
unexecuted signing/CI pipeline, and unvalidated installer/upgrade lifecycle.
This report is not an independent penetration-test certificate.

## Performance

New checks are asynchronous, user-triggered, bounded, and cache-aware; no
new scan/download is triggered on every render. The actual initial packaged
window was reported shown in approximately **2.4 seconds** on this host.
This is a single observation, not a baseline comparison or startup benchmark.
Full performance regression proof remains incomplete.

## Commits

- `03212b296d9f625f04e91a9597077f0200f75d2f` - reviewed modernization,
  assistant removal, versioning, resource/Windows foundations, tests/docs.
- `e8820b8953c2da27c20f1dd6c7bac57eada4d5f8` - preserve exact official SDK
  license bytes; this is the **packaged source revision**.
- This report is committed separately after packaging; that documentation
  commit does not change the artifact's source revision.

No commits were amended, pushed, or publicly tagged.

## Remaining Issues

1. Supply real signing credentials and execute the protected signing pipeline.
2. Validate clean install/uninstall and previous-published-build upgrade in a
   disposable Windows environment, including profile preservation.
3. Complete supported stable Windows coverage and feature-level visual QA;
   do not generalize an insider-host observation to every supported system.
4. Implement/audit full OEM candidate discovery, rank/OS/architecture/
   signature applicability and rollback, or retain clearly bounded manual
   behavior; current hardware-ID candidates are not installation decisions.
5. Close uncertain external links and remaining manual resolver adapters.
6. Obtain legal review for identified redistribution/relinking obligations;
   plan .NET 8 migration before 2026-11-10.
7. Retain/diagnose the earlier native CLR abort if it reproduces; its causal
   relationship to the fixed LHM race is not established.

## Certification

Source preservation, genuine restore, assistant removal, frontend versioning,
future-version discovery tests, read-only Windows/Driver Store foundations,
affected automated tests, and fresh unsigned packaging have evidence.
The complete A-T production acceptance set **does not pass**: applicability,
all-link/resource closure, legal/signing, supported-system visual/regression,
installer, and upgrade gates remain partial or unproved.

This is a meaningful implementation candidate, not a planning-only pass and
not a production-readiness certificate. No public publication is authorized.

FORGEREMS_V1.2.4_PARTIAL
