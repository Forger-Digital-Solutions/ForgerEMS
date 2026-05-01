# ForgerEMS Beta Testing Guide

Copyright © 2026 Forger Digital Solutions. All rights reserved.

**Beta issue? Send logs/screenshots to ForgerDigitalSolutions@outlook.com** — include build v1.1.4 and steps. Do not email API keys, passwords, serial numbers, or private documents.

Beta feedback / support: **ForgerDigitalSolutions@outlook.com**

This build is beta software. Use caution and report issues with logs/screenshots.

## Safety First

- Do not use production/important USB drives without backups.
- Double-check USB drive letter and size before starting Setup/Update/Ventoy actions.
- Do not select tiny EFI/VTOYEFI partitions; select the large removable data partition.
- Offline Local Kyra works without API keys.
- Free API providers are optional and may have limits/outages.
- System context sharing is OFF by default.
- API keys are session-only for this beta.
- Do not paste API keys into chat; use provider settings fields only.
- Manual toolkit items exist because of licensing/EULA restrictions.

## Quick Beta Pass

1. Launch app and verify it opens cleanly.
2. In USB Builder:
   - confirm blocked warning appears on unsafe/EFI partitions
   - confirm no target selected path is clear
   - verify drive letter/size visibility before actions
3. In System Intelligence:
   - run scan
   - verify summary/cards render even when fields are missing/unknown
   - verify resale section shows offline estimate + confidence wording
4. In Toolkit Manager:
   - run health scan
   - verify Installed/Missing/Manual/Failed counts are understandable
   - open manual link for a manual item (if available)
5. In Kyra:
   - confirm Offline Local mode works with no API key
   - tap **Refresh Provider Status** after setting a test env var (or session key) and confirm the credential source line updates
   - test one free provider if desired
   - ask resale prompts: "What should I list this for?" and "Make me a listing."
   - verify eBay comps status is honest (active-only/unconfigured) and OfferUp/Facebook are manual/future only
6. Settings → **App updates**:
   - confirm “check now” does not freeze the UI and handles offline without crashing
   - confirm no installer runs unless you explicitly download/launch it
7. Diagnostics:
   - try Link / Download Safety Checker with a known-good vendor HTTPS URL and a deliberately suspicious example
   - try Downloaded file / EXE safety on a small text file renamed to `.exe` and confirm the app never runs it (read-only hash + heuristics)
   - try WSL command runner with safe commands; confirm Stop does not crash the app
8. Logs:
   - confirm logs update and are readable
   - copy logs and include screenshot when reporting issues

## Useful Paths

- Runtime logs: `%LOCALAPPDATA%\ForgerEMS\Runtime\logs`
- Runtime reports: `%LOCALAPPDATA%\ForgerEMS\Runtime\reports`
- Runtime diagnostics: `%LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics`
- USB-side reports (if generated): `<USB_ROOT>\_reports`

## Report Template

- Build version: **ForgerEMS Beta v1.1.4 — Whole-App Intelligence Preview** (or copy exact string from the app footer / diagnostics)
- What you clicked:
- Expected result:
- Actual result:
- Screenshot(s):
- Log snippet:
- Whether estimate was offline-only or comps-assisted:
