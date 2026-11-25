; Carbon Monitor - Inno Setup Installer Script
; Requires Inno Setup 6 (https://jrsoftware.org/isdl.php)
;
; Build with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\CarbonMonitor.iss
;
; Prerequisites:
;   The application must be published first:
;   dotnet publish src/CarbonMonitor.App/CarbonMonitor.App.csproj -c Release -r win-x64 --self-contained true -o publish/

#define MyAppName "Carbon Monitor"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Carbon Monitor"
#define MyAppExeName "CarbonMonitor.App.exe"
#define MyAppURL "https://github.com/carbonmonitor"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\output
OutputBaseFilename=CarbonMonitor-Setup-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\src\CarbonMonitor.App\Resources\carbon-monitor.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
LicenseFile=..\LICENSE
InfoBeforeFile=..\docs\THIRD-PARTY-NOTICES.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Launch Carbon Monitor at Windows sign-in"; Flags: unchecked
Name: "launchafter"; Description: "Start Carbon Monitor after installation"; Flags: checkedonce

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CarbonMonitor"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start Carbon Monitor"; Flags: nowait postinstall skipifsilent; Tasks: launchafter

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
; Clean up any running instance before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillCarbonMonitor"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove startup registry entry if it exists
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CarbonMonitor');
  end;
end;
