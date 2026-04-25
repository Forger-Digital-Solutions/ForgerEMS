=====================================================
                FORGEREMS TECHBENCH USB
=====================================================

Owner:   Not set
Created: 2026-03-10
Purpose: System repair, recovery, diagnostics, OS install, portable bench tools
Status:  Verify after major updates

BOOT (Ventoy)
-------------
D:\ISO\Windows -> Windows 10/11 ISOs + WinPE tools
D:\ISO\Linux   -> Ubuntu, Mint, Kali, Fedora, Endless OS, SystemRescue
D:\ISO\Tools   -> Clonezilla, Rescuezilla, GParted, MemTest, Hiren's PE, UBCD

PORTABLE APPS (Run in Windows / WinPE)
--------------------------------------
D:\Tools\Portable\Disk
D:\Tools\Portable\Hardware
D:\Tools\Portable\Network
D:\Tools\Portable\System
D:\Tools\Portable\Remote
D:\Tools\Portable\USB
D:\Tools\Portable\GPU
D:\Tools\Portable\Security

DRIVERS
-------
D:\Drivers\Audio
D:\Drivers\Bluetooth
D:\Drivers\Chipset
D:\Drivers\Graphics
D:\Drivers\Input
D:\Drivers\Network
D:\Drivers\Storage
D:\Drivers\Wireless

WORKING FOLDERS
---------------
D:\_logs
D:\_reports
D:\_downloads
D:\_archive

MANAGED VS MANUAL
-----------------
Managed/updateable by Update-ForgerEMS.ps1:
- items listed in ForgerEMS.updates.json
- generated DOWNLOAD/INFO shortcuts
- _downloads, _archive, and _logs workflow data

Catalog split:
- active managed autodownload -> enabled manifest-managed file items
- disabled but eligible -> disabled manifest-managed file items
- info / placeholder / manual / review-first -> manifest-managed page shortcuts
- see DOWNLOAD-CATALOG.txt for the current bucket list
- see MANAGED-DOWNLOAD-MAINTENANCE.txt for revalidation,
  fragility ranking, and fallback rules

Manual/unmanaged unless you add a source workflow later:
- portable third-party tools copied into Tools\Portable
- offline driver bundles under Drivers
- MediCat.USB

NOTES
-----
- Install Ventoy first with Ventoy2Disk.
- Copy ISO files into the matching ISO folders.
- Setup-ForgerEMS.ps1 now auto-starts the managed autodownload pass unless you use -LayoutOnly.
- Setup launches managed downloads in the background by default.
- Use -WaitForManagedDownloads if you want Setup to stay attached until downloads finish.
- Use the remaining DOWNLOAD shortcuts for placeholder/manual/review-first items.
- Managed autodownload still depends on upstream availability.
- Revalidate managed downloads before rebuild/shipping:
  .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads
- Review the latest summary at:
  .\.verify\managed-download-revalidation\latest\managed-download-summary.txt
- MediCat is folder-based; place it at root if you use it.
- Run Update-ForgerEMS.ps1 later to refresh manifest-managed items on demand.
- Portable apps and driver bundles remain manual unless a maintained source/update workflow is added.
- Re-running this script is safe.
=====================================================
