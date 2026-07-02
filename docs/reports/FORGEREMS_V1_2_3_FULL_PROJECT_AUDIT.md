# ForgerEMS v1.2.3-preview.1 Full Project Audit

Date: 2026-07-02
Branch: main
Baseline commit: `61cd219d296b971e819f8513433b30704451e2f3`

## Checked

- Public docs, README, FAQ/About/legal/privacy/third-party notice paths, and release notes.
- WPF startup, first-run UX, About/legal/help UI, Kyra memory exports, and support bundle export.
- Installer script, release build script, validation script, package docs inclusion, and portable ZIP behavior.
- USB Builder profile catalog, item picker data, profile selectors, generated USB docs, setup/update backend category filters.
- Existing v1.2.3 regression areas: USB Builder picker, persistent Live Logs panel, removed Internet widget, retired Network Pulse settings, non-automatic benchmarks, Port / USB result cards, and Port Mapping Wizard benchmark display.
- TODO/FIXME, local paths, secret-looking values, outdated wording, and preview overclaims by repository search and targeted file review.

## Fixed

- Added project-provided Terms of Use, Privacy/Data Handling, Legal Notices, User Consent Flow, release notes, and audit report.
- Added first-run in-app Terms gate with local terms version, timestamp, app version/build, and terms hash storage.
- Deferred normal WPF startup initialization and automatic update checks until current Terms acceptance exists.
- Added separate confirmation before Kyra memory export, Kyra Intelligence memory export, and support bundle creation.
- Added later-access Legal/Privacy/Terms/About/Third-party notices UI buttons.
- Added ForgerEMS Portable App to the USB Builder default profile with `_apps\ForgerEMS`, `_docs\ForgerEMS`, and `_logs\ForgerEMS` paths.
- Updated backend setup/update category classification so portable app selections flow into actual USB runs.
- Updated project publish content to include legal/help docs in packaged output.

## Manual Validation Still Needed

- Interactive installer wizard click-through, including the license page, if the environment cannot show UI.
- Manual 1366x768 visual review on a real display.
- End-to-end USB build on a physical removable USB target.
- Manual GitHub release page edit/publish after owner review and push.

## Intentionally Left Alone

- Network Pulse was retired from the shell and Settings; its dormant implementation was removed, and stale persisted settings/reports are ignored instead of resurfaced.
- Persistent Live Logs panel remains present.
- No global shell Internet widget or dedicated Live Logs tab was re-added.
- No cloud sync, telemetry, automatic support upload, or automatic benchmarks were added.
- Existing System Intelligence/Hardware X-Ray code remains where it supports current local snapshot features, but public wording was narrowed to preview/local-device boundaries.

## Remaining Blockers

None known at audit-write time. Final build/package/test validation results are recorded in the handoff report after commands complete.
