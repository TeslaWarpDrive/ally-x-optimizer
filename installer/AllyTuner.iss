; Ally Tuner installer (Inno Setup 6.3+). Built by CI; see .github/workflows/ci.yml.
; Values below are overridden on the ISCC command line: /DAppVersion=... /DPublishDir=... /DOutputDir=...

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
; AppId must never change: it is how Windows recognises upgrades and uninstalls.
AppId={{047C9B90-4E7D-4DEE-9114-C30F13CB6E33}
AppName=Ally Tuner
AppVersion={#AppVersion}
AppVerName=Ally Tuner {#AppVersion}
VersionInfoVersion={#FileVersion}
AppPublisher=Ally Tuner
DefaultDirName={autopf}\Ally Tuner
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 22H2 (build 22621) or later: must match SupportedOSPlatformVersion in src/AllyTuner.App/AllyTuner.App.csproj
; (a unit test checks they agree).
MinVersion=10.0.22621
OutputDir={#OutputDir}
OutputBaseFilename=AllyTuner-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\AllyTuner.exe
UninstallDisplayName=Ally Tuner
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Ally Tuner"; Filename: "{app}\AllyTuner.exe"
Name: "{autodesktop}\Ally Tuner"; Filename: "{app}\AllyTuner.exe"; Tasks: desktopicon

[Run]
; shellexec so Windows shows the normal admin prompt for the app's requireAdministrator manifest.
Filename: "{app}\AllyTuner.exe"; Description: "{cm:LaunchProgram,Ally Tuner}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Program files only. Settings and logs in %LOCALAPPDATA%\AllyTuner are kept on purpose.
Type: filesandordirs; Name: "{app}"
