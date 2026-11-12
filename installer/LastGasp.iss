; Builds LastGasp-Setup.exe. The GitHub workflow passes AppVersion and PublishDir;
; locally: iscc /DAppVersion=0.0.1 /DPublishDir=..\publish installer\LastGasp.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "Last Gasp"
#define AppExe "LastGasp.exe"

[Setup]
AppId={{6C1E2F7A-3B5D-4C8E-9A1F-2D7B4E6C9A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SIL Global
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user only, so installing needs no admin rights.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\output
OutputBaseFilename=LastGasp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"
