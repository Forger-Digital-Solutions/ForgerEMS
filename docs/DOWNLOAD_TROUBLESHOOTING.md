# ForgerEMS — Download & install help (beginner-friendly)

**Who this is for:** Anyone downloading ForgerEMS from **GitHub Releases** who hits browser quirks, half-finished files, or Windows warnings.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — describe what you see; attach **sanitized** screenshots only (no secrets).

---

## Rule #1: download the ZIP — not the EXE first

On **[GitHub Releases](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases)** → open the version you want → **Assets**.

- **Do download:** `ForgerEMS-v<version>.zip` **or** `ForgerEMS-Beta-v<version>.zip` (same bundle style; pick one).  
- **Do not start here:** `ForgerEMS-Setup-v<version>.exe` — that file is real, but it is the **advanced / direct** installer. Browsers and **SmartScreen** are usually **stricter** on a raw `.exe` than on a `.zip` you extract yourself.

ForgerEMS does **not** bypass Windows security. The **safe, recommended path** is: **ZIP → extract → `START_HERE.bat` → installer inside the bundle**.

---

## The safe install flow (step by step)

1. **Download the ZIP** from **Assets** and wait until it is **100% finished** (see the next section if the filename looks wrong).  
2. **Right-click the ZIP → Extract All…** (or use your favorite zip tool). Choose a **short folder name**, for example `Desktop\ForgerEMS`.  
3. Open the new folder. You should see files such as **`START_HERE.bat`**, `VERIFY.txt`, and an installer with a clear name inside the bundle.  
4. Double-click **`START_HERE.bat`**. Read what it prints. It is the supported entry point for verification and install.  
5. Complete the installer **only when you are comfortable** you got the ZIP from the **official** release page.

That is the whole idea: **one official ZIP** → **extract** → **`START_HERE.bat`** → **installer**.

---

## What `.crdownload` means (Chrome)

If your file is named something like `ForgerEMS-v1.1.12-rc.3.zip.crdownload`:

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
Get-FileHash .\ForgerEMS-v1.1.12-rc.3.zip -Algorithm SHA256
```

Compare the `Hash` value to the line in `CHECKSUMS.sha256` for that exact filename. **They must match** before you treat the file as trustworthy.

Inside the extracted folder, there may be another **`CHECKSUMS.sha256`** listing **inner** files (installer, batch files). Use that to confirm extraction was not corrupted.

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
- [LEGAL.md](LEGAL.md) / [PRIVACY.md](PRIVACY.md) — beta notices and data handling summary  
