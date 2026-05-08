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
- Deep Sensor Mode is read-only. It does not control fans, voltage, clocks, overclocking, undervolting, BIOS, firmware, or device settings.

Reports may include hardware model, CPU/GPU/RAM/storage info, battery info, network adapter details, USB device details, Windows version, provider status, and diagnostic notes.

Default support reports should be redacted where supported, but you should still review reports before sharing. Do not send product keys, serial numbers, service tags, API keys, tokens, passwords, private documents, or sensitive personal files to support.

---

## Kyra (offline by default)

- **Offline / local Kyra** uses built-in rules and optional **local reports** you already generated. **Beta testers are not asked to supply API keys in the app** for this path.  
- **Optional online** providers can use ForgerEMS Gateway, BYOK provider cards, environment variables, LM Studio, or Ollama. BYOK is optional and not part of the default beta tester checklist.
- **Session BYOK keys** are kept in memory only until the app closes. **Saved BYOK keys** use Windows protected local storage when available and are never written as plaintext appsettings JSON. Environment variables remain supported for advanced operators.
- **ForgerEMS Gateway** beta access sends prompts to a ForgerEMS-managed HTTPS gateway using a revocable beta token. Provider API keys stay server-side and are not shipped in the desktop app. Usage limits may apply during beta.
- Gateway logs and responses must stay sanitized. Never include beta token values, provider keys, product keys, private documents, or raw support-bundle content in requests, logs, or troubleshooting output.
- When online paths are enabled and you allow context sharing, **sanitized** text may be sent per **Kyra AI Settings** — not a dump of your entire disk.
- **System Intelligence context** may be included in Kyra answers depending on provider/settings. Use offline/local mode when you do not want hardware summaries or report context sent to an online AI provider.
- **API-first mode** may try configured providers before Local Kyra for normal chat, but missing keys, failures, timeouts, and privacy gates fall back to local/offline answers.
- Placeholder provider values are ignored, so example keys or example URLs do not accidentally enable online traffic.
- Consensus/multi-provider comparison is disabled by default because it can spend more provider quota; enable it only intentionally.
- Kyra keeps recent chat context locally so troubleshooting stays coherent. Memory is redacted and must not include API keys, tokens, passwords, product keys, private documents, raw logs, or unredacted support bundles.
- Live weather, news, finance, stocks, crypto, sports, or statistics answers require a configured current-data tool/provider. If no provider is configured, Kyra should say so rather than invent live facts.
- For hardware part research, local System Intelligence provides device facts only. External compatibility, seller availability, exact SKUs, and prices require configured live research, and Kyra should disclose when that path is off or unavailable.
- Normal Kyra chat hides provider/debug routing detail; detailed routing remains in local logs, Diagnostics, and support bundles for troubleshooting.

### Realtime Kyra Gateway (research)

When enabled, **current-data questions** may be answered via `POST /v1/kyra/research` on the ForgerEMS Worker. The app sends a **sanitized prompt** and optional **broad System Intelligence summary** (only when operator env + user toggles allow). **Provider API keys are not included in the desktop app**; they live as Worker secrets. Disable with `FORGEREMS_KYRA_GATEWAY_ENABLED=false`, clear URL/token, or turn off live research in Kyra AI Settings.

**Hardware part research** (`hardware_part_lookup` intent): when used, the Worker may receive **vendor + model family + part category + coarse local bands** (for example storage bus band, battery wear band) to ground compatibility and pricing answers. It must **not** include service tags, serial numbers, full file paths, raw logs, emails, IPs, user identifiers, API keys, tokens, product keys, or private documents. Do not send any of those items in support email either.

See our data commitment in the [Kyra Intelligence Network](#kyra-intelligence-network) section below.

Contract reference: [gateway/GATEWAY_RESEARCH_CONTRACT.md](../gateway/GATEWAY_RESEARCH_CONTRACT.md).

---

## Kyra Intelligence Network

Kyra Intelligence Network means **Local-first repair memory + optional anonymous community learning**.

**Default = Local Only.** Local Kyra Memory can store sanitized, machine-scoped repair notes on this PC so Kyra can learn from prior issues/fixes on the same machine. You can turn Local Kyra Memory off in Settings. If it is off, Kyra should not write new local repair memory; existing memory remains until you delete it.

What Kyra may store locally:

- machine class
- hardware category summary
- health score band
- issue category
- warning category
- user-confirmed fix
- USB benchmark summary
- USB target safety result
- best-use recommendation category
- resale prep note category
- scan timestamp
- confidence level
- anonymized model family if safe
- local machine profile ID generated by ForgerEMS, not a hardware serial

What ForgerEMS never collects, stores for Kyra Intelligence sharing, displays in sharing previews, exports for sharing, or uploads:

- API keys
- tokens
- passwords
- product keys
- serial numbers
- private documents
- private file contents
- full local file paths
- email addresses
- IP addresses
- user names
- exact location
- raw logs containing secrets
- raw provider responses containing secrets

Optional Anonymous Community Learning is off by default and requires explicit opt-in. Community sharing is not active in this release — the setting is visible for preview only. Declining does not block app usage, and opting out takes effect immediately.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

To opt out or manage memory: open **Settings → Kyra Intelligence**, turn off **Local Kyra Memory** and/or **Community Intelligence Sharing**, then use **Export Memory** or **Delete Memory** if needed. **Gateway Research** is separate and controls realtime public-info lookup when configured.

---

## Research Mode and live data limitations

Kyra Research Mode is for current or time-sensitive questions: crypto prices, stocks, weather, news, sports, current versions, drivers, Ventoy/tool releases, resale comps, Windows issues, security advisories, CVEs, and general current research.

Kyra must use a configured live tool/provider first. If a provider is unavailable, not configured, rate-limited, or unsupported, Kyra should say that plainly. It should not use stale knowledge-cutoff wording for prices, news, versions, or advisories, and it should not fabricate live data.

---

## Logs and sharing

Full local logs may contain sensitive context. Before you share:

1. Open **View Full Logs** (or your log folder) and **read** what you are about to send.  
2. Prefer **sanitized** excerpts or in-app “safe summary” features when available.  
3. **Never** paste API keys, tokens, or private documents into email or chat.

---

## Third-party AI or network endpoints

If an **online** provider is enabled by an operator, prompts and optional context are handled under **that provider’s** terms and your network path. **Offline/local modes** remain available where implemented.

**Toolkit Manager → Verify Links:** When you opt in, the desktop app issues short **HEAD** or minimal **ranged GET** requests to official URLs from your toolkit manifest/catalog so it can record HTTP metadata (status, redirects, length hints). **Those checks do not download complete payloads and do not execute downloaded third-party files.** Kyra and logs avoid embedding raw query strings or secret-bearing URLs.

---

## Third-party tools

Tools you install separately are governed by their own policies.

---

## Beta

Privacy-related behavior may change between beta builds; check in-app **Kyra AI Settings** and **Settings → App updates** for the current behavior on your build.
