#ifndef AppVersion
#define AppVersion "1.0.0-beta"
#endif

#ifndef AppVersionInfoVersion
#define AppVersionInfoVersion "1.0.0.0"
#endif

[Setup]
AppName=LocalTelemetry
AppVerName=LocalTelemetry
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
Filename: "{app}\LocalTelemetry.exe"; Description: "{cm:LaunchProgram,LocalTelemetry}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\LocalTelemetry"; Check: RetryDeleteData

[Code]
const
    StartupTaskName = 'LocalTelemetry Startup';
    PawnIoCleanupScript =
        '$log = $env:ProgramData + ''\LocalTelemetry\pawnio_cleanup.log''' + #13#10 +
        '''[PawnIO cleanup] started: '' + (Get-Date) | Set-Content $log' + #13#10 +
        '$repo = ''C:\Windows\System32\DriverStore\FileRepository''' + #13#10 +
        'if ($env:PROCESSOR_ARCHITECTURE -eq ''x86'' -or $env:PROCESSOR_ARCHITECTURE -eq ''ARM64'') { $pnp = Join-Path $env:windir ''Sysnative\pnputil.exe''; if (-not (Test-Path $pnp)) { $pnp = Join-Path $env:windir ''System32\pnputil.exe'' } } else { $pnp = Join-Path $env:windir ''System32\pnputil.exe'' }' + #13#10 +
        '''[PawnIO cleanup] pnputil: '' + $pnp + '' exists: '' + (Test-Path $pnp) | Add-Content $log' + #13#10 +
        'Start-Sleep -Seconds 2' + #13#10 +
        'for ($attempt = 1; $attempt -le 5; $attempt++) {' + #13#10 +
        '    $dirs = @(Get-ChildItem $repo -Directory -Filter ''pawnio.inf*'' -ErrorAction SilentlyContinue)' + #13#10 +
        '    if ($dirs.Count -eq 0) { ''[PawnIO cleanup] no package found'' | Add-Content $log; break }' + #13#10 +
        '    foreach ($d in $dirs) {' + #13#10 +
        '        $inf = Join-Path $d.FullName ''pawnio.inf''' + #13#10 +
        '        ''[PawnIO cleanup] deleting: '' + $inf + '' (attempt '' + $attempt + '')'' | Add-Content $log' + #13#10 +
        '        & $pnp /delete-driver $inf /uninstall /force *>&1 | Add-Content $log' + #13#10 +
        '        ''[PawnIO cleanup] pnputil exit code: '' + $LASTEXITCODE | Add-Content $log' + #13#10 +
        '    }' + #13#10 +
        '    if (-not (Test-Path (Join-Path $repo ''pawnio.inf*''))) { ''[PawnIO cleanup] package removed'' | Add-Content $log; break }' + #13#10 +
        '    ''[PawnIO cleanup] retrying...'' | Add-Content $log' + #13#10 +
        '    Start-Sleep -Seconds 3' + #13#10 +
        '}' + #13#10 +
        'exit 0';

var
    DataDeletePending: Boolean;

function CreateStartupTask: Boolean;
var
    Scheduler, RootFolder, Task, Trigger, Action: Variant;
begin
    Result := False;
    try
        Scheduler := CreateOleObject('Schedule.Service');
        Scheduler.Connect;
        RootFolder := Scheduler.GetFolder('\');
        Task := Scheduler.NewTask(0);
        Task.RegistrationInfo.Description := 'Starts LocalTelemetry at user logon (silent, highest privileges).';
        Task.Principal.LogonType := 3;  // TASK_LOGON_INTERACTIVE_TOKEN
        Task.Principal.RunLevel := 1;   // TASK_RUNLEVEL_HIGHEST
        Trigger := Task.Triggers.Create(9); // TASK_TRIGGER_LOGON
        Trigger.Enabled := True;
        Action := Task.Actions.Create(0); // TASK_ACTION_EXEC
        Action.Path := ExpandConstant('{app}\LocalTelemetry.exe');
        Action.Arguments := '--minimized';
        RootFolder.RegisterTaskDefinition(StartupTaskName, Task, 6, '', '', 3, '');
        Result := True;
    except
        Log('CreateStartupTask failed: ' + GetExceptionMessage);
    end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
    MarkerPath: string;
begin
    if CurStep = ssPostInstall then
    begin
        // Create 'app.mode' marker file inside application directory.
        // AppSettings.cs checks for this file's existence to route settings to LocalAppData.
        MarkerPath := ExpandConstant('{app}\app.mode');
        SaveStringToFile(MarkerPath, 'standard', False);

        // Create the "Run at Windows startup" scheduled task if the user selected it.
        // Uses the Task Scheduler COM API because schtasks.exe /Create /SC ONLOGON
        // silently fails with 0x80004005 on some Windows 11 builds.
        if WizardIsTaskSelected('startup') then
        begin
            if not CreateStartupTask then
                MsgBox('LocalTelemetry could not be added to Windows startup. You can enable it later in Settings > General > "Start with Windows".', mbError, MB_OK);
        end;
    end;
end;

function IsAppRunning(const ExeName: string): Boolean;
var
    ResultCode: Integer;
begin
    Exec('cmd.exe', '/c tasklist /FI "IMAGENAME eq ' + ExeName + '" 2>NUL | find /I /N "' + ExeName + '" >NUL', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Result := (ResultCode = 0);
end;

procedure WaitForAppExit;
var
    Waited: Integer;
begin
    Waited := 0;
    while (IsAppRunning('LocalTelemetry.exe') or IsAppRunning('LocalTelemetry.Notifier.exe')) and (Waited < 40) do
    begin
        Sleep(250);
        Waited := Waited + 1;
    end;
end;

function DeleteUserData(const Dir: string): Boolean;
var
    Attempt: Integer;
begin
    Result := False;
    for Attempt := 1 to 3 do
    begin
        if not DirExists(Dir) then
        begin
            Result := True;
            Exit;
        end;
        if DelTree(Dir, True, True, True) then
        begin
            Result := True;
            Exit;
        end;
        Sleep(500);
    end;
end;

function RetryDeleteData: Boolean;
begin
    Result := DataDeletePending;
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
    DataDir, TempDir, AppDir, PawnIoLog, PawnIoScript: string;
    ResultCode: Integer;
begin
    if CurUninstallStep = usUninstall then
    begin
        // Remove the startup scheduled task created during install
        Exec('schtasks.exe', '/Delete /TN "' + StartupTaskName + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Force kill any running instances of main app and background notifier before file deletion
        Exec('taskkill.exe', '/f /im LocalTelemetry.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('taskkill.exe', '/f /im LocalTelemetry.Notifier.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        WaitForAppExit;

        // Remove the PawnIO kernel driver service, uninstall registry keys and files
        // that the app installed at runtime (see PawnIoDevice.TryInstall). The app is
        // killed above so the driver handle is closed before stopping the service.
        Exec('sc.exe', 'stop PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('sc.exe', 'delete PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Remove the PawnIO kernel driver package from the Windows DriverStore via pnputil.
        // The kernel driver ignores the SCM stop control (sc stop returns 1052), so only
        // pnputil /uninstall can unload it and delete the package. A generated PowerShell
        // script (with retries) is used because right after the app is force-killed the
        // driver may still be settling and pnputil may report it as in use. A script file
        // is used instead of a cmd for /d loop because cmd mangling of the do-clause
        // breaks quoted pnputil paths.
        PawnIoLog := ExpandConstant('{commonappdata}\LocalTelemetry\pawnio_cleanup.log');
        PawnIoScript := ExpandConstant('{tmp}\pawnio_cleanup.ps1');
        ForceDirectories(ExpandConstant('{commonappdata}\LocalTelemetry'));
        SaveStringToFile(PawnIoScript, PawnIoCleanupScript, False);
        Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + PawnIoScript + '"',
             '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
        RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
        if DirExists('C:\Program Files\PawnIO') then
            DelTree('C:\Program Files\PawnIO', True, True, True);

        // Delete all application files (LocalTelemetry.exe, LocalTelemetry.Notifier.exe, DLLs & runtime files) inside {app}
        AppDir := ExpandConstant('{app}');
        if DirExists(AppDir) then
            DelTree(AppDir, True, True, True);

        // Prompt user to remove AppData user settings, logs & traffic history.
        // Defaults to No (MB_DEFBUTTON2) - nothing is deleted unless the user
        // explicitly approves. On approval, delete with retries; if the folder
        // still cannot be removed, flag it so the [UninstallDelete] fallback
        // (Check: RetryDeleteData) retries it during the final uninstall phase.
        DataDir := ExpandConstant('{localappdata}\LocalTelemetry');
        TempDir := ExpandConstant('{localappdata}\Temp\PawnIo');
        DataDeletePending := False;
        if DirExists(DataDir) then
        begin
            if MsgBox('Do you also want to remove all saved settings, traffic logs and local hardware monitoring history?',
                      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
            begin
                if not DeleteUserData(DataDir) then
                    DataDeletePending := True;
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
