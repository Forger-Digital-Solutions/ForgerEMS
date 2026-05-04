# Update system — v1.2.0 Public Preview

ForgerEMS checks **GitHub Releases** for the configured owner/repo (defaults: `Forger-Digital-Solutions/ForgerEMS`, overridable via `FORGEREMS_GITHUB_OWNER` / `FORGEREMS_GITHUB_REPO`).

## Selection rules

1. Fetch up to 100 releases from the GitHub API (`published_at` ordering in service logic).
2. Filter by **channel** / prerelease policy from **Settings → App updates** (`UpdateReleaseChannel`).
3. Compare the newest **eligible** release version to the installed **semantic version** from `AppReleaseInfo.Version` (also mirrored in assembly informational version).
4. Prefer **HTTPS** ZIP assets matching `ForgerEMS-v*.zip` patterns; installer EXE is a separate “advanced” path.

## States users see

- **Up to date** — eligible latest ≤ installed (or ignored version).
- **Update available** — newer eligible release with a suitable asset.
- **No suitable assets** — release exists but has no acceptable download asset.
- **Offline / needs attention** — timeout, DNS, non-JSON, or HTTP errors surfaced without raw bodies in UI.

## Beta vs preview

Prerelease tags (`preview`, `beta`, `rc`) are normal. Use **Include Beta / RC** in Settings when tracking mechanical RCs; public-preview users may stay on preview channels per release policy.

## Further reading

- Historical detail: [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md)
- Environment knobs: [ENVIRONMENT.md](ENVIRONMENT.md)
