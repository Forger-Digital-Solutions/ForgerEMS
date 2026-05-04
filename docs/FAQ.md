# ForgerEMS — Frequently asked questions (Beta)

**ForgerEMS** = **Forger Engineering Maintenance Suite**, built by **Forger Digital Solutions**.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com)

This page is practical guidance, not legal advice. See also [LEGAL.md](LEGAL.md) and [PRIVACY.md](PRIVACY.md).

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

## What is `START_HERE.bat`?

It is the **supported entry point** after you extract the release ZIP. It walks you through checks and launches the installer **from the verified folder** you chose, instead of fighting the strictest “unknown EXE from the internet” path first.

---

## Does Kyra need an API key?

**No — not for normal beta use.** Kyra works **offline** with built-in rules and optional **local** reports you already generated (for example System Intelligence). The app **does not prompt beta testers to paste API keys** for basic help.

Optional **online** models only appear when an **operator** has already configured the machine or deployment (advanced). Operators: [KYRA_PROVIDER_ENVIRONMENT_SETUP.md](KYRA_PROVIDER_ENVIRONMENT_SETUP.md).

---

## Why does Kyra say it does not have live data?

Kyra is **not a live web browser**. Offline answers use **rules and what is already on your PC** (your scans, toolkit state, logs you choose to summarize). Some **integrated tools** may add fresh data when that feature exists; otherwise Kyra may **suggest** you open an external source — it does not silently browse the web for you.

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

---

## Is this safe?

No beta program can promise “100% safe,” but ForgerEMS is designed for **technician workflows**: confirmations for risky steps, **ZIP-first** distribution with checksums, and **local-first** scans. **You** remain responsible for what you download and run, including third-party ISOs and tools. See [LEGAL.md](LEGAL.md).

---

## How do I verify `CHECKSUMS.sha256`?

Open PowerShell in the folder that contains the ZIP and `CHECKSUMS.sha256`. For example:

```powershell
Get-FileHash .\ForgerEMS-v1.1.12-rc.3.zip -Algorithm SHA256
```

Compare the `Hash` line to the line in `CHECKSUMS.sha256` for that filename.

---

## Why does USB speed say “Not measured yet”?

Read/write labels come from a **USB benchmark** on a **safe removable** target you selected. Until a benchmark completes for that selection, the UI shows that speed has not been measured.

---

## How do I use USB mapping?

In **USB Intelligence**: **Start USB Mapping** → **Capture Current Port** → move the device to another physical port → **Detect Port Change** → enter a short label → **Save Port Label**. Labels are stored in your **local** machine profile.

---

## Does ForgerEMS upload my system info?

There is **no automatic upload** to Forger Digital Solutions when you run local scans. Reports and logs stay under **`%LOCALAPPDATA%\ForgerEMS\`**. If an **online** Kyra path is enabled in your environment, only **sanitized** context is sent, per settings — not raw secrets.

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

## What is ForgerEMS in one sentence?

A Windows technician suite for **USB toolkit building**, **USB Intelligence**, **System Intelligence**, **Diagnostics**, **Toolkit Manager**, and **Kyra**.
