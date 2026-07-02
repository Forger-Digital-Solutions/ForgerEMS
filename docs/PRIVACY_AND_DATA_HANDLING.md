# ForgerEMS Privacy and Data Handling

Applies to: ForgerEMS `v1.2.3-preview.1` public preview

ForgerEMS is local-first. It does not upload logs, support bundles, reports, Kyra memory, sensor data, USB inventories, or local device snapshots automatically.

## Local Data

ForgerEMS stores runtime data under `%LOCALAPPDATA%\ForgerEMS\Runtime`, including logs, reports, profiles, cache files, settings, USB Builder profile choices, and the local Terms acceptance record at `config\terms-consent.json`.

## Local Device Context

Local snapshots may include Windows version, device model, CPU/GPU/RAM/storage details, battery or sensor availability, network adapter summaries, USB target details, toolkit status, benchmark results, and diagnostic notes. Missing readings are coverage limits, not failures.

## Kyra and Providers

Kyra is local-first. Offline/local behavior does not need API keys. If an online provider or gateway is configured and enabled, prompts and optional sanitized context may be sent according to those settings. Provider keys should not be pasted into chat or support email.

## Exports and Support Bundles

Support bundles, Kyra memory exports, local context exports, and report exports may include local device/context information. ForgerEMS shows a separate confirmation before these actions. Review exported files before sending them.

## USB Builder and Downloads

Some USB Builder features rely on internet access, vendor sites, managed downloads, manual folders, user-supplied files, permissions, or third-party licenses. Downloaded content is governed by the source/vendor terms.

## No Automatic Uploads

ForgerEMS does not add cloud sync, telemetry, automatic log upload, or automatic support-bundle upload in this pass. Telemetry/crash reporting feature flags default off unless explicitly enabled through the project environment configuration.

## Support

Report issues with app version, steps, screenshots if useful, and redacted logs only after review. Do not send passwords, API keys, product keys, recovery keys, private documents, private customer data, or sensitive personal files.
