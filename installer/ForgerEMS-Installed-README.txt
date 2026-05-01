ForgerEMS
Forger Digital Solutions

Copyright © 2026 Forger Digital Solutions. All rights reserved.
Beta issue? Send logs/screenshots to ForgerDigitalSolutions@outlook.com
Do not email API keys, passwords, serial numbers, or private documents.
Beta feedback / support: ForgerDigitalSolutions@outlook.com
Include build version, steps, and logs.

This installer places the native Windows frontend in Program Files.

Installed app:
- ForgerEMS.exe
- backend\ (verified bundled backend release-bundle)
- docs\ForgerEMS-Installed-README.txt

Bundled backend contents include:
- Verify-VentoyCore.ps1
- Setup-ForgerEMS.ps1
- Update-ForgerEMS.ps1
- manifests\
- backend support docs and verification history

What this installer does NOT include:
- third-party payloads
- ISO files
- Drivers\
- Tools\Portable\
- Ventoy binaries

Runtime data stays under:
%LOCALAPPDATA%\ForgerEMS\Runtime\

Beta safety notes:
- This is beta software; do not use important USB drives without backups.
- Always confirm USB drive letter and size before Setup/Update/Ventoy actions.
- Do not select the tiny EFI/VTOYEFI partition for toolkit staging.
- Offline Local Kyra works without API keys.
- Free API providers are optional and may have limits or outages.
- System context sharing is OFF by default.
- API keys are session-only in this beta and should be entered in settings fields, not chat.
- Optional Cloudflare Workers AI needs CLOUDFLARE_API_KEY and CLOUDFLARE_ACCOUNT_ID; use Refresh Provider Status after changing environment variables.
- The app may check GitHub for newer ForgerEMS releases (Settings → App updates). It does not silently download or install updates.

Important:
The WPF app remains a frontend controller for the existing PowerShell backend,
but installed mode now uses the bundled backend by default.

Advanced override options still exist:
- repo mode
- external release-bundle mode

If the bundled backend is missing, corrupted, or version-mismatched, the app
will fail gracefully and only fall back to an external backend context when one
is available.
