#define AppName "TelePilot"
#define AppExeName "TelePilot.exe"

#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\artifacts\release\1.1.1\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release\1.1.1\win-x64"
#endif

#if Runtime == "win-x64"
  #define ArchitectureName "x64"
  #define ArchitecturesAllowedValue "x64compatible"
#elif Runtime == "win-arm64"
  #define ArchitectureName "arm64"
  #define ArchitecturesAllowedValue "arm64"
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
MinVersion=10.0.19045
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
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany=TelePilot
VersionInfoDescription=Instalator TelePilot
VersionInfoProductName=TelePilot
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
LegacyPerUserInstallDetected=An older per-user development build of TelePilot was detected. Uninstall that build from Windows Settings > Apps > Installed apps, then run this installer again.%n%nYour settings and playlist sources will remain preserved.
polish.LegacyPerUserInstallDetected=Wykryto starszą wersję testową TelePilot zainstalowaną w profilu użytkownika. Odinstaluj ją w Ustawieniach Windows > Aplikacje > Zainstalowane aplikacje, a następnie uruchom ten instalator ponownie.%n%nUstawienia i źródła playlist pozostaną zachowane.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Versioned archive from the publisher's official GitHub mirror; rotating packages/ links can disappear.
Source: "https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip"; DestName: "ffmpeg-9.0.1-essentials_build.zip"; DestDir: "{app}\tools\ffmpeg"; Hash: "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9"; ExternalSize: 321521840; Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion; Check: ShouldInstallFfmpeg

[Icons]
Name: "{group}\TelePilot"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\TelePilot"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Uruchom TelePilot"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  LegacyPerUserUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{873edad3-c012-470d-a2e9-f3b66d236796}_is1';

function FfmpegExists(): Boolean;
var
  BundledDirectory: String;
begin
  BundledDirectory := ExpandConstant('{app}\tools\ffmpeg');
  Result := FileExists(BundledDirectory + '\ffmpeg.exe') or
    FileExists(BundledDirectory + '\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe');
  if not Result then
    Result := FileSearch('ffmpeg.exe', GetEnv('PATH')) <> '';
end;

function ShouldInstallFfmpeg(): Boolean;
begin
  Result := not FfmpegExists();
  if Result then
    Log('FFmpeg nie został znaleziony; instalator pobierze zweryfikowaną paczkę.')
  else
    Log('FFmpeg jest już dostępny; pobieranie pominięte.');
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
