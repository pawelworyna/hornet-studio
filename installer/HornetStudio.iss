; HornetStudio Inno Setup Script
; Supports both:
; 1) All Users (Program Files - requires UAC elevation)
; 2) Current User (AppData\Local\Programs - no admin rights required)

#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#ifndef MyAppArch
#define MyAppArch "x64"
#endif

#define MyAppName "HornetStudio"
#define MyAppPublisher "HornetStudio"
#define MyAppURL "https://github.com/pawelworyna/hornet-studio"
#define MyAppExeName "HornetStudio.Desktop.exe"

#ifndef SourceDir
#define SourceDir "..\dist\win-" + MyAppArch
#endif

#ifndef OutputBaseName
#define OutputBaseName "HornetStudio-Setup-" + MyAppArch + "-v" + MyAppVersion
#endif

[Setup]
; Unique GUID for AppId (do not change for subsequent updates)
AppId={{5D4E1C6E-902B-4F8A-9A4A-8C0898E0883C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion} ({#MyAppArch})
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Dual mode install: Program Files (Admin) vs AppData (User)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

; Visuals & Branding
SetupIconFile=..\src\HornetStudio.Desktop\Assets\hornet.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Output configuration
OutputDir=..\artifacts\installer
OutputBaseFilename={#OutputBaseName}

; Architecture
#if MyAppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Note: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
