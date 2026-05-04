# Performance / responsiveness audit — v1.1.12-rc.1

**Method:** Code review + known hotspots (no profiler run in this pass).

---

## App startup

| Area | Finding |
|------|---------|
| Self-contained single-file publish | Cold start dominated by .NET + WPF — acceptable for technician tool. |
| Backend discovery | Strict validation may add delay on bad installs — **correct** tradeoff vs silent failure. |

## Background scans

| Area | Finding |
|------|---------|
| System Intelligence | Long-running PowerShell — must stay async in ViewModel (existing pattern). |
| USB detection / topology | WMI-heavy — can be slow on large USB trees; cancellation paths should be exercised manually. |

## USB benchmark

| Area | Finding |
|------|---------|
| `UsbBenchmarkService` | Honors `CancellationToken`; maps native cancel strings to **Cancelled** result — good. |
| Overlap | `MainViewModel` tracks in-flight benchmarks — manual smoke should confirm no double-run UI deadlock. |

## Large downloads

| Area | Finding |
|------|---------|
| Managed downloads | Progress surfaces through PowerShell logs — if gaps reported, improve **progress text only** (future). |

## Kyra

| Area | Finding |
|------|---------|
| Offline path | Bounded rules — fast. |
| Online path | Network-bound — timeouts must fail soft to offline (existing Kyra behavior spec). |

## WMI timeouts

| Area | Finding |
|------|---------|
| `Invoke-ForgerEMSSystemScan.ps1` | If timeouts observed in field, capture script logs and tune per-subsystem (not changed blindly here). |

---

## Safe improvements this RC

- **Tests:** Malformed System Intelligence JSON → unified diagnostics item (`parse_error`) to prevent blank UI states (see `DiagnosticsServiceTests`).

## Not changed (would be feature work)

- New global debounce layer for USB selection.
- Disk cache of Ventoy downloads beyond existing verification.
