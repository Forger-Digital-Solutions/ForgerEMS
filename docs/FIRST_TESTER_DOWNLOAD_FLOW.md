# First-tester download flow (copy-paste friendly)

Use this for **Chrome**, **Edge**, or any browser. ForgerEMS does **not** bypass SmartScreen.

---

## Golden path (do this)

1. Open **GitHub Releases**: https://github.com/Forger-Digital-Solutions/ForgerEMS/releases  
2. Find the version you were invited to test (example: **v1.1.12-rc.3**).  
3. Under **Assets**, download **one** of these ZIPs (same contents inside):
   - `ForgerEMS-v1.1.12-rc.3.zip` **or**
   - `ForgerEMS-Beta-v1.1.12-rc.3.zip`  
4. Wait until the file name ends in **`.zip`**. If you see **`.crdownload`**, the download is **not done** — wait, or cancel and retry. **Do not rename** `.crdownload` to `.zip`.  
5. Download **`CHECKSUMS.sha256`** from the same release page.  
6. In PowerShell, in the folder that contains the ZIP:

   ```powershell
   Get-FileHash .\ForgerEMS-v1.1.12-rc.3.zip -Algorithm SHA256
   ```

   Match the hash to the line in `CHECKSUMS.sha256` for that filename.  
7. **Right-click the ZIP → Extract All…** Pick a **short** folder (e.g. `Desktop\ForgerEMS`).  
8. Open the extracted folder **`ForgerEMS-v1.1.12-rc.3`**.  
9. Double-click **`START_HERE.bat`**.  
10. If **SmartScreen** appears: only use **More info → Run anyway** if you verified the ZIP from the **official** GitHub release and the hash matched.  
11. After install/launch: dismiss or follow the **welcome** overlay — run **System Scan**, open **USB Builder**, select a **removable USB** before **USB Benchmark**.  
12. **Kyra** works **offline** by default — **no API keys** required for normal beta testing. Optional online providers are **operator-managed**; see **`docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md`** in the repo (or **Kyra Advanced** in the app) only if your pilot explicitly uses them.

---

## What *not* to do

- Do **not** download `ForgerEMS-Setup-v….exe` first unless you are an **advanced** tester (more SmartScreen friction).  
- Do **not** run installers from random links, Discord DMs, or email attachments.  
- Do **not** send API keys or serials to support — see **FAQ**.

More detail: [DOWNLOAD_TROUBLESHOOTING.md](DOWNLOAD_TROUBLESHOOTING.md) · [FAQ.md](FAQ.md)

---

## Message you can send to testers

You can paste the block below into email or Discord.

```
ForgerEMS beta — please use the ZIP, not the raw EXE.

1) Open our GitHub Releases page (official repo only).
2) Under Assets, download ONE of these (same files inside):
   - ForgerEMS-v1.1.12-rc.2.zip
   - OR ForgerEMS-Beta-v1.1.12-rc.2.zip
3) If Chrome shows a file ending in .crdownload, wait until it becomes .zip — do not rename it.
4) Download CHECKSUMS.sha256 from the same release and verify the ZIP hash in PowerShell (Get-FileHash).
5) Extract the ZIP, open folder ForgerEMS-v1.1.12-rc.2, double-click START_HERE.bat.
6) If SmartScreen appears, only use "Run anyway" if you trust the official release and the hash matched.

Kyra works offline by default — no API keys needed. Optional online setup:
https://github.com/Forger-Digital-Solutions/ForgerEMS/blob/main/docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md

Issues (sanitized logs only): ForgerDigitalSolutions@outlook.com
```

*(Update the version string in the message when you ship a newer tag.)*
