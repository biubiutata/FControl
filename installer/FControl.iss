#define MyAppName "FControl"
#define MyAppPublisher "biubiutata"
#ifndef MyAppVersion
#define MyAppVersion "1.0.6"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\installer-publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif
#ifndef ArchName
#define ArchName "win-x64"
#endif
#define AppMutexName "FControl-7222A32D-CF3D-4E32-A2B4-FD93E0C8859C"

[Setup]
AppId={{7222A32D-CF3D-4E32-A2B4-FD93E0C8859C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={code:GetDefaultDirName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\FControl.exe
SetupIconFile=..\Assets\AppIcon.ico
OutputDir={#OutputDir}
OutputBaseFilename=FControl-{#MyAppVersion}-{#ArchName}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
#ifdef ArchitecturesAllowed
ArchitecturesAllowed={#ArchitecturesAllowed}
#endif
#ifdef ArchitecturesInstallIn64BitMode
ArchitecturesInstallIn64BitMode={#ArchitecturesInstallIn64BitMode}
#endif
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
AppMutex={#AppMutexName}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FControl"; Filename: "{app}\FControl.exe"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\FControl"; Filename: "{app}\FControl.exe"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\FControl.exe"; Description: "{cm:LaunchProgram,FControl}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKA; Subkey: "Software\biubiutata\FControl"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Code]
const
  AppRegKey = 'Software\biubiutata\FControl';
  AppRegValue = 'InstallPath';
  AppUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7222A32D-CF3D-4E32-A2B4-FD93E0C8859C}_is1';

var
  SavedInstallPath: string;

function NormalizeInstallPath(Value: string): string;
var
  EndQuote: Integer;
begin
  Result := Trim(Value);
  if (Length(Result) > 1) and (Result[1] = '"') then
  begin
    Delete(Result, 1, 1);
    EndQuote := Pos('"', Result);
    if EndQuote > 0 then
      Result := Copy(Result, 1, EndQuote - 1);
  end;

  if FileExists(Result) then
    Result := ExtractFileDir(Result);
end;

function TryUseInstallPath(Value: string; var InstallPath: string): Boolean;
var
  NormalizedPath: string;
begin
  Result := False;
  NormalizedPath := NormalizeInstallPath(Value);
  if (NormalizedPath <> '') and DirExists(NormalizedPath) and FileExists(AddBackslash(NormalizedPath) + 'FControl.exe') then
  begin
    InstallPath := NormalizedPath;
    Result := True;
  end;
end;

function TryGetInstallPathFromRoot(RootKey: Integer; var InstallPath: string): Boolean;
var
  Value: string;
begin
  Result := False;

  if RegQueryStringValue(RootKey, AppRegKey, AppRegValue, Value) and TryUseInstallPath(Value, InstallPath) then
  begin
    Result := True;
    Exit;
  end;

  if RegQueryStringValue(RootKey, AppUninstallKey, 'InstallLocation', Value) and TryUseInstallPath(Value, InstallPath) then
  begin
    Result := True;
    Exit;
  end;

  if RegQueryStringValue(RootKey, AppUninstallKey, 'Inno Setup: App Path', Value) and TryUseInstallPath(Value, InstallPath) then
  begin
    Result := True;
    Exit;
  end;

  if RegQueryStringValue(RootKey, AppUninstallKey, 'UninstallString', Value) and TryUseInstallPath(Value, InstallPath) then
  begin
    Result := True;
    Exit;
  end;
end;

function GetExistingInstallPath: string;
begin
  Result := '';
  if TryGetInstallPathFromRoot(HKLM, Result) then
    Exit;

  if TryGetInstallPathFromRoot(HKCU, Result) then
    Exit;
end;

function InitializeSetup: Boolean;
begin
  SavedInstallPath := GetExistingInstallPath;
  Result := True;
end;

function GetDefaultDirName(Param: string): string;
begin
  Result := SavedInstallPath;
  if Result = '' then
    Result := ExpandConstant('{autopf}\{#MyAppName}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    DeleteFile(ExpandConstant('{autodesktop}\FControl.lnk'));
    DeleteFile(ExpandConstant('{commondesktop}\FControl.lnk'));
  end;
end;

procedure InitializeWizard;
begin
  if SavedInstallPath <> '' then
    WizardForm.DirEdit.Text := SavedInstallPath;
end;
