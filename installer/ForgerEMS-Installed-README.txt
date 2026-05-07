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
- providers\sensors\ (bundled local read-only sensor provider and license notices when packaged)

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
- Beta cloud Kyra can use FORGEREMS_KYRA_GATEWAY_URL + FORGEREMS_KYRA_GATEWAY_BETA_TOKEN only.
- No direct provider API keys are included in this release bundle.
- Gateway beta token is revocable/rotatable and is not a provider API key.
- Free API providers are optional and may have limits or outages.
- System context sharing is OFF by default.
- API keys are session-only in this beta and should be entered in settings fields, not chat.
- Optional Cloudflare Workers AI needs CLOUDFLARE_API_KEY and CLOUDFLARE_ACCOUNT_ID; use Refresh Provider Status after changing environment variables.
- The app may check GitHub for newer ForgerEMS releases (Settings → App updates). It does not silently download or install updates.
- Deep Sensor Mode is optional and off unless you enable it during install or later in Settings.
- It uses bundled local read-only hardware sensors for deeper Hardware X-Ray coverage. It reads only while ForgerEMS is running or scanning.
- Deep Sensor Mode does not control fans, voltages, clocks, BIOS, firmware, overclocking, or undervolting. It does not install a background service, use cloud sensor telemetry, or require third-party downloads.
- LibreHardwareMonitorLib is included where packaged under MPL-2.0 with notices:
  providers\sensors\THIRD-PARTY-NOTICES.txt
  providers\sensors\LICENSES\LibreHardwareMonitor-MPL-2.0.txt
- Some readings depend on firmware, drivers, permissions, and hardware support. Unavailable readings are coverage limits, not failures.
- Review reports before sharing. Do not send product keys, API keys, tokens, passwords, private documents, or sensitive personal files.

Kyra Intelligence (privacy summary):
ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

The installer Kyra Intelligence page starts local-only. Leave every box unchecked to keep Kyra Local Only. Optional community sharing choices are applied the first time ForgerEMS creates Kyra settings on this Windows profile.

You can change everything later in the app under Settings → Kyra Intelligence:
- Keep Local Only / turn off community sharing
- export Kyra memory
- delete or reset Kyra memory
- view what would be shared
- turn realtime gateway research on or off
- control sanitized System Intelligence context

ForgerEMS never shares API keys, gateway tokens, passwords, product keys, serial numbers, private files, full paths, emails, IP addresses, exact location, or raw logs.

Important:
The WPF app remains a frontend controller for the existing PowerShell backend,
but installed mode now uses the bundled backend by default.

Advanced override options still exist:
- repo mode
- external release-bundle mode

If the bundled backend is missing, corrupted, or version-mismatched, the app
will fail gracefully and only fall back to an external backend context when one
is available.
