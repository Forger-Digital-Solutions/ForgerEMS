#define MyAppName "ForgerEMS"
#define MyAppPublisher "Forger Digital Solutions"
#define MyAppExeName "ForgerEMS.exe"
#define MyAppId "{{9B46E50F-0EF6-4E37-92BB-13C29D43F20B}"

#ifndef AppVersion
  #define AppVersion "1.0.0"
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
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersion}
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

[Files]
Source: "{#PublishDir}\ForgerEMS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BackendBundleDir}\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\installer\ForgerEMS-Installed-README.txt"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ForgerEMS"; Flags: nowait postinstall skipifsilent unchecked
