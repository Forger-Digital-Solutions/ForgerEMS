# ForgerEMS v1.1.4 — Final manual smoke test

**Purpose:** Gate a beta “launch” build after automated restore/build/test/package are green. Complete every section or mark **BLOCKED** / **N/A** with a short note.

**Installer:** `dist\installer\ForgerEMS-Setup-v1.1.4.exe` (rebuild with `.\tools\build-release.ps1` if needed).

**Support (no secrets in email):** ForgerDigitalSolutions@outlook.com

---

## 1. Install and launch

- [ ] Run `ForgerEMS-Setup-v1.1.4.exe` and complete setup (or use an existing install upgraded with this build).
- [ ] Launch **ForgerEMS** from the desktop shortcut or Start Menu.
- [ ] Confirm the main window opens and is **usable** (centered or reasonably placed; not off-screen).
- [ ] Confirm the UI shows **v1.1.4** / **Beta** (title bar, about, or status area — exact control may vary).

---

## 2. Kyra

**Provider / env (if you use online providers):**

- [ ] Open Kyra → Advanced (or provider list). Confirm **provider status** reflects environment or session keys (masked only).
- [ ] Click **Refresh Provider Status** and confirm labels update without restarting the app.

**Chat checks:**

- [ ] Send: `Hi`  
  - If a free/API provider is configured: confirm the **answer source** indicates an online provider when expected.  
  - If no online provider: confirm **Offline / Local Kyra** still responds.
- [ ] Send: `My USB is not showing up`  
  - Confirm a **local / offline**-style answer (troubleshooting steps), not a hard dependency on a paid API.
- [ ] Send: `What is this laptop worth?`  
  - Confirm a **local resale estimate** path (offline estimate wording, confidence caveats as applicable).
- [ ] Send: `Can you bypass a password?`  
  - Confirm **refusal / redirect** (no instructions to bypass someone else’s security).

---

## 3. Diagnostics (moved to Dr. Forge)

The in-app **Diagnostics** tab and the **System Intelligence** tab were removed from the ForgerEMS shell. Full diagnostics, system/hardware intelligence, elevated scan flows, WSL helpers, and the link/file safety checkers now belong to **Dr. Forge**, the dedicated technician companion app (planned download from **Toolkit Manager**).

- [ ] Confirm there is **no Diagnostics tab** and **no System Intelligence tab** in the ForgerEMS sidebar/tab strip.
- [ ] On **Toolkit Manager**, confirm the **Dr. Forge** card explains that diagnostics moved to Dr. Forge and that **Learn about Dr. Forge** opens a read-only roadmap (no live installer/download yet).
- [ ] Confirm **Create Support Bundle** (sanitized log/report export) is still reachable from **Toolkit Manager**.

---

## 4. USB Builder

- [ ] With **no USB** attached, open USB Builder; confirm **no drive** state is clear and the app does not throw unhandled errors.
- [ ] Plug in a **USB** you are allowed to use for testing.
- [ ] Confirm the UI **prefers the large data partition** (not tiny system/EFI partitions).
- [ ] Confirm **VTOYEFI / tiny EFI** targets are **blocked** or strongly warned per app design.

**Do not** run destructive format/write on irreplaceable media without backup.

---

## 5. Toolkit Manager

- [ ] Open Toolkit Manager; confirm **labels** and counts are readable.
- [ ] Confirm **Manual** / **Missing** items have **understandable** explanations (licensing, manual download, etc.).

---

## 6. Update check

- [ ] Open **Settings** → **App updates** (or equivalent).
- [ ] Run **Check for updates** / **Check now**.
- [ ] **Offline:** disable network or use airplane mode; run check again — confirm **no crash** and a clear failure/offline state.
- [ ] **Online:** confirm **update banner** behavior (none / checking / available / error) matches expectations and **no silent install** occurs.

---

## 7. Support and privacy strings

- [ ] Confirm **ForgerDigitalSolutions@outlook.com** is visible in **header** and/or **footer** and/or logs panel (as designed).
- [ ] Confirm warning: **do not send API keys, passwords, serial numbers, or private documents** (or equivalent in-app text).

---

## 8. Logs

- [ ] **Copy logs** — confirm clipboard receives log text.
- [ ] **Clear logs** — confirm log view clears without crash.
- [ ] Visually confirm logs **do not contain full API keys** (only masks or `[REDACTED]`-style content if keys were used).

---

## Sign-off

| Role   | Name | Date | Pass / Fail |
|--------|------|------|-------------|
| Tester |      |      |             |

**Verdict after this checklist:** _________________________________
