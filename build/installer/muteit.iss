; MUTEit installer (Inno Setup 6.x).
;
; Build:
;   Automatic: every Release build in Visual Studio (or "dotnet build -c Release") runs ISCC after the build –
;   see the target "MuteitInstaller" in src\MUTEit.App\MUTEit.App.csproj. Output: artifacts\muteit_v<version>_setup.exe.
;
;   Manual: build the Release configuration, then open this file in the Inno Setup Compiler and press Compile (Ctrl+F9).
;   Input is the normal (self-contained) build folder:
;     src\MUTEit.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\
;   Another folder (e.g. a copy of the build output) can be passed with /DSourceDir=<folder>.
;
;   The version is read from MUTEit.exe (product version), so it never has to be passed by hand.
;
; English is the default language; German is offered in the language dialog.
; Targets: Windows 10 / 11, 64-bit.
;
; Default: installation for all users (Program Files, admin rights). The dialog at the start also offers an installation
; for the current user only (%LocalAppData%\Programs\MUTEit, no admin rights). "Start with Windows" is always written
; for the user who runs the setup (HKCU Run).

#ifndef SourceDir
  #define SourceDir SourcePath + "..\..\src\MUTEit.App\bin\Release\net10.0-windows10.0.19041.0\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir SourcePath + "..\..\artifacts"
#endif

#define AppExe AddBackslash(SourceDir) + "MUTEit.exe"
#if !FileExists(AppExe)
  #error MUTEit.exe not found in the build folder. Build the Release configuration first (Visual Studio: Release, Build) or pass /DSourceDir=<folder>.
#endif

#ifndef AppVersion
  #define AppVersion GetStringFileInfo(AppExe, "ProductVersion")
#endif
#if AppVersion == ""
  #undef AppVersion
  #define AppVersion GetVersionNumbersString(AppExe)
#endif

[Setup]
AppId={{8F4E9A12-3B7C-4D5E-9F1A-2C6B8D0E4F3A}
AppName=MUTEit
AppVersion={#AppVersion}
AppVerName=MUTEit {#AppVersion}
AppPublisher=sksdesign | complicatiion
AppPublisherURL=https://sksdesign.de
AppSupportURL=https://github.com/complicatiion/MUTEit
AppCopyright=Copyright (C) 2026 Sven Klocke
DefaultDirName={autopf}\MUTEit
DefaultGroupName=MUTEit
DisableProgramGroupPage=yes
LicenseFile=..\..\licenses\LICENSE.md
OutputDir={#OutputDir}
OutputBaseFilename=muteit_v{#AppVersion}_setup
SetupIconFile=..\..\src\MUTEit.App\Assets\muteit.ico
UninstallDisplayIcon={app}\MUTEit.exe
UninstallDisplayName=MUTEit {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=images\wizard-100.bmp,images\wizard-125.bmp,images\wizard-150.bmp,images\wizard-175.bmp,images\wizard-200.bmp,images\wizard-225.bmp,images\wizard-250.bmp
WizardSmallImageFile=images\wizard-small-100.bmp,images\wizard-small-125.bmp,images\wizard-small-150.bmp,images\wizard-small-175.bmp,images\wizard-small-200.bmp,images\wizard-small-225.bmp,images\wizard-small-250.bmp
WizardImageStretch=no
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; All users by default (system-wide, UAC prompt); "only for me" can be chosen in the dialog or with /CURRENTUSER.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
CloseApplications=no
RestartApplications=no
; Language dialog with English preselected (first entry; no detection from the Windows language)
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
; First entry = default
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.GroupExtras=Additional options:
de.GroupExtras=Weitere Optionen:
en.TaskAutostart=Start MUTEit with Windows (system tray, current user)
de.TaskAutostart=MUTEit mit Windows starten (System-Tray, aktueller Benutzer)
en.TaskOverlay=Show the desktop overlay (microphone state above all windows)
de.TaskOverlay=Desktop-Overlay anzeigen (Mikrofonstatus über allen Fenstern)
en.TaskTaskbar=Show the taskbar button (one click mutes or unmutes)
de.TaskTaskbar=Taskleisten-Schalter anzeigen (ein Klick schaltet stumm oder aktiv)
en.TaskDesktopIcon=Create a desktop shortcut (opens the settings)
de.TaskDesktopIcon=Desktop-Verknüpfung erstellen (öffnet die Einstellungen)
en.LaunchMuteit=Start MUTEit
de.LaunchMuteit=MUTEit starten
en.SettingsShortcut=MUTEit – Settings
de.SettingsShortcut=MUTEit – Einstellungen
en.PurgeQuestion=Also delete your MUTEit settings?
de.PurgeQuestion=Auch deine MUTEit-Einstellungen löschen?

[Tasks]
; Autostart and desktop shortcut are on by default, overlay and taskbar button off (same as the app defaults).
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:GroupExtras}"
Name: "overlay"; Description: "{cm:TaskOverlay}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked
Name: "taskbar"; Description: "{cm:TaskTaskbar}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupExtras}"

[Files]
; Build output without debug symbols and without portable data
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,MUTEit.portable,data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MUTEit"; Filename: "{app}\MUTEit.exe"
Name: "{group}\{cm:SettingsShortcut}"; Filename: "{app}\MUTEit.exe"; Parameters: "--settings"
Name: "{autodesktop}\MUTEit"; Filename: "{app}\MUTEit.exe"; Parameters: "--settings"; Tasks: desktopicon

[Run]
; Autostart is a per-user Run key: written as the user who started the setup, not as an elevated admin.
Filename: "{app}\MUTEit.exe"; Parameters: "--register-autostart"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: autostart
Filename: "{app}\MUTEit.exe"; Parameters: "--unregister-autostart"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: not autostart
; Overlay and taskbar button: only switched on (an update never switches off what was enabled in the app).
Filename: "{app}\MUTEit.exe"; Parameters: "--setup overlay=1"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: overlay
Filename: "{app}\MUTEit.exe"; Parameters: "--setup taskbar=1"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: taskbar
Filename: "{app}\MUTEit.exe"; Description: "{cm:LaunchMuteit}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Quit the tray cleanly (it applies the configured exit state of the microphone), then remove the autostart entry.
Filename: "{app}\MUTEit.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitTray"
Filename: "{app}\MUTEit.exe"; Parameters: "--unregister-autostart"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAutostart"

[Code]
{ Quit a running tray before files are replaced (never "taskkill": the microphone keeps its configured exit state). }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\MUTEit.exe')) then
    Exec(ExpandConstant('{app}\MUTEit.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) and
     (MsgBox(CustomMessage('PurgeQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{userappdata}\MUTEit'), True, True, True);
end;
