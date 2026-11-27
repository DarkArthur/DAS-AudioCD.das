; DiscCheck installer (Inno Setup 6). Build with installer\build-installer.ps1.
; Defines passed by the build script: AppVersion, SourceDir (publish output), OutputDir, RepoRoot.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif

#define AppName "DiscCheck"
#define AppExe "DiscCheck.App.exe"

[Setup]
; Fixed GUID: lets a new version upgrade the previous one in place. Do not change it.
AppId={{6F1D2A53-8C4B-4E7A-9B3D-5A0E7C2F9D14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=DAS
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile={#RepoRoot}\src\DiscCheck.App\Assets\Icons\disccheck.ico
OutputDir={#OutputDir}
OutputBaseFilename=DiscCheck-Setup-{#AppVersion}-win-x64
; The app needs administrator rights to talk to the optical drive (app.manifest: requireAdministrator).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

; User data (settings.json, Database, logs) lives under the user profile and is intentionally kept on uninstall.
