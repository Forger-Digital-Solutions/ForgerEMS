# Security / privacy audit — v1.1.12-rc.1

---

## Automated / scripted checks

| Check | Result |
|-------|--------|
| `.\tools\check-secrets.ps1` | **2026-05-03 run:** exit **0**; hits were **only** under `tests/` (fake `sk-…` / `AIza…` fixtures) — acceptable. Re-run with `-Strict` before any tag. |
| Kyra safe context | `KyraSafeContextBuilder` + tests (`KyraSafeContextBuilderTests`, `SensitiveDataRedactorTests`). |
| USB JSON snapshot | `UsbIntelligenceProTests.UsbSnapshotJson_WithBenchmark_DoesNotLeakRawPnpOrWmiIds` |

---

## Data handling

| Topic | Status |
|-------|--------|
| Local-first reports | `%LOCALAPPDATA%\ForgerEMS\` — FAQ states no silent vendor upload for local scans. |
| Optional online Kyra | Sanitized summaries; session API keys not persisted in JSON settings. |

---

## Unsafe command execution

| Topic | Status |
|-------|--------|
| Backend scripts | Run from discovered backend root; user confirms elevated operations where required. |
| No “disable Defender” guidance added | Confirmed for this pass. |

---

## Logging / support hygiene

| Topic | Status |
|-------|--------|
| Docs + FAQ | Instruct users to **review** logs before emailing support. |

---

## Gaps / honest limitations

- Heuristic secret scan is **not** full SAST.
- SmartScreen and reputation are **external** controls — documented, not bypassed.
