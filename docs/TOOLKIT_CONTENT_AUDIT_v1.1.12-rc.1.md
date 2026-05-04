# Toolkit / manifest content audit — v1.1.12-rc.1

**Sources:** `manifests/vendor.inventory.json`, `manifests/ForgerEMS.updates.json` (managed downloads catalog — not fully enumerated row-by-row here).

---

## vendor.inventory.json — summary table

| Item | Managed / Manual | Checksum | URL | Download behavior | Legal / safety | Status |
|------|------------------|----------|-----|---------------------|----------------|--------|
| `Tools\Portable\*` roots | Manual | Empty | Empty | None auto | Third-party tools; operator curated | OK |
| `Drivers\*` staging (except shortcuts) | Manual | Empty | Empty | None auto | Drivers are vendor-owned | OK |
| `DOWNLOAD - *.url` under Drivers (Realtek/Intel/AMD/NVIDIA) | Managed (shortcut) | Empty | Official vendor URLs | Shortcut / page link only | Official vendor destinations | OK — checksum N/A for `.url` intent |
| `MediCat.USB` | Manual | Empty | Community site | Never auto-bundled | Large external bundle | OK — explicit external workflow |

**Checksum policy:** Managed **shortcuts** intentionally have empty `checksum` — they are not large binaries. Large ISOs in **`ForgerEMS.updates.json`** carry SHA expectations where the pipeline enforces them — audit those rows before changing download URLs.

---

## What must never be auto-downloaded without explicit legal review

- Full **Windows ISOs**, **OEM recovery images**, **paid tool binaries**.
- **MediCat** or other community aggregates as a single blob.
- Any payload that would **violate** vendor redistribution terms.

---

## Empty folders / counts

- Toolkit health script determines **missing vs manual**; UI uses `ToolkitHealthItemView.StatusDisplayUi` — tested for `MANUAL_REQUIRED` vs “Managed missing”.

---

## Fixes in this pass

- **None** to manifests — documentation only.

## Still needed

- Periodic URL health review for `.url` shortcuts (manual spot-check from release QA machine).
