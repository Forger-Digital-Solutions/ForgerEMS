# Public FAQ — ForgerEMS v1.2.0 Public Preview

## What is ForgerEMS?

A Windows desktop **technician suite** for safer USB toolkit work, local system intelligence, toolkit manifest health, diagnostics helpers, and Kyra — an in-app assistant that prefers **offline** and **local-scan** facts.

## Is it free?

The **Public Preview** build line is **free** to try. Future **Pro** packaging may exist; this preview does not include payment.

## What is Pro Preview?

A **label** for advanced or best-effort surfaces (e.g. deeper USB intelligence). During preview, many capabilities stay unlocked for feedback — see Settings → feature maturity card.

## Is Kyra online?

**Optional.** Default posture is offline/local. Online models require operator configuration and appropriate keys in environment variables — never share keys in email or screenshots.

## Does it work offline?

Yes for core flows that use local scripts and disk-backed reports. Update checks and online models need network.

## Does it collect data?

**Telemetry defaults to off** unless `FORGEREMS_TELEMETRY_ENABLED` is set. Crash reporting defaults off unless explicitly enabled.

## Is there an internet speed test?

If present in a build, treat it as **experimental** / best-effort — not a lab-grade benchmark suite.

## Is it safe with my C: drive?

USB Builder is designed to **block** Windows/system/internal OS drives for destructive flows. You are still responsible for picking the correct removable volume.

## Why are some tools manual links?

Licensing, redistribution limits, or vendor hosting — **manual/info items are not failed downloads.**

## Why does Windows hide some USB or sensor data?

OEM drivers, ACPI gaps, and privacy choices limit WMI / API fidelity — we surface “not exposed by Windows” instead of guessing.

## How do beta testers get Pro later?

Commercial terms are TBD. For now `FORGEREMS_LICENSE_TIER=BetaTesterPro` or the in-app beta entitlement flag marks **beta** pro-style access locally — no cloud license server.

## Something broke — what should I send?

Version, steps, screenshots, and **Diagnostics → Export Support Bundle** ZIP to **ForgerDigitalSolutions@outlook.com** — not private documents.
