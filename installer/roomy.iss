; Roomy installer for the current Windows user.
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\dist\publish"
#endif

[Setup]
AppId={{6B0F3B2E-6C1A-4C7E-9E3B-52A1F0D7C6A4}
AppName=Roomy
AppVersion={#AppVersion}
AppPublisher=А. О. Серов
AppPublisherURL=https://github.com/LeksssGray/roomy
AppSupportURL=https://github.com/LeksssGray/roomy/issues
DefaultDirName={localappdata}\Programs\Roomy
DefaultGroupName=Roomy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Roomy-Setup
SetupIconFile=..\src\Roomy\Assets\roomy.ico
UninstallDisplayIcon={app}\Roomy.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
VersionInfoVersion={#AppVersion}
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.BackgroundOptions=Фоновые возможности
russian.StartWithWindows=Запускать Roomy при входе в Windows
russian.LowSpaceCheck=Проверять свободное место и предупреждать при нехватке
russian.WeeklyCheck=Проверять временные файлы и кэш во время простоя
russian.LaunchRoomy=Запустить Roomy
russian.WaitForRoomy=Закройте Roomy. Если выполняется очистка, дождитесь её завершения.
russian.ConfigureError=Roomy установлен. Фоновые возможности можно настроить в приложении.
english.BackgroundOptions=Background features
english.StartWithWindows=Start Roomy when I sign in to Windows
english.LowSpaceCheck=Check available space and warn when it is low
english.WeeklyCheck=Check temporary files and caches when the computer is idle
english.LaunchRoomy=Launch Roomy
english.WaitForRoomy=Close Roomy. If a cleanup is running, wait until it finishes.
english.ConfigureError=Roomy is installed. Open its settings to set up the background features.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:BackgroundOptions}"; Check: IsFirstInstall
Name: "lowspace"; Description: "{cm:LowSpaceCheck}"; GroupDescription: "{cm:BackgroundOptions}"; Check: IsFirstInstall
Name: "weekly"; Description: "{cm:WeeklyCheck}"; GroupDescription: "{cm:BackgroundOptions}"; Check: IsFirstInstall

[Files]
Source: "{#PayloadDir}\Roomy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Roomy"; Filename: "{app}\Roomy.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Roomy"; Filename: "{app}\Roomy.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Roomy.exe"; Description: "{cm:LaunchRoomy}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Roomy.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RoomyBackground"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Roomy"; Flags: dontcreatekey uninsdeletevalue

[Code]
var
  FirstInstall: Boolean;

procedure InitializeWizard;
begin
  FirstInstall := not RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B0F3B2E-6C1A-4C7E-9E3B-52A1F0D7C6A4}_is1');
end;

function IsFirstInstall: Boolean;
begin
  Result := FirstInstall;
end;

function StopInstalledRoomy: Boolean;
var
  Exe, Version: String;
  ExitCode: Integer;
begin
  Exe := ExpandConstant('{app}\Roomy.exe');
  Result := True;
  if GetVersionNumbersString(Exe, Version) and (Pos('0.1.', Version) <> 1) then
    Result := Exec(Exe, '--prepare-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not FirstInstall and not StopInstalledRoomy then
    Result := CustomMessage('WaitForRoomy');
end;

function InitializeUninstall: Boolean;
begin
  Result := StopInstalledRoomy;
  if not Result and not UninstallSilent then
    MsgBox(CustomMessage('WaitForRoomy'), mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Args: String;
  ExitCode: Integer;
begin
  if (CurStep <> ssPostInstall) or not FirstInstall then
    Exit;
  Args := '--configure-install';
  if WizardIsTaskSelected('startup') then Args := Args + ' --start-with-windows';
  if WizardIsTaskSelected('lowspace') then Args := Args + ' --low-space-check';
  if WizardIsTaskSelected('weekly') then Args := Args + ' --weekly-check';
  if not ExecAsOriginalUser(ExpandConstant('{app}\Roomy.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or
    (ExitCode <> 0) then
  begin
    Log('Roomy background preferences could not be configured.');
    if not WizardSilent then
      MsgBox(CustomMessage('ConfigureError'), mbInformation, MB_OK);
  end;
end;
