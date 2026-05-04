# ForgerEMS update system (GitHub Releases)

This document describes how **in-app update checks** relate to **GitHub Releases** — not to every git push or branch tip.

---

## What the app checks

ForgerEMS checks **published GitHub Releases** for:

**`Forger-Digital-Solutions/ForgerEMS`**

It does **not**:

- Watch raw git commits or default branches  
- Infer “latest” from random download URLs  
- Treat the installer filename as the source of truth for version numbers  

The app uses the GitHub **Releases** API, reads the list of releases, and picks the **newest eligible release** using **`published_at`** (newest first), then applies your **channel** setting:

- **Include Beta / RC** (typical for beta builds): prereleases are allowed.  
- **Stable only**: prereleases are skipped; only non-prerelease releases count.

The **version** used for comparison comes from the release **`tag_name`** / **`name`** (for example `v1.1.12-rc.3`, `1.1.12-rc.3`, or `ForgerEMS v1.1.12-rc.3`), **not** from guessing based on asset filenames.

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

(Annotated tags are fine if your release process uses them; the important part is that a **GitHub Release** exists for the tag and **`published_at`** reflects when it went public.)

After GitHub Actions (for example `.github/workflows/release.yml`) finishes, the release page should show assets such as:

- `ForgerEMS-Beta-v{version}.zip` or `ForgerEMS-v{version}.zip` (**recommended** for humans)  
- `ForgerEMS-Setup-v{version}.exe` (**advanced / direct**; SmartScreen is often stricter)  
- `CHECKSUMS.sha256`, `DOWNLOAD_BETA.txt`, and other release metadata as you publish them  

**Users will not receive updates from every commit** — only when a new **release** (with a `published_at` they can fetch) supersedes what they already have under the selected channel.

---

## What the app offers after a release is selected

After the newest eligible release is chosen, the app **inspects assets** on that release:

1. Preferred **ZIP** patterns (ForgerEMS + Beta naming, or `ForgerEMS-v*.zip`), then other **.zip** assets.  
2. A **standalone `.exe`** is treated as **Advanced** in the UI — not the primary “just download this” path for beta testers.

If the tag cannot be parsed as a semver, the app may still show that a **newer release may exist**, with a link to the **GitHub Release** page, without crashing.

Under **Settings → App updates**, copy explains:

> Latest release is chosen by GitHub release publish date, then assets are inspected.

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
| Recommended ZIP asset was not found | A release exists, but the expected ZIP naming was not found; still open the release page and pick assets manually if needed. |

Always prefer **official release assets** over cloning main or downloading from unofficial mirrors.
