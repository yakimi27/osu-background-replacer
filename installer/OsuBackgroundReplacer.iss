; Inno Setup script for osu! Background Replacer (unpackaged self-contained WinUI 3)
; Build via:
;   "D:\Apps\Inno Setup 6\ISCC.exe" installer\OsuBackgroundReplacer.iss
; Optional overrides:
;   /DMyArch=x64|x86|arm64
;   /DMyAppVersion=2.0.0
;   /DPublishDir=path\to\publish

#ifndef MyArch
  #define MyArch "x64"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif

#ifndef Flavor
  #define Flavor "self-contained"
#endif

#if Flavor == "framework-dependent"
  #define FlavorSuffix "-framework-dependent"
  #define DefaultPublishSubdir "publish-fd"
#else
  #define FlavorSuffix ""
  #define DefaultPublishSubdir "publish"
#endif

#ifndef PublishDir
  #define PublishDir "..\OsuBackgroundReplacerMain\bin\Release\net11.0-windows10.0.19041.0\win-" + MyArch + "\" + DefaultPublishSubdir
#endif

#define MyAppName "osu! Background Replacer"
#define MyAppPublisher "yakimi27"
#define MyAppURL "https://github.com/yakimi27/osu-background-replacer"
#define MyAppExeName "OsuBackgroundReplacer.exe"

[Setup]
AppId={{01FFE073-0E10-401F-A468-19ACFBEF6F6A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\out
OutputBaseFilename=OsuBackgroundReplacer-{#MyAppVersion}-win-{#MyArch}{#FlavorSuffix}-setup
SetupIconFile=..\OsuBackgroundReplacerMain\Assets\obr_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

; Per-user install by default so WinUI 3 pickers (FolderPicker/FileOpenPicker/FileSavePicker)
; and Drag & Drop are not broken by UAC elevation / UIPI.
PrivilegesRequired=lowest

#if MyArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif MyArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
