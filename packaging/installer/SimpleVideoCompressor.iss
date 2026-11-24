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

[Code]
function HasDotNet8DesktopRuntime(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  RegKey: String;
begin
  Result := False;
  RegKey := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if RegGetValueNames(HKLM, RegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  if RegGetValueNames(HKLM64, RegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  RuntimeExe: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not HasDotNet8DesktopRuntime() then
    begin
      WizardForm.StatusLabel.Caption := '未检测到 .NET 8 桌面运行环境，正在自动下载并安装（仅首次需要）...';
      RuntimeExe := ExpandConstant('{tmp}\windowsdesktop-runtime-8-win-x64.exe');
      try
        DownloadTemporaryFile('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-8-win-x64.exe', '', nil);
        if not Exec(RuntimeExe, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        begin
          MsgBox('自动安装 .NET 8 运行环境失败（错误码：' + IntToStr(ResultCode) + '）。请稍后手动安装 .NET 8 Desktop Runtime (x64) 后再打开软件。', mbError, MB_OK);
        end;
      except
        MsgBox('自动下载 .NET 8 运行环境失败：' + GetExceptionMessage() + #13#10 + '请稍后手动安装 .NET 8 Desktop Runtime (x64) 后再打开软件。', mbError, MB_OK);
      end;
    end;
  end;
end;
