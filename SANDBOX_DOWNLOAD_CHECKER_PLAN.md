# Sandbox / download checker — future design (ForgerEMS)

This document records intent only. **v1.1.4 does not** auto-launch Windows Sandbox, Hyper-V VMs, or execute quarantined downloads.

## Goals (future)

- Optional operator-controlled workflow to copy a file into an isolated environment for manual inspection.
- Strict opt-in: no silent execution, no detonation pipelines, no bypass of Windows security policy.

## Non-goals

- Automatic malware execution or “trust scores” presented as guarantees.
- Scraping third-party warez or illegal sources.

## Beta (current)

- Local URL heuristics + optional HTTPS HEAD + optional download to `%LOCALAPPDATA%\ForgerEMS\Quarantine` with SHA256 only.
- ForgerEMS **v1.1.4** may notify about newer app builds via GitHub Releases; it never auto-runs installers or sandbox VMs.

## References

- Windows Sandbox (optional Windows component), Hyper-V, VMware, VirtualBox for manual operator use.
