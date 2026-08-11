#define AppName "JitenMPC-BE"
#ifndef AppVersion
  #define AppVersion "0.5.2"
#endif
#define AppPublisher "SlothUchiha"
#define AppURL "https://github.com/SlothUchiha/JitenMPC-BE"
#define AppExeName "JitenMPC-BE.exe"

[Setup]
AppId={{52307245-8D26-458A-B03D-3D37298A1DC1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\Programs\JitenMPC-BE
DefaultGroupName=JitenMPC-BE
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=JitenMPC-BE
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\installer-output
OutputBaseFilename=JitenMPC-BE-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\JitenMPC-BE"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Run]
; Interactive installs show the normal launch checkbox. Silent in-app upgrades also
; run this entry so the freshly-updated JitenMPC-BE starts again automatically.
Filename: "{app}\{#AppExeName}"; Description: "Launch JitenMPC-BE"; Flags: nowait postinstall
