# Mechanical smoke test matrix — v1.1.12-rc.1

Use this as a **manual** checklist after automated `dotnet test` passes. Mark Pass/Fail/Skip with notes.

---

## 1. App launch

| Step | Expected |
|------|----------|
| Clean install from ZIP → `START_HERE.bat` | Installer runs; completes |
| First launch | Main window visible; not off-screen at 1080p |
| Welcome / onboarding | Overlay dismissible; no crash |
| Settings → About / Legal / Privacy | Documents open in-app or browser as designed |

---

## 2. USB Builder

| Case | Expected |
|------|----------|
| No USB selected | Safe messaging; no destructive default |
| USB selected (safe removable) | Targets list sane; logs stream on actions |
| EFI / VTOYEFI / tiny slices | Blocked or warned per existing rules |
| Ventoy / package status | Visible from backend context |
| Preflight / verify flows | Completes or surfaces actionable errors |

---

## 3. USB Intelligence

| Case | Expected |
|------|----------|
| No USB | Benchmark / mapping disabled or clearly explained |
| USB selected | Panel populated |
| Run benchmark | Progress + completion or clear failure |
| Cancel benchmark | “Cancelled” / non-success terminal state |
| USB removed mid-benchmark | Handled without hang |
| Mapping: start → capture → detect → label → save | Profile persists across restart |
| Kyra safe USB summary | No raw PNP IDs (see automated `UsbIntelligenceProTests`) |

---

## 4. System Intelligence

| Case | Expected |
|------|----------|
| First scan | JSON report written; cards populate |
| Cached / stale | Banners / text reflect freshness |
| Malformed JSON on disk | Diagnostics adds parse item (`DiagnosticsService` + new unit test) |
| Privacy | No secrets in footer copy |

---

## 5. Toolkit Manager

| Case | Expected |
|------|----------|
| Manifest present | Items listed |
| Managed missing vs manual required | **Manual required** never shown as generic “managed missing” |
| Verification issue | Clear status |
| Managed download flows | Respect legal gating (no surprise ISO fetch) |

---

## 6. Diagnostics

| Case | Expected |
|------|----------|
| Online / offline | Network heuristic lines sane |
| WSL present/absent | Warning only when absent |
| Checklist renders | From latest unified JSON |

---

## 7. Kyra

| Case | Expected |
|------|----------|
| Offline | Answer without provider |
| “USB slow” / “best port” | Uses local heuristics / docs refs |
| Toolkit manual-required | Explains manual vs managed |
| Beta readiness | References quickstart + checklist pattern |
| No sensitive leak | Session keys / paths policy per FAQ |

---

## 8. Installer / release

| Case | Expected |
|------|----------|
| ZIP layout | `ForgerEMS-v{ver}/START_HERE.bat`, `ForgerEMS Installer.exe`, `VERIFY.txt`, inner checksums |
| Root `CHECKSUMS.sha256` | Matches installer + ZIP + `release.json` + `DOWNLOAD_BETA.txt` |
| Direct EXE | Advanced path only; SmartScreen expected |
| Uninstall / reinstall | Preserves runtime under `%LOCALAPPDATA%\ForgerEMS\` per prior docs |
