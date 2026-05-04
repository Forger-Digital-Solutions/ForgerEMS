# CI / GitHub Actions release audit — v1.1.12-rc.1

## `build.yml`

| Step | Assessment |
|------|------------|
| `dotnet restore` / `build` / `test` | Correct gate for PRs and `main`. |
| Manifest JSON parse | Good lightweight schema smoke. |
| `Verify-VentoyCore.ps1` | Ensures backend bundle expectations. |
| `build-release.ps1 -DryRun` | Validates release script still runs on CI agent. |
| `upload-artifact` for `release/**`, `dist/**` | Useful for debugging; artifacts are **not** committed to git — OK. |

**Note:** Dry-run uses `-Version 1.0.0-beta.ci` — exercises script with prerelease-like id; `ConvertTo-WindowsVersion` must accept that pattern (numeric core `1.0.0`).

---

## `release.yml`

| Step | Assessment |
|------|------------|
| Tag / `workflow_dispatch` version resolution | Correct (`v` prefix stripped). |
| `choco install innosetup` | Appropriate for Windows runner. |
| `build-backend-release` → `build-release` | Correct ordering. |
| Post-build `dotnet test` | Good safety net before publish. |
| `softprops/action-gh-release` files | Includes **ZIP**, **EXE**, `CHECKSUMS.sha256`, `release.json`, `DOWNLOAD_BETA.txt` — matches mechanical spec. |
| `release-body.md` composition | **Improved** in this pass: release header text built without accidental leading-space markdown from YAML here-strings (see workflow file). |

---

## Risks

| Risk | Mitigation |
|------|------------|
| `fail_on_unmatched_files: true` | Ensures missing artifact fails loudly — good. |
| Token scope `contents: write` | Standard for `GITHUB_TOKEN` releases — keep branch protection rules in place. |

---

## Not done in repo automation

- Code signing — external to Actions until certificates exist.
