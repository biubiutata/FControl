#define MyAppName "FControl"
#define MyAppPublisher "biubiutata"
#ifndef MyAppVersion
#define MyAppVersion "1.0.2"
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

[Setup]
AppId={{7222A32D-CF3D-4E32-A2B4-FD93E0C8859C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FControl"; Filename: "{app}\FControl.exe"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\FControl"; Filename: "{app}\FControl.exe"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\FControl.exe"; Description: "{cm:LaunchProgram,FControl}"; Flags: nowait postinstall skipifsilent
