; Build through Build-Installer.ps1. AppId and AppMutex must remain stable.
#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-Installer.ps1
#endif
#ifndef AppMutexName
  #error AppMutexName must be supplied by Build-Installer.ps1
#endif
#define AppName "IslandDesk"
#define AppExe "IslandDesk.exe"

[Setup]
AppId={{9BBE998F-0584-4868-AFE8-2ED902A95A75}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=IslandDesk
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=IslandDesk Setup
DefaultDirName={localappdata}\Programs\IslandDesk
DefaultGroupName=IslandDesk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern dynamic
SetupIconFile=..\Assets\IslandDesk.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=IslandDesk
AppMutex={#AppMutexName}
SetupMutex=Local\IslandDesk.Setup.9BBE998F-0584-4868-AFE8-2ED902A95A75
CloseApplications=no
RestartApplications=no
OutputDir=..\dist
#ifdef OnlineInstaller
OutputBaseFilename=IslandDesk-Setup-Online
#else
OutputBaseFilename=IslandDesk-Setup
#endif
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppRunningError=IslandDesk is currently running.%n%nRight-click its system tray icon and choose Exit to save your preferences and close it safely. Then click OK to continue, or Cancel to exit Setup.
UninstallAppRunningError=IslandDesk is currently running.%n%nRight-click its system tray icon and choose Exit. Then click OK to continue, or Cancel to exit Uninstall.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
#ifdef OnlineInstaller
Source: "Verify-Runtime.ps1"; Flags: dontcopy
#endif
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\IslandDesk.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IslandDesk"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\IslandDesk"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\IslandDesk\Deployment"; ValueType: string; ValueName: "ExecutablePath"; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch IslandDesk"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunchApplication

#ifdef OnlineInstaller
[InstallDelete]
; Only old runtime-pack assets identified by the self-contained .deps.json.
; Never a wildcard, never app-specific WinRT dependencies or user preferences.
#include CleanupFile
#endif

[Code]
#ifdef OnlineInstaller
#include "DotNetRuntime.iss"
#endif
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function CanLaunchApplication: Boolean;
begin
#ifdef OnlineInstaller
  Result := not RuntimeRestartNeeded;
#else
  Result := True;
#endif
end;

#ifdef OnlineInstaller
var
  CleanupChecked, CleanupRequired: Boolean;

function MigratingSelfContainedInstallation: Boolean;
var
  RegisteredExe: String;
  RuntimeConfig: AnsiString;
begin
  if not CleanupChecked then
  begin
    CleanupChecked := True;
    CleanupRequired := RegQueryStringValue(HKCU, 'Software\IslandDesk\Deployment', 'ExecutablePath', RegisteredExe) and
      (CompareText(RegisteredExe, ExpandConstant('{app}\{#AppExe}')) = 0);
    if CleanupRequired then
      CleanupRequired := LoadStringFromFile(ExpandConstant('{app}\IslandDesk.runtimeconfig.json'), RuntimeConfig);
    if CleanupRequired then
      CleanupRequired := Pos('"includedFrameworks"', RuntimeConfig) > 0;
  end;
  Result := CleanupRequired;
end;
#endif

function IslandDeskRunning: Boolean;
begin
  { The window check also recognizes prototypes predating the instance mutex. }
  Result := CheckForMutexes('{#AppMutexName}') or
    (FindWindowByWindowName('IslandDesk') <> 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Check again at the point of installation, even if the app started after Setup. }
  Result := '';
  if IslandDeskRunning then
  begin
    Result := 'Exit IslandDesk from its system tray menu to save your preferences, then click Retry. No application will be forcibly closed.';
    Exit;
  end;
#ifdef OnlineInstaller
  Result := EnsureDesktopRuntime(NeedsRestart);
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingCommand: String;
begin
  if CurStep = ssPostInstall then
  begin
    { Preserve the startup opt-in, migrating any old development/publish path.
      A new installation without an existing opt-in leaves startup OFF. }
    if RegQueryStringValue(HKCU, RunKey, 'IslandDesk', ExistingCommand) and
       (Trim(ExistingCommand) <> '') then
      if not RegWriteStringValue(HKCU, RunKey, 'IslandDesk',
        '"' + ExpandConstant('{app}\{#AppExe}') + '"') then
        RaiseException('IslandDesk was installed, but its Windows startup entry could not be updated. Turn startup off and on again in Settings.');
  end;
end;

function InitializeUninstall: Boolean;
begin
  { Let Inno's AppMutex dialog offer OK/Cancel retries for current builds.
    Only older prototypes need this fallback before uninstalling. }
  Result := CheckForMutexes('{#AppMutexName}') or
    (FindWindowByWindowName('IslandDesk') = 0);
  if not Result then
    MsgBox('Exit IslandDesk from its system tray menu to save your preferences, then run Uninstall again.',
      mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingCommand, InstalledCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\{#AppExe}') + '"';
    if RegQueryStringValue(HKCU, RunKey, 'IslandDesk', ExistingCommand) then
      if (CompareText(Trim(ExistingCommand), InstalledCommand) = 0) or
         (CompareText(Trim(ExistingCommand), ExpandConstant('{app}\{#AppExe}')) = 0) then
        if not RegDeleteValue(HKCU, RunKey, 'IslandDesk') then
          Log('Could not remove the installed application startup entry.');
  end;
  { Never remove %LocalAppData%\IslandDesk or its preferences.json. }
end;
