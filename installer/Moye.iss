#ifndef MoyeVersion
  #error MoyeVersion must be supplied by scripts/publish-installer.ps1.
#endif
#ifndef MoyePayload
  #error MoyePayload must be supplied by scripts/publish-installer.ps1.
#endif
#ifndef MoyeOutput
  #error MoyeOutput must be supplied by scripts/publish-installer.ps1.
#endif

[Setup]
; Keep this identity stable so later installers update the same application.
AppId=net.yikeugene.moye
AppName=Penroam
AppVersion={#MoyeVersion}
AppPublisher=Penroam contributors
AppPublisherURL=https://github.com/yikeugene/Penroam
AppSupportURL=https://github.com/yikeugene/Penroam/issues
AppUpdatesURL=https://github.com/yikeugene/Penroam/releases/latest
DefaultDirName={localappdata}\Programs\Penroam
; Reuse the original Moye location when upgrading the same AppId.
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern
LicenseFile=..\LICENSE
InfoBeforeFile=UpgradeNotes.txt
SetupIconFile=..\src\Moye\Assets\Penroam.ico
UninstallDisplayIcon={app}\Penroam.exe
VersionInfoVersion={#MoyeVersion}.0
VersionInfoDescription=Penroam Setup
OutputDir={#MoyeOutput}
OutputBaseFilename=Penroam-{#MoyeVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
; The payload comes only from the checked, self-contained publish directory.
Source: "{#MoyePayload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Inspect shortcut targets before deleting the legacy executable.
; Leave any unrelated, manually repurposed shortcut alone.
Type: files; Name: "{autodesktop}\Moye.lnk"; Check: IsLegacyShortcut(ExpandConstant('{autodesktop}\Moye.lnk'))
Type: files; Name: "{autoprograms}\Moye.lnk"; Check: IsLegacyShortcut(ExpandConstant('{autoprograms}\Moye.lnk'))
; Only remove known legacy application files, never the notebook-data folder.
Type: files; Name: "{app}\Moye.exe"
Type: files; Name: "{app}\Moye.dll"
Type: files; Name: "{app}\Moye.deps.json"
Type: files; Name: "{app}\Moye.runtimeconfig.json"

[Icons]
; No optional task: every normal installation creates the desktop shortcut.
Name: "{autodesktop}\Penroam"; Filename: "{app}\Penroam.exe"; WorkingDir: "{app}"; AppUserModelID: "net.yikeugene.moye"
Name: "{autoprograms}\Penroam"; Filename: "{app}\Penroam.exe"; WorkingDir: "{app}"; AppUserModelID: "net.yikeugene.moye"

[Run]
Filename: "{app}\Penroam.exe"; Description: "Launch Penroam"; Flags: nowait postinstall skipifsilent; Check: not IsAppUpdate
; postinstall also prevents reopening a partially replaced app when a restart is needed.
Filename: "{app}\Penroam.exe"; WorkingDir: "{app}"; Description: "Reopen Penroam"; Flags: nowait postinstall runasoriginaluser; Check: IsAppUpdate

; No UninstallDelete entry: user notebooks live outside {app} and are retained.

[Code]
const
  UpdateSynchronize = $00100000;
  UpdateWaitObject0 = 0;
  UpdateWaitTimeout = 258;
  UpdateInvalidParameter = 87;

var
  AppUpdateRequested: Boolean;
  UpdateParentHandle: THandle;
  UpdateParentError: String;

function UpdateOpenProcess(DesiredAccess: DWORD; InheritHandle: BOOL; ProcessId: DWORD): THandle;
external 'OpenProcess@kernel32.dll stdcall';
function UpdateWaitForSingleObject(Handle: THandle; Milliseconds: DWORD): DWORD;
external 'WaitForSingleObject@kernel32.dll stdcall';
function UpdateCloseHandle(Handle: THandle): BOOL;
external 'CloseHandle@kernel32.dll stdcall';

function IsAppUpdate: Boolean;
begin
  Result := AppUpdateRequested;
end;

function InitializeSetup: Boolean;
var
  ParentText: String;
  ParentId, Index, ErrorCode: Integer;
begin
  Result := True;
  ParentText := ExpandConstant('{param:PENROAMUPDATE|not-requested}');
  AppUpdateRequested := ParentText <> 'not-requested';
  if not AppUpdateRequested then
    Exit;

  { Only accept the positive decimal process ID produced by Penroam. }
  ParentId := StrToIntDef(ParentText, 0);
  for Index := 1 to Length(ParentText) do
    if (ParentText[Index] < '0') or (ParentText[Index] > '9') then
      ParentId := 0;
  if ParentId <= 0 then begin
    UpdateParentError := 'The update request is invalid. Open Penroam and try updating again.';
    Exit;
  end;

  { Capture a handle now, so the later wait cannot switch to a reused process ID. }
  UpdateParentHandle := UpdateOpenProcess(UpdateSynchronize, False, ParentId);
  if UpdateParentHandle = 0 then begin
    ErrorCode := DLLGetLastError;
    { The app may already have finished closing before Setup initializes. }
    if ErrorCode <> UpdateInvalidParameter then
      UpdateParentError := 'Setup could not check whether Penroam has closed. Close Penroam and run the update again.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  WaitResult: DWORD;
  WaitStep: Integer;
begin
  Result := UpdateParentError;
  if (Result <> '') or (not AppUpdateRequested) or (UpdateParentHandle = 0) then
    Exit;

  Log('Waiting for Penroam to finish saving and exit before replacing application files.');
  { Wait at most one minute; never terminate an application or overwrite live files. }
  WaitResult := UpdateWaitTimeout;
  for WaitStep := 1 to 240 do begin
    WaitResult := UpdateWaitForSingleObject(UpdateParentHandle, 250);
    if WaitResult <> UpdateWaitTimeout then
      Break;
    WizardForm.Refresh;
  end;
  if WaitResult = UpdateWaitObject0 then begin
    UpdateCloseHandle(UpdateParentHandle);
    UpdateParentHandle := 0;
  end
  else if WaitResult = UpdateWaitTimeout then
    Result := 'Penroam is still open. Your notes have not been changed. Close Penroam and run the update again.'
  else
    Result := 'Setup could not wait for Penroam to close. Close Penroam and run the update again.';
end;

procedure DeinitializeSetup;
begin
  if UpdateParentHandle <> 0 then
    UpdateCloseHandle(UpdateParentHandle);
end;
function IsLegacyShortcut(ShortcutPath: String): Boolean;
var
  Shell, Shortcut: Variant;
  TargetPath: String;
begin
  Result := False;
  if not FileExists(ShortcutPath) then
    Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(ShortcutPath);
    TargetPath := Shortcut.TargetPath;
    Result := CompareText(TargetPath, ExpandConstant('{app}\Moye.exe')) = 0;
  except
    Log('Leaving an unreadable legacy shortcut in place: ' + ShortcutPath);
  end;
end;

procedure RegisterExtraCloseApplicationsResources;
begin
  { Legacy names are no longer in [Files], but may still be running on upgrade. }
  RegisterExtraCloseApplicationsResource(ExpandConstant('{app}\Moye.exe'));
  RegisterExtraCloseApplicationsResource(ExpandConstant('{app}\Moye.dll'));
end;
