ForgerEMS
Forger Digital Solutions

This installer places the native Windows frontend in Program Files.

Installed app:
- ForgerEMS.exe

What this installer does NOT include:
- the backend repo
- release-bundle scripts
- third-party payloads
- Ventoy binaries

Runtime data stays under:
%LOCALAPPDATA%\ForgerEMS\Runtime\

Important:
The WPF app remains a frontend controller for the existing PowerShell backend.
Backend discovery still depends on running within a repo tree or a release-bundle
tree that contains:
- Verify-VentoyCore.ps1
- Setup-ForgerEMS.ps1
- Update-ForgerEMS.ps1

If the installed app starts and reports that the backend is unavailable, launch
it in a working context where the backend scripts are present.
