# ForgerEMS v1.1.12-rc.2 — Release notes (beta readiness)

**Channel:** Beta (readiness / first-impression hardening)  

## DOWNLOAD THE ZIP — NOT THE EXE

**Recommended:** Download **one** of:

- **`ForgerEMS-v1.1.12-rc.2.zip`**, or  
- **`ForgerEMS-Beta-v1.1.12-rc.2.zip`** (identical bytes; easier label for testers — either ZIP is valid)

Then **extract**, open folder **`ForgerEMS-v1.1.12-rc.2`**, run **`START_HERE.bat`**.

**Advanced / direct installer (same bits, harsher SmartScreen path):** `ForgerEMS-Setup-v1.1.12-rc.2.exe`

---

## What changed since rc.1

- **ZIP-first copy** everywhere: `DOWNLOAD_BETA.txt`, `VERIFY.txt`, `START_HERE.bat`, README, FAQ, download troubleshooting, GitHub release body template.  
- **Dual ZIP assets** on GitHub Actions + `CHECKSUMS.sha256` (primary + `ForgerEMS-Beta-v…` alias).  
- **First-tester flow** doc with copy-paste email: `docs/FIRST_TESTER_DOWNLOAD_FLOW.md`.  
- **Kyra provider / env guide:** `docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md`; in-app Kyra Advanced help + About + first Kyra message updated.  
- **Welcome overlay** copy: clearer beta + next steps + offline Kyra default.

---

## Verification

- Root **`CHECKSUMS.sha256`** lists: installer, **both** ZIPs, `release.json`, `DOWNLOAD_BETA.txt`.  
- Inner ZIP **`CHECKSUMS.sha256`** matches files inside the bundle.

---

## Known limitations

- **SmartScreen** until signing / reputation.  
- **USB topology** best-effort.  
- **Pro licensing** not enforced in beta.

---

## Support

**ForgerDigitalSolutions@outlook.com** — sanitized logs only; no secrets.
