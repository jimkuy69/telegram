#define AppName "Telegram Control Studio"
#define AppVersion "0.1.0"
#define AppPublisher "Telegram Control Studio"
#define AppExeName "TelegramControlStudio.exe"

[Setup]
AppId={{A6B4C4F4-8E1A-4F6A-8A1C-5B4C2A94B9D1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}TelegramControlStudio
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=TelegramControlStudio-Setup-{#AppVersion}
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}{#AppExeName}

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{localappdata}\TelegramControlStudio"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
