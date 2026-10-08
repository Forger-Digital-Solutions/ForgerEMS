# ForgerEMS Privacy and Data Handling

Applies to: ForgerEMS `v1.2.4`

ForgerEMS is local-first. It does not upload logs, support bundles, reports, sensor data, USB inventories, or local device snapshots automatically.

## Local Data

ForgerEMS stores runtime data under `%LOCALAPPDATA%\ForgerEMS\Runtime`, including logs, reports, profiles, cache files, settings, USB Builder profile choices, and the local Terms acceptance record at `config\terms-consent.json`.

## Local Device Context

Local snapshots may include Windows version, device model, CPU/GPU/RAM/storage details, battery or sensor availability, network adapter summaries, USB target details, toolkit status, benchmark results, and diagnostic notes. Missing readings are coverage limits, not failures.

## Dr. Forge Intake

When configured, ForgerEMS stores the selected `drforge.exe` path, last readiness state, and last local Dr. Forge report/archive paths under the local Runtime config folder. Generated Dr. Forge reports and archives stay under `%LOCALAPPDATA%\ForgerEMS\Runtime\reports\drforge`. The in-app recent report history and local report preview read that app-managed folder only; they do not crawl Documents or arbitrary user folders.

Dr. Forge reports may include local device/context information, sensor availability, findings, notes, and unavailable telemetry reasons. The in-app preview is read-only and bounded: known JSON schemas can be grouped into report-derived sections, unknown JSON falls back to capped raw/metadata preview, Markdown is shown as capped plain text, and ZIP/archive previews are metadata-only with no extraction. Unavailable readings remain unavailable, not zero. Review reports before sharing.

Dr. Forge report/archive files are included in ForgerEMS support bundles only when the user explicitly chooses to include them and confirms the support-bundle export. Previewing or generating a report does not upload it and does not attach it to a support bundle automatically.

## Exports and Support Bundles

Support bundles, local context exports, and report exports may include local device/context information. ForgerEMS shows a separate confirmation before these actions. Review exported files before sending them.

## USB Builder and Downloads

Some USB Builder features rely on internet access, vendor sites, managed downloads, manual folders, user-supplied files, permissions, or third-party licenses. Downloaded content is governed by the source/vendor terms.

## User-Triggered Requests to External Services

The app makes no ambient telemetry calls, but user-triggered and settings-controlled update features contact external services and share the normal metadata of a network request:

- **Windows maintenance scan (WUA / online search):** a user-initiated Windows / Driver Store update check performs a Windows Update Agent query through the operating system. As with any WUA online search, update-applicability information about the device (OS/build, update applicability data) may be communicated to the Microsoft, WSUS, or enterprise update service configured by the machine's OS/policy. ForgerEMS collects nothing extra and stores only the returned read-only result locally; whether the service is contacted at all depends on that OS/policy configuration.
- **Update checks and managed resource downloads:** version metadata requests to the GitHub API and metadata/asset downloads from GitHub and allow-listed vendor origins expose the user's IP address and normal HTTP request metadata (URL, user agent, timestamp) to those services, subject to their own policies. Any configured GitHub API token is sent only to `api.github.com`, never to artifact/vendor hosts.
- **No automatic upload:** ForgerEMS never automatically uploads diagnostic reports, snapshots, or driver findings. Support-bundle and report files are shared only by explicit user action.

## No Automatic Uploads

ForgerEMS does not add cloud sync, telemetry, automatic log upload, or automatic support-bundle upload in this pass. Telemetry/crash reporting feature flags default off unless explicitly enabled through the project environment configuration.

## Support

Report issues with app version, steps, screenshots if useful, and redacted logs only after review. Do not send passwords, API keys, product keys, recovery keys, private documents, private customer data, or sensitive personal files.
