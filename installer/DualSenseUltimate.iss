; Inno Setup 6.2+ script for DualSense Ultimate.
; Build: scripts\publish.ps1 (publishes the app to artifacts\publish and compiles this script).

#define AppName "DualSense Ultimate"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "DualSenseUltimate.exe"
#define ViGEmUrl "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe"
#define HidHideUrl "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"

[Setup]
AppId={{6F1C2C1E-7B0B-4B7C-9C39-0D5A1E3F4A21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DualSense Ultimate contributors
AppPublisherURL=https://github.com/diego233l/Dualsense-Ultimate
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default: no administrator rights needed. The user may choose "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=DualSenseUltimate-Setup-{#AppVersion}
SetupIconFile=..\src\DualSenseUltimate.App\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Detects a running instance (same name as the single-instance mutex in Program.cs) and asks to close it.
AppMutex=DualSenseUltimate.SingleInstance
CloseApplications=yes
LicenseFile=..\LICENSE

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar DualSense Ultimate con Windows (minimizado)"; GroupDescription: "Inicio:"; Flags: unchecked
Name: "vigem"; Description: "Descargar e instalar ViGEmBus (necesario solo para emular mandos Xbox / DualShock 4)"; GroupDescription: "Controladores opcionales (requieren permisos de administrador):"; Flags: unchecked
Name: "hidhide"; Description: "Descargar e instalar HidHide (oculta el mando físico al emular; evita la doble entrada)"; GroupDescription: "Controladores opcionales (requieren permisos de administrador):"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\USER_MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DualSenseUltimate"; ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{tmp}\ViGEmBus_Setup.exe"; Parameters: "/passive /norestart"; StatusMsg: "Instalando ViGEmBus..."; Flags: shellexec waituntilterminated skipifdoesntexist; Tasks: vigem
Filename: "{tmp}\HidHide_Setup.exe"; Parameters: "/passive /norestart"; StatusMsg: "Instalando HidHide..."; Flags: shellexec waituntilterminated skipifdoesntexist; Tasks: hidhide
Filename: "{app}\{#AppExe}"; Description: "Iniciar {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Un-hides any controller hidden through HidHide and removes the autostart entry, so nothing is left orphaned.
Filename: "{app}\{#AppExe}"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "DsuCleanup"

[Code]
var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (WizardIsTaskSelected('vigem') or WizardIsTaskSelected('hidhide')) then
  begin
    DownloadPage.Clear;
    if WizardIsTaskSelected('vigem') then DownloadPage.Add('{#ViGEmUrl}', 'ViGEmBus_Setup.exe', '');
    if WizardIsTaskSelected('hidhide') then DownloadPage.Add('{#HidHideUrl}', 'HidHide_Setup.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        // The drivers are optional: continue without them and tell the user how to get them later.
        MsgBox('No se pudieron descargar los controladores opcionales. Puedes instalarlos más tarde desde Ajustes -> Controladores.' + #13#10 + GetExceptionMessage, mbInformation, MB_OK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\DualSenseUltimate');
    if DirExists(DataDir) then
      if MsgBox('¿Quieres eliminar también tus perfiles y ajustes?' + #13#10 + DataDir + #13#10#13#10 +
                'Elige «No» para conservarlos por si vuelves a instalar la aplicación.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    MsgBox('DualSense Ultimate se ha desinstalado.' + #13#10#13#10 +
           'Si instalaste ViGEmBus o HidHide y ya no los usa ninguna otra aplicación (DS4Windows, Sunshine...), ' +
           'puedes quitarlos desde «Aplicaciones instaladas» de Windows.', mbInformation, MB_OK);
  end;
end;
