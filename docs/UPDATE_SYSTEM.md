# ForgerEMS update system (GitHub Releases)

This document describes how **in-app update checks** relate to **GitHub Releases** — not to every git push or branch tip.

**Current release (v1.2.4):** the in-app display line is **ForgerEMS v1.2.4** (see `AppReleaseInfo`). Historical preview tags used semver prereleases such as `v1.2.4-preview.4`. The parser accepts semantic prerelease tags only when Beta/RC is explicitly enabled; this does not imply those tags are published. Locally built **unsigned candidates** share the version number but are distinct from production releases — they carry no Authenticode signature, are marked `unsignedCandidate`/`productionEligible=false` in their `release.json`, and must never be published or treated as shipped releases. Env overrides: `FORGEREMS_GITHUB_OWNER`, `FORGEREMS_GITHUB_REPO`, `FORGEREMS_UPDATE_USER_AGENT` — see `docs/ENVIRONMENT.md` and `docs/UPDATE-SYSTEM-v1.2.0.md`.

---

## What the app checks

ForgerEMS checks **published GitHub Releases** for:

**`Forger-Digital-Solutions/ForgerEMS`**

It does **not**:

- Watch raw git commits or default branches  
- Infer “latest” from random download URLs  
- Treat the installer filename as the source of truth for version numbers  

The app uses the GitHub **Releases** API, reads the list of releases, and picks the eligible release with the **highest semantic version** parsed from `tag_name`/`name` under your **channel** setting:

- **Include Beta / RC**: prereleases are allowed.
- **Stable only** (default): prereleases are skipped — both GitHub-flagged prereleases and releases whose parsed version carries a semver prerelease suffix, even if mislabeled as stable.

The **version** used for comparison comes from the release **`tag_name`** / **`name`** (for example `v1.2.4` or `ForgerEMS v1.2.4`; the parser also accepts historical prerelease-shaped tags such as `v1.2.4-preview.4`, `1.2.4-preview.4`, and `v1.1.12-rc.*` under the Beta/RC channel), **not** from guessing based on asset filenames.

---

## When do users see an update?

Users see an update when **you** publish a **GitHub Release** for a **tagged** version — after CI has attached assets. **Pushing commits alone does not ship an update** to testers who only use the released app.

Typical flow:

```bash
git add -A
git commit -m "chore(release): prepare v1.1.12-rc.3"
git tag v1.1.12-rc.3
git push origin main
git push origin v1.1.12-rc.3
```

(Annotated tags are fine if your release process uses them; the important part is that a **GitHub Release** exists for the tag and the tag — or, failing that, the release title — carries a parseable semantic version; `published_at` is only informational.)

After GitHub Actions (for example `.github/workflows/release.yml`) finishes, the release page should show assets such as:

- `ForgerEMS-v{version}.zip` (**recommended** for humans)  
- `ForgerEMS-Setup-v{version}.exe` (**advanced / direct**; SmartScreen is often stricter)  
- `CHECKSUMS.sha256`, `DOWNLOAD_BETA.txt`, and other release metadata as you publish them  

**Users will not receive updates from every commit** — only when a new **release** (with a higher semantic version than what they run) is eligible under the selected channel. A release where neither the tag nor the release name yields a parseable version is skipped and never offered.

---

## What the app offers after a release is selected

After the newest eligible release is chosen, the app **inspects assets** on that release:

1. Preferred **ZIP** patterns (ForgerEMS + Beta naming, or `ForgerEMS-v*.zip`), then other **.zip** assets.  
2. A **standalone `.exe`** is treated as **Advanced** in the UI — not the primary “just download this” path for beta testers.

If neither a release's tag nor its name parses as a semver, that release is **skipped** — it is never offered as an update. If every published release is unparseable, the check fails with a metadata-invalid diagnostic rather than guessing.

Under **Settings → App updates**, copy explains:

> Latest release is chosen by highest semantic version tag, then assets are inspected.

The **Copy Update Diagnostics** button copies a safe diagnostic summary for support. It includes version/channel/update-check state and redacted failure detail; it is not an installer action and does not copy secrets.

---

## Tester workflow (ZIP-first)

1. Wait for the release job to finish after the tag is published.  
2. Open the **GitHub Release** page (from the app link or the repo **Releases** tab).  
3. Download the **`ForgerEMS-…​.zip`** asset.  
4. Verify **`CHECKSUMS.sha256`** when provided on the same release.  
5. Extract → **`START_HERE.bat`** → complete install per prompts.  

Nothing in ForgerEMS **auto-installs** or **auto-runs** an update; downloads go where **you** choose (for example `%LOCALAPPDATA%\ForgerEMS\Updates` when you use **Download** in Settings).

---

## Troubleshooting

| Message | Meaning |
|--------|---------|
| No published ForgerEMS release / no stable release | No matching release for the selected channel, or only prereleases when **Stable only** is on. |
| Network / timeout | Offline, DNS, firewall, or GitHub unreachable. |
| Update source could not be reached | Often a 404 on the releases API (wrong repo, private repo, or path). |
| Access denied / rate limited (403/429) | GitHub rejected the API call. Common causes: **missing or invalid User-Agent** (fixed in app builds that send `ForgerEMS/{version}` via `HttpClient.DefaultRequestHeaders.UserAgent`), **unauthenticated rate limit** (60 requests/hour per IP — wait and retry), corporate proxy blocking `api.github.com`, or a private/wrong repo. Operators may set optional `FORGEREMS_GITHUB_TOKEN` (PAT with **public_repo** read scope only) in the user environment to raise limits — never commit tokens. |
| Recommended ZIP asset was not found | A release exists, but the expected ZIP naming was not found; still open the release page and pick assets manually if needed. |

Always prefer **official release assets** over cloning main or downloading from unofficial mirrors.
