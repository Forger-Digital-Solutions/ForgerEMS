# ForgerEMS ⇄ Dr. Forge integration contract

Status: design-only. Dr. Forge is not built yet. This document is the
integration shape ForgerEMS will honor when Dr. Forge is available, and the
shape Dr. Forge must produce.

## Design principles

1. **ForgerEMS stays stable.** A missing, stale, malformed, or crashed Dr.
   Forge must never destabilize ForgerEMS.
2. **Read-only handoff.** Dr. Forge writes; ForgerEMS reads.
3. **No invented values.** ForgerEMS displays what Dr. Forge reports, marks
   stale snapshots stale, and surfaces unavailable reasons verbatim.
4. **No hidden background traffic.** Dr. Forge does not talk to the network,
   does not call home, and does not report telemetry. ForgerEMS does not push
   Dr. Forge data anywhere.
5. **Crash containment.** Dr. Forge runs in its own process. ForgerEMS does not
   load Dr. Forge in-process.

## Snapshot file

Dr. Forge writes a single JSON snapshot file under the user-local sensor
directory ForgerEMS already owns:

```
%LOCALAPPDATA%\ForgerEMS\sensors\dr-forge-latest.json
```

Schema (illustrative, v0 draft):

```json
{
  "schemaVersion": 1,
  "drForgeVersion": "0.1.0-preview",
  "machineKey": "stable-local-hash",
  "capturedAtUtc": "2026-06-03T18:21:09Z",
  "freshnessSeconds": 12,
  "source": "DrForge",
  "confidence": "high",
  "unavailableReasons": [],
  "cpu": {
    "packageTempCelsius": 54.0,
    "coreTempsCelsius": [54.0, 55.0, 52.0, 51.0],
    "fanRpms": [1850]
  },
  "gpu": [
    { "vendor": "NVIDIA", "model": "RTX 4060", "tempCelsius": 48.0, "fanRpms": [1100] }
  ],
  "battery": {
    "designedCapacityMwh": 90000,
    "fullChargeCapacityMwh": 81000,
    "cycleCount": 142,
    "chargeRateMw": 26500
  },
  "storage": [
    {
      "model": "Samsung 990 Pro 1TB",
      "tempCelsius": 41.0,
      "powerOnHours": 1820,
      "smartAttributes": { "wearLevel": 4 }
    }
  ],
  "boardCapabilities": {
    "tpmVersion": "2.0",
    "secureBootState": "Enabled"
  }
}
```

Fields ForgerEMS cares about most:

- `schemaVersion` — ForgerEMS refuses unknown major versions.
- `capturedAtUtc` + `freshnessSeconds` — drives the stale badge.
- `unavailableReasons` — bubbled up as friendly explanations in the UI.
- `confidence` — `high` / `medium` / `low` to drive any conservative rounding
  in summary copy.

## ForgerEMS read path

1. On launch and on USB-target change, ForgerEMS checks for
   `dr-forge-latest.json`.
2. If present and `schemaVersion` is supported, it is parsed.
3. `capturedAtUtc` older than a configurable freshness window (default: 5
   minutes) → snapshot is shown but marked **stale**.
4. Missing / malformed / unsupported schema → ForgerEMS shows Dr. Forge
   status **Data unavailable** with the parse reason; no exception bubbles
   to the user.
5. ForgerEMS never writes to this file. Dr. Forge owns it.

## Dr. Forge write path

1. Dr. Forge runs read-only sensor collection.
2. Writes to `dr-forge-latest.json.tmp` first.
3. Atomically renames over `dr-forge-latest.json`.
4. Never partially writes the file.
5. If a sensor read throws, Dr. Forge records the reason in
   `unavailableReasons` and proceeds with the remaining sensors. Partial
   snapshots are better than no snapshot.

## What Dr. Forge **must not** do

- Modify any ForgerEMS file outside `dr-forge-*.json` in the sensor folder.
- Modify the system registry.
- Modify firmware, BIOS, EC, fan curves, voltages, or charging behavior.
- Install kernel drivers or services without a user-approved installer
  (out of scope for the first version entirely).
- Make network calls.
- Run elevated unless the user explicitly elevated the launch (Admin Inventory
  parity).

## Failure modes ForgerEMS must handle gracefully

- File missing → Dr. Forge: **Not installed / Not running**.
- File present but `schemaVersion` too new → Dr. Forge: **Update available
  (ForgerEMS too old to parse)**.
- File parse error → Dr. Forge: **Data unavailable (parse error)** + log line.
- File older than freshness window → Dr. Forge: **Stale** badge, snapshot
  values shown read-only with a stale chip.
- Dr. Forge process detected but no file → Dr. Forge: **Installed, not yet
  reporting**.

## Versioning

- `schemaVersion` is a single integer. Breaking shape changes bump the major.
- Additive fields are non-breaking; ForgerEMS ignores unknown fields.
- ForgerEMS pins a supported range (`[minSupported, maxSupported]`). Outside
  range → graceful "update available" / "ForgerEMS too old" copy.

## Trust boundary

Dr. Forge is local-only. ForgerEMS treats Dr. Forge data the same way it
treats any other locally-collected sensor data: it labels the source, shows
the timestamp, and surfaces unavailable reasons. Nothing from Dr. Forge is
sent to a remote server by ForgerEMS.
