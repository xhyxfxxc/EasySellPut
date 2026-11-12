; Per-user installer for 美丽稻谷卖方绿泡群专用终端.
; Compile with tools\InnoSetup\ISCC.exe. No "tyu" in visible names.
#define MyAppName "美丽稻谷卖方绿泡群专用终端"
#define MyAppNameEn "Meili Daogu Seller Green-Bubble Group Terminal"
#define MyAppPublisher "美丽稻谷"
#define MyAppVersion "0.4.1"
#define MyAppExeName "美丽稻谷卖方绿泡群专用终端.exe"
#define MyAppAumid "MeiliDaogu.SellerTerminal"

[Setup]
AppId={{7E4B2C91-8F06-4D3A-B5E1-9A6C0D2F8E33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Personal use
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName}
VersionInfoCopyright=Personal use
DefaultDirName={localappdata}\Programs\美丽稻谷终端
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\dist
OutputBaseFilename=美丽稻谷卖方绿泡群专用终端-安装程序
SetupIconFile=..\..\src\Meili.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=yes
MinVersion=10.0.19041
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no
LicenseFile=license-zh.txt

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"; LicenseFile: "license-zh.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "license-en.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\payload\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "{#MyAppAumid}"; Comment: "{#MyAppName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "{#MyAppAumid}"; Tasks: desktopicon; Comment: "{#MyAppName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"