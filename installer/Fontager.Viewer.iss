; Fontager Viewer — Inno Setup script (unpackaged WinUI publish folder).
; 1) Visual Studio: Release + x64 → Publish (FolderProfile) → win-x64\publish
; 2) Install Inno Setup 6: https://jrsoftware.org/isinfo.php
; 3) Open this file in Inno Setup Compiler → Build → Compile
; Output: installer\output\Fontager.Viewer-1.2.1-win-x64-setup.exe

#define MyAppName "Fontager Viewer"
#define MyAppVersion "1.2.1"
#define MyAppPublisher "Fontager"
#define MyAppURL "https://github.com/ysfemreAlbyrk/Fontager"
#define MyAppExeName "Fontager Viewer.exe"
; Relative to this .iss file (installer\). Change if your publish lands under bin\Release\...
#define PublishDir "..\Fontager.Viewer\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{A7E4F2B8-9C1D-4E6A-B3F0-8D2C5E7A1B94}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\Fontager\Viewer
DefaultGroupName=Fontager
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\Fontager.Viewer\Assets\Logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=output
OutputBaseFilename=Fontager.Viewer-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  FontCacheLink = '{app}\FontCache';
  FontCacheTarget = '{commonappdata}\Fontager\FontCache';

procedure EnsureFontCacheJunction();
var
  ResultCode: Integer;
  AppCache, TargetCache: String;
begin
  AppCache := ExpandConstant(FontCacheLink);
  TargetCache := ExpandConstant(FontCacheTarget);
  if not ForceDirectories(TargetCache) then
    Exit;
  if DirExists(AppCache) then
    Exit;
  Exec('cmd.exe', '/c mklink /J "' + AppCache + '" "' + TargetCache + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnsureFontCacheJunction();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppCache: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppCache := ExpandConstant(FontCacheLink);
    if DirExists(AppCache) then
      Exec('cmd.exe', '/c rmdir "' + AppCache + '"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
