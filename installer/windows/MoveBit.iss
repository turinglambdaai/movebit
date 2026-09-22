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
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/turinglambdaai/movebit/issues
AppUpdatesURL=https://github.com/turinglambdaai/movebit/releases/latest
AppComments=Healthy work rhythm companion
DefaultDirName={localappdata}\Programs\MoveBit
DefaultGroupName=MoveBit
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist\installer
OutputBaseFilename=MoveBit-Setup-windows-x64
SetupIconFile=..\..\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=MoveBit
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

; Modern Windows-native presentation. Inno Setup 6.6+ follows the system's
; light/dark mode; 6.7+ supports the warm MoveBit background colors below.
WizardStyle=modern dynamic windows11 hidebevels
WizardSizePercent=115
WizardResizable=no
WizardBackColor=#FFF8F0
WizardBackColorDynamicDark=#17130F
WizardSmallImageFile=..\..\Assets\icon.png
WizardSmallImageBackColor=#FFF8F0
WizardSmallImageBackColorDynamicDark=#17130F

VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=MoveBit Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Messages]
WelcomeLabel1=Welcome to MoveBit
WelcomeLabel2=MoveBit helps you build healthier work rhythms without getting in your way.%n%nSetup installs MoveBit only for your Windows account. No administrator permission is required.
FinishedHeadingLabel=MoveBit is ready
FinishedLabel=MoveBit has been installed successfully.%n%nLaunch it now; closing the main window keeps MoveBit running quietly in the system tray.
BeveledLabel=MoveBit · healthier work rhythms

[Tasks]
Name: "desktopicon"; Description: "Add a shortcut to the desktop"; GroupDescription: "Shortcuts"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MoveBit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MoveBit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MoveBit"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WindowsRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  WindowsRunValue = 'MoveBit';

function InstalledRunCommand(): String;
begin
  Result := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingRunCommand: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  { Preserve an existing autostart preference from a portable copy, but migrate
    its executable path to the stable installer directory. Do not enable autostart
    for users who had never opted in. }
  if RegQueryStringValue(HKCU, WindowsRunKey, WindowsRunValue, ExistingRunCommand) then
    RegWriteStringValue(HKCU, WindowsRunKey, WindowsRunValue, InstalledRunCommand());
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingRunCommand: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  { Remove only the startup entry that still belongs to this installed copy.
    If the user later pointed MoveBit at another portable copy, leave it alone. }
  if RegQueryStringValue(HKCU, WindowsRunKey, WindowsRunValue, ExistingRunCommand) and
     (CompareText(ExistingRunCommand, InstalledRunCommand()) = 0) then
    RegDeleteValue(HKCU, WindowsRunKey, WindowsRunValue);
end;
