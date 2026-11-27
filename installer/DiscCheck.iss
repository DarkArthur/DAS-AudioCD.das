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
AppPublisher=DarkArthur DAS
AppCopyright=Copyright (C) 2026 DarkArthur DAS
AppPublisherURL=https://github.com/DarkArthur/DAS-AudioCD.das
AppSupportURL=https://github.com/DarkArthur/DAS-AudioCD.das/issues
AppUpdatesURL=https://github.com/DarkArthur/DAS-AudioCD.das/releases
; GPL-2.0 shown and accepted during setup (Requirement 25.8).
LicenseFile={#RepoRoot}\LICENSE
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
; Licence texts and notices (GPL-2.0 of the project and of the QPxTool port, LGPL-3.0 of FLAKE, CC-BY 4.0 assets, OFL font).
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\src\DAS.Design\LICENSE-ASSETS.md"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\src\DAS.Design\Assets\Fonts\OFL.txt"; DestDir: "{app}\licenses"; DestName: "Inter-OFL.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

; User data (settings.json, Database, logs) lives under the user profile and is intentionally kept on uninstall.
