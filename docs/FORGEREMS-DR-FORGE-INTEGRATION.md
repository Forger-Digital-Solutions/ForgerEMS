# ForgerEMS <-> Dr. Forge packaged CLI integration contract

Status: first safe bridge implemented for packaged Dr. Forge CLI artifacts.

Readiness summary and enforced safety boundaries: [integrations/DR-FORGE-INTEGRATION-READINESS.md](integrations/DR-FORGE-INTEGRATION-READINESS.md).

ForgerEMS integrates with Dr. Forge only through the packaged CLI/Core process boundary. ForgerEMS does not load Dr. Forge WPF or provider internals, does not vendor unsafe internals, and does not depend on private developer paths.

## Design principles

1. **ForgerEMS stays stable.** Missing, malformed, timed-out, or failed Dr. Forge packages show friendly setup-needed or failed states.
2. **Process boundary only.** ForgerEMS starts `drforge.exe` with explicit arguments and captures stdout/stderr. It does not load Dr. Forge assemblies in-process.
3. **No invented values.** Null or missing readings render as `Unavailable`, never zero.
4. **Local only.** The bridge adds no telemetry, account, activation, licensing, or network behavior.
5. **No privilege escalation.** The bridge does not install/start services, load drivers, request auto-elevation, or call `sudo` / `pkexec`.

## Package location

ForgerEMS accepts an explicit path to `drforge.exe`.

When no explicit path is selected, ForgerEMS may search only app-local bundled locations such as:

- `tools\drforge\windows-x64\drforge.exe`
- `tools\drforge\drforge.exe`
- `drforge\windows-x64\drforge.exe`
- `drforge\drforge.exe`

Production code must not hardcode developer-private absolute paths.

## Manifest and checksums

When present, ForgerEMS reads `drforge-cli-release-manifest.json` and accepts:

```json
{
  "schema": "drforge-cli-release-manifest/1.0"
}
```

If the manifest references `SHA256SUMS.txt`, or a `SHA256SUMS.txt` file exists next to `drforge.exe`, ForgerEMS verifies listed package files before running readiness/report actions. Checksum path entries must stay inside the package directory.

If no checksum file exists, ForgerEMS reports that local checksum verification is unavailable instead of pretending the package was verified.

## Readiness commands

ForgerEMS runs:

```powershell
drforge.exe --version
drforge.exe sensor-core --help
drforge.exe sensors driver-status --json
```

Readiness commands are timeout-bounded. Non-zero exit codes, stderr failures, launch failures, and timeouts from `--version` or `sensor-core --help` return structured failure states for the UI. `sensors driver-status --json` is an optional compatibility probe: if an older CLI does not support it, ForgerEMS keeps the package usable through the user-mode report bridge.

## Driver status JSON contract

ForgerEMS accepts Dr. Forge driver status schema `forger-sensor-driver-preflight/1.1` conservatively. The current safe state is:

- production driver shipped: `false`
- no installed/running driver
- user-mode fallback active: `true`
- no driver action taken
- driver-required readings remain unavailable

This state is normal, safe, and not an error. Unknown fields are ignored. Unsupported future schemas are summarized as unsupported instead of inventing driver state. ForgerEMS does not install, start, load, register, download, or activate any driver based on this output.

The Toolkit Manager Dr. Forge card displays this as explicit local status: package/configured state, CLI version/commit when available, user-mode fallback, no production driver shipped/loaded, no driver action taken, and driver-required readings unavailable until a future signed-driver phase.

## Report and archive commands

The first bridge uses the sensor-core CLI contract:

```powershell
drforge.exe sensor-core snapshot --json
drforge.exe sensor-core report <snapshot.json> --format json --out <report.json>
drforge.exe sensor-core archive <snapshot.json> --out <archive-folder>
```

ForgerEMS writes the snapshot stdout to the local Runtime reports folder, then asks Dr. Forge to transform that snapshot into a report or archive. The bridge does not pass service/deep-provider flags in this phase.

The card lists recent app-managed Dr. Forge reports and archives from the Runtime reports folder only. It does not crawl Documents, PATH, arbitrary user folders, or Dr. Forge install folders. Missing or unreadable report folders show friendly empty/unavailable states.

## Report JSON contract

ForgerEMS recognizes current Dr. Forge local contract families:

- `forge-sensor-core/1.0` saved snapshots
- `forge-hardware-intake-report/1.0` intake reports
- `forge-hardware-intake-archive/1.0` archive manifests/folders as metadata-only history entries
- `drforge-cli-release-manifest/1.0` CLI release manifests
- `forger-sensor-driver-preflight/1.1` current no-driver readiness

ForgerEMS parses enough of `forge-hardware-intake-report/1.0` and `forge-sensor-core/1.0` to display:

- report schema
- source schema
- platform
- safety mode
- key available readings
- unavailable readings as `Unavailable`
- findings
- notes
- ring-0/deep telemetry gaps

Null remains unavailable. ForgerEMS never converts null readings to zero.

## UI states

The UI may show:

- `Not configured`
- `Package found`
- `Ready`
- `Running intake`
- `Report ready`
- `Archive ready`
- `Unavailable`
- `Failed`

Expected actions:

- Select Dr. Forge CLI path
- Check Dr. Forge package
- Refresh status
- Generate report
- Generate archive
- Open report folder
- Copy summary
- Copy status summary

## Persistence

ForgerEMS persists only:

- selected Dr. Forge executable path
- last readiness state
- last local report/archive output path when it is under the ForgerEMS Runtime reports folder

No secrets, telemetry payloads, account state, activation state, or licensing state are stored by the bridge.

## Support bundles and privacy

Generated Dr. Forge reports and archives may include local device/context information, findings, notes, and unavailable telemetry reasons. Users should review reports before sharing them.

ForgerEMS support bundles include Dr. Forge report/archive files only when the user explicitly chooses to include them and confirms the support-bundle export. The exporter only accepts Dr. Forge artifacts under the local Runtime reports folder and only includes a small allowlist of archive files.

## Remaining gaps

Deep telemetry such as fan RPM, voltage rails, EC/SuperIO/MSR readings requires future safe providers or signed privileged components. The first bridge does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity.
