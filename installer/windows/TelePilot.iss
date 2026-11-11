#define AppName "TelePilot"
#define AppExeName "TelePilot.exe"

#ifndef AppVersion
  #define AppVersion "2.1.1"
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\artifacts\release\" + AppVersion + "\" + Runtime + "\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release\" + AppVersion + "\" + Runtime
#endif

#if Runtime == "win-x64"
  #define ArchitecturesAllowedValue "x64compatible"
  #define MinWindowsVersion "10.0.19045"
#elif Runtime == "win-arm64"
  #define ArchitecturesAllowedValue "arm64"
  #define MinWindowsVersion "10.0.22000"
#else
  #error Unsupported Windows runtime. Use win-x64 or win-arm64.
#endif

[Setup]
AppId={{873edad3-c012-470d-a2e9-f3b66d236796}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=TelePilot
SetupIconFile=..\..\src\TelePilot.App\Assets\TelePilot.ico
DefaultDirName={autopf}\TelePilot
DefaultGroupName=TelePilot
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#ArchitecturesAllowedValue}
ArchitecturesInstallIn64BitMode={#ArchitecturesAllowedValue}
MinVersion={#MinWindowsVersion}
OutputDir={#OutputDir}
OutputBaseFilename=TelePilot-{#AppVersion}-{#Runtime}-setup
Compression=lzma2
SolidCompression=yes
ArchiveExtraction=full
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousLanguage=no
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany=TelePilot
VersionInfoDescription=TelePilot Setup
VersionInfoProductName=TelePilot
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
LaunchTelePilot=Launch TelePilot
polish.LaunchTelePilot=Uruchom TelePilot
LegacyPerUserInstallDetected=An older per-user development build of TelePilot was detected. Uninstall that build from Windows Settings > Apps > Installed apps, then run this installer again.%n%nYour settings and playlist sources will remain preserved.
polish.LegacyPerUserInstallDetected=Wykryto starszą wersję testową TelePilot zainstalowaną w profilu użytkownika. Odinstaluj ją w Ustawieniach Windows > Aplikacje > Zainstalowane aplikacje, a następnie uruchom ten instalator ponownie.%n%nUstawienia i źródła playlist pozostaną zachowane.
VlcCleanupFailed=Setup could not remove obsolete VLC files. Close TelePilot and run Setup again.
polish.VlcCleanupFailed=Instalator nie mógł usunąć starych plików VLC. Zamknij TelePilot i uruchom instalator ponownie.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#OutputDir}\vlc-files.txt"; Flags: dontcopy
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#if Runtime == "win-x64"
Source: "https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip"; DestName: "ffmpeg-9.0.1-essentials_build.zip"; DestDir: "{app}\tools\ffmpeg"; Hash: "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9"; ExternalSize: 321521840; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: ShouldInstallFfmpeg
#elif Runtime == "win-arm64"
; BtbN keeps its month-end builds for two years. Use a fixed archive, never the moving latest tag.
Source: "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n9.0.2-39-g7adfbe40dc-winarm64-lgpl-9.0.zip"; DestName: "ffmpeg-n9.0.2-39-g7adfbe40dc-winarm64-lgpl-9.0.zip"; DestDir: "{app}\tools\ffmpeg-arm64"; Hash: "d1981518b99a1c05d6b864200b1ac9459b09e2ee174feffe84b6e6bb0bfa79e2"; ExternalSize: 256581782; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: ShouldInstallFfmpeg
#endif

[Icons]
Name: "{group}\TelePilot"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\TelePilot"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
Type: files; Name: "{app}\installer-language.txt"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchTelePilot}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  LegacyPerUserUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{873edad3-c012-470d-a2e9-f3b66d236796}_is1';
  ReparsePointAttribute = $400;

function FfmpegExists(): Boolean;
#if Runtime == "win-x64"
var
  BundledDirectory: String;
begin
  BundledDirectory := ExpandConstant('{app}\tools\ffmpeg');
  Result := FileExists(BundledDirectory + '\ffmpeg.exe') or
    FileExists(BundledDirectory + '\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe');
  if not Result then
    Result := FileSearch('ffmpeg.exe', GetEnv('PATH')) <> '';
end;
#else
begin
  Result := FileExists(ExpandConstant('{app}\tools\ffmpeg-arm64\ffmpeg-n9.0.2-39-g7adfbe40dc-winarm64-lgpl-9.0\bin\ffmpeg.exe'));
end;
#endif

function ShouldInstallFfmpeg(): Boolean;
begin
  Result := not FfmpegExists();
  if Result then
    Log('FFmpeg was not found; Setup will download the verified archive.')
  else
    Log('FFmpeg is already available; skipping the download.');
end;

function LegacyInstallExistsInLoadedHives(const RootKey: Integer): Boolean;
var
  UserHives: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey, '', UserHives) then
    Exit;

  for Index := 0 to GetArrayLength(UserHives) - 1 do
  begin
    if RegKeyExists(RootKey, UserHives[Index] + '\' + LegacyPerUserUninstallKey) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function LegacyPerUserInstallExists(): Boolean;
begin
  Result := RegKeyExists(HKCU32, LegacyPerUserUninstallKey);
  if not Result and IsWin64 then
    Result := RegKeyExists(HKCU64, LegacyPerUserUninstallKey);
  if not Result then
    Result := LegacyInstallExistsInLoadedHives(HKU32);
  if not Result and IsWin64 then
    Result := LegacyInstallExistsInLoadedHives(HKU64);
end;

function InitializeSetup(): Boolean;
begin
  Result := not LegacyPerUserInstallExists();
  if not Result then
    MsgBox(ExpandConstant('{cm:LegacyPerUserInstallDetected}'), mbError, MB_OK);
end;

procedure RemoveObsoleteVlcFiles(const Root, RelativeDir: String; const CurrentFiles: TStringList);
var
  Entry: TFindRec;
  RelativePath, FullPath: String;
begin
  if not FindFirst(Root + RelativeDir + '*', Entry) then
    Exit;
  try
    repeat
      if (Entry.Name <> '.') and (Entry.Name <> '..') then
      begin
        RelativePath := RelativeDir + Entry.Name;
        FullPath := Root + RelativePath;
        if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if (Entry.Attributes and ReparsePointAttribute) = 0 then
            RemoveObsoleteVlcFiles(Root, RelativePath + '\', CurrentFiles);
        end
        else if ((CompareText(ExtractFileExt(Entry.Name), '.dll') = 0) or
                 (CompareText(Entry.Name, 'plugins.dat') = 0)) and (CurrentFiles.IndexOf(RelativePath) < 0) then
        begin
          Log('Removing obsolete VLC file: ' + FullPath);
          if not DeleteFile(FullPath) then
            RaiseException(ExpandConstant('{cm:VlcCleanupFailed}'));
        end;
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
end;

procedure CleanUpVlcUpgrade();
var
  Root, OtherRuntime: String;
  CurrentFiles: TStringList;
begin
  Root := ExpandConstant('{app}\libvlc\');
#if Runtime == "win-x64"
  OtherRuntime := Root + 'win-arm64';
#else
  OtherRuntime := Root + 'win-x64';
#endif
  if DirExists(OtherRuntime) and not DelTree(OtherRuntime, True, True, True) then
    RaiseException(ExpandConstant('{cm:VlcCleanupFailed}'));

  ExtractTemporaryFile('vlc-files.txt');
  CurrentFiles := TStringList.Create;
  try
    CurrentFiles.LoadFromFile(ExpandConstant('{tmp}\vlc-files.txt'));
    if CurrentFiles.Count = 0 then
      RaiseException(ExpandConstant('{cm:VlcCleanupFailed}'));
    RemoveObsoleteVlcFiles(Root + '{#Runtime}\', '', CurrentFiles);
  finally
    CurrentFiles.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LanguageCode: AnsiString;
begin
  if CurStep = ssPostInstall then
  begin
    CleanUpVlcUpgrade();
    if ActiveLanguage = 'polish' then
      LanguageCode := 'pl'
    else
      LanguageCode := 'en';
    if not SaveStringToFile(ExpandConstant('{app}\installer-language.txt'), LanguageCode, False) then
      Log('Could not save the installer language for first-run setup.');
  end;
end;
