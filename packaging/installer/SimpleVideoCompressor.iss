#define MyAppId "8C3E6E24-5D4A-4B9E-A91B-7F2D9C4F81A2"
#define MyAppName "极简视频压缩工具"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "SimpleVideoCompressor.exe"
#endif
#ifndef MyStageDir
  #error "MyStageDir must be provided to the compiler."
#endif
#ifndef MyOutputDir
  #define MyOutputDir "."
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "SimpleVideoCompressor-Setup"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=SimpleVideoCompressor
DefaultDirName={localappdata}\Programs\SimpleVideoCompressor
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\..\src\SimpleVideoCompressor\Assets\app.ico
UninstallDisplayIcon={app}\app\{#MyAppExeName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} 安装程序
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
DisableDirPage=no
DisableProgramGroupPage=yes

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Files]
Source: "{#MyStageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\app\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\app\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\app\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
