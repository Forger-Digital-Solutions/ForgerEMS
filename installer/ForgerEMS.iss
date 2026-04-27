#define MyAppName "ForgerEMS"
#define MyAppPublisher "Forger Digital Solutions"
#define MyAppExeName "ForgerEMS.exe"
#define MyAppId "{{9B46E50F-0EF6-4E37-92BB-13C29D43F20B}"

#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif

#ifndef AppVersionInfo
  #define AppVersionInfo "1.0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish"
#endif

#ifndef BackendBundleDir
  #define BackendBundleDir "..\dist\backend-stage\backend"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ForgerEMS
DefaultGroupName=ForgerEMS
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=ForgerEMS-Setup-v{#AppVersion}
SetupIconFile=..\src\ForgerEMS.Wpf\Assets\ForgerEMS.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#AppVersionInfo}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersionInfo}
VersionInfoDescription={#MyAppName} installer
SetupLogging=yes
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousLanguage=yes
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\manifests"
Type: filesandordirs; Name: "{app}\docs"
Type: files; Name: "{app}\Verify-VentoyCore.ps1"
Type: files; Name: "{app}\Setup-ForgerEMS.ps1"
Type: files; Name: "{app}\Update-ForgerEMS.ps1"
Type: files; Name: "{app}\ForgerEMS.Runtime.ps1"
Type: files; Name: "{app}\Setup_Toolkit.ps1"
Type: files; Name: "{app}\Setup_USB_Toolkit.ps1"
Type: files; Name: "{app}\ForgerEMS.updates.json"
Type: files; Name: "{app}\VERSION.txt"
Type: files; Name: "{app}\RELEASE-BUNDLE.txt"
Type: files; Name: "{app}\CHECKSUMS.sha256"
Type: files; Name: "{app}\SIGNATURE.txt"

[Files]
Source: "{#PublishDir}\ForgerEMS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\ForgerEMS.Wpf\Assets\ForgerEMS.ico"; DestDir: "{app}"; DestName: "ForgerEMS.ico"; Flags: ignoreversion
Source: "{#BackendBundleDir}\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\manifests\*"; DestDir: "{app}\manifests"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\installer\ForgerEMS-Installed-README.txt"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\ForgerEMS.ico"
Name: "{autodesktop}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\ForgerEMS.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ForgerEMS"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\manifests"
Type: filesandordirs; Name: "{app}\docs"
Type: files; Name: "{app}\Verify-VentoyCore.ps1"
Type: files; Name: "{app}\Setup-ForgerEMS.ps1"
Type: files; Name: "{app}\Update-ForgerEMS.ps1"
Type: files; Name: "{app}\ForgerEMS.Runtime.ps1"
Type: files; Name: "{app}\Setup_Toolkit.ps1"
Type: files; Name: "{app}\Setup_USB_Toolkit.ps1"
Type: files; Name: "{app}\ForgerEMS.updates.json"
Type: files; Name: "{app}\VERSION.txt"
Type: files; Name: "{app}\RELEASE-BUNDLE.txt"
Type: files; Name: "{app}\CHECKSUMS.sha256"
Type: files; Name: "{app}\SIGNATURE.txt"
