# GitHub Actions Immutable-SHA Pinning — Implemented

Action references in the active workflows are **pinned to verified commit SHAs**
as of the v1.2.4 modernization pass. The pinned commits were resolved from each
action's official repository and recorded in the frozen audit evidence
(`.verify/v1.2.4-continuation/GitHub-actions-pins.json`).

Current pins:

- `actions/checkout@11d5960a326750d5838078e36cf38b85af677262` (v4) — `build.yml`, `release.yml`
- `actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9` (v4) — `build.yml`, `release.yml`
- `actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02` (v4) — `build.yml`
- `softprops/action-gh-release@3bb12739c298aeb8a4eeaf626c5b8d85266b0e65` (v2) — `release.yml`

The retired `kyra-sdk-package-mode.yml` workflow was removed with the Kyra
product surface; no Kyra workflow references remain.

When bumping an action, resolve the new release tag from the action's official
GitHub repository, verify the full commit SHA is the tag target, update the pin,
and retain the `# vX` comment. Keep an update mechanism (for example
Dependabot) or document the manual review cadence alongside the pins.

This is supply-chain hardening; it is not a certification claim.
