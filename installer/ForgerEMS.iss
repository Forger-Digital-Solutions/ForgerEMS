#define MyAppName "ForgerEMS"
#define MyAppPublisher "Forger Digital Solutions"
#define MyAppExeName "ForgerEMS.exe"
#define MyAppId "{{9B46E50F-0EF6-4E37-92BB-13C29D43F20B}"

#ifndef AppVersion
  #error AppVersion must be passed by the build script (sourced from the repository VERSION file).
#endif

#ifndef AppVersionInfo
  #error AppVersionInfo must be passed by the build script (numeric core derived from AppVersion).
#endif

#ifndef ReleaseIdentifier
  #define ReleaseIdentifier "ForgerEMS v" + AppVersion
#endif

#ifndef DisplayVersion
  #define DisplayVersion ReleaseIdentifier
#endif

#define MyAppIconName "ForgerEMS-v" + AppVersion + "-transparent.ico"

#ifndef PublishDir
  #define PublishDir "..\src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish"
#endif

#ifndef BackendBundleDir
  #define BackendBundleDir "..\dist\backend-stage\backend"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

; Signing mode is explicit and mutually exclusive: the build passes exactly one
; of RequireSigning or UnsignedCandidate. Neither = reject, both = reject.
#if defined(RequireSigning) && defined(UnsignedCandidate)
  #error RequireSigning and UnsignedCandidate are mutually exclusive — pass exactly one.
#endif
#if !defined(RequireSigning) && !defined(UnsignedCandidate)
  #error Signing mode must be declared — pass /DRequireSigning or /DUnsignedCandidate from the build script.
#endif

#ifdef RequireSigning
  #ifndef SignedUninstallerDir
    #error SignedUninstallerDir must be passed by the build script (fresh signing directory for the cached signed uninstaller).
  #endif
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#DisplayVersion}
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
LicenseFile=..\installer\ForgerEMS-License.txt
UninstallDisplayIcon={app}\{#MyAppIconName}
VersionInfoVersion={#AppVersionInfo}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersionInfo}
VersionInfoDescription={#ReleaseIdentifier} installer
SetupLogging=yes
MinVersion=10.0.19041
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousLanguage=yes
ChangesAssociations=no
#ifdef RequireSigning
SignTool=ForgerEMSRelease
SignedUninstaller=yes
SignedUninstallerDir="{#SignedUninstallerDir}"
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "deepsensormode"; Description: "Enable Deep Sensor Mode by default (read-only local hardware sensors). Admin Inventory Scan may still ask for Windows UAC approval."; GroupDescription: "ForgerEMS Deep Sensor Mode:"; Flags: unchecked

[Registry]
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorMode"; ValueData: "ReadOnly"; Flags: uninsdeletevalue; Tasks: deepsensormode
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorMode"; ValueData: "Off"; Flags: uninsdeletevalue; Check: IsDeepSensorModeTaskDisabled
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorDisclosure"; ValueData: "ForgerEMS includes LibreHardwareMonitorLib as a bundled local read-only sensor provider under MPL-2.0 with notices. Deep Sensor Mode reads supported hardware sensor data while the app is running or System Intelligence scans execute. Sensor access is local only. ForgerEMS does not control fans, voltage, clocks, BIOS, firmware, overclocking, undervolting, or other hardware-control actions. Some deeper sensor/security checks may ask for Windows administrator approval when you run Admin Inventory Scan; the installer does not grant permanent admin permission."; Flags: uninsdeletevalue

[InstallDelete]
Type: files; Name: "{autodesktop}\ForgerEMS.lnk"
Type: files; Name: "{commondesktop}\ForgerEMS.lnk"
Type: files; Name: "{autoprograms}\ForgerEMS.lnk"
Type: files; Name: "{commonprograms}\ForgerEMS.lnk"
Type: files; Name: "{app}\ForgerEMS.ico"
Type: files; Name: "{app}\ForgerEMS-v*.ico"
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\manifests"
Type: filesandordirs; Name: "{app}\docs"
Type: filesandordirs; Name: "{app}\providers"
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
Source: "{#PublishDir}\LibreHardwareMonitorLib.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\src\ForgerEMS.Wpf\Assets\ForgerEMS.ico"; DestDir: "{app}"; DestName: "{#MyAppIconName}"; Flags: ignoreversion
Source: "{#BackendBundleDir}\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\manifests\*"; DestDir: "{app}\manifests"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\providers\*"; DestDir: "{app}\providers"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\installer\ForgerEMS-Installed-README.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\ABOUT_FORGEREMS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\BETA_ISSUE_REPORT_TEMPLATE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\BETA_TESTER_QUICKSTART.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\DOWNLOAD_TROUBLESHOOTING.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\ENVIRONMENT.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\FAQ.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\FIRST_TESTER_DOWNLOAD_FLOW.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\FORGER-SENSOR-STACK.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\LEGAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\LEGAL_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\LINUX-WINE-COMPATIBILITY.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\PRIVACY_AND_DATA_HANDLING.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\SENSOR-LIMITATIONS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\TERMS_OF_USE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\THIRD-PARTY-SENSOR-NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\UPDATE_SYSTEM.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\USER_CONSENT_FLOW.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\marketing\PUBLIC-FAQ.md"; DestDir: "{app}\docs\marketing"; Flags: ignoreversion
Source: "..\docs\RELEASE_NOTES_v{#AppVersion}.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\SECURITY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIconName}"
Name: "{autodesktop}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIconName}"; Check: ShouldCreateDesktopIcon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ForgerEMS"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  HadDesktopIcon: Boolean;

function UpdateReadyMemo(
  Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DeepSensorSummary: String;
begin
  Result := '';

  if MemoUserInfoInfo <> '' then
    Result := MemoUserInfoInfo;
  if MemoDirInfo <> '' then
  begin
    if Result <> '' then Result := Result + NewLine + NewLine;
    Result := Result + MemoDirInfo;
  end;
  if MemoTypeInfo <> '' then
  begin
    if Result <> '' then Result := Result + NewLine + NewLine;
    Result := Result + MemoTypeInfo;
  end;
  if MemoComponentsInfo <> '' then
  begin
    if Result <> '' then Result := Result + NewLine + NewLine;
    Result := Result + MemoComponentsInfo;
  end;
  if MemoGroupInfo <> '' then
  begin
    if Result <> '' then Result := Result + NewLine + NewLine;
    Result := Result + MemoGroupInfo;
  end;
  if MemoTasksInfo <> '' then
  begin
    if Result <> '' then Result := Result + NewLine + NewLine;
    Result := Result + MemoTasksInfo;
  end;

  if WizardIsTaskSelected('deepsensormode') then
    DeepSensorSummary := 'Deep Sensor Mode: on'
  else
    DeepSensorSummary := 'Deep Sensor Mode: off';

  if Result <> '' then Result := Result + NewLine + NewLine;
  Result := Result +
    'ForgerEMS choices:' + NewLine +
    Space + DeepSensorSummary;
end;

function InitializeSetup(): Boolean;
begin
  HadDesktopIcon :=
    FileExists(ExpandConstant('{autodesktop}\ForgerEMS.lnk')) or
    FileExists(ExpandConstant('{commondesktop}\ForgerEMS.lnk'));
  Result := True;
end;

function ShouldCreateDesktopIcon(): Boolean;
begin
  Result := HadDesktopIcon or WizardIsTaskSelected('desktopicon');
end;

function IsDeepSensorModeTaskDisabled(): Boolean;
begin
  Result := not WizardIsTaskSelected('deepsensormode');
end;

[UninstallDelete]
Type: files; Name: "{app}\ForgerEMS-v*.ico"
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\manifests"
Type: filesandordirs; Name: "{app}\docs"
Type: filesandordirs; Name: "{app}\providers"
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
