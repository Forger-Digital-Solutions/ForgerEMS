# ForgerEMS — Privacy overview (Public Preview)

**Built by Forger Digital Solutions.** This is a practical summary of how the app handles data on your PC; it is **not** a substitute for a formal privacy policy review by your counsel.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — never send secrets in email.

---

## What is **not** collected by Forger Digital Solutions

Under normal, **offline-first** use:

- **Telemetry / crash reporting** default to **off** unless you explicitly set `FORGEREMS_TELEMETRY_ENABLED` / `FORGEREMS_CRASH_REPORTING_ENABLED` (see [ENVIRONMENT.md](ENVIRONMENT.md)).
- Forger Digital Solutions **does not** operate a hidden analytics pipeline that continuously uploads your desktop activity.  
- The app **does not** collect or centralize **API keys**, **passwords**, or **product keys** for upload to Forger Digital Solutions.  
- **Local scans** (System Intelligence, diagnostics, toolkit health) produce files on **your disk** — they are **not** silently exfiltrated to us when you are simply using those features.

**In-app update checks** query **public GitHub Releases** metadata over HTTPS when you use Settings or scheduled checks — that is GitHub’s API, not a private Forger “telemetry” endpoint. See [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md).

**Optional online Kyra** may contact **third-party** AI endpoints when enabled by an operator; that traffic is governed by those providers’ policies, not by “hidden” ForgerEMS analytics.

---

## What **may** be stored locally

ForgerEMS stores configuration, reports, and logs on your machine, typically under:

- **`%LOCALAPPDATA%\ForgerEMS\`**  
- Subfolders often include **`Runtime\reports`**, **`Runtime\logs`**, and **`logs`**.

These files can contain **paths**, **device names**, **diagnostics**, and similar technician-oriented detail. **Review and redact** before you attach anything to a bug report.

---

## Kyra (offline by default)

- **Offline / local Kyra** uses built-in rules and optional **local reports** you already generated. **Beta testers are not asked to supply API keys in the app** for this path.  
- **Optional online** providers are **developer/operator-managed** (environment or deployment configuration). They are **advanced** — not part of the default beta tester checklist.  
- When online paths are enabled and you allow context sharing, **sanitized** text may be sent per **Kyra Advanced** settings — not a dump of your entire disk.

---

## Logs and sharing

Full local logs may contain sensitive context. Before you share:

1. Open **View Full Logs** (or your log folder) and **read** what you are about to send.  
2. Prefer **sanitized** excerpts or in-app “safe summary” features when available.  
3. **Never** paste API keys, tokens, or private documents into email or chat.

---

## Third-party AI or network endpoints

If an **online** provider is enabled by an operator, prompts and optional context are handled under **that provider’s** terms and your network path. **Offline/local modes** remain available where implemented.

---

## Third-party tools

Tools you install separately are governed by their own policies.

---

## Beta

Privacy-related behavior may change between beta builds; check in-app **Settings → Kyra Advanced** and **Settings → App updates** for the current behavior on your build.
