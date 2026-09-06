; ─────────────────────────────────────────────────────────────────────────────
; RetroBat Led Manager - installeur de BORNE (Inno Setup)
; Installe le plugin dans <RetroBat>\plugins\LedManager, branche son hook
; EmulationStation, et - via apiexpose-bootstrap.iss - installe APIExpose dans le
; dossier frère plugins\APIExpose s'il manque (APIExpose déjà présent = intact).
; Compilation : ISCC.exe installer\LedManager.iss
; ─────────────────────────────────────────────────────────────────────────────

#define AppName "RetroBat Led Manager"
#define AppVersion "1.5.0"
#define AppExe "LedManager.exe"

[Setup]
AppId={{3F8D1B94-6A2C-4D77-B0E5-LEDMANAGER01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NelfeTech
AppPublisherURL=https://www.nelfetech.com
DefaultDirName={code:GetPluginInstallDir|LedManager}
DirExistsWarning=no
AppendDefaultDirName=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=LedManager-Setup
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
french.SelectDirDesc=Choisissez le dossier plugins\LedManager de VOTRE RetroBat (ex. D:\RetroBat\plugins\LedManager).

[Files]
Source: "..\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "\src\*,\docs\*,\wiki\*,\media\*,\state\*,\artifacts\*,\dist\*,\installer\*,\tests\*,\.git\*,\.github\*,\.log\*,\.cache\*,\.versioning\*,\.archive\*,\.temp\*,\.graceful_exit\*,\obj\*,\bin\*,\site\*,\.gitignore,\.gitattributes,\mkdocs.yml,\LedManager.sln,\Directory.Build.props,\build.bat,\build-LedManager.bat,\build-PicoCommandSender.bat,\build-Setup.bat,\release.ps1,\config.ini,\config.ini.bak,\tools\wiki-panels-generator\*,CAHIER*,*.log,*.pdb,*.lib,__pycache__\*,*.pyc,\tools\*.ps1,\tools\*.py,*.bak"

; Dépendance APIExpose (dossier frère) - DÉTECTION (fournit ApiExposeInstalled) ;
; on avertit dans [Code] si absent (installée par APIExpose-Cabinet-Setup, pas ici)
#include "..\..\APIExpose\installer\apiexpose-bootstrap.iss"
#include "..\..\APIExpose\installer\retrobat-detect.iss"

[Dirs]
Name: "{app}\state"; Flags: uninsneveruninstall

[Run]
Filename: "{app}\install-es-start-hook.bat"; WorkingDir: "{app}"; Description: "Démarrer Led Manager avec RetroBat (hook EmulationStation)"; Flags: postinstall skipifsilent
Filename: "{app}\LedManagerSetup.exe"; WorkingDir: "{app}"; Description: "Ouvrir Led Manager Setup maintenant"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopLed"
Filename: "{app}\uninstall-es-start-hook.bat"; WorkingDir: "{app}"; Flags: runhidden; RunOnceId: "UnhookLed"

[Code]
// APIExpose (dossier frère) est requis : on ne le bundle pas (dossier complet +
// Data Pack, via APIExpose-Cabinet-Setup) ; on avertit s'il manque, sans bloquer.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    WarnIfNotRetroBat();
    if not ApiExposeInstalled() then
      MsgBox('APIExpose n''est pas installé à côté (plugins\APIExpose).'#13#10#13#10
        + 'Led Manager en a besoin pour fonctionner. Lancez d''abord'#13#10
        + 'APIExpose-Cabinet-Setup.exe - l''installation continue quand même.',
        mbInformation, MB_OK);
  end;
end;
