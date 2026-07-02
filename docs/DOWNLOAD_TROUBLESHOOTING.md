# ForgerEMS — Download & install help (beginner-friendly)

**Who this is for:** Anyone downloading ForgerEMS from **GitHub Releases** who hits browser quirks, half-finished files, or Windows warnings.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — describe what you see; attach **sanitized** screenshots only (no secrets).

---

## Rule #1: download the portable ZIP first

On **[GitHub Releases](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases)** → open the version you want → **Assets**.

- **Do download:** `ForgerEMS-v<version>.zip`.  
- **Do not start here unless you specifically want installed mode:** `ForgerEMS-Setup-v<version>.exe` — that file is real, but it is the direct installer. Browsers and **SmartScreen** are usually **stricter** on a raw `.exe` than on a `.zip` you extract yourself.

ForgerEMS does **not** bypass Windows security. The **recommended preview path** is: **ZIP → extract → `START_HERE.bat` or `ForgerEMS.exe` → first-run Terms gate**. The installer is a separate asset for users who prefer installed mode.

---

## The safe install flow (step by step)

1. **Download the ZIP** from **Assets** and wait until it is **100% finished** (see the next section if the filename looks wrong).  
2. **Right-click the ZIP → Extract All…** (or use your favorite zip tool). Choose a **short folder name**, for example `Desktop\ForgerEMS`.  
3. Open the new folder. You should see files such as **`ForgerEMS.exe`**, **`START_HERE.bat`**, `VERIFY.txt`, `docs\`, `backend\`, and `CHECKSUMS.sha256`.
4. Double-click **`START_HERE.bat`**, or run **`ForgerEMS.exe`** directly. Read what the script prints.
5. On first launch, read and accept the ForgerEMS Terms of Use before the main tools unlock.

That is the whole idea: **one official portable ZIP** → **extract** → **`START_HERE.bat` / `ForgerEMS.exe`** → **local first-run consent**.

---

## What `.crdownload` means (Chrome)

If your file is named something like `ForgerEMS-v1.2.3-preview.1.zip.crdownload`:

- The download is **still in progress** or **stuck**.  
- **Do not** rename it to `.zip` and **do not** try to open it.  
- Wait until Chrome **renames** it to end in **`.zip`**. If it never finishes, cancel and try again (see **Retry a clean download** below).

Edge uses similar temporary names during download; same rule: wait for the final **`.zip`**.

---

## Retry a clean download

1. In your **Downloads** folder, delete any **partial** files for that version (`.crdownload`, `.tmp`, or a `.zip` that is suspiciously tiny).  
2. Close extra browser tabs that might be pointing at an old release.  
3. Open the release again, press **Ctrl+F5** to hard-refresh.  
4. Click the **ZIP** asset again and **wait** until the browser shows a normal completed file.

If downloads are blocked at work or school, you may need **IT** to allow `github.com` — ForgerEMS cannot override enterprise policy.

---

## Why SmartScreen may warn

Windows **SmartScreen** warns on software that is **new** or **not yet widely trusted**. Beta builds often hit this until **code signing** and reputation mature.

**Normal for beta:**

- “Windows protected your PC” or **Unknown publisher** on a downloaded **.exe**.  
- A **“More info”** link, then **“Run anyway”** — only use that if you **trust the source** (official GitHub release) and, when possible, **verified the hash**.

**Still prefer:** ZIP → extract → **`START_HERE.bat`** so you are not fighting the strictest path first.

**Not safe:** Turning off Defender, running random “fix” scripts, or disabling security “to make it work.”

---

## Verify the ZIP (optional but smart)

From the **same** GitHub release, download **`CHECKSUMS.sha256`** (if published for that release).

In **PowerShell**, `cd` to the folder that contains **both** the ZIP and `CHECKSUMS.sha256`, then:

```powershell
Get-FileHash .\ForgerEMS-v1.2.3-preview.1.zip -Algorithm SHA256
```

Compare the `Hash` value to the line in `CHECKSUMS.sha256` for that exact filename. **They must match** before you treat the file as trustworthy.

Inside the extracted folder, there may be another **`CHECKSUMS.sha256`** listing **inner** files (`ForgerEMS.exe`, backend/docs files, batch files). Use that to confirm extraction was not corrupted.

---

## Quick symptom table

| What you see | What it usually means | What to do |
|----------------|------------------------|------------|
| `.crdownload` at the end of the name | Still downloading (or stuck) | Wait; or cancel and retry |
| SmartScreen on a raw `.exe` | Reputation / direct download | Prefer **ZIP → START_HERE.bat** |
| “Publisher unknown” | Beta / not fully signed yet | Expected; verify **official** release + hash |
| Hash does not match | Corrupt or wrong file | Delete file; download again from **Assets** |
| “Failed – blocked” | Policy or antivirus | Try another network or IT allowlist |

---

## More reading

- [FAQ.md](FAQ.md) — short answers to common questions  
- [FIRST_TESTER_DOWNLOAD_FLOW.md](FIRST_TESTER_DOWNLOAD_FLOW.md) — first-run narrative  
- [TERMS_OF_USE.md](TERMS_OF_USE.md), [PRIVACY_AND_DATA_HANDLING.md](PRIVACY_AND_DATA_HANDLING.md), and [LEGAL_NOTICES.md](LEGAL_NOTICES.md) — current preview terms and data handling notes
