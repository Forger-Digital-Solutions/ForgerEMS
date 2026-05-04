# Git cleanliness audit — v1.1.12-rc.1

**Branch at audit time:** `main` (HEAD `2696195` and later local edits)  
**Scope:** `git status --short` snapshot taken during mechanical RC work.

---

## Summary

The working tree contained **many modified tracked files** and **many untracked** paths consistent with an in-progress feature branch that was **never fully committed** after prior work (USB Intelligence, Kyra, docs, tests). That is **not** a hygiene failure of the audit itself — it means **operators must commit intentionally** before sharing a clean RC tag.

`release/`, `dist/`, `bin/`, `obj/`, `TestResults/` are **ignored** by `.gitignore` and did **not** appear in `git status` — correct for generated outputs.

---

## Classification (from status snapshot)

### 1. Commit (source / docs / workflows — intended product)

| Path pattern | Notes |
|--------------|--------|
| `src/ForgerEMS.Wpf/**` (MainWindow, ViewModels, Services, Intelligence, etc.) | Product code |
| `tests/ForgerEMS.Wpf.Tests/**` | Tests |
| `docs/*.md` (FAQ, BETA_*, LEGAL, RELEASE_NOTES, new audits) | Product documentation |
| `.github/workflows/release.yml` | Release automation (was untracked — **should be committed**) |
| `tools/build-release.ps1`, `tools/build-forgerems-installer.ps1`, etc. | Release tooling |
| `README.md`, `SECURITY.md`, `RELEASE_PROCESS.md`, `.gitignore` | Repo policy / onboarding |

### 2. Generated / ignore (do **not** commit)

| Path | Notes |
|------|--------|
| `release/**`, `dist/**`, `**/bin/`, `**/obj/`, `TestResults/` | Per `.gitignore` |
| `*.exe`, `*.zip` in workspace | Broad ignore; release outputs |

**Suspicious but ignored:** A local tree may still contain old snapshots such as `release/v1.0.1/` from earlier experiments. They are **not** tracked by git if under ignored paths; delete locally if disk hygiene matters — **do not** add to git.

### 3. Accidental / remove (review before commit)

| Item | Action |
|------|--------|
| Duplicate **human testing** docs (`v1.1.9`, `v1.1.10`, `v1.1.11`) | **Keep** for history unless product policy says archive — they are not identical; avoid deleting without PM sign-off. |
| `RELEASE_NOTES_v1.1.4-beta.md` modified alongside newer beta work | **Review diff** — ensure no accidental revert of release history. |

### 4. Needs human review

| Item | Question |
|------|----------|
| Large batch of `??` under `src/.../Services/Intelligence/` | Confirm all files belong in-repo and are not experimental dumps. |
| `.gitignore` change | Confirm `*.exe` ignore does not block any **intentionally** tracked binary under `tools/` (spot-check: no tracked `.exe` in repo today). |

---

## Duplicate docs observation

- **Release notes:** Multiple `RELEASE_NOTES_*.md` and `docs/RELEASE_NOTES_*.md` — **intentional** versioned history; do not merge blindly.
- **Beta checklists:** `v1.1.9` / `v1.1.10` / `v1.1.11` filenames — historical; current RC uses **v1.1.12-rc.1** go/no-go plus prior checklist content.

---

## Secrets / env

- No `.env` appeared in `git status`.
- Run `.\tools\check-secrets.ps1` before any tag (see security audit doc).

---

## Verdict

**Safe to proceed** with mechanical RC once: (1) `ConvertTo-WindowsVersion` supports prerelease versions (fixed in `build-release.ps1`), (2) audit docs committed, (3) operator runs secret scan + full test + release dry-run/full build locally.
