; Desktop Deck setup. Built by DesktopDeck.Installer.csproj, which passes in AppVersion, SourceDir (the published app) and OutputDir.
; Per-user install: no administrator prompt, nothing outside the user's own folders.
#ifndef AppVersion
  #error Build this with DesktopDeck.Installer.csproj (it passes AppVersion, SourceDir and OutputDir).
#endif

#define AppName "Desktop Deck"
#define AppExe "DesktopDeck.exe"
; The app's own data (settings, layouts, sign-ins) lives in %LOCALAPPDATA%\DesktopDeck. That is not the install folder.
#define DataFolder "{localappdata}\DesktopDeck"

[Setup]
; Identifies this program to Windows for upgrades and uninstalling. It must never change, or an upgrade installs next to the old version.
AppId={{08A2A668-3080-4FA0-A87A-D4CC9F54E969}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\src\DesktopDeck\DesktopDeck.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=DesktopDeckSetup-{#AppVersion}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
Compression=lzma2
SolidCompression=yes
#ifndef NoAppMutex
; The running deck holds this mutex (Local\DesktopDeck); Setup and the uninstaller then ask for it to be closed.
; NoAppMutex exists for testing on a PC whose own deck is running.
AppMutex=DesktopDeck
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent

[Code]
var
  DeleteUserData: Boolean;

{ True when a Desktop Deck started from this folder is running. Asks Windows (WMI), so a deck running from somewhere else does not count. }
function DeckRunningFrom(Dir: String): Boolean;
var
  Locator, Service, Processes, Found: Variant;
  Path: String;
  I: Integer;
begin
  Result := False;
  Path := AddBackslash(Dir) + '{#AppExe}';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\cimv2');
    Processes := Service.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''{#AppExe}''');
    for I := 0 to Processes.Count - 1 do
    begin
      Found := Processes.ItemIndex(I).ExecutablePath;
      { A process Windows will not describe (another user's, say) has no path; it is not ours. }
      if (VarType(Found) = varOleStr) and SameText(Found, Path) then
      begin
        Result := True;
        Exit;
      end;
    end;
  except
    { WMI is not available: fall back on the mutex, which also covers a deck running from another folder. }
    Result := CheckForMutexes('DesktopDeck');
  end;
end;

{ Asks a running deck to close itself (it saves first) and waits up to ten seconds. Never forces it: if it stays open, Setup's own prompt takes over. }
procedure CloseRunningDeck(Dir: String);
var
  Exe: String;
  ResultCode, Waited: Integer;
begin
  Exe := AddBackslash(Dir) + '{#AppExe}';
  if not FileExists(Exe) then Exit;
  if not DeckRunningFrom(Dir) then Exit;
  Log('Asking the running Desktop Deck to close.');
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Waited := 0;
  while DeckRunningFrom(Dir) and (Waited < 10000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
end;

{ Setup checks AppMutex right at its start, before any page, so on an upgrade the old deck is closed here, in the folder it was installed to. }
function InitializeSetup: Boolean;
var
  Previous: String;
begin
  Result := True;
  Log('InitializeSetup: looking for an earlier install.');
  if RegQueryStringValue(HKCU, ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1'), 'Inno Setup: App Path', Previous) then
    CloseRunningDeck(Previous);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningDeck(ExpandConstant('{app}'));
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  CloseRunningDeck(ExpandConstant('{app}'));
  { One question, and the answer is No for a silent uninstall: settings and sign-ins are only removed when asked for. }
  DeleteUserData := (not UninstallSilent) and
    (MsgBox('Also delete your Desktop Deck settings, layouts and saved sign-ins?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value, Data, App: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  { Start at login: remove the entry only when it points at this install, so uninstalling one copy keeps another copy's entry. }
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopDeck', Value) then
    if SameText(Value, '"' + ExpandConstant('{app}\{#AppExe}') + '"') then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopDeck');

  if DeleteUserData then
  begin
    Data := ExpandConstant('{#DataFolder}');
    App := ExpandConstant('{app}');
    { Never delete a data folder that holds the program itself. }
    if (CompareText(Copy(App + '\', 1, Length(Data) + 1), Data + '\') <> 0) and DirExists(Data) then
      DelTree(Data, True, True, True);
  end;
end;
