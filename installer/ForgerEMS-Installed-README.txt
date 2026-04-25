ForgerEMS
Forger Digital Solutions

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

Important:
The WPF app remains a frontend controller for the existing PowerShell backend,
but installed mode now uses the bundled backend by default.

Advanced override options still exist:
- repo mode
- external release-bundle mode

If the bundled backend is missing, corrupted, or version-mismatched, the app
will fail gracefully and only fall back to an external backend context when one
is available.
