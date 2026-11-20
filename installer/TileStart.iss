#define AppName "TileStart"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#define AppPublisher "Narylr350"
#define AppExeName "TileStart.Host.exe"

[Setup]
AppId={{A42394D4-9E18-46F2-9DBA-D391397EE12F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
AppendDefaultDirName=yes
DirExistsWarning=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
Uninstallable=not IsPortableUpdate
; Autostart and Explorer context menus intentionally remain per-user.
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
#ifdef PatchDirectory
OutputBaseFilename=TileStart-Patch-win-x64
#else
#ifdef FrameworkDependent
OutputBaseFilename=TileStart-Setup-no-runtime-win-x64
#else
OutputBaseFilename=TileStart-Setup-win-x64
#endif
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
; Explorer hosts TileStart.ShellHook.dll. Never let Restart Manager close the
; desktop shell just to replace an in-use hook; PrepareToInstall stops the Host
; and waits for the hook to unload instead.
CloseApplications=no
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
#ifdef PatchDirectory
Source: "{#PatchDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PatchManifest}"; Flags: dontcopy
#else
#ifdef FrameworkDependent
Source: "..\artifacts\package\TileStart-FrameworkDependent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\package\TileStart-BundledRuntime.txt"; Flags: dontcopy
#else
Source: "..\artifacts\package\TileStart\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#endif

[UninstallDelete]
; This marker is the only installer-created file outside Inno Setup's normal
; install log. Never add a recursive {app} deletion rule here: foreign files
; in the directory must survive uninstall.
Type: files; Name: "{app}\.tilestart-installation"

[Icons]
Name: "{group}\TileStart"; Filename: "{app}\{#AppExeName}"; Check: not IsPortableUpdate
Name: "{autodesktop}\TileStart"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; Check: not IsPortableUpdate

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TileStart"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\*\shell\TileStart.AddToAppList"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\*\shell\TileStart.PinTile"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TileStart.AddToAppList"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TileStart.PinTile"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\Drive\shell\TileStart.AddToAppList"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\Drive\shell\TileStart.PinTile"; Flags: uninsdeletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.exe\shell\TileStart.AddToAppList"; Flags: deletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.exe\shell\TileStart.PinTile"; Flags: deletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.lnk\shell\TileStart.AddToAppList"; Flags: deletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.lnk\shell\TileStart.PinTile"; Flags: deletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.appref-ms\shell\TileStart.AddToAppList"; Flags: deletekey; Check: not IsPortableUpdate
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.appref-ms\shell\TileStart.PinTile"; Flags: deletekey; Check: not IsPortableUpdate

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--configure-nvidia-overlay"; Flags: runhidden waituntilterminated; StatusMsg: "正在配置 NVIDIA Overlay 兼容性..."; Check: not IsPortableUpdate
Filename: "{app}\{#AppExeName}"; Description: "启动 TileStart"; Flags: nowait postinstall skipifsilent; Check: not IsPortableUpdate

[Code]
#ifdef FrameworkDependent
var
  BundledRuntimeFiles: TArrayOfString;
  RuntimeBackupDirectory: String;
  RuntimeSwitchCommitted: Boolean;
#endif

function IsPortableUpdate: Boolean;
begin
  Result := ExpandConstant('{param:TILESTARTUPDATE|}') = 'portable';
end;

function IsSelfUpdate: Boolean;
begin
  Result := IsPortableUpdate or (ExpandConstant('{param:TILESTARTUPDATE|}') = 'installed');
end;

function DirectoryIsEmpty(const Directory: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if not DirExists(Directory) then
    exit;

  if FindFirst(AddBackslash(Directory) + '*', FindRec) then
  begin
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := False;
        break;
      end;
    until not FindNext(FindRec);
    FindClose(FindRec);
  end;
end;

function IsRootDirectory(const Directory: String): Boolean;
var
  NormalizedDirectory: String;
begin
  NormalizedDirectory := RemoveBackslashUnlessRoot(Directory);
  Result := (Length(NormalizedDirectory) > 0) and
    (NormalizedDirectory[Length(NormalizedDirectory)] = '\');
end;

function IsExistingTileStartDirectory(const Directory: String): Boolean;
var
  BasePath: String;
begin
  BasePath := AddBackslash(Directory);
  Result := FileExists(BasePath + 'TileStart.Host.exe') and
    (FileExists(BasePath + '.tilestart-installation') or
      (FileExists(BasePath + 'unins000.exe') and FileExists(BasePath + 'unins000.dat')));
end;

function ValidateInstallDirectory(var ErrorMessage: String): Boolean;
var
  InstallDirectory: String;
begin
  InstallDirectory := WizardDirValue;
  ErrorMessage := '';

  if IsRootDirectory(InstallDirectory) then
    ErrorMessage := '不能将 TileStart 直接安装到磁盘或共享根目录。请选择专用的 TileStart 子目录。'
  else if IsPortableUpdate then
  begin
    if IsAdminInstallMode or IsExistingTileStartDirectory(InstallDirectory) or
      not FileExists(AddBackslash(InstallDirectory) + 'TileStart.Host.exe') or
      not FileExists(AddBackslash(InstallDirectory) + 'TileStart.Host.dll') then
      ErrorMessage := '更新目标必须是现有的 TileStart 便携版目录。';
  end
  else if IsSelfUpdate and not IsExistingTileStartDirectory(InstallDirectory) then
    ErrorMessage := '更新目标必须是现有的 TileStart 安装目录。'
  else if DirExists(InstallDirectory) and not DirectoryIsEmpty(InstallDirectory) and
    not IsExistingTileStartDirectory(InstallDirectory) then
    ErrorMessage := '所选目录已包含其他内容，并且不是现有的 TileStart 安装目录。' + #13#10 +
      '为防止覆盖或卸载其他文件，请选择空目录或新建 TileStart 专用子目录。';

  Result := ErrorMessage = '';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not ValidateInstallDirectory(ErrorMessage) then
  begin
    MsgBox(ErrorMessage, mbError, MB_OK);
    Result := False;
  end;
end;

function WaitForTileStartHost(TimeoutMs: Integer): Boolean;
var
  ElapsedMs: Integer;
begin
  ElapsedMs := 0;
  while CheckForMutexes('Local\TileStart.Host') and (ElapsedMs < TimeoutMs) do
  begin
    Sleep(250);
    ElapsedMs := ElapsedMs + 250;
  end;

  Result := not CheckForMutexes('Local\TileStart.Host');
end;

function StopTileStart: Boolean;
var
  AppPath: String;
  ResultCode: Integer;
begin
  AppPath := ExpandConstant('{app}\TileStart.Host.exe');
  if FileExists(AppPath) then
  begin
    Exec(AppPath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  if not WaitForTileStartHost(10000) and not IsSelfUpdate then
  begin
    { Killing only the Host still lets its watcher observe the process exit and
      unload the Explorer hook. Never kill the Injector directly. }
    Exec(ExpandConstant('{cmd}'), '/C taskkill /IM TileStart.Host.exe /F >NUL 2>&1', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
  end;

  Result := WaitForTileStartHost(10000);
end;

#ifdef PatchDirectory
function PatchBaselineMatches: Boolean;
var
  Lines: TArrayOfString;
  I: Integer;
  BasePath, Path, InstalledVersion: String;
begin
  Result := False;
  BasePath := AddBackslash(ExpandConstant('{app}'));
  try
    ExtractTemporaryFile('TileStart-Update.txt');
    if not LoadStringsFromFile(ExpandConstant('{tmp}\TileStart-Update.txt'), Lines) then
      exit;
    if (GetArrayLength(Lines) < 3) or (Lines[0] <> 'TileStart.Update.v1') then
      exit;
    if not GetVersionNumbersString(BasePath + 'TileStart.Host.exe', InstalledVersion) or
      (InstalledVersion <> Lines[1] + '.0') then
      exit;
    for I := 2 to GetArrayLength(Lines) - 1 do
    begin
      Path := Copy(Lines[I], 67, MaxInt);
      StringChangeEx(Path, '/', '\', True);
      if not FileExists(BasePath + Path) or
        (CompareText(GetSHA256OfFile(BasePath + Path), Copy(Lines[I], 1, 64)) <> 0) then
        exit;
    end;
    Result := True;
  except
    Log(GetExceptionMessage);
  end;
end;
#endif

#ifdef FrameworkDependent
function HasNet8Framework(DotNetDirectory, Framework: String): Boolean;
var
  Directory: String;
  FindRec: TFindRec;
begin
  Result := False;
  Directory := AddBackslash(DotNetDirectory) + 'shared\' + Framework + '\';
  if FindFirst(Directory + '8.0.*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
          (StrToIntDef(Copy(FindRec.Name, 5, MaxInt), -1) >= 0) and
          FileExists(Directory + FindRec.Name + '\' + Framework + '.deps.json') then
        begin
          Result := True;
          exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasX64DesktopRuntime: Boolean;
var
  DotNetDirectory: String;
begin
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', DotNetDirectory) then
  begin
    DotNetDirectory := ExpandConstant('{pf64}\dotnet');
    if ProcessorArchitecture = paArm64 then
      DotNetDirectory := DotNetDirectory + '\x64';
  end;
  Result := FileExists(AddBackslash(DotNetDirectory) + 'dotnet.exe') and
    HasNet8Framework(DotNetDirectory, 'Microsoft.NETCore.App') and
    HasNet8Framework(DotNetDirectory, 'Microsoft.WindowsDesktop.App');
end;

function StageBundledRuntime: Boolean;
var
  AppDirectory, Backup, Source, Target: String;
  I: Integer;
begin
  Result := True;
  AppDirectory := AddBackslash(ExpandConstant('{app}'));
  if not (FileExists(AppDirectory + 'hostfxr.dll') or FileExists(AppDirectory + 'coreclr.dll')) then
    exit;

  Result := False;
  Backup := AppDirectory + '.tilestart-runtime-rollback';
  if DirExists(Backup) then
    exit;
  ExtractTemporaryFile('TileStart-BundledRuntime.txt');
  if not LoadStringsFromFile(ExpandConstant('{tmp}\TileStart-BundledRuntime.txt'), BundledRuntimeFiles) or
    not CreateDir(Backup) then
    exit;

  RuntimeBackupDirectory := AddBackslash(Backup);
  for I := 0 to GetArrayLength(BundledRuntimeFiles) - 1 do
  begin
    Source := AppDirectory + BundledRuntimeFiles[I];
    if FileExists(Source) then
    begin
      Target := RuntimeBackupDirectory + BundledRuntimeFiles[I];
      if not ForceDirectories(ExtractFileDir(Target)) or not RenameFile(Source, Target) then
        exit;
    end;
  end;
  Result := True;
end;

procedure DeinitializeSetup;
var
  AppDirectory, BackupFile: String;
  I: Integer;
  Restored: Boolean;
begin
  if RuntimeBackupDirectory = '' then
    exit;
  Restored := True;
  if not RuntimeSwitchCommitted then
  begin
    AppDirectory := AddBackslash(ExpandConstant('{app}'));
    for I := 0 to GetArrayLength(BundledRuntimeFiles) - 1 do
    begin
      BackupFile := RuntimeBackupDirectory + BundledRuntimeFiles[I];
      if FileExists(BackupFile) and not RenameFile(BackupFile, AppDirectory + BundledRuntimeFiles[I]) then
        Restored := False;
    end;
  end;
  if Restored then
    DelTree(RemoveBackslashUnlessRoot(RuntimeBackupDirectory), True, True, True)
  else
    Log('The previous runtime could not be fully restored. Backup retained: ' + RuntimeBackupDirectory);
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if not ValidateInstallDirectory(Result) then
    exit;

#ifdef FrameworkDependent
  if not HasX64DesktopRuntime then
  begin
    Result := '请先安装 Microsoft .NET 8 Desktop Runtime（x64），或使用自包含安装器。';
    exit;
  end;
#endif

  if StopTileStart then
  begin
    Result := '';
#ifdef PatchDirectory
    if not PatchBaselineMatches then
      Result := '热补丁与现有版本或文件不匹配。请重新检查更新以使用完整安装包。';
#endif
#ifdef FrameworkDependent
    if not StageBundledRuntime then
      Result := '无法安全移出现有的随包运行库。安装已中止。';
#endif
  end
  else
    Result := '无法安全停止 TileStart。为避免影响 Windows 资源管理器，安装已中止。请重启 Windows 后重试。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
#ifdef FrameworkDependent
  if CurStep = ssPostInstall then
    RuntimeSwitchCommitted := True;
#endif
  if (CurStep = ssPostInstall) and not IsPortableUpdate then
    SaveStringToFile(ExpandConstant('{app}\.tilestart-installation'),
      'TileStart installation directory', False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopTileStart;
    if FileExists(ExpandConstant('{app}\TileStart.Host.exe')) then
    begin
      Exec(ExpandConstant('{app}\TileStart.Host.exe'), '--remove-startup-registration', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode);
      Exec(ExpandConstant('{app}\TileStart.Host.exe'), '--remove-nvidia-overlay-configuration', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode);
    end;
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TileStart');
  end;
end;
