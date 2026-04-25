# Ventoy Hosted CI Troubleshooting

Use this guide when the first hosted GitHub Actions run does not behave like
the local baseline.

## `pwsh` / PowerShell Issues

Check:

- the workflow uses `runs-on: windows-latest`
- the workflow uses `shell: pwsh`
- the failing step is calling the expected repo-relative script path

Likely symptoms:

- "command not found"
- script not recognized
- PowerShell parses the command differently than expected

Practical response:

- confirm the step uses `.\ventoy-core\Verify-VentoyCore.ps1`
- confirm the step uses `.\Tools\build-release.ps1`
- confirm the workflow file in the pushed commit matches the local file

## Repo-Relative Path Mistakes

Check:

- all workflow script paths are relative to the repository root
- the repo root contains `.github/`, `ventoy-core/`, `manifests/`, and `Tools/`

Likely symptoms:

- file-not-found errors
- build script cannot find `manifests/ForgerEMS.updates.json`
- verification cannot find the bundled manifest

Practical response:

- inspect the checkout contents in the hosted run
- confirm no step changed the working directory unexpectedly
- keep workflow calls rooted from the checkout root

## Casing / Path Separator Issues

Hosted Windows runners are case-insensitive, but consistency still matters.

Check:

- `.github/workflows/ventoy-core.yml`
- `ventoy-core/Verify-VentoyCore.ps1`
- `Tools/build-release.ps1`

Practical response:

- keep repo paths in docs, scripts, and workflow steps aligned with actual
  on-disk names
- prefer repo-relative paths exactly as they appear in the tree

## Execution Policy / Permission Surprises

Likely symptoms:

- a script refuses to launch
- nested PowerShell process fails unexpectedly

Practical response:

- confirm the workflow is running under `pwsh`
- confirm the scripts are invoked directly from the repo checkout
- note that the core scripts already use `-ExecutionPolicy Bypass` for their
  internal compatibility launches where needed

## Checksum / Signature Validation Failures

Likely symptoms:

- `release-checksums-are-valid` fails
- `release-signature-is-valid` fails
- bundle verification passes locally but not in hosted CI

Practical response:

- rebuild the release bundle from the same commit
- confirm `CHECKSUMS.sha256` was generated after the final copied files were in
  place
- confirm `SIGNATURE.txt` was generated after `CHECKSUMS.sha256`
- treat mismatches as release drift, not as harmless noise

## Online Warning Noise

Likely symptoms:

- `-Online` reports Windows/Microsoft download probe failures
- third-party sites return `403` or `405` to HEAD requests
- vendor inventory warnings remain noisy

Practical response:

- remember the online step is non-blocking by design
- distinguish managed checksum failures from vendor provenance warnings
- treat HEAD-restricted upstreams as operational noise unless the managed
  download becomes unverifiable

## First Hosted Run Success Criteria

The first hosted proof run is good when:

1. the workflow triggers on push
2. offline verification passes
3. `Tools/build-release.ps1` passes
4. online verification runs and does not fail the workflow
5. artifacts upload successfully
