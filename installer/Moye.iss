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
AppPublisherURL=https://github.com/yikeugene/moye
AppSupportURL=https://github.com/yikeugene/moye/issues
AppUpdatesURL=https://github.com/yikeugene/moye/releases/latest
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
Filename: "{app}\Penroam.exe"; Description: "Launch Penroam"; Flags: nowait postinstall skipifsilent

; No UninstallDelete entry: user notebooks live outside {app} and are retained.

[Code]
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
