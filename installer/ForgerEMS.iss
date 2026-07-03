#define MyAppName "ForgerEMS"
#define MyAppPublisher "Forger Digital Solutions"
#define MyAppExeName "ForgerEMS.exe"
#define MyAppId "{{9B46E50F-0EF6-4E37-92BB-13C29D43F20B}"

#ifndef AppVersion
  #define AppVersion "1.2.4-preview.1"
#endif

#ifndef AppVersionInfo
  #define AppVersionInfo "1.2.4.0"
#endif

#ifndef ReleaseIdentifier
  #define ReleaseIdentifier "ForgerEMS v1.2.4 Public Preview"
#endif

#ifndef DisplayVersion
  #define DisplayVersion "ForgerEMS v1.2.4 Public Preview"
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
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousLanguage=yes
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "deepsensormode"; Description: "Enable Deep Sensor Mode by default (read-only local hardware sensors). Admin Inventory Scan may still ask for Windows UAC approval."; GroupDescription: "ForgerEMS Deep Sensor Mode:"; Flags: unchecked

[Registry]
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorMode"; ValueData: "ReadOnly"; Flags: uninsdeletevalue; Tasks: deepsensormode
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorMode"; ValueData: "Off"; Flags: uninsdeletevalue; Check: IsDeepSensorModeTaskDisabled
Root: HKLM; Subkey: "Software\ForgerEMS"; ValueType: string; ValueName: "DeepSensorDisclosure"; ValueData: "ForgerEMS includes LibreHardwareMonitorLib as a bundled local read-only sensor provider under MPL-2.0 with notices. Deep Sensor Mode reads supported hardware sensor data while the app is running or System Intelligence scans execute. Sensor access is local only. ForgerEMS does not control fans, voltage, clocks, BIOS, firmware, overclocking, undervolting, or hardware writes. Some deeper sensor/security checks may ask for Windows administrator approval when you run Admin Inventory Scan; the installer does not grant permanent admin permission."; Flags: uninsdeletevalue

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
Source: "..\src\ForgerEMS.Wpf\Assets\ForgerEMS.ico"; DestDir: "{app}"; DestName: "{#MyAppIconName}"; Flags: ignoreversion
Source: "{#BackendBundleDir}\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\manifests\*"; DestDir: "{app}\manifests"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\providers\*"; DestDir: "{app}\providers"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\installer\ForgerEMS-Installed-README.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\ABOUT_FORGEREMS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\FAQ.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\TERMS_OF_USE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\PRIVACY_AND_DATA_HANDLING.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\LEGAL_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\USER_CONSENT_FLOW.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\RELEASE_NOTES_v1.2.4-preview.1.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIconName}"
Name: "{autodesktop}\ForgerEMS"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppIconName}"; Check: ShouldCreateDesktopIcon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ForgerEMS"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  HadDesktopIcon: Boolean;
  KyraWizardPage: TWizardPage;
  KyraIntroA: TNewStaticText;
  KyraIntroB: TNewStaticText;
  KyraIntroC: TNewStaticText;
  KyraLocalOnlyText: TNewStaticText;
  KyraOptionalText: TNewStaticText;
  KyraPreviewButton: TNewButton;
  KyraRepairChk: TNewCheckBox;
  KyraHwChk: TNewCheckBox;
  KyraResolvedChk: TNewCheckBox;
  KyraCrashChk: TNewCheckBox;

procedure KyraPreviewButtonClick(Sender: TObject);
begin
  MsgBox(
    'Preview only. If you opt in, ForgerEMS prepares anonymous categories only:' + #13#10 + #13#10 +
    '- issue/warning category' + #13#10 +
    '- broad hardware/performance pattern category' + #13#10 +
    '- resolved issue/fix category' + #13#10 +
    '- crash/error category, if selected' + #13#10 + #13#10 +
    'Never shared: API keys, gateway tokens, passwords, product keys, serial numbers, private files, full paths, emails, IP addresses, exact location, or raw logs.',
    mbInformation,
    MB_OK);
end;

procedure BuildKyraIntelligenceWizardPage;
var
  Y: Integer;
begin
  KyraWizardPage := CreateCustomPage(wpSelectTasks,
    'Help Kyra get smarter for technicians',
    '');
  KyraIntroA := TNewStaticText.Create(KyraWizardPage);
  KyraIntroA.Parent := KyraWizardPage.Surface;
  KyraIntroA.Caption :=
    'Kyra can remember local repair notes on this PC. Anonymous community sharing is optional and off by default.';
  KyraIntroA.Left := 0;
  KyraIntroA.Top := 0;
  KyraIntroA.Width := KyraWizardPage.SurfaceWidth;
  KyraIntroA.Height := ScaleY(28);
  KyraIntroA.WordWrap := True;
  Y := KyraIntroA.Top + KyraIntroA.Height + ScaleY(3);

  KyraIntroB := TNewStaticText.Create(KyraWizardPage);
  KyraIntroB.Parent := KyraWizardPage.Surface;
  KyraIntroB.Caption :=
    'Realtime Gateway Research sends only sanitized context. ForgerEMS never shares API keys, tokens, passwords, product keys, serials, private files, paths, emails, IPs, location, or raw logs.';
  KyraIntroB.Left := 0;
  KyraIntroB.Top := Y;
  KyraIntroB.Width := KyraWizardPage.SurfaceWidth;
  KyraIntroB.Height := ScaleY(40);
  KyraIntroB.WordWrap := True;
  Y := KyraIntroB.Top + KyraIntroB.Height + ScaleY(4);

  KyraIntroC := TNewStaticText.Create(KyraWizardPage);
  KyraIntroC.Parent := KyraWizardPage.Surface;
  KyraIntroC.Caption := 'Keep Local Only (default): leave every box unchecked and continue.';
  KyraIntroC.Left := 0;
  KyraIntroC.Top := Y;
  KyraIntroC.Width := KyraWizardPage.SurfaceWidth;
  KyraIntroC.Height := ScaleY(16);
  KyraIntroC.WordWrap := True;
  Y := KyraIntroC.Top + KyraIntroC.Height + ScaleY(3);

  KyraLocalOnlyText := TNewStaticText.Create(KyraWizardPage);
  KyraLocalOnlyText.Parent := KyraWizardPage.Surface;
  KyraLocalOnlyText.Caption := 'Help Improve Kyra (optional):';
  KyraLocalOnlyText.Left := 0;
  KyraLocalOnlyText.Top := Y;
  KyraLocalOnlyText.Width := KyraWizardPage.SurfaceWidth;
  KyraLocalOnlyText.Height := ScaleY(15);
  KyraLocalOnlyText.WordWrap := True;
  Y := KyraLocalOnlyText.Top + KyraLocalOnlyText.Height + ScaleY(2);

  KyraRepairChk := TNewCheckBox.Create(KyraWizardPage);
  KyraRepairChk.Parent := KyraWizardPage.Surface;
  KyraRepairChk.Caption := 'Help improve Kyra with anonymous repair intelligence';
  KyraRepairChk.Left := 0;
  KyraRepairChk.Top := Y;
  KyraRepairChk.Width := KyraWizardPage.SurfaceWidth;
  KyraRepairChk.Height := ScaleY(16);
  KyraRepairChk.Checked := False;
  Y := KyraRepairChk.Top + KyraRepairChk.Height + ScaleY(1);

  KyraHwChk := TNewCheckBox.Create(KyraWizardPage);
  KyraHwChk.Parent := KyraWizardPage.Surface;
  KyraHwChk.Caption := 'Share anonymous hardware/performance patterns';
  KyraHwChk.Left := 0;
  KyraHwChk.Top := Y;
  KyraHwChk.Width := KyraWizardPage.SurfaceWidth;
  KyraHwChk.Height := ScaleY(16);
  KyraHwChk.Checked := False;
  Y := KyraHwChk.Top + KyraHwChk.Height + ScaleY(1);

  KyraResolvedChk := TNewCheckBox.Create(KyraWizardPage);
  KyraResolvedChk.Parent := KyraWizardPage.Surface;
  KyraResolvedChk.Caption := 'Share resolved issue/fix categories';
  KyraResolvedChk.Left := 0;
  KyraResolvedChk.Top := Y;
  KyraResolvedChk.Width := KyraWizardPage.SurfaceWidth;
  KyraResolvedChk.Height := ScaleY(16);
  KyraResolvedChk.Checked := False;
  Y := KyraResolvedChk.Top + KyraResolvedChk.Height + ScaleY(1);

  KyraCrashChk := TNewCheckBox.Create(KyraWizardPage);
  KyraCrashChk.Parent := KyraWizardPage.Surface;
  KyraCrashChk.Caption := 'Share crash/error diagnostics';
  KyraCrashChk.Left := 0;
  KyraCrashChk.Top := Y;
  KyraCrashChk.Width := KyraWizardPage.SurfaceWidth;
  KyraCrashChk.Height := ScaleY(16);
  KyraCrashChk.Checked := False;
  Y := KyraCrashChk.Top + KyraCrashChk.Height + ScaleY(5);

  KyraPreviewButton := TNewButton.Create(KyraWizardPage);
  KyraPreviewButton.Parent := KyraWizardPage.Surface;
  KyraPreviewButton.Caption := 'View What Would Be Shared';
  KyraPreviewButton.Left := 0;
  KyraPreviewButton.Top := Y;
  KyraPreviewButton.Width := ScaleX(180);
  KyraPreviewButton.Height := ScaleY(22);
  KyraPreviewButton.OnClick := @KyraPreviewButtonClick;
  KyraOptionalText := TNewStaticText.Create(KyraWizardPage);
  KyraOptionalText.Parent := KyraWizardPage.Surface;
  KyraOptionalText.Caption := 'You can change these choices later in Settings.';
  KyraOptionalText.Left := KyraPreviewButton.Left + KyraPreviewButton.Width + ScaleX(10);
  KyraOptionalText.Top := Y + ScaleY(4);
  KyraOptionalText.Width := KyraWizardPage.SurfaceWidth - KyraOptionalText.Left;
  KyraOptionalText.Height := ScaleY(18);
  KyraOptionalText.WordWrap := True;
end;

function UpdateReadyMemo(
  Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DeepSensorSummary: String;
  KyraSummary: String;
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

  if KyraRepairChk.Checked or KyraHwChk.Checked or KyraResolvedChk.Checked or KyraCrashChk.Checked then
    KyraSummary := 'Kyra Community Intelligence: optional sharing selected'
  else
    KyraSummary := 'Kyra Community Intelligence: local-only';

  if Result <> '' then Result := Result + NewLine + NewLine;
  Result := Result +
    'ForgerEMS choices:' + NewLine +
    Space + DeepSensorSummary + NewLine +
    Space + KyraSummary;
end;

procedure WriteKyraInstallerRegistryDefaults;
var
  R, H, Res, C: Cardinal;
begin
  if WizardSilent() then
  begin
    R := 0;
    H := 0;
    Res := 0;
    C := 0;
  end
  else
  begin
    if KyraRepairChk.Checked then R := 1 else R := 0;
    if KyraHwChk.Checked then H := 1 else H := 0;
    if KyraResolvedChk.Checked then Res := 1 else Res := 0;
    if KyraCrashChk.Checked then C := 1 else C := 0;
  end;
  RegWriteDWordValue(HKLM, 'Software\ForgerEMS', 'KyraShareRepairIntelligence', R);
  RegWriteDWordValue(HKLM, 'Software\ForgerEMS', 'KyraShareHardwarePatterns', H);
  RegWriteDWordValue(HKLM, 'Software\ForgerEMS', 'KyraShareResolvedCategories', Res);
  RegWriteDWordValue(HKLM, 'Software\ForgerEMS', 'KyraShareCrashDiagnostics', C);
end;

procedure DeleteKyraInstallerRegistryDefaults;
begin
  RegDeleteValue(HKLM, 'Software\ForgerEMS', 'KyraShareRepairIntelligence');
  RegDeleteValue(HKLM, 'Software\ForgerEMS', 'KyraShareHardwarePatterns');
  RegDeleteValue(HKLM, 'Software\ForgerEMS', 'KyraShareResolvedCategories');
  RegDeleteValue(HKLM, 'Software\ForgerEMS', 'KyraShareCrashDiagnostics');
end;

function InitializeSetup(): Boolean;
begin
  HadDesktopIcon :=
    FileExists(ExpandConstant('{autodesktop}\ForgerEMS.lnk')) or
    FileExists(ExpandConstant('{commondesktop}\ForgerEMS.lnk'));
  Result := True;
end;

procedure InitializeWizard;
begin
  BuildKyraIntelligenceWizardPage;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteKyraInstallerRegistryDefaults;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    DeleteKyraInstallerRegistryDefaults;
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
