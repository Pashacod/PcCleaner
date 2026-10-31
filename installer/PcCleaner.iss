; Установщик PcCleaner (Inno Setup 7). Собирается из publish.ps1 после dotnet publish.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F2B8E4A-3C1D-4B7E-9A55-2D8C0F1E7B39}
AppName=PcCleaner
AppVersion={#AppVersion}
AppVerName=PcCleaner {#AppVersion}
AppPublisher=Павел
DefaultDirName={autopf}\PcCleaner
DefaultGroupName=PcCleaner
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=PcCleaner-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Cleaner.App\Assets\app.ico
UninstallDisplayIcon={app}\PcCleaner.exe
CloseApplications=force

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "..\dist\PcCleaner.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PcCleaner"; Filename: "{app}\PcCleaner.exe"
Name: "{autodesktop}\PcCleaner"; Filename: "{app}\PcCleaner.exe"; Tasks: desktopicon

[Run]
; shellexec: программа требует администратора (манифест), а postinstall запускается от обычного
; пользователя — CreateProcess падает с кодом 740. Через ShellExecute Windows покажет UAC.
Filename: "{app}\PcCleaner.exe"; Description: "Запустить PcCleaner"; Flags: postinstall nowait skipifsilent shellexec
; Обновление из программы (/UPDATE=1): после тихой установки сразу запускаем новую версию.
Filename: "{app}\PcCleaner.exe"; Flags: nowait shellexec; Check: IsUpdate

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
Type: filesandordirs; Name: "{app}\logs"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
