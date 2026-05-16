# ForgerEMS Signed Driver Roadmap

The signed-driver path is not part of the current ForgerEMS beta.

A driver would only be considered if user-mode Windows APIs and reviewed bundled read-only providers cannot expose important sensor data safely.

## Requirements Before Any Driver Exists

- Microsoft driver signing and Hardware Developer Program process
- read-only sensor scope only
- no fan control
- no voltage control
- no clock control
- no overclocking or undervolting
- no BIOS or firmware writes
- installer-managed distribution, so users do not download it separately
- clear admin/security disclosure
- safe fallback when driver is absent, blocked, or unavailable

## Current Status

ForgerEMS reports the driver provider as `Not included`.

Missing temperatures, fan RPM, or package power should be shown as coverage limitations, not device failure.

