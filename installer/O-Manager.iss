; O-Manager installer (Inno Setup 6, free: https://jrsoftware.org/isdl.php)
;
; Makes one file, O-Manager-Setup.exe, that installs the game, adds Start menu and desktop shortcuts
; and an uninstaller (Settings > Apps). Normally you never run this by hand:
; Unity menu Tools > O-Manager > Build release builds the game and then compiles this script.
;
; By hand:  ISCC.exe /DAppVersion=1.0.4 /DBuildDir="C:\...\Builds\Windows" installer\O-Manager.iss
;
; The game is installed per user in %LOCALAPPDATA%\Programs\O-Manager (like Discord or VS Code), so
; neither the install nor the automatic updates need the "Do you want to allow this app to make changes"
; box. Saved runners and money live in the registry (PlayerPrefs); updates never touch them, and uninstalling
; asks whether to delete them (CurUninstallStepChanged below).
;
; The game's own updater (Assets/Scripts/UI/GameUpdater.cs) runs this setup with
;   /SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1
; Setup then waits for the game to close, replaces the files and starts the game again.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\Builds\Windows"
#endif
#ifndef OutputDir
  #define OutputDir "..\Builds\Release"
#endif

#ifndef Threads
  #define Threads "4"
#endif
#define AppName "O-Manager"
#define AppExe "O-Manager.exe"

[Setup]
; Never change AppId: it is how Windows knows a new setup is the same game (an update, not a second copy).
AppId={{95F24B93-4EEC-40B4-BEEC-94221C11EB42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=O-Manager
AppPublisherURL=https://o-manager.se
AppSupportURL=https://o-manager.se
AppUpdatesURL=https://o-manager.se
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
; Per user, no admin rights needed ({autopf} becomes %LOCALAPPDATA%\Programs)
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=O-Manager-Setup
SetupIconFile=O-Manager.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; lzma2/normal (2026-11-26): max took 5 min for 1.0.10 even on 4 cores; normal is about twice as fast and the
; setup is only a few percent bigger. Threads comes from Build release (the computer's number of cores).
Compression=lzma2/normal
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads={#Threads}
WizardStyle=modern
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(AddBackslash(CompilerPath) + "Languages\Swedish.isl")
Name: "swedish"; MessagesFile: "compiler:Languages\Swedish.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Remove the old game data first, so maps and files taken out of the game do not linger after an update
Type: filesandordirs; Name: "{app}\O-Manager_Data"
Type: filesandordirs; Name: "{app}\MonoBleedingEdge"

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*_BurstDebugInformation_DoNotShip,*_BackUpThisFolder_ButDontShipItWithYourGame,*.pdb"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; A normal install: the "Start O-Manager" tick box on the last page
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An update started from inside the game: start the game again by itself
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsGameUpdate

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function IsGameUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

// The game starts this setup and then quits. Give it up to 20 seconds to close its window,
// so its files are free to be replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Waited: Integer;
begin
  Result := '';
  if IsGameUpdate then
  begin
    Waited := 0;
    while (FindWindowByWindowName('{#AppName}') <> 0) and (Waited < 20000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  end;
end;

// Uninstalling asks whether the saved progress should go too (runners, money, player name, lobby name and the
// cloud sign-in). Saves live in the registry (Unity PlayerPrefs) and outlive an uninstall, so without this a
// reinstall on the same computer starts with everything from before. Updates never uninstall, so they keep it.
// Own courses and maps (in %USERPROFILE%\AppData\LocalLow\DefaultCompany\O-Manager) are left alone: the Unity
// Editor uses the same folder on a developer's computer.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    if MsgBox('Also delete your saved progress (runners, money, player name and lobby name)?' + #13#10#13#10 +
              'Choose No to keep it for the next time you install the game.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\DefaultCompany\O-Manager');
    end;
  end;
end;
