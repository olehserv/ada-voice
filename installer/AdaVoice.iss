; Inno Setup script for AdaVoice (beta distribution).
;
; Packages the self-contained publish output from scripts/publish.ps1 into a normal
; Windows installer/uninstaller. Not the v1 production installer (see
; docs/plans/production-readiness-plan.md #7) -- this is the beta-trial packaging step, built
; to replace "unzip and run the raw exe" with a double-click Setup experience.
;
; Per-user install (no admin / UAC prompt): the target audience is a single non-technical
; user on their own PC, and an elevation prompt on first run would work against the whole
; point of this change. PrivilegesRequired=lowest + a LocalAppData install dir keeps it that
; way. Uninstall is still registered normally in "Add or remove programs" either way.
;
; Build with scripts/build-installer.ps1 -Version vX.Y.Z (do not run ISCC directly -- that
; script locates ISCC.exe and passes MyAppVersion for you). Requires
; artifacts/publish/win-x64 to already exist (run scripts/publish.ps1 first).

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "AdaVoice"
#define MyAppPublisher "AdaVoice"
#define MyAppExeName "AdaVoice.App.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{8F2C6E1A-9B3D-4E7C-9A1F-6D2B7C4A5E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=AdaVoice-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
; Unsigned build (same as the zip today) -- SmartScreen still warns once on first run.
; Code signing is a separate, paid, later step (see INSTALL.md).

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
