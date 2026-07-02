# Running ForgerEMS on Linux under Wine

> ForgerEMS is a Windows-first technician suite. This document describes
> the **experimental compatibility mode** that lets the app start and run
> a useful subset of its functionality under Wine on Linux. ForgerEMS is
> not a native Linux application and does not claim full feature parity on
> Linux.

**Status:** experimental compatibility mode. The app starts cleanly under
Wine staging on Nobara/Fedora. USB drive write actions are disabled in
this prerelease — use native Windows for USB writing.

## What works under Wine

- Cold start without `wpfgfx_cor3` / `wined3d` crashes (rendering forced
  to `SoftwareOnly`).
- USB Builder catalog browsing, profile selection, and the Profile pack
  planner (read-only).
- Drive Validator wizard (read-only sampled validation; non-destructive).
- Download manifest viewing and freshness display.
- Port / USB Intelligence read-only summaries and latest cached check results.
- Kyra offline copilot, link safety analyzer, local file safety analyzer.
- About / FAQ / Legal / Privacy info windows.
- Compatibility banner with honest "Wine compatibility mode" messaging
  and an optional Linux helper summary.

## What is read-only or limited

- The Linux helper (`tools/linux/forgerems-linux-helper.sh`) is invoked
  as a background probe when compatibility mode is detected. It emits
  read-only JSON describing distro, kernel, block devices, mounts,
  removable devices, and any Ventoy partitions. ForgerEMS displays the
  summary in the compatibility banner but **does not** automatically
  authorize any USB write action based on helper output.
- ACPI thermal zone probe (`MSAcpi_ThermalZoneTemperature`) — short-circuits
  to ProviderUnavailable. Confidence is not penalized.
- NVIDIA SMI probe — `nvidia-smi.exe` is not invoked under Wine.
- Health evaluation — TPM, Secure Boot, and BitLocker fields surface as
  "not checked in Wine compatibility mode" instead of dragging confidence
  down.

## What is disabled under Wine

- Setup USB
- Update USB
- Rename USB
- Install / Update Ventoy
- Toolkit Update
- Full Managed Download

These all go through `Update-ForgerEMS.ps1` / `Setup-USB.ps1` which depend
on Windows PowerShell + CIM/WMI. The buttons remain visible but are
inactive while compatibility mode is on. The compatibility banner explains
why.

## What is unsupported

- Native sensor reads (LibreHardwareMonitor).
- WMI-based diagnostics (TPM, BitLocker, Secure Boot, Defender).
- UAC / admin relaunch (Wine has no equivalent of `runas`).
- Direct USB write through Win32 `DeviceIoControl`.

## What still requires Windows native

- Any USB drive write or partition-modifying step.
- Real System Intelligence scans against TPM / firmware / SMART data.
- Inno Setup installer execution (only relevant at upgrade time).

## What still needs future Linux helper work

- Optional drive enumeration based on helper output exposed inside the
  USB Builder list (today the helper data is informational only).
- Mapping helper-detected removable devices to ForgerEMS profile actions.
- Linux-native equivalents for `Get-NetAdapter` / `powercfg` /
  `Get-StorageReliabilityCounter`.

## Tested environments

| Distro | Wine | Status |
|---|---|---|
| Nobara 43 (Fedora-based) | wine-11.8 staging | Primary target. App starts with software rendering forced; compatibility banner visible. |
| Fedora 41 | wine-staging 9.x+ | Expected to work with the same setup. |
| Ubuntu 24.04 LTS | wine-staging from WineHQ apt repo | Expected to work. Use the WineHQ apt repo, not distro packages. |
| Debian 12 | wine-staging from WineHQ apt repo | Expected to work. |
| Linux Mint 22 | wine-staging from WineHQ apt repo | Expected to work. |
| Arch / Manjaro | wine-staging from extra/multilib | Expected to work; `wine-mono` and `wine-gecko` install via pacman. |
| openSUSE Tumbleweed / Leap 15.6+ | wine-staging from Emulators repo | Expected to work; the WineHQ packages on openSUSE pull in winetricks deps cleanly. |

Vanilla Wine (non-staging) is **not** recommended — WPF needs several
staging-only patches to render at all.

## One-time setup

1. **Install Wine staging.** Use the WineHQ repository for your distro;
   the default distro package is usually too old.

2. **(Recommended) use a dedicated 64-bit prefix:**

   ```sh
   export WINEARCH=win64
   export WINEPREFIX="$HOME/.wine-forgerems"
   wineboot --init
   ```

3. **Install Wine prerequisites with winetricks:**

   ```sh
   winetricks dotnet48
   winetricks corefonts
   winetricks vcrun2022
   ```

   - `dotnet48` is only required if a legacy framework-dependent helper
     path is in play — the published `ForgerEMS.exe` ships a self-contained
     .NET 8 runtime and does not require it. Install only if you hit a
     missing-runtime error.
   - `corefonts` keeps WPF text rendering looking correct.
   - `vcrun2022` provides Visual C++ 2015-2022 redistributables that some
     WPF rendering paths require.

## Launch

Recommended:

```sh
cd /path/to/release/current/app
WINEDEBUG=-all wine ForgerEMS.exe
```

If startup still fails, try the diagnostic launch:

```sh
WINEDEBUG=warn+seh,err+all \
WINEDLLOVERRIDES="dwrite=n,b;dxgi=n,b" \
wine ForgerEMS.exe
```

- `WINEDEBUG=-all` silences Wine's verbose channel logs.
- `WINEDLLOVERRIDES="dwrite=n,b;dxgi=n,b"` prefers Wine's built-in DLLs
  over native ones installed by other tools — important when another app
  has previously installed `dwrite.dll` into the prefix.

ForgerEMS detects Wine on startup and forces WPF into `SoftwareOnly`
render mode automatically; you do not have to pass any extra flag for
that.

## Where logs and diagnostics live

Inside the Wine prefix:

- Startup log:
  `~/.wine-forgerems/drive_c/users/<you>/AppData/Local/ForgerEMS/logs/startup.log`
- Crash dump (only on failure):
  `~/.wine-forgerems/drive_c/users/<you>/AppData/Local/ForgerEMS/Runtime/diagnostics/startup-crash.txt`

Look for these lines in `startup.log` when filing a bug:

```
Compatibility.Platform: WindowsUnderWine
Compatibility.IsWine: True
Compatibility.IsCompatibilityMode: True
Compatibility.ForceSoftwareRendering: True
RenderMode forced to SoftwareOnly (Wine compatibility)
LinuxHelper.Availability: Available | ScriptMissing | ShellUnavailable | TimedOut | Failed | ParseError | UnsupportedSchema
```

## Linux helper

`tools/linux/forgerems-linux-helper.sh` emits a JSON snapshot for the app:

```sh
tools/linux/forgerems-linux-helper.sh -o /tmp/forgerems-host.json
```

- Schema: `forgerems-linux-helper/1`.
- Read-only. Never writes to `/sys`, `/proc`, or any block device.
- Degrades silently when `lsblk` / `blkid` / `udevadm` / `smartctl` are
  missing; their availability is reported in `tools_available`.
- Invoked automatically by ForgerEMS under compatibility mode after the
  window is shown; failure paths are logged but never crash the app.

## Reporting a crash on Wine

Please include:

1. Distro and version (`cat /etc/os-release` or `lsb_release -a`).
2. `wine --version`.
3. The first ~200 lines of
   `drive_c/users/<you>/AppData/Local/ForgerEMS/logs/startup.log`.
4. Terminal output (entire `wine ForgerEMS.exe` invocation).
5. The output of `tools/linux/forgerems-linux-helper.sh` (read-only).
6. Whether the Wine crash dialog appeared and, if so, its full text.

Email the bundle to the address in `docs/PRIVACY.md` or attach it to the
GitHub issue. Do not include personal files from the prefix — the
`startup.log` only contains paths and signal names, never WINEPREFIX
values.

## Troubleshooting

| Symptom | Likely cause | Workaround |
|---|---|---|
| App crashes at startup with `wpfgfx_cor3` / `wined3d` in the log | Hardware rendering attempted | Should not happen — ForgerEMS forces `SoftwareOnly`. Send `startup.log` if it recurs. |
| Blank window for several seconds at launch | First-run JIT under software rendering | Wait. Subsequent launches are faster. |
| Banner present but the LinuxHelper line says "Available" with 0 removable devices | Helper ran but no USB plugged in | Plug a USB stick and re-launch. |
| Banner present but LinuxHelper line says "ScriptMissing" | Running from an installed `.exe` outside the repo tree | Copy `tools/linux/forgerems-linux-helper.sh` next to the executable's `tools/linux` folder. |
| USB Builder action buttons stay grey | Compatibility mode disables targeted USB actions | Expected. Use native Windows for USB writing. |
| Sensor card empty | LibreHardwareMonitor gated off | Expected. Use the host distro's `sensors` / `nvme smart-log`. |

## Smoke test commands (Nobara 43, primary target)

```sh
cd /path/to/release/current/app

# Normal launch:
WINEDEBUG=-all wine ForgerEMS.exe

# If that crashes:
WINEDEBUG=warn+seh,err+all \
WINEDLLOVERRIDES="dwrite=n,b;dxgi=n,b" \
wine ForgerEMS.exe

# Then collect:
#   - Wine crash dialog text
#   - drive_c/users/<you>/AppData/Local/ForgerEMS/logs/startup.log
#   - Terminal output
#   - cat /etc/os-release
#   - wine --version
```
