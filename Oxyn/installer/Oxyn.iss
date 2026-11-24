#define MyAppName "OXYN"
#define MyAppVersion "2.3.0"
#define MyAppPublisher "Fenix"
#define MyAppExeName "Oxyn.exe"

[Setup]
AppId={{DAFC493C-D04F-4A9C-8B9A-AED16CD8DF67}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; Version lue par la mise à jour intégrée d'OXYN (Paramètres > Général).
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
; Même AppId que SysDeck (ancien nom) : OXYN remplace l'installation existante, mais dans son propre dossier.
UsePreviousAppDir=no
UsePreviousGroup=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Installation par utilisateur : aucune élévation admin n'est demandée, ni à
; l'installation ni au lancement (le compte Windows courant est un compte
; standard, pas administrateur).
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=OxynSetup
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
Name: "startupicon"; Description: "Lancer OXYN au démarrage de Windows"; GroupDescription: "Options :"; Flags: unchecked
Name: "sensors"; Description: "Tous les capteurs : températures, ventilateurs, tensions (installe le pilote PawnIO, demande une autorisation administrateur)"; GroupDescription: "Options :"

[Files]
Source: "..\bin\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Pont des capteurs : copié dans ProgramData et lancé par une tâche planifiée élevée (voir [Code]).
Source: "..\..\Oxyn.SensorBridge\bin\Release\net472\*"; DestDir: "{app}\SensorBridge"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Tools\InstallSensorBridge.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
; Installateur officiel du pilote PawnIO (namazso, signé), lancé en silence par InstallSensorBridge.ps1.
Source: "PawnIO\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: sensors

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[InstallDelete]
; Restes de l'ancien nom (SysDeck) : dossier du programme et raccourcis. Les réglages (%AppData%\SysDeck) ne sont
; PAS supprimés : OXYN les déplace lui-même vers %AppData%\Oxyn au premier lancement.
Type: filesandordirs; Name: "{localappdata}\Programs\SysDeck"
Type: filesandordirs; Name: "{userprograms}\SysDeck"
Type: files; Name: "{userdesktop}\SysDeck.lnk"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SysDeck"; Flags: deletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OXYN"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent
; Mise à jour depuis OXYN (installation silencieuse) : OXYN est relancé tout seul.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
; Supprime la tâche et le pont des capteurs, et l'ancienne tâche de température (création élevée, donc suppression élevée :
; une seule demande UAC). Le pilote PawnIO reste installé : d'autres logiciels peuvent s'en servir (désinstallable à part).
Filename: "{cmd}"; Parameters: "/c schtasks /End /TN OXYN-SensorBridge & taskkill /IM Oxyn.SensorBridge.exe /F & schtasks /Delete /TN OXYN-SensorBridge /F & schtasks /Delete /TN OXYN-CpuTempBridge /F & rmdir /s /q ""C:\ProgramData\Oxyn\SensorBridge"""; Flags: shellexec runhidden waituntilterminated; Verb: "runas"; RunOnceId: "DeleteSensorBridge"

[Code]
procedure InstallSensorBridge();
var
  Params: String;
  ResultCode: Integer;
begin
  // Compte de la session (domaine\nom), lu avant l'élévation : élevé, le script pourrait tourner
  // sous un autre compte administrateur.
  Params := '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' +
    ExpandConstant('{app}\Tools\InstallSensorBridge.ps1') + '" -User "' +
    ExpandConstant('{%USERDOMAIN}\{%USERNAME}') + '" -Source "' +
    ExpandConstant('{app}\SensorBridge') + '" -PawnIO "' +
    ExpandConstant('{tmp}\PawnIO_setup.exe') + '"';

  if not ShellExec('runas', 'powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
     or (ResultCode <> 0) then
    MsgBox('Le pont des capteurs n''a pas pu être installé (autorisation refusée ou erreur).' + #13#10 +
      'OXYN fonctionne quand même, sans la température du processeur ni les capteurs de la carte mère ; ' +
      'relancez l''installation pour réessayer (détails : C:\ProgramData\Oxyn\install-sensors.log).',
      mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Pas pendant une mise à jour silencieuse : elle demanderait une autorisation administrateur sans prévenir.
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('sensors') and not WizardSilent then
    InstallSensorBridge();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Ferme toute instance en cours avant de copier les fichiers, pour éviter
  // une erreur de fichier verrouillé si OXYN tourne déjà.
  Exec('taskkill.exe', '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM SysDeck.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);   // ancien nom
  Result := '';
end;
