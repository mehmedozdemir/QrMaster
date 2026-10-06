; Built by CI:  iscc /DAppVersion=1.2.3 /DNumericVersion=1.2.3.0 /DArch=x64 /DSourceDir=..\artifacts\publish\win-x64 installer\QrMaster.iss
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{B8F0C2A4-6B1E-4C58-9D3A-5E2F7A1C9D10}
AppName=QR Master
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=QR Master
DefaultDirName={autopf}\QR Master
DefaultGroupName=QR Master
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts\installer
OutputBaseFilename=QrMaster-Setup-{#AppVersion}-win-{#Arch}
SetupIconFile=..\src\QrMaster\Assets\app.ico
UninstallDisplayIcon={app}\QrMaster.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\QR Master"; Filename: "{app}\QrMaster.exe"
Name: "{autodesktop}\QR Master"; Filename: "{app}\QrMaster.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Run]
; Normal interactive install
Filename: "{app}\QrMaster.exe"; Description: "{cm:LaunchProgram,QR Master}"; Flags: nowait postinstall skipifsilent
; Silent in-app update (the updater passes /LAUNCH=1): relaunch the new version
Filename: "{app}\QrMaster.exe"; Flags: nowait; Check: LaunchAfterUpdate

[Code]
function LaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:LAUNCH|0}') = '1';
end;
