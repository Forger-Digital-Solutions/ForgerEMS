# Ventoy Distribution Readiness

## Current Score

Current readiness: `8.8/10`

## Why It Is Not Lower

The project now has:

- canonical script and manifest locations
- backward-compatible entrypoints
- path-safety and dry-run guarantees
- deterministic release assembly
- generated release checksums and integrity signatures
- offline verification by default
- optional online upstream/provenance warnings
- explicit ownership boundaries
- release classification metadata
- active local CI workflow configuration under `.github/workflows/`
- hard checksum gating for `candidate` and `stable`

That is enough for disciplined real-world candidate/stable packaging with a
credible local release process.

## Why It Is Not Higher

The project still lacks:

- hosted CI run history proving the workflow passes after a remote push
- cryptographic publisher signing
- source-owned pipelines for bundled vendor payloads
- higher-confidence provenance coverage across the shipped vendor inventory

## Concrete Gate For 9/10

All of these must be true:

1. the first source-only baseline commit exists on `main`
2. `origin` is configured and the branch has been pushed upstream
3. `.github/workflows/ventoy-core.yml` passes on a GitHub-hosted Windows run
4. the hosted run shows:
   - offline verify pass
   - build-release pass
   - online verify executed as non-blocking
   - uploaded release artifacts
5. the hosted release bundle passes the human review checklist

## Concrete Reasons It Still Stays Below 10/10

Any of these keep the project below `10/10`:

1. release integrity is sealed, but not publisher-identity signed
2. bundled vendor/community payloads are still not fully source-owned
3. `ForgerTools` artifacts still lack a tracked source/build pipeline inside
   this release discipline
4. vendor inventory provenance is still incomplete for many shipped
   non-managed items

## What Still Blocks Production-Grade Distribution

- the GitHub Actions workflow is prepared locally but not yet proven by a
  hosted run
- `SIGNATURE.txt` is an integrity seal, not a publisher-identity signature
- vendor/community payload ownership is still mixed
- some upstreams remain operationally noisy or HEAD-restricted
