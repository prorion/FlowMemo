; FlowMemo 설치파일 (Inno Setup 6). 직접 컴파일하지 말고 installer\build.ps1로 만든다.
; 이 파일에 한글이 있으므로 UTF-8 BOM을 유지해야 한다.

#ifndef AppVersion
  #error build.ps1로 컴파일하세요 (AppVersion이 없습니다).
#endif
#ifndef PublishDir
  #error build.ps1로 컴파일하세요 (PublishDir가 없습니다).
#endif

[Setup]
; 업데이트와 제거를 같은 프로그램으로 인식하는 값이다. 바꾸지 않는다.
AppId={{5B1F7C2E-8A34-4D6B-9F21-3C7E0D4A6B18}
AppName=FlowMemo
AppVersion={#AppVersion}
AppVerName=FlowMemo {#AppVersion}
AppPublisher=prorion
AppPublisherURL=https://github.com/prorion/FlowMemo
VersionInfoVersion={#AppVersion}
OutputBaseFilename=FlowMemo-Setup-{#AppVersion}
SetupIconFile=..\FlowMemo.WpfUi\Images\icon.ico
UninstallDisplayIcon={app}\FlowMemo.exe
WizardStyle=modern
ShowLanguageDialog=no
DisableProgramGroupPage=yes
Compression=lzma2/ultra64
SolidCompression=yes

; 관리자 권한 없이 %LocalAppData%\Programs\FlowMemo에 설치한다.
; 메모 DB는 %AppData%\FlowMemo에 있으므로 업데이트·제거의 영향을 받지 않는다.
PrivilegesRequired=lowest
DefaultDirName={autopf}\FlowMemo
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; App.xaml.cs의 Release용 UniqueMutexName과 같아야 한다.
; 강제로 끄지 않고 트레이의 끝내기로 정상 종료하도록 안내한다.
AppMutex=79468726-ded4-4926-845d-b4bbf668ef27
CloseApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FlowMemo"; Filename: "{app}\FlowMemo.exe"
Name: "{autodesktop}\FlowMemo"; Filename: "{app}\FlowMemo.exe"; Tasks: desktopicon

[Registry]
; 앱의 '시작 프로그램' 설정이 작업 스케줄러를 쓸 수 없을 때 대신 만드는 값이다 (2.0.2 이하는 항상 여기에 썼다).
; 설치할 때는 건드리지 않고, 제거할 때만 지운다.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FlowMemo"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\FlowMemo.exe"; Description: "{cm:LaunchProgram,FlowMemo}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 앱의 '시작 프로그램' 설정이 만든 작업 스케줄러 작업을 지운다.
; 이름은 StartupRegistration.TaskNameFor와 같아야 한다: "FlowMemo (사용자 이름)". 없으면 schtasks가 실패만 하고 넘어간다.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""FlowMemo ({username})"" /F"; Flags: runhidden; RunOnceId: "DeleteStartupTask"
