# Replacing the bundled LibreHardwareMonitor / PawnIO components

ForgerEMS ships `LibreHardwareMonitorLib.dll` (MPL-2.0; embeds PawnIO
kernel-module binaries under LGPL-2.1) as a **loose, replaceable assembly** — it
is intentionally excluded from single-file bundling so you can rebuild and
replace it. The runtime binds the DLL at the **app root** (`ForgerEMS.exe`
folder). The copy under `providers\sensors\` is the same file kept beside its
notices/licenses for the packaged provider payload; replace **both** copies with
the same build.

## Steps

1. **Close ForgerEMS completely** (no running instance).
2. **Back up the original DLLs** to a separate folder (do not overwrite them —
   keep `LibreHardwareMonitorLib.dll` from the app root and
   `providers\sensors\LibreHardwareMonitorLib.dll`).
3. **Obtain the pinned sources:**
   - LibreHardwareMonitor at commit
     `3d331e3370efb858411f19511373eff65a218701` (tag `0.9.6` upstream):
     https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
   - The bundled PawnIO 0.1.6 modules — source and the compiler toolchain
     (the shipped source ZIP `providers\sensors\SOURCE\PawnIO-Modules-0.1.6-source.zip`
     contains both the upstream compiler **binary** RPM and the **source** RPM).
4. **Build your modified modules** using the upstream build instructions for the
   PawnIO modules, then **build LibreHardwareMonitorLib** with your modified,
   interface-compatible modules embedded.
5. **Replace the DLLs:** copy your rebuilt `LibreHardwareMonitorLib.dll` over
   the app-root copy and the `providers\sensors\` copy.

## What ForgerEMS does and does not enforce

- No version pin, hash check, or signature requirement **in ForgerEMS** blocks a
  replacement DLL. The app loads whatever compatible
  `LibreHardwareMonitorLib.dll` is present at the app root.
- That is a ForgerEMS-side statement only. **Upstream PawnIO module/driver
  trust restrictions are separate**: whether the signed PawnIO kernel driver
  accepts arbitrary modified/unsigned modules is governed by the upstream
  driver, not by ForgerEMS, and is **not tested or promised** here. A rebuilt
  `LibreHardwareMonitorLib.dll` that loads may still fail to operate its
  embedded modules if upstream trust requirements are not met.
- ForgerEMS makes **no certification** of your modified DLL — it is not signed
  by us, is not covered by any ForgerEMS validation, and runs at your own risk.
- This document is a technical corresponding-source/relinking aid, **not legal
  certification**. Nothing here is legal advice or a universal compliance
  determination. The exact scope of your LGPL/MPL obligations for a modified
  build (including corresponding-source availability for the PawnIO modules)
  is your responsibility to review.
