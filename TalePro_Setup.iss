[Setup]
AppId={{TALE-PRO-STUDIO-8A92F3B1-CUSTOM}
AppName=Tale-Pro Video Processing Studio
AppVersion=1.0.0
AppPublisher=Talha Technologies
AppPublisherURL=https://talepro.com
AppSupportURL=https://talepro.com/support
DefaultDirName={autopf}\Tale-Pro Studio
DefaultGroupName=Tale-Pro Studio
AllowNoIcons=yes
OutputDir=C:\Users\WORK\Desktop
OutputBaseFilename=Tale-Pro_Official_Setup
SetupIconFile=C:\TalePro_Project\TalePro.ico
UninstallDisplayIcon={app}\TalePro.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "C:\TalePro_Project\ReleaseBuild\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "C:\TalePro_Project\TalePro.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Tale-Pro Studio"; Filename: "{app}\TalePro_Final.exe"; IconFilename: "{app}\TalePro.ico"
Name: "{autodesktop}\Tale-Pro Studio"; Filename: "{app}\TalePro_Final.exe"; Tasks: desktopicon; IconFilename: "{app}\TalePro.ico"

[Run]
Filename: "{app}\TalePro_Final.exe"; Description: "{cm:LaunchProgram,Tale-Pro Studio}"; Flags: nowait postinstall skipifsilent