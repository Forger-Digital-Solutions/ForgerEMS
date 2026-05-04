# ForgerEMS Beta v1.1.4 — Whole-App Intelligence Preview

Recommended download for beta testers:
Download ForgerEMS-v1.1.4-beta.1.zip, extract it, then double-click START_HERE.bat.

## Overview

This pre-release focuses on **Kyra** AI orchestration, **System Intelligence**-driven answers, safer **USB / toolkit** workflows, **diagnostics** hardening, and **in-app update** awareness—while keeping **offline Local Kyra** fully usable without API keys.

## Major features

- **Kyra AI provider orchestration** — Hybrid routing between Local Kyra and configured online providers (free pool / BYOK), with privacy-aware system context, failover, and clear **response source** labels.
- **Offline / local mode** — Local Kyra and local rules remain available with no API keys; machine-specific answers prefer **System Intelligence** when a scan exists.
- **Provider refresh** — Environment-backed API credentials, session key handling, and clearer provider status in **Kyra Advanced Settings**.
- **System Intelligence / resale** — Local scan context for upgrades, resale prep, health summaries, and listing-style drafts where applicable (offline estimates; no live marketplace scraping in this beta).
- **USB Builder & Toolkit Manager** — Continued integration with bundled backend scripts, managed downloads, and technician-oriented flows.
- **Diagnostics** — WSL command execution hardening, link safety checks, and downloaded-file safety analysis hooks aligned with technician workflows.
- **Update notifications** — GitHub release check path and in-app update awareness (see settings / header behavior in this build).

## Known beta limitations

- Free API providers may rate-limit, change terms, or have outages; **offline fallback** is recommended for critical paths.
- **Online system context sharing** is **off by default**; enable only if you accept sanitized summaries leaving the machine.
- Resale **pricing** is **local/heuristic** unless a future marketplace provider is officially configured.
- Some provider slots remain **stubs** for future broker/API work.

## Manual verification

Use the checklist in **`FINAL_MANUAL_SMOKE_TEST.md`** after install.

## Support

**ForgerDigitalSolutions@outlook.com**
