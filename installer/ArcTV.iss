; Inno Setup script for the Arc TV installer. Built by .github/workflows/release.yml, which passes the version (/DAppVersion=1.2.3) and the folder
; the app was published to (/DSourceDir=...). The installer is per user (no administrator rights needed) and unsigned: Windows will say the publisher
; is unknown (SmartScreen: "More info" > "Run anyway").

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6B0F3C1E-5D1A-4C57-9E43-A7A1C1F0AC7B}
AppName=Arc TV
AppVersion={#AppVersion}
AppPublisher=Arc TV
DefaultDirName={autopf}\Arc TV
DefaultGroupName=Arc TV
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-out
OutputBaseFilename=ArcTV-Setup-{#AppVersion}
SetupIconFile=..\src\ArcTV.App\Assets\ArcTV.ico
UninstallDisplayIcon={app}\ArcTV.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Arc TV"; Filename: "{app}\ArcTV.exe"
Name: "{autodesktop}\Arc TV"; Filename: "{app}\ArcTV.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ArcTV.exe"; Description: "Start Arc TV"; Flags: nowait postinstall skipifsilent
