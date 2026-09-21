#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif

#define MyAppName "MoveBit"
#define MyAppPublisher "turinglambdaai"
#define MyAppURL "https://movebit.jrtx.site/"
#define MyAppExeName "MoveBit.exe"

[Setup]
AppId={{E4B9E01D-65D0-4C25-BCF1-3CC22B8C9E02}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/turinglambdaai/movebit/issues
AppUpdatesURL=https://github.com/turinglambdaai/movebit/releases/latest
DefaultDirName={localappdata}\Programs\MoveBit
DefaultGroupName=MoveBit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist\installer
OutputBaseFilename=MoveBit-Setup-windows-x64
SetupIconFile=..\..\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=MoveBit Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MoveBit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MoveBit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MoveBit"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
