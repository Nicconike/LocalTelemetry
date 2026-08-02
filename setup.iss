#ifndef AppVersion
#define AppVersion "1.0.0-beta"
#endif

#ifndef AppVersionInfoVersion
#define AppVersionInfoVersion "1.0.0.0"
#endif

[Setup]
AppName=LocalTelemetry
AppVersion={#AppVersion}
AppPublisher=Nicconike
AppPublisherURL=https://github.com/Nicconike/LocalTelemetry
AppSupportURL=https://github.com/Nicconike/LocalTelemetry/issues
AppUpdatesURL=https://github.com/Nicconike/LocalTelemetry/releases
VersionInfoVersion={#AppVersionInfoVersion}
VersionInfoProductVersion={#AppVersionInfoVersion}
VersionInfoDescription=LocalTelemetry Setup
VersionInfoCopyright=Copyright (C) 2026 Nicconike
VersionInfoOriginalFileName=LocalTelemetrySetup.exe
DefaultDirName={commonpf}\LocalTelemetry
DefaultGroupName=LocalTelemetry
OutputDir=.
OutputBaseFilename=LocalTelemetrySetup
Compression=lzma2
AppId={{LocalTelemetry_TaskbarApp}}
SetupIconFile=src\LocalTelemetry.App\app.ico
UninstallDisplayIcon={app}\LocalTelemetry.exe
WizardStyle=modern
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.19041
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=admin
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=LocalTelemetry.exe,LocalTelemetry.Notifier.exe

[Messages]
StatusExtractFiles=Extracting application files...
StatusCreateIcons=Creating Start Menu & Desktop shortcuts...

[Tasks]
Name: startmenuicon; Description: "Create a &Start Menu shortcut"; GroupDescription: "Shortcuts:"
Name: desktopicon; Description: "Create a &Desktop shortcut"; GroupDescription: "Shortcuts:"
Name: startup; Description: "Run LocalTelemetry at Windows &startup"; GroupDescription: "System options:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LocalTelemetry"; Filename: "{app}\LocalTelemetry.exe"; Tasks: startmenuicon
Name: "{commondesktop}\LocalTelemetry"; Filename: "{app}\LocalTelemetry.exe"; Tasks: desktopicon

[Run]
Filename: "schtasks.exe"; Parameters: "/Create /TN ""LocalTelemetry Startup"" /TR """"{app}\LocalTelemetry.exe"""" --minimized /SC ONLOGON /RL HIGHEST /F"; Tasks: startup; Flags: runhidden
Filename: "{app}\LocalTelemetry.exe"; Description: "{cm:LaunchProgram,LocalTelemetry}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
    MarkerPath: string;
begin
    if CurStep = ssPostInstall then
    begin
        // Create 'app.mode' marker file inside application directory.
        // AppSettings.cs checks for this file's existence to route settings to LocalAppData.
        MarkerPath := ExpandConstant('{app}\app.mode');
        SaveStringToFile(MarkerPath, 'normal', False);
    end;
end;

function IsAppRunning(const ExeName: string): Boolean;
var
    ResultCode: Integer;
begin
    Exec('cmd.exe', '/c tasklist /FI "IMAGENAME eq ' + ExeName + '" 2>NUL | find /I /N "' + ExeName + '" >NUL', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Result := (ResultCode = 0);
end;

function InitializeUninstall: Boolean;
var
    ResultCode: Integer;
begin
    Result := True;
    if IsAppRunning('LocalTelemetry.exe') or IsAppRunning('LocalTelemetry.Notifier.exe') then
    begin
        if MsgBox('LocalTelemetry is currently running in the background.' + #13#10 + #13#10 +
                  'To proceed with uninstallation, the application needs to be closed.' + #13#10 + #13#10 +
                  'Do you want to close LocalTelemetry and continue uninstalling?',
                  mbConfirmation, MB_YESNO) = IDYES then
        begin
            Exec('taskkill.exe', '/f /im LocalTelemetry.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            Exec('taskkill.exe', '/f /im LocalTelemetry.Notifier.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        end
        else
        begin
            Result := False; // Cancel uninstallation
        end;
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
    DataDir, TempDir, AppDir: string;
    ResultCode: Integer;
begin
    if CurUninstallStep = usUninstall then
    begin
        // Force kill any running instances of main app and background notifier before file deletion
        Exec('taskkill.exe', '/f /im LocalTelemetry.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('taskkill.exe', '/f /im LocalTelemetry.Notifier.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Delete all application files (LocalTelemetry.exe, LocalTelemetry.Notifier.exe, DLLs & runtime files) inside {app}
        AppDir := ExpandConstant('{app}');
        if DirExists(AppDir) then
            DelTree(AppDir, True, True, True);
    end;

    if CurUninstallStep = usPostUninstall then
    begin
        // Prompt user to remove AppData user settings & logs
        DataDir := ExpandConstant('{localappdata}\LocalTelemetry');
        TempDir := ExpandConstant('{localappdata}\Temp\PawnIo');

        if DirExists(DataDir) then
        begin
            if MsgBox('Do you also want to remove all saved settings, traffic logs and local hardware monitoring history?',
                      mbConfirmation, MB_YESNO) = IDYES then
            begin
                DelTree(DataDir, True, True, True);
                if DirExists(TempDir) then
                    DelTree(TempDir, True, True, True);
            end
            else
            begin
                MsgBox('Your settings, logs and monitoring history have been preserved at:' + #13#10 + #13#10 + DataDir,
                       mbInformation, MB_OK);
            end;
        end;
    end;
end;
