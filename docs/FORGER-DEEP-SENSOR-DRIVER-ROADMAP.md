# Forger Deep Sensor Driver Roadmap

Forger Deep Sensor Driver is a future ForgerEMS-owned signed read-only driver roadmap. It is not included in this build.

## Purpose

The goal is HWiNFO-class hardware depth through ForgerEMS-owned local components, without a user-installed HWiNFO, AIDA64, CPU-Z, or other third-party sensor tool requirement.

## Hard Boundaries

- Read-only only.
- No fan control.
- No voltage control.
- No clock control.
- No overclocking or undervolting.
- No BIOS or firmware flashing.
- No kernel experimentation in release builds.
- No EC, SMBus, or PCI config writes unless a later audited design proves a specific read path safe.
- Explicit install consent required.
- Driver signing and release gate required.
- Fail closed on unsupported hardware.

## Threat Model

Primary risks:

- kernel crash or boot instability
- unsafe chipset, EC, SMBus, or PCI access
- vendor firmware quirks
- malicious replacement of driver binaries
- stale or revoked signed driver versions
- false confidence from unsupported hardware

Required mitigations:

- read-only IOCTL surface
- strict caller validation
- no network capability
- signed binary verification before install/start
- supported hardware allowlist
- denied-by-default probing for unknown controllers
- crash telemetry stored locally for user review
- beta flag until proven stable

## Crash Containment

The driver must fail closed. Unsupported hardware should return a structured unsupported status, not attempt risky probing.

Release candidates require:

- Driver Verifier pass
- suspend/resume testing
- hibernate/fast-startup testing
- hot-plug and dock testing
- install/uninstall/reinstall testing
- rollback testing

## Supported Hardware Allowlist

The first driver beta should support a narrow hardware allowlist. Each allowlist entry must include:

- chipset/controller identifiers
- laptop/desktop/OEM class
- allowed read operations
- known unsafe registers or buses
- test hardware coverage
- rollback notes

Unknown hardware is denied by default.

## Telemetry Source Labeling

Every reading must include:

- value and unit
- source component
- source path
- confidence
- last updated time
- unavailable reason when missing

ForgerEMS must never synthesize fake temperatures, fan speeds, voltages, power, or charging wattage.

## Signing And Revocation Plan

Before any public driver build:

- complete legal/licensing review
- use production signing only after beta gate approval
- publish driver version and hash in release metadata
- maintain revocation list for blocked versions
- block version mismatch between app, service, and driver
- document rollback path

## Update And Rollback

Updates must be explicit and reversible:

- install only after consent
- stop service before driver replacement
- keep previous known-good package
- verify signature and hash before start
- restore previous version on health-check failure
- expose Installed, Running, Needs repair, Disabled, and Version mismatch states

## Test Matrix

Minimum matrix:

- Windows 10 and Windows 11 supported builds
- admin and standard user app launches
- Intel, AMD, NVIDIA, and integrated-only machines
- laptop, desktop, mini PC, and workstation classes
- AC, battery, docked, and undocked states
- suspend/resume/hibernate
- clean install, upgrade, rollback, uninstall
- unsupported hardware fail-closed cases

## Release Gates

The driver remains beta-only until:

- threat model is reviewed
- allowlist is reviewed
- signing/revocation is implemented
- rollback is implemented
- Driver Verifier and crash containment pass
- legal/licensing review passes
- no hardware-control writes exist
