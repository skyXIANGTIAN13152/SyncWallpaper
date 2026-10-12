; Build through build-installer.ps1. PackageDir must be a validated, self-contained
; release package, never the source checkout or an installed user's directory.
; The installer owns only the program files listed below. User data is not copied,
; migrated, overwritten, or recursively removed by setup or uninstall.

#ifndef PackageDir
  #error PackageDir must name the validated self-contained release package.
#endif
#ifndef OutputDir
  #error OutputDir must name the setup output directory.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef AppVersionNumeric
  #error AppVersionNumeric is required (for example 1.3.0.1).
#endif
#ifndef ChineseLanguageFile
  #define ChineseLanguageFile "compiler:Languages\ChineseSimplified.isl"
#endif

#ifdef TestBuild
  #define ProductName "SyncWallpaper 安装验证"
  #define InstallerId "{{81228564-7240-4574-A4BE-80BED83EDDA2}"
  #define SetupSuffix "-test"
  #define InstallerMutex "SyncWallpaper.Setup.Test"
#else
  #define ProductName "屏序 SyncWallpaper"
  #define InstallerId "{{D5A038DB-908B-4626-AD96-5E096EA57F42}"
  #define SetupSuffix ""
  #define InstallerMutex "SyncWallpaper.Setup"
#endif

[Setup]
AppId={#InstallerId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
AppPublisher=SyncWallpaper
AppPublisherURL=https://github.com/skyXIANGTIAN13152/SyncWallpaper
AppSupportURL=https://github.com/skyXIANGTIAN13152/SyncWallpaper/issues
AppUpdatesURL=https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductName={#ProductName}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={code:GetDefaultInstallDir}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
AlwaysShowDirOnReadyPage=yes
AppendDefaultDirName=no
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AllowRootDirectory=no
AllowUNCPath=no
AllowNetworkDrive=no
CloseApplications=no
SetupMutex={#InstallerMutex}
RestartApplications=no
RestartIfNeededByRun=no
UninstallRestartComputer=no
UninstallFilesDir={app}\.uninstall
UninstallDisplayIcon={app}\AppIcon.ico
UninstallDisplayName={#ProductName}
SetupIconFile={#PackageDir}\AppIcon.ico
LicenseFile={#PackageDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=SyncWallpaper-{#AppVersion}-win-x64-setup{#SetupSuffix}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
SetupLogging=yes

[Languages]
Name: "zhCN"; MessagesFile: "{#ChineseLanguageFile}"

[Messages]
WelcomeLabel2=此向导将安装 [name/ver]。%n%n安装前请保存正在编辑的组合，并从系统托盘退出屏序。安装器只更新程序文件，保留原有组合、壁纸和其他个人数据。%n%n如需更新便携版，请在下一步选择包含 App、Config、Wallpapers 的原目录。若选择其他目录，将安装为独立副本，不会迁移原有数据。
SelectDirLabel3=请选择屏序的安装目录。更新便携版时，请选择原来的根目录（不要选择其中的 App 文件夹）。原有组合和壁纸将保留。
FinishedLabel=屏序已安装完成。%n%n原有组合和壁纸会继续保存在安装目录中。通过 Windows 卸载屏序时，个人数据仍会保留。

#ifndef TestBuild
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Icons]
Name: "{userprograms}\屏序 SyncWallpaper"; Filename: "{app}\App\SyncWallpaper.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\AppIcon.ico"; Flags: uninsneveruninstall
Name: "{userdesktop}\屏序 SyncWallpaper"; Filename: "{app}\App\SyncWallpaper.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\AppIcon.ico"; Tasks: desktopicon; Flags: uninsneveruninstall

[Run]
Filename: "{app}\App\SyncWallpaper.App.exe"; WorkingDir: "{app}"; Description: "启动屏序"; Flags: nowait postinstall skipifsilent
#endif

[Files]
; Recursion is deliberately confined to the three release-only subdirectories.
; There is no wildcard over PackageDir itself and no [InstallDelete] or
; [UninstallDelete]: Inno's file ownership log removes only installed files.
Source: "{#PackageDir}\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\Diagnostics\*"; DestDir: "{app}\Diagnostics"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\syncwallpaper-icon.svg"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\package-manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\upgrade.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'SyncWallpaper';
  AppRelativePath = 'App\SyncWallpaper.App.exe';

function NormalizePath(const Value: String): String;
begin
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Trim(Value)));
end;

function SamePath(const Left, Right: String): Boolean;
begin
  Result := CompareText(NormalizePath(Left), NormalizePath(Right)) = 0;
end;

function IsInsidePath(const Path, Parent: String): Boolean;
var
  Prefix: String;
begin
  Prefix := AddBackslash(NormalizePath(Parent));
  Result := SamePath(Path, Parent) or
    (CompareText(Copy(NormalizePath(Path), 1, Length(Prefix)), Prefix) = 0);
end;

function ExecutableFromCommand(const Command: String): String;
var
  Value: String;
  I: Integer;
begin
  Result := '';
  Value := Trim(Command);
  if Value = '' then Exit;
  if Value[1] = '"' then
  begin
    Delete(Value, 1, 1);
    I := Pos('"', Value);
    if I = 0 then Exit;
    Result := Copy(Value, 1, I - 1);
  end
  else
  begin
    // Accept only the unambiguous .exe ending, never execute the command.
    I := Pos('.exe', Lowercase(Value));
    if I = 0 then Exit;
    Result := Copy(Value, 1, I + 3);
  end;
end;

function GetDefaultInstallDir(Param: String): String;
var
  Command, ExePath, AppDirectory, Candidate: String;
begin
#ifdef TestBuild
  Result := ExpandConstant('{localappdata}\SyncWallpaper-InstallerTest');
#else
  Result := ExpandConstant('{localappdata}\Programs\SyncWallpaper');
  // UsePreviousAppDir gives an existing Inno installation precedence over this.
  if RegQueryStringValue(HKCU, RunKey, RunValue, Command) then
  begin
    ExePath := ExecutableFromCommand(Command);
    if (ExePath <> '') and FileExists(ExePath) and
      (CompareText(ExtractFileName(ExePath), 'SyncWallpaper.App.exe') = 0) then
    begin
      AppDirectory := RemoveBackslashUnlessRoot(ExtractFileDir(ExePath));
      Candidate := ExtractFileDir(AppDirectory);
      if (CompareText(ExtractFileName(AppDirectory), 'App') = 0) and
        FileExists(AddBackslash(Candidate) + 'package-manifest.json') then
        Result := Candidate;
    end;
  end;
#endif
end;

function IsReparsePoint(const Path: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(Path, FindRec) then
  begin
    try
      Result := (FindRec.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function FindUnsafeProgramTree(const Directory: String): String;
var
  FindRec: TFindRec;
  Path: String;
begin
  Result := '';
  if not DirExists(Directory) then Exit;
  if FindFirst(AddBackslash(Directory) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Path := AddBackslash(Directory) + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then
          begin
            Result := Path;
            Exit;
          end;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            Result := FindUnsafeProgramTree(Path);
            if Result <> '' then Exit;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function ValidateInstallDirectory(const Directory: String): String;
var
  Root, Current, Parent, Unsafe: String;
  RootFiles: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := '';
  Root := NormalizePath(Directory);
  if (Length(Root) < 4) or (Copy(Root, 1, 2) = '\\') or
    (Copy(Root, 2, 2) <> ':\') or (Pos(':', Copy(Root, 3, MaxInt)) > 0) then
  begin
    Result := '请选择本机磁盘中的专用文件夹，不能使用磁盘根目录或网络路径。';
    Exit;
  end;
  if IsInsidePath(Root, ExpandConstant('{win}')) or
    SamePath(Root, ExpandConstant('{commonpf64}')) or
    SamePath(Root, ExpandConstant('{commonpf32}')) or
    SamePath(Root, ExpandConstant('{localappdata}\Programs')) or
    SamePath(Root, GetEnv('USERPROFILE')) or
    SamePath(Root, ExpandConstant('{localappdata}')) or
    SamePath(Root, ExpandConstant('{userappdata}')) or
    SamePath(Root, ExpandConstant('{userdesktop}')) or
    SamePath(Root, ExpandConstant('{userdocs}')) then
  begin
    Result := '此路径是系统或个人文件的公共目录。请为屏序选择一个专用文件夹。';
    Exit;
  end;
  if (CompareText(ExtractFileName(Root), 'App') = 0) or
    FileExists(AddBackslash(Root) + 'SyncWallpaper.App.exe') then
  begin
    Result := '请选择包含 App 文件夹的上一级根目录，不要选择 App 文件夹本身。';
    Exit;
  end;

  // Reject linked ancestors and source trees before any file is written.
  Current := Root;
  while Length(Current) > 3 do
  begin
    if IsReparsePoint(Current) then
    begin
      Result := '安装路径不能包含链接或目录联接：' + Current;
      Exit;
    end;
    if DirExists(AddBackslash(Current) + '.git') or
      FileExists(AddBackslash(Current) + '.git') or
      FileExists(AddBackslash(Current) + 'SyncWallpaper.sln') then
    begin
      Result := '此路径位于源码工程中。请保留源码目录，并选择单独的程序安装文件夹。';
      Exit;
    end;
    Parent := ExtractFileDir(Current);
    if SamePath(Current, Parent) then Break;
    Current := Parent;
  end;

  RootFiles := ['App', 'Diagnostics', 'docs', '.uninstall', 'README.md',
    'LICENSE', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md', 'AppIcon.ico',
    'syncwallpaper-icon.svg', 'package-manifest.json', 'install.ps1',
    'upgrade.ps1', 'uninstall.ps1'];
  for I := 0 to GetArrayLength(RootFiles) - 1 do
  begin
    Current := AddBackslash(Root) + RootFiles[I];
    if IsReparsePoint(Current) then
    begin
      Result := '程序文件不能是链接或目录联接：' + Current;
      Exit;
    end;
    Unsafe := FindUnsafeProgramTree(Current);
    if Unsafe <> '' then
    begin
      Result := '程序目录中包含链接或目录联接，请先处理后再安装：' + Unsafe;
      Exit;
    end;
  end;

  if FindFirst(AddBackslash(Root) + '.syncwallpaper-update-*', FindRec) then
  begin
    FindClose(FindRec);
    Result := '此目录存在正在进行或尚未完成恢复的自动更新。请先完成更新或恢复，再运行安装器。';
  end;
end;

function CheckTargetProcesses(const Directory: String): String;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  ExePath, TargetApp, TargetDiagnostics: String;
begin
  Result := '';
  TargetApp := AddBackslash(NormalizePath(Directory)) + AppRelativePath;
  TargetDiagnostics := AddBackslash(NormalizePath(Directory)) +
    'Diagnostics\SyncWallpaper.Diagnostics.exe';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE ' +
      'Name="SyncWallpaper.App.exe" OR Name="SyncWallpaper.Diagnostics.exe" OR ' +
      'Name="SyncWallpaper.Updater.exe"');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      // The real update helper runs from a temporary runner outside App.
      // Wait for every update helper to finish before modifying an installation.
      if CompareText(Process.Name, 'SyncWallpaper.Updater.exe') = 0 then
      begin
        Result := '屏序的自动更新助手仍在运行。请等待更新完成，再运行安装器。';
        Exit;
      end;
      if VarIsNull(Process.ExecutablePath) or VarIsEmpty(Process.ExecutablePath) then
      begin
        Result := '无法确认正在运行的屏序程序所在位置。请退出所有屏序和诊断程序后重试。';
        Exit;
      end;
      ExePath := Process.ExecutablePath;
      if SamePath(ExePath, TargetApp) or SamePath(ExePath, TargetDiagnostics) then
      begin
        Result := '此目录中的屏序仍在运行。请保存编辑内容，从系统托盘退出屏序，并关闭诊断程序后重试。安装器不会强制结束程序。';
        Exit;
      end;
    end;
  except
    Log('Unable to inspect running applications: ' + GetExceptionMessage);
    Result := '无法检查程序是否正在运行。请确认 Windows Management Instrumentation 服务可用后重试。';
  end;
end;

function CheckInstalledVersion(const Directory: String): String;
var
  ExistingVersion, PackageVersion: Int64;
  ExePath: String;
begin
  Result := '';
  ExePath := AddBackslash(NormalizePath(Directory)) + AppRelativePath;
  if not FileExists(ExePath) then Exit;
  if not GetPackedVersion(ExePath, ExistingVersion) then
  begin
    Result := '无法读取现有程序的版本。为避免意外覆盖，请先确认安装目录中的程序文件完整。';
    Exit;
  end;
  if not StrToVersion('{#AppVersionNumeric}', PackageVersion) then
  begin
    Result := '安装包版本信息无效，请重新下载安装包。';
    Exit;
  end;
  if ComparePackedVersion(ExistingVersion, PackageVersion) > 0 then
    Result := '此目录中已有更新的屏序版本（' + VersionToStr(ExistingVersion) +
      '）。安装器不会降级覆盖，请下载最新安装包。';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Error := ValidateInstallDirectory(WizardDirValue);
    if Error = '' then Error := CheckInstalledVersion(WizardDirValue);
    if Error <> '' then
    begin
      // Silent setup simulates Next clicks too; never block it on a dialog.
      SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Recheck immediately before installation, including silent setup.
  NeedsRestart := False;
  Result := ValidateInstallDirectory(ExpandConstant('{app}'));
  if Result = '' then Result := CheckInstalledVersion(ExpandConstant('{app}'));
  if Result = '' then Result := CheckTargetProcesses(ExpandConstant('{app}'));
end;

function InitializeUninstall(): Boolean;
var
  Error: String;
begin
  Error := ValidateInstallDirectory(ExpandConstant('{app}'));
  if Error = '' then Error := CheckTargetProcesses(ExpandConstant('{app}'));
  Result := Error = '';
  if not Result then SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
end;

#ifndef TestBuild
procedure RemoveOwnedShortcut(const ShortcutPath: String);
var
  Shell, Shortcut: Variant;
  TargetPath: String;
begin
  if not FileExists(ShortcutPath) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(ShortcutPath);
    TargetPath := Shortcut.TargetPath;
    if (TargetPath <> '') and
      SamePath(TargetPath, ExpandConstant('{app}\') + AppRelativePath) then
    begin
      if not DeleteFile(ShortcutPath) then
        Log('Unable to remove owned shortcut: ' + ShortcutPath);
    end
    else
      Log('Preserving shortcut that targets another application: ' + ShortcutPath);
  except
    Log('Preserving shortcut whose target could not be verified: ' + ShortcutPath +
      ' (' + GetExceptionMessage + ')');
  end;
end;
#endif

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, ExePath: String;
begin
#ifndef TestBuild
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnedShortcut(ExpandConstant('{userprograms}\屏序 SyncWallpaper.lnk'));
    RemoveOwnedShortcut(ExpandConstant('{userdesktop}\屏序 SyncWallpaper.lnk'));
    // Never remove a startup entry that now belongs to another installation.
    if RegQueryStringValue(HKCU, RunKey, RunValue, Command) then
    begin
      ExePath := ExecutableFromCommand(Command);
      if (ExePath <> '') and SamePath(ExePath, ExpandConstant('{app}\') + AppRelativePath) then
        RegDeleteValue(HKCU, RunKey, RunValue);
    end;
  end;
#endif
end;
