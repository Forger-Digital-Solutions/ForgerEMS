# ForgerEMS — Frequently asked questions (Public Preview)

**ForgerEMS** = **Forger Engineering Maintenance Suite**, built by **Forger Digital Solutions**.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com)

This page is practical guidance, not legal advice. See also [LEGAL.md](LEGAL.md) and [PRIVACY.md](PRIVACY.md). Environment variables: [ENVIRONMENT.md](ENVIRONMENT.md). Campaign FAQ: [marketing/PUBLIC-FAQ.md](marketing/PUBLIC-FAQ.md).

ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows.

---

## What should I download first?

**The ZIP — not the standalone EXE.** On [GitHub Releases](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases), under **Assets**, choose `ForgerEMS-v<version>.zip` or `ForgerEMS-Beta-v<version>.zip`, extract, then run **`START_HERE.bat`**.

Step-by-step: [FIRST_TESTER_DOWNLOAD_FLOW.md](FIRST_TESTER_DOWNLOAD_FLOW.md) · Browser issues: [DOWNLOAD_TROUBLESHOOTING.md](DOWNLOAD_TROUBLESHOOTING.md).

---

## Why is Windows warning me?

**SmartScreen** and similar protections flag **new or less-known** programs. ForgerEMS beta builds are expected to show **more friction** on a raw **installer EXE** than on a **ZIP** you extract yourself.

ForgerEMS does **not** ask you to disable Windows security. Prefer **ZIP → `START_HERE.bat`**, and verify **`CHECKSUMS.sha256`** when you can. Details: [DOWNLOAD_TROUBLESHOOTING.md](DOWNLOAD_TROUBLESHOOTING.md).

---

## Why should I download the ZIP instead of the EXE?

You get **one bundle** with `START_HERE.bat`, verification hints, checksums, and the installer — a clearer and **safer first path** than double-clicking an unfamiliar **`.exe`** straight from the browser. Chrome and Edge also behave differently on `.exe` vs `.zip`.

---

## Why is the command-center background static?

ForgerEMS uses a packaged static command-center image so the preview app stays responsive while logs, toolkit checks, and Kyra run.

---

## What is `START_HERE.bat`?

It is the **supported entry point** after you extract the release ZIP. It walks you through checks and launches the installer **from the verified folder** you chose, instead of fighting the strictest “unknown EXE from the internet” path first.

---

## Does Kyra need an API key?

**No — not for normal beta use.** Kyra works **offline** with built-in rules and optional **local** reports you already generated (for example System Intelligence).

Optional online paths are configured in **Kyra AI Settings**. Beta builds may use **ForgerEMS Gateway** so testers get limited Kyra API time without receiving owner provider API keys. Advanced users can also use **Bring Your Own Key** provider cards for OpenAI-compatible, Claude, Gemini, OpenRouter, Groq, Mistral, Cerebras, GitHub Models, Cloudflare, LM Studio, or Ollama. Operators: [KYRA_PROVIDER_ENVIRONMENT_SETUP.md](KYRA_PROVIDER_ENVIRONMENT_SETUP.md).

When API-first mode is enabled, Kyra tries configured providers in priority order. Credential precedence is session key, protected saved key, environment variable, then Gateway/local/offline fallback. Missing keys, placeholders, disabled providers, failed calls, rate limits, and timeouts are skipped or handled safely.

Placeholder values such as `REPLACE_ME`, `YOUR_*`, `local-model-name`, or `example.local` are ignored and do not count as configured providers. For installed-app testing, use Windows **User** environment variables and check them with `tools/show-forgerems-env-status.ps1`.

### Kyra Beta Gateway quick answers

- The **beta gateway** is aimed at **live research / tool** calls (crypto, weather, news, etc.), not every casual “hello.” Everyday chat uses your normal free/BYOK LLM providers when configured. If the gateway fails for chat-like prompts, that can be expected — check **Kyra AI Settings** provider order and gateway status.
- Beta users only need gateway vars (`FORGEREMS_KYRA_GATEWAY_URL` and `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN`) when cloud access is enabled.
- Direct provider keys are optional BYOK and are not required for default beta.
- Session BYOK keys are never written to disk. Saved BYOK keys use Windows protected local storage when available and are never written as plaintext appsettings JSON.
- Gateway beta tokens are revocable and can be rotated quickly.
- If gateway limit is reached, Kyra can return a friendly beta-limit message and still fall back to local/offline.

---

## Why does Kyra say it does not have live data?

Kyra is **not a live web browser**. Offline answers use **rules and what is already on your PC** (your scans, toolkit state, logs you choose to summarize). Some **integrated tools** may add fresh data when that feature exists; otherwise Kyra may **suggest** you open an external source — it does not silently browse the web for you.

Weather and crypto can use built-in no-key paths when enabled. News requires NewsAPI/GNews configuration. Stocks support `finnhub`, `alphavantage`, or `fmp` when configured. Economic/statistics data is limited/shell status unless a supported provider is wired. Kyra should not make up live prices, weather, or breaking news.

Kyra Research Mode should route current/live prompts first to the **ForgerEMS realtime gateway** (`/v1/kyra/research`) when that path is enabled and configured, then to **Kyra AI Settings live tools** where applicable: crypto, stocks/finance, weather, news, sports, latest/current software versions, drivers, Ventoy/tool versions, resale/current market pricing, current Windows issues, security advisories, CVEs, and general current research. If the live tool is unavailable or rate-limited, Kyra should say so honestly and avoid stale “knowledge cutoff” pricing or fabricated versions.

Normal Kyra replies hide provider routing dumps. Beta troubleshooting detail remains available in logs, Diagnostics, support bundles, and explicit technical-detail flows.

---

## Can Kyra answer hardware, parts, and upgrade questions?

**Yes — local scan first.** After **System Intelligence**, Kyra can explain storage type (e.g. NVMe vs SATA when the scan shows it), RAM summary when exposed, battery wear bands, and realistic upgrade advice (for example, laptop GPU/CPU usually not upgradable).

**Exact part numbers and “cheapest” prices** need **live research** (gateway or other configured tools) with real sources; otherwise Kyra should say what is known locally, call out **likely compatible candidates**, and tell you to **confirm against the service manual, battery label, or official compatibility** before buying. For Dell battery questions, the source priority is Dell support/service manual/parts pages first, Dell-compatible part numbers from trustworthy references second, and reputable sellers only as availability references. Marketplace titles alone are not proof of fit.

**Privacy:** realtime part lookup uses **sanitized** model family and coarse bands — not service tags, serials, full paths, or secrets by default. See [PRIVACY.md](PRIVACY.md) and [KYRA_BEHAVIOR_SPEC.md](../KYRA_BEHAVIOR_SPEC.md).

---

## How much does Kyra remember?

Kyra keeps recent chat context locally so troubleshooting stays coherent, with a 100-turn default and a 1-200 turn clamp. Persistent memory is optional and redacted. Kyra memory should never store API keys, tokens, passwords, product keys, raw logs, or private documents.

Kyra Intelligence Network adds **Local Kyra Memory** for sanitized machine-scoped repair notes. It may store categories and summaries such as machine class, health score band, issue/warning category, user-confirmed fix, USB target safety result, best-use category, resale prep category, confidence, scan timestamp, and a ForgerEMS-generated local machine profile ID. It should not store giant prompt transcripts or raw logs.

Use **Settings → Kyra Intelligence** to turn Local repair memory off, export Kyra memory, delete Kyra memory, or view the sanitized preview for optional community learning.

---

## What is Kyra Intelligence Network?

Kyra Intelligence Network is **Local-first repair memory + optional anonymous community learning**.

- Default is **Local Only**.
- Anonymous community intelligence sharing is **off by default**.
- Declining does not block app usage.
- Community sharing is not active in this release. The setting is visible for preview only.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

---

## Does Kyra know this device automatically?

Yes, after you run System Intelligence. Kyra loads the latest local System Intelligence / Hardware X-Ray report and can answer questions like what device this is, what it is best for, what sensors are missing, whether it can run games, and how to position it for resale. If the scan is missing or stale, Kyra should say that clearly.

---

## How do updates work?

The app checks **official GitHub Releases** for this repo (not every git commit). New versions appear when maintainers publish a **tagged GitHub Release**. The app picks the latest eligible release by **publish date**, then inspects assets (**ZIP** recommended, **EXE** advanced in the UI).

Read: [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md).

---

## Why is not my USB detected?

Common causes: the volume is **too small** or is a **special boot slice** (pick the large **data** partition), the device is **locked** by another app, or Windows has not finished mounting it. In **USB Intelligence** / **USB Builder**, pick a **large removable data** partition, not tiny EFI-style slices unless the flow explicitly asks for them.

If a benchmark was never run for the current selection, speed may show as **not measured yet** until you complete a run.

---

## What does “Manual Required” mean in Toolkit Manager?

Licensing, vendor rules, or verification limits mean ForgerEMS **cannot legally or safely auto-download** that item. Use the **link or instructions** in the app, place files where the manifest expects, then run **Refresh Health**.
If a managed file is present but checksum source resolution is currently unavailable, ForgerEMS reports it as present with pending verification rather than missing.
If a related managed download is already installed and checksum-verified, a missing info shortcut can show as **covered by managed download** / **shortcut suppressed** with **no action needed**. That is not a Manual Required blocker.

---

## What is Driver Hub?

**Driver Hub** is a curated official-link app-store-style catalog for GPU utilities, OEM support portals, chipset/network/audio driver pages, BIOS/firmware support links, and Linux driver guidance.

It is **not** a sketchy driver-updater clone. Each card shows **one clear primary action** — `Get`, `Open Driver Page`, `Open Support Page`, `Open Official Download`, or `Open Firmware Guidance` — that opens the official vendor/project page. Helper actions (**Copy Link**, **Add Shortcut to USB**, and **Open Page** when it differs from the primary URL) are tucked behind a small `⋯` overflow button on each card so the catalog stays scannable like the App Store / Microsoft Store. ForgerEMS does **not** claim a driver is outdated or current unless a real version comparison exists, does **not** auto-run installers, does **not** auto-download model-specific OEM packages, does **not** upload service tags or serial numbers, and does **not** automate BIOS/firmware flashing.

Recommendations are hints only, based on detected manufacturer/GPU/CPU/platform data from System Intelligence when available. Brand tiles are text monograms, not bundled vendor logo assets. Firmware cards remind you to confirm the exact model, power, battery/AC state, and vendor instructions before updates.

Releases run an optional link-health pass via [`tools/Test-DriverHubLinks.ps1`](../tools/Test-DriverHubLinks.ps1), which probes each catalog URL with a reasonable timeout and a real-browser user agent. Some vendor pages reject automated checkers (HTTP 401 / 403 / 429) even when the page opens fine in a real browser — the script reports those as `ForbiddenLikelyOk` and only fails on confirmed 404s. The unit tests never depend on live vendor reachability.

---

## What is the USB Builder Profile?

The **USB Builder tab → USB Builder Profile** lets you pick which toolkit packs Setup USB and Update USB seed or refresh on the selected target. Each pack is one of:

- **Core ForgerEMS USB structure** (required, cannot be turned off): the folders, logs, manifest, and Ventoy safety structure ForgerEMS needs to operate.
- **Windows installers and recovery** (default on): official Microsoft Windows 10 / 11 / Server download shortcuts, ADK / WinPE references, and the modern Windows workflow folders.
- **Legacy Windows manual media drop folders** (default on): tracking folders for Windows 8.1 and older. ForgerEMS never downloads legacy Windows ISOs.
- **Linux rescue and installer tools** (default on): managed Linux rescue ISOs and installer/recovery workflows.
- **Diagnostics and rescue utilities** (default on): disk, imaging, hardware, network, security, and portable technician utilities.
- **OEM recovery links and vendor tools** (default on): official vendor support, driver, and recovery utility shortcuts.
- **macOS installer workflow** (default off, manual media required).
- **Android platform tools and firmware workflow** (default off, manual media required).
- **iOS / iPadOS restore workflow** (default off, manual media required).

Buttons: **Select recommended** restores the default set, **Select all** turns every pack on, **Reset to defaults** matches a fresh install. Your choice is saved per user at `%LOCALAPPDATA%\ForgerEMS\Runtime\config\usb-builder-profile.json`.

Important behavior:

- **Unchecking a pack only skips seeding/updating it on this run.**
- **It does not delete files already on the USB.** Existing user-supplied media, drop folders, and prior toolkit content are left alone.
- Core safety structure always runs even if everything else is off.

---

## Does ForgerEMS auto-download macOS, iOS / iPadOS, or Android media?

**No.** ForgerEMS is Windows-first and does not redistribute or auto-download:

- macOS installers, DMGs, or PKGs.
- iOS / iPadOS IPSW files.
- Android OEM firmware (Samsung, Motorola, OnePlus, etc.).

What the mobile/macOS packs do provide:

- **macOS**: shortcuts to Apple's official download, recovery, and `createinstallmedia` guides. A compatible Mac may be required. Drop user-supplied installer media into `ISO\macOS\macOS-Manual-Installer-Drop\<version>\` for tracking. ForgerEMS does not redistribute Apple installers.
- **Android**: official download shortcuts for Android SDK Platform-Tools (adb / fastboot), Google Pixel factory / OTA images, and AOSP source/build documentation. Samsung, Motorola, and OnePlus open the official vendor support sites. Drop user-supplied firmware into `ISO\Android\Android-Manual-Firmware-Drop\<vendor>\`. Flashing the wrong firmware can wipe data or brick devices. ForgerEMS does not redistribute Android firmware.
- **iOS / iPadOS**: shortcuts to Apple's official Apple Devices for Windows, Finder / iTunes, recovery mode, and Apple Configurator restore guides. IPSW files are user-supplied; drop them into `ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop\<device>\`. Restores can erase devices. Activation Lock and Apple ID ownership are outside ForgerEMS. ForgerEMS does not use third-party IPSW indexes.

ForgerEMS never bypasses licenses, activation, DRM, account locks, or vendor authorization flows.

---

## What do the toolkit action labels mean?

Every catalog item uses one of these labels in its `.url` filename or display:

- **DOWNLOAD** / **AUTO DOWNLOAD**: ForgerEMS can safely download or update this item from an official, redistributable, machine-resolvable source. Checksum verified when the upstream publishes one.
- **MANUAL DOWNLOAD**: ForgerEMS opens an official vendor page; you must choose the variant, sign in, accept license terms, or pick the right device/model. ForgerEMS does not bypass that flow.
- **MANUAL MEDIA REQUIRED** / **MANUAL ISO REQUIRED** / **MANUAL INSTALLER REQUIRED** / **MANUAL IPSW REQUIRED** / **MANUAL FIRMWARE REQUIRED**: you must supply the ISO, installer, IPSW, or firmware yourself. Used for legacy Windows, macOS installers, iOS / iPadOS IPSW, and OEM Android firmware. Drop your legally obtained file in the matching folder; ForgerEMS does not redistribute these.
- **GUIDE**: official how-to instructions (Apple `createinstallmedia`, recovery mode, AOSP build guide, etc.).
- **INFO**: true reference material — release notes, lifecycle pages, ADK references. Not a substitute for a missing download.

---

## What does “Verify Links” do in Toolkit Manager?

**Verify Links** asks ForgerEMS to contact official toolkit URLs using **safe HTTP metadata checks** — typically **HEAD**, with a **small ranged GET** fallback when some hosts block HEAD. The verifier records reachability, HTTP status (when available), redirect hosts, content-length hints when exposed, and compares those signals to your manifest/checksum columns **without fetching entire installers or ISOs** and **without executing anything it downloads**. Runs are **timeout-bounded** and **cancellable**, and work gracefully **offline** (results fall back to Unknown / Offline rather than pretending links were validated). Kyra can summarize the latest saved verification when it aligns with your toolkit-health report and USB scope.

---

## What is the Toolkit Readiness Score?

A 0–100 score for your current toolkit state on the selected USB target. It starts at 100 and is reduced by: missing required items (−12 each, max −50), checksum verification failures (−15 each, max −40), managed updates available (−6 each), pending verification items (−3 each), link verification failures from **Verify Links** (−6 each, max −18), USB target warnings, and Ventoy issues. Labels: **Ready** (≥85), **Mostly Ready** (70–84), **Needs Attention** (45–69), **Not Ready** (<45 or hard blockers present). Run **Refresh Health** to recalculate. The score also shows your top strengths, top blockers, and a next recommended action.

---

## What is a machine profile?

A local file ForgerEMS saves under `%LOCALAPPDATA%\ForgerEMS\Runtime\profiles\` to remember this machine's health score, toolkit readiness, USB benchmark results, best-use category, and resale estimates between sessions. The profile uses a ForgerEMS-generated ID — not your hardware serial number. It is never uploaded automatically. You can view, export, or delete it from **Settings → Kyra Intelligence → Export Memory / Delete Memory**.

---

## Is this safe?

No beta program can promise “100% safe,” but ForgerEMS is designed for **technician workflows**: confirmations for risky steps, **ZIP-first** distribution with checksums, and **local-first** scans. **You** remain responsible for what you download and run, including third-party ISOs and tools. See [LEGAL.md](LEGAL.md).

---

## How do I verify `CHECKSUMS.sha256`?

Open PowerShell in the folder that contains the ZIP and `CHECKSUMS.sha256`. For example:

```powershell
Get-FileHash .\ForgerEMS-v1.2.3-preview.1.zip -Algorithm SHA256
```

Compare the `Hash` line to the line in `CHECKSUMS.sha256` for that filename.

---

## Why does USB speed say “Not measured yet”?

Read/write labels come from a **USB benchmark** on a **safe removable** target you selected. Until a benchmark completes for that selection, the UI shows that speed has not been measured.
When Windows cache effects are suspected, ForgerEMS marks read speed as unverified and prioritizes measured write speed plus topology/port evidence for builder recommendations. A later clean benchmark can replace the suspect reading.

---

## How do I use USB mapping?

In **USB Intelligence**: **Start USB Mapping** → **Capture Current Port** → move the device to another physical port → **Detect Port Change** → enter a short label → **Save Port Label**. Labels are stored in your **local** machine profile.

---

## Does ForgerEMS upload my system info?

There is **no automatic upload** to Forger Digital Solutions when you run local scans. Reports and logs stay under **`%LOCALAPPDATA%\ForgerEMS\`**. If an **online** Kyra path is enabled in your environment, only **sanitized** context is sent, per settings — not raw secrets.

---

## Does ForgerEMS require HWiNFO, AIDA64, CPU-Z, or vendor tools?

No. ForgerEMS uses its own local **Forger Sensor Stack**. Forger Sensor Core is active by default, and approved bundled providers are used only where legally allowed. There is no paid third-party tool requirement and no user-required HWiNFO, AIDA64, CPU-Z, or vendor-tool download for System Intelligence.

---

## What is the difference between Standard Scan and Elevated Scan?

**Standard Scan** runs without administrator privileges and covers most hardware, health, USB, and toolkit checks. It is always available and is the default.

**Elevated Scan** is an optional deeper scan that requests Windows administrator access to extend coverage — for example, certain security checks, low-level sensor data, and system areas that Windows restricts to admin processes. If ForgerEMS is already running as administrator, the Elevated Scan path runs directly without a UAC prompt. If ForgerEMS is not elevated, the app requests UAC by relaunching itself as administrator and continues the scan automatically after approval.

If Windows or your security policy blocks the admin handoff (UAC cancelled, endpoint security policy, execution policy, or SmartScreen), ForgerEMS now shows a friendly explanation rather than a raw error code. The Standard Scan result remains available and is not affected.

**Restart as Administrator** is optional. It improves Elevated Scan and Deep Sensor Mode coverage but is not required for normal use. **Copy Admin Command** remains a beta diagnostic fallback for environments where UAC launch cannot be automated.

---

## What is Deep Sensor Mode?

Deep Sensor Mode is an optional local read-only sensor mode inside the Forger Sensor Stack. It may improve **Hardware X-Ray** coverage for temperatures, clocks, load, fan RPM, and storage wear when supported. It enables bundled reviewed sensor providers where packaged and does not require separate user downloads; it is not permanent administrator permission and it is not an external tool bridge.

Forger Sensor Service is a future optional local service and is not installed in this build. Forger Deep Sensor Driver is roadmap only and is not included in this build.

---

## Does ForgerEMS control my fans, voltage, clocks, BIOS, or firmware?

No. ForgerEMS only reads supported sensor data. It does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware.

---

## Why are some sensors missing?

Some machines do not expose certain sensors through Windows, firmware, drivers, permissions, or available read-only providers. Unavailable readings are coverage limits, not failures.

---

## Does ForgerEMS upload my sensor data?

No automatic upload. Reports and logs stay local unless you choose to copy, export, or share them.

---

## Can Deep Sensor Mode require administrator access?

Some sensors may require admin access, vendor drivers, firmware support, or the future Forger Deep Sensor Driver, but Deep Sensor Mode itself is not the same as admin permission. Windows may ask for UAC approval when you run Elevated Scan. ForgerEMS reports unavailable readings honestly when approval or hardware support is not available.

This is not a failure. Many laptops do not expose CPU package power, fan speed, VRM, or EC telemetry through standard Windows APIs.

---

## Is LibreHardwareMonitor included?

Yes, where packaged, ForgerEMS includes **LibreHardwareMonitorLib** as a bundled local read-only sensor provider under MPL-2.0 with license and notice files included.

---

## Can I turn Deep Sensor Mode off?

Yes. Deep Sensor Mode can be **Off** or **Read-only local sensors**. Environment variable/testing overrides may also be supported.

Related docs: [FORGER-SENSOR-STACK.md](FORGER-SENSOR-STACK.md), [SENSOR-LIMITATIONS.md](SENSOR-LIMITATIONS.md), and [FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md](FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md).

---

## What does Kyra see?

**Offline Kyra** uses built-in rules and optional **local reports** you already generated. With your permission, a **sanitized** summary may be sent to configured online providers — not raw serials, product keys, or full private paths in the safe-summary path.

---

## What is Free vs Pro preview?

During beta, **Pro** or preview capabilities may be visible for feedback; **licensing is not enforced** yet. Treat preview labels as informational.

---

## Where are logs stored?

Typical locations: **`%LOCALAPPDATA%\ForgerEMS\logs`** and **`%LOCALAPPDATA%\ForgerEMS\Runtime\logs`**. Reports often appear under **`%LOCALAPPDATA%\ForgerEMS\Runtime\reports`**. Review and **redact** before sharing.

---

## How do I report beta issues?

Email **ForgerDigitalSolutions@outlook.com** with app version, Windows version, steps, expected vs actual, and screenshots. Attach **sanitized** log excerpts only. **Do not** send API keys, tokens, passwords, product keys, serial numbers, or private files. Template: [BETA_ISSUE_REPORT_TEMPLATE.md](BETA_ISSUE_REPORT_TEMPLATE.md).

---

## What is Drive Validator?

**Drive Validator** (USB Builder tab → **Open Drive Validator** opens the Drive Validator Wizard) writes **temporary ForgerEMS test files** into **free space** on a selected removable USB target, reads them back, and checks for verification errors or suspicious capacity behavior. It does **not** format the drive and does **not** delete your existing files — only files under `.forgerems-drive-validator` are created and removed afterward.

- The **Drive Validator Wizard** walks you through **Select target → Choose mode → Safety review → Running → Results** with a live **media integrity map** of region tiles. The compact card on the USB Builder tab is the entry point and shows the last validation status/age for the selected drive.
- **Quick Safe Check** and **Sampled Capacity Check** use bounded writes spread across free space. A passing result is reported as **"No issues found in sampled validation"** — it does **not** prove the drive is genuine, healthy, or fit for any specific use. Sophisticated fake-capacity media can still evade a small sample.
- **Full Free-Space Validation** is the **strongest non-destructive mode** (it writes across the available free-space budget). It is **slow**, causes **heavy USB writes**, and requires an explicit acknowledgement checkbox in the wizard. Even a clean Full Free-Space pass is evidence of correct file-system-visible behavior, **not** a sector-level guarantee.
- **Destructive Full Media validation** is **not available in this build**. The wizard surfaces it as "not available". If it is ever enabled later, it will require typing an exact confirmation phrase and will erase the entire drive.
- Drive Validator cannot **directly inspect NAND chips** through normal Windows file I/O. Safe modes operate at the file-system level. They can detect many fake-capacity, aliasing, failing-media, short-read/write, and I/O-error patterns — but a passing result is advisory evidence, **not** a 100% authenticity certificate.
- Drive Validator **refuses to run** against the Windows OS drive, system / boot / EFI / VTOYEFI / recovery partitions, BitLocker-encrypted volumes, and internal fixed disks. The existing USB Builder hard safety blocks still apply.
- A **failed drive should not be trusted for a ForgerEMS/Ventoy toolkit**. The wizard's results step explains the recommended next step (Sampled / Full Free-Space, or consider replacing the drive).
- Drive Validator results are **advisory evidence for a technician**. They are not a warranty, certification, or replacement for vendor diagnostics. **Back up** important data before any heavy validation, and ForgerEMS is **not responsible** for failing media or any destructive action you explicitly confirm.
- Cached results are keyed by a composite identity (root path + best-effort volume serial + drive model + size + label) so a different drive that mounts on the same letter is **not** treated as already validated. Entries older than 30 days expire.

### How is Drive Validator different from Speed Benchmark?

**Speed Benchmark** measures throughput; some of its reads may be served from OS cache, so a fast number does not prove the drive can faithfully return what was written. **Drive Validator** writes unique deterministic signatures into bounded regions and verifies the *content* on read-back, looking for mismatches, aliasing, zero/0xFF fills, short reads/writes, and per-region speed collapse. A fast drive can still fail validation. A slow drive can still be valid but may not be ideal for a technician toolkit.

---

## What is ForgerEMS in one sentence?

A Windows technician suite for **USB toolkit building**, **USB Intelligence**, **System Intelligence**, **Driver Hub**, **Diagnostics**, **Toolkit Manager**, and **Kyra**.

---

## Does ForgerEMS guarantee repair, data recovery, malware removal, hardware diagnosis, or compatibility?

**No.** ForgerEMS is **technician-assist software**, not a replacement for professional judgement. Dev Beta builds do not promise guaranteed repair, guaranteed data recovery, guaranteed malware removal, guaranteed hardware diagnosis, guaranteed driver/component compatibility, guaranteed pricing or marketplace accuracy, or guaranteed legal/regulatory compliance. System Intelligence and Hardware X-Ray may report **Unknown**, **NotExposed**, or **Inferred** when firmware, drivers, permissions, or sensor providers do not expose data — these are coverage limits, not failures. Confirm critical decisions with additional testing and treat third-party tools, OS images, and vendor utilities under their own licenses and terms.

---

## Are support bundles uploaded automatically?

**No.** Support bundles are user-controlled. ForgerEMS attempts to redact local usernames, private paths, API keys, tokens, bearer values, and product keys from logs, diagnostics, and exported bundles, but you should still **review every bundle before sharing it**. The app does not automatically upload bundles, sensor data, scan reports, or USB inventories anywhere.

---

## Can I run ForgerEMS on Linux through Wine?

**Experimentally, yes.** ForgerEMS detects Wine on startup, forces WPF
into `SoftwareOnly` render mode, and shows a yellow compatibility banner
at the top of the window. Catalog browsing, profiles, the Drive Validator
wizard (read-only), and Kyra still work. USB drive write actions — Setup
USB, Update USB, Rename USB, Install/Update Ventoy, Toolkit Update, and
Full Managed Download — are disabled under Wine in this prerelease. Use
native Windows for any USB writing step. See
[docs/LINUX-WINE-COMPATIBILITY.md](LINUX-WINE-COMPATIBILITY.md) for the
full guide, tested distros, launch flags, and how to collect logs for a
bug report.
