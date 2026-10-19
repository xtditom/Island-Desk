{ Included only in the online installer. No code runs inside IslandDesk. }
var
  RuntimeDownload: TDownloadWizardPage;
  RuntimeRestartNeeded: Boolean;
  RuntimeInstallerName, RuntimeUrl, RuntimeSha256, RuntimeSha512: String;
  MinimumDesktopVersion, MinimumCoreVersion: String;

procedure ConfigureRuntime;
begin
  RuntimeInstallerName := 'windowsdesktop-runtime-{#DesktopRuntimeVersion}-win-' + SelectedArchitecture + '.exe';
  { Generated from the validated per-architecture Microsoft runtime lock. }
#include RuntimeDefinitions
end;

function IsStableCompatibleVersion(const Value, Minimum: String): Boolean;
var
  Version, RequiredVersion: Int64;
begin
  { This product targets the stable 10.0 Desktop Runtime servicing line, not
    preview builds, .NET 9, or .NET 11. Minimum comes from runtimeconfig.json. }
  Result := (Copy(Value, 1, 5) = '10.0.') and (Pos('-', Value) = 0) and
    StrToVersion(Value, Version) and StrToVersion(Minimum, RequiredVersion);
  if Result then
    Result := ComparePackedVersion(Version, RequiredVersion) >= 0;
end;

function IsCompatibleHost(const FileName: String): Boolean;
var
  Data: AnsiString;
  Offset, Machine, ExpectedMachine: Integer;
begin
  Result := False;
  if not LoadStringFromFile(FileName, Data) then Exit;
  if Length(Data) < 64 then Exit;
  if (Data[1] <> 'M') or (Data[2] <> 'Z') then Exit;
  { Reject unreasonable offsets before accessing the PE header. }
  if (Ord(Data[63]) <> 0) or (Ord(Data[64]) <> 0) then Exit;
  Offset := Ord(Data[61]) + Ord(Data[62]) * 256;
  if (Offset < 64) or (Offset + 6 > Length(Data)) then Exit;
  if SelectedArchitecture = 'arm64' then ExpectedMachine := $AA64
  else if SelectedArchitecture = 'x64' then ExpectedMachine := $8664
  else ExpectedMachine := $014C;
  Machine := Ord(Data[Offset + 5]) + Ord(Data[Offset + 6]) * 256;
  Result := (Data[Offset + 1] = 'P') and (Data[Offset + 2] = 'E') and
    (Ord(Data[Offset + 3]) = 0) and (Ord(Data[Offset + 4]) = 0) and (Machine = ExpectedMachine);
end;

function FindSharedFramework(const Root, Framework, Minimum, RequiredFile: String;
  var SelectedVersion: String): Boolean;
var
  FindRec: TFindRec;
  FrameworkRoot: String;
  CandidateVersion, BestVersion: Int64;
begin
  Result := False;
  SelectedVersion := '';
  BestVersion := 0;
  FrameworkRoot := AddBackslash(Root) + 'shared\' + Framework + '\';
  if FindFirst(FrameworkRoot + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
          IsStableCompatibleVersion(FindRec.Name, Minimum) and
          FileExists(FrameworkRoot + FindRec.Name + '\' + RequiredFile) then
        begin
          StrToVersion(FindRec.Name, CandidateVersion);
          if (not Result) or (ComparePackedVersion(CandidateVersion, BestVersion) > 0) then
          begin
            SelectedVersion := FindRec.Name;
            BestVersion := CandidateVersion;
            Result := True;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function DesktopRuntimeInstalled: Boolean;
var
  Root, DesktopVersion, CoreVersion: String;
begin
  { Follow the selected apphost's overrides, then architecture registration.
    A runtime for a different architecture never satisfies this prerequisite. }
  Root := GetEnv('DOTNET_ROOT_' + Uppercase(SelectedArchitecture));
  if (Root = '') and (SelectedArchitecture = 'x86') and IsWin64 then Root := GetEnv('DOTNET_ROOT(x86)');
  if Root = '' then Root := GetEnv('DOTNET_ROOT');
  if Root = '' then
    if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + SelectedArchitecture, 'InstallLocation', Root) then
      if IsWin64 then RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + SelectedArchitecture, 'InstallLocation', Root);
  if Root = '' then
  begin
    if SelectedArchitecture = 'x86' then Root := ExpandConstant('{commonpf32}\dotnet')
    else if (SelectedArchitecture = 'x64') and IsArm64 then Root := ExpandConstant('{commonpf64}\dotnet\x64')
    else Root := ExpandConstant('{commonpf64}\dotnet');
  end;
  Result := IsCompatibleHost(AddBackslash(Root) + 'dotnet.exe') and
    FindSharedFramework(Root, 'Microsoft.WindowsDesktop.App', MinimumDesktopVersion, 'PresentationFramework.dll', DesktopVersion) and
    FindSharedFramework(Root, 'Microsoft.NETCore.App', MinimumCoreVersion, 'coreclr.dll', CoreVersion);
  { Desktop 10.0.x itself requires Core 10.0.x or later in the same servicing
    line. A newer desktop paired with an older Core runtime is incomplete. }
  if Result then Result := IsStableCompatibleVersion(CoreVersion, DesktopVersion) and
    IsCompatibleHost(AddBackslash(Root) + 'shared\Microsoft.NETCore.App\' + CoreVersion + '\coreclr.dll');
  Log('Compatible .NET 10 Desktop Runtime ' + SelectedArchitecture + ' at ' + Root + ': ' + IntToStr(Ord(Result)));
end;

procedure InitializeWizard;
begin
  ConfigureRuntime;
  RuntimeDownload := CreateDownloadPage('Microsoft .NET Desktop Runtime',
    'Downloading the official ' + SelectedArchitecture + ' runtime. The download is verified before it is run.', nil);
  RuntimeDownload.ShowBaseNameInsteadOfUrl := True;
end;

function EnsureDesktopRuntime(var NeedsRestart: Boolean): String;
var
  RuntimePath, VerifyScript, Parameters, VerifyLog, InstallLog, LogDirectory: String;
  Code: Integer;
begin
  Result := '';
  if DesktopRuntimeInstalled then
  begin
    NeedsRestart := RuntimeRestartNeeded;
    Exit; { No download or elevation when present. }
  end;
  Result := 'Microsoft .NET 10 Desktop Runtime ' + SelectedArchitecture + ' is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and retry, or use the corresponding IslandDesk Offline installer.';
  { Unattended installs never consent to a download or elevation implicitly. }
  if WizardSilent then Exit;
  if SuppressibleMsgBox('IslandDesk needs Microsoft .NET 10 Desktop Runtime (' + SelectedArchitecture + ').'#13#10#13#10 +
    'Download and install the official Microsoft runtime now? This shared runtime requires administrator approval. IslandDesk itself will still install for your user only.'#13#10#13#10 +
    'Choose No to stop here without changing IslandDesk files. The offline installer needs no separate runtime.',
    mbConfirmation, MB_YESNO, IDNO) <> IDYES then Exit;

  RuntimePath := ExpandConstant('{tmp}\') + RuntimeInstallerName;
  RuntimeDownload.Clear;
  RuntimeDownload.Add(RuntimeUrl, RuntimeInstallerName, RuntimeSha256);
  RuntimeDownload.Show;
  try
    try
      RuntimeDownload.Download;
    except
      if RuntimeDownload.AbortedByUser then
        Result := 'Runtime download cancelled. No IslandDesk files were changed.'
      else
        Result := 'The Microsoft runtime could not be downloaded or its SHA-256 did not match. Check your connection and retry, install the runtime manually, or use the offline installer.'#13#10 + GetExceptionMessage;
      Exit;
    end;
  finally
    RuntimeDownload.Hide;
  end;

  try
    ExtractTemporaryFile('Verify-Runtime.ps1');
    VerifyScript := ExpandConstant('{tmp}\Verify-Runtime.ps1');
    LogDirectory := ExpandConstant('{localappdata}\IslandDesk\InstallerLogs');
    if not ForceDirectories(LogDirectory) then
      RaiseException('Cannot create the runtime installation log directory.');
    VerifyLog := LogDirectory + '\RuntimeVerification.log';
    InstallLog := LogDirectory + '\DesktopRuntime.log';
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      VerifyScript + '" -Path "' + RuntimePath + '" -ExpectedSha512 "' + RuntimeSha512 + '" -LogPath "' + VerifyLog + '"';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters,
      '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Cannot start Windows PowerShell to verify Microsoft''s signature.');
    if Code <> 0 then
    begin
      DeleteFile(RuntimePath);
      Result := 'Runtime verification failed. Nothing was executed and no IslandDesk files were changed. Check ' + VerifyLog + ' or install the official runtime manually.';
      Exit;
    end;
    { Only the authenticated Microsoft runtime is elevated, never IslandDesk Setup.
      /norestart prevents an unexpected reboot; /passive shows Microsoft progress. }
    Parameters := '/install /passive /norestart /log "' + InstallLog + '"';
    if not ShellExec('runas', RuntimePath, Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
    begin
      Result := 'Runtime installation was cancelled or administrator permission was not available (Windows error ' + IntToStr(Code) + '). Retry, ask an administrator to install the runtime, or use the offline version.';
      Exit;
    end;
    if (Code <> 0) and (Code <> 3010) then
    begin
      Result := 'Microsoft runtime installation failed or was cancelled (exit code ' + IntToStr(Code) + '). See ' + InstallLog + '. No IslandDesk files were changed.';
      Exit;
    end;
    RuntimeRestartNeeded := Code = 3010;
    NeedsRestart := RuntimeRestartNeeded;
    if not DesktopRuntimeInstalled then
    begin
      if RuntimeRestartNeeded then
        Result := 'Restart Windows to complete the runtime installation, then run Setup again.'
      else
        Result := 'A compatible ' + SelectedArchitecture + ' Desktop Runtime is still unavailable. Check the runtime log and DOTNET_ROOT overrides, then restart Setup.';
      Exit;
    end;
    Result := '';
  except
    Result := 'The runtime prerequisite could not be prepared safely. No IslandDesk files were changed.'#13#10 + GetExceptionMessage;
  end;
end;
