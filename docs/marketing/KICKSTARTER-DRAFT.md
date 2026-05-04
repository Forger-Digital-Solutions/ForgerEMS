# Kickstarter draft — ForgerEMS (Public Preview)

**Honest positioning:** v1.2.0 is a **Public Preview** Windows desktop app — not a finished shrink-wrapped retail SKU. USB, diagnostics, and AI-assisted guidance involve real hardware risk; we lead with safety and offline-first design. **Marketplace / automated resale valuation** is **planned or stubbed**, not a shipped consumer product inside the app.

## Title ideas

- ForgerEMS — Technician USB & Repair Command Station (Windows)
- ForgerEMS — Safer Ventoy USBs, System Intelligence, Kyra Offline-First Assistant

## One-line pitch

A Windows technician suite that helps you **build safer repair USBs**, **audit toolkit health**, **scan the machine you are working on**, and get **Kyra** answers grounded in **your** scans — **offline by default**.

## Problem

Repair benches and resellers juggle Ventoy sticks, scattered tools, opaque Windows health signals, and risky “is this the right USB port?” guesswork.

## Solution

ForgerEMS centralizes USB builder workflows (with strict unsafe-target blocking), System Intelligence summaries, Toolkit Manager manifest health, Diagnostics, and Kyra — with optional online models **only** when operators configure them.

## Who it is for

Technicians, rebuilders, laptop flippers, and advanced home users who already respect backups and drive selection.

## Feature list (honest)

- **USB Builder (Beta)** — removable targets; blocks OS/system partitions.
- **System Intelligence (Beta)** — local scan cards; some values are “not exposed by Windows.”
- **Toolkit Manager (Beta)** — managed vs manual/info items.
- **Kyra (Preview)** — offline/local first; online optional.
- **USB Intelligence / port mapping (Pro Preview)** — benchmark-driven hints; topology is best-effort.
- **Update checker** — GitHub Releases; ZIP-first guidance.

## Free Public Preview vs Pro Preview

**Public Preview** today is **free** for testing and campaign feedback. **Pro Preview** labels mark advanced surfaces (USB Intelligence depth, future marketplace stubs) — **not** a paid wall yet.

## Beta tester promise

BetaTesterPro-style access can be flagged locally (`FORGEREMS_LICENSE_TIER=BetaTesterPro` or in-app beta entitlement JSON) — no fake activation server.

## Safety / privacy

- USB Builder does not target internal OS drives by design.
- Telemetry defaults **off**.
- Support bundles are **redacted**; send to **ForgerDigitalSolutions@outlook.com** only if comfortable.

## Roadmap (high level)

- Hardening + signing path exploration
- Deeper USB topology where Windows allows
- Optional marketplace / valuation providers (explicitly **planned** where stubbed)

## Risks / challenges

- Unsigned builds face SmartScreen friction — ZIP-first flow documented.
- Windows hides some sensors — UI must say so honestly.

## Limitations (current)

- No payment integration
- Some providers are **stubs** or operator-only
- WSL embedded runner remains **experimental** when enabled

## Call to action

Try the Public Preview build from GitHub Releases (ZIP-first), run System Intelligence, plug a **removable** USB, and tell us what broke — with a redacted support bundle if possible.
