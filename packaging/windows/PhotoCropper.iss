; PhotoCropper Inno Setup Script
; Generates a Windows Installer bundling both GUI and CLI applications.

#define MyAppName "PhotoCropper"
#ifndef MyAppVersion
  #define MyAppVersion "3.2.0"
#endif
#define MyAppPublisher "Atm0n"
#define MyAppURL "https://github.com/Atm0n/PhotoCropper"
#define MyAppExeName "PhotoCropper.exe"
#define MyCliExeName "PhotoCropper.Cli.exe"

[Setup]
AppId={{D1A3F531-E4F9-44F8-9F31-9A3E4E81E991}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\publish\installer
OutputBaseFilename=PhotoCropper-{#MyAppVersion}-Windows-Setup
SetupIconFile=..\..\src\PhotoCropper.Gui\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "envPath"; Description: "Add PhotoCropper CLI to PATH environment variable"; GroupDescription: "System Integration:"

[InstallDelete]
; Clean up redundant duplicate binaries from previous installations
Type: files; Name: "{app}\PhotoCropper.Gui.exe"
Type: files; Name: "{app}\photocropper-cli.exe"

[Files]
; GUI application single-file executable and any accompanying files (excluding unrenamed PhotoCropper.Gui.exe)
Source: "..\..\publish\gui\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Excludes: "PhotoCropper.Gui.exe"
Source: "..\..\publish\gui\win-x64\PhotoCropper.Gui.exe"; DestDir: "{app}"; DestName: "PhotoCropper.exe"; Flags: ignoreversion
; CLI application single-file executable and lightweight terminal alias
Source: "..\..\publish\cli\win-x64\PhotoCropper.Cli.exe"; DestDir: "{app}"; DestName: "PhotoCropper.Cli.exe"; Flags: ignoreversion
Source: "photocropper-cli.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "photocropper.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} CLI"; Filename: "{cmd}"; Parameters: "/K """"{app}\PhotoCropper.Cli.exe"""" --help"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files the app downloads or writes outside {app}: the AI models (the LaMa repair-brush model is downloaded
; on demand; the face model is unpacked there from the app and comes back on next use) and crash reports. Settings are asked about in CurUninstallStepChanged;
; projects and photos are never touched.
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}\models"
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}\logs"
Type: dirifempty; Name: "{localappdata}\{#MyAppName}"

[Registry]
; Store whether PATH task was selected during installation
Root: HKA; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: dword; ValueName: "PathConfigured"; ValueData: 1; Tasks: envPath; Flags: uninsdeletekeyifempty

[Code]
const
  EnvKeyUser = 'Environment';
  EnvKeySystem = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

function GetEnvRoot(): Integer;
begin
  if IsAdminInstallMode() then
    Result := HKEY_LOCAL_MACHINE
  else
    Result := HKEY_CURRENT_USER;
end;

function GetEnvSubKey(): String;
begin
  if IsAdminInstallMode() then
    Result := EnvKeySystem
  else
    Result := EnvKeyUser;
end;

function NeedsAddPath(PathToAdd: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(GetEnvRoot(), GetEnvSubKey(), 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + UpperCase(PathToAdd) + ';', ';' + UpperCase(OrigPath) + ';') = 0;
end;

procedure AddAppToPath();
var
  OrigPath, AppDir, NewPath: string;
begin
  AppDir := ExpandConstant('{app}');
  if not RegQueryStringValue(GetEnvRoot(), GetEnvSubKey(), 'Path', OrigPath) then
    OrigPath := '';

  if NeedsAddPath(AppDir) then
  begin
    if (OrigPath = '') then
      NewPath := AppDir
    else if (OrigPath[Length(OrigPath)] = ';') then
      NewPath := OrigPath + AppDir
    else
      NewPath := OrigPath + ';' + AppDir;

    RegWriteStringValue(GetEnvRoot(), GetEnvSubKey(), 'Path', NewPath);
  end;
end;

procedure RemoveAppFromPath();
var
  OrigPath, AppDir: string;
  P: Integer;
begin
  AppDir := ExpandConstant('{app}');
  if not RegQueryStringValue(GetEnvRoot(), GetEnvSubKey(), 'Path', OrigPath) then
    exit;

  P := Pos(';' + UpperCase(AppDir) + ';', ';' + UpperCase(OrigPath) + ';');
  if P > 0 then
  begin
    if Pos(UpperCase(AppDir) + ';', UpperCase(OrigPath)) = 1 then
      Delete(OrigPath, 1, Length(AppDir) + 1)
    else if Pos(';' + UpperCase(AppDir), UpperCase(OrigPath)) = (Length(OrigPath) - Length(AppDir)) then
      Delete(OrigPath, Length(OrigPath) - Length(AppDir), Length(AppDir) + 1)
    else if Pos(';' + UpperCase(AppDir) + ';', UpperCase(OrigPath)) > 0 then
    begin
      P := Pos(';' + UpperCase(AppDir) + ';', UpperCase(OrigPath));
      Delete(OrigPath, P, Length(AppDir) + 1);
    end
    else if UpperCase(OrigPath) = UpperCase(AppDir) then
      OrigPath := '';

    RegWriteStringValue(GetEnvRoot(), GetEnvSubKey(), 'Path', OrigPath);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('envPath') then
      AddAppToPath();
  end;
end;

procedure RemoveSettingsIfWanted();
var
  SettingsDir: string;
begin
  SettingsDir := ExpandConstant('{userappdata}\{#MyAppName}');
  // A silent uninstall can't ask, so it keeps them (as does answering No, the default).
  if UninstallSilent() or not DirExists(SettingsDir) then
    exit;

  if MsgBox('Also remove your PhotoCropper settings (preferences and the list of recent projects)?' + #13#10 + #13#10 +
            'Your projects and photos are not affected either way.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(SettingsDir, True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveAppFromPath()
  else if CurUninstallStep = usPostUninstall then
    RemoveSettingsIfWanted();
end;
