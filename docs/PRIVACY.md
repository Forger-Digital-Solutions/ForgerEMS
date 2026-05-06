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

**Optional online Kyra** may contact the **ForgerEMS Gateway** or third-party AI endpoints when enabled by an operator; that traffic is governed by the gateway/provider configuration and is not hidden ForgerEMS analytics.

---

## What **may** be stored locally

ForgerEMS stores configuration, reports, and logs on your machine, typically under:

- **`%LOCALAPPDATA%\ForgerEMS\`**  
- Subfolders often include **`Runtime\reports`**, **`Runtime\logs`**, and **`logs`**.

These files can contain **paths**, **device names**, **diagnostics**, and similar technician-oriented detail. **Review and redact** before you attach anything to a bug report.

---

## System Intelligence, Hardware X-Ray, and Deep Sensor Mode

ForgerEMS runs diagnostics locally. **Deep Sensor Mode** reads supported local hardware sensor data only while the app is running or System Intelligence / Hardware X-Ray scans are executed.

- Sensor data is **not sold**.
- Sensor data is **not automatically uploaded**.
- Reports are **not automatically sent** to support.
- You choose when to copy, export, or share reports.

Reports may include hardware model, CPU/GPU/RAM/storage info, battery info, network adapter details, USB device details, Windows version, provider status, and diagnostic notes.

Default support reports should be redacted where supported, but you should still review reports before sharing. Do not send product keys, serial numbers, service tags, API keys, tokens, passwords, private documents, or sensitive personal files to support.

---

## Kyra (offline by default)

- **Offline / local Kyra** uses built-in rules and optional **local reports** you already generated. **Beta testers are not asked to supply API keys in the app** for this path.  
- **Optional online** providers are **developer/operator-managed** (environment or deployment configuration). They are **advanced** — not part of the default beta tester checklist.  
- **ForgerEMS Gateway** beta access sends prompts to a ForgerEMS-managed HTTPS gateway using a revocable beta token. Provider API keys stay server-side and are not shipped in the desktop app. Usage limits may apply during beta.
- When online paths are enabled and you allow context sharing, **sanitized** text may be sent per **Kyra Advanced** settings — not a dump of your entire disk.
- **System Intelligence context** may be included in Kyra answers depending on provider/settings. Use offline/local mode when you do not want hardware summaries or report context sent to an online AI provider.
- **API-first mode** may try configured providers before Local Kyra for normal chat, but missing keys, failures, timeouts, and privacy gates fall back to local/offline answers.
- Placeholder provider values are ignored, so example keys or example URLs do not accidentally enable online traffic.
- Consensus/multi-provider comparison is disabled by default because it can spend more provider quota; enable it only intentionally.
- Kyra keeps recent chat context locally so troubleshooting stays coherent. Memory is redacted and must not include API keys, tokens, passwords, product keys, private documents, raw logs, or unredacted support bundles.
- Live weather, news, finance, stocks, crypto, sports, or statistics answers require a configured current-data tool/provider. If no provider is configured, Kyra should say so rather than invent live facts.

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
