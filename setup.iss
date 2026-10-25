#ifndef AppVersion
#define AppVersion "1.0.0"
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
Compression=lzma2/ultra64
LZMADictionarySize=65536
LZMANumFastBytes=273
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
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Uninstall.bat,*.xml,*.pdb"

[Icons]
Name: "{group}\LocalTelemetry"; Filename: "{app}\LocalTelemetry.exe"; Tasks: startmenuicon
Name: "{commondesktop}\LocalTelemetry"; Filename: "{app}\LocalTelemetry.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LocalTelemetry.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,LocalTelemetry}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
    StartupTaskName = 'LocalTelemetry Startup';
    CSIDL_LOCAL_APPDATA = $001C;
    ProfileListKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList';
          PawnIoCleanupScript =
          '$log = $env:TEMP + ''\pawnio_cleanup.log''' + #13#10 +
          '''[PawnIO cleanup] started: '' + (Get-Date) | Set-Content $log' + #13#10 +
          'if ($env:PROCESSOR_ARCHITECTURE -eq ''x86'' -or $env:PROCESSOR_ARCHITECTURE -eq ''ARM64'') { $pnp = Join-Path $env:windir ''Sysnative\pnputil.exe''; if (-not (Test-Path $pnp)) { $pnp = Join-Path $env:windir ''System32\pnputil.exe'' } } else { $pnp = Join-Path $env:windir ''System32\pnputil.exe'' }' + #13#10 +
          '''[PawnIO cleanup] pnputil: '' + $pnp + '' exists: '' + (Test-Path $pnp) | Add-Content $log' + #13#10 +
          'Start-Sleep -Seconds 2' + #13#10 +
          '$rebootNeeded = $false' + #13#10 +
          '$pnpLines = & $pnp /enum-drivers 2>&1' + #13#10 +
          '$curOem = ''''' + #13#10 +
          '$pawnioOems = @()' + #13#10 +
          'foreach ($line in $pnpLines) {' + #13#10 +
          '    if ($line -match ''Published Name:\s+(oem\d+\.inf)'') { $curOem = $matches[1] }' + #13#10 +
          '    elseif ($line -match ''Original Name:\s+pawnio\.inf'') { if ($curOem) { $pawnioOems += $curOem } }' + #13#10 +
          '}' + #13#10 +
          '''[PawnIO cleanup] found OEM drivers: '' + ($pawnioOems -join '', '') | Add-Content $log' + #13#10 +
          'foreach ($oem in $pawnioOems) {' + #13#10 +
          '    ''[PawnIO cleanup] deleting driver package: '' + $oem | Add-Content $log' + #13#10 +
          '    & $pnp /delete-driver $oem /uninstall /force *>&1 | Add-Content $log' + #13#10 +
          '    if ($LASTEXITCODE -eq 3010) { $rebootNeeded = $true }' + #13#10 +
          '}' + #13#10 +
          '& sc.exe stop PawnIO *>&1 | Add-Content $log' + #13#10 +
          '& sc.exe delete PawnIO *>&1 | Add-Content $log' + #13#10 +
          'if ($rebootNeeded) { exit 3010 } else { exit 0 }';

var
    DownloadPage: TDownloadWizardPage;
    DotNetDownloaded: Boolean;
    RequiresReboot: Boolean;
    UninstallChkSettings: TNewCheckBox;
    UninstallChkTraffic: TNewCheckBox;
    UninstallChkLogs: TNewCheckBox;

function CreateStartupTask: Boolean;
var
    Scheduler, RootFolder, Task, Trigger, Action: Variant;
begin
    Result := False;
    try try Scheduler := CreateOleObject('Schedule.Service');
        Scheduler.Connect;
        RootFolder := Scheduler.GetFolder('\');
        Task := Scheduler.NewTask(0);
        Task.RegistrationInfo.Description := 'Starts LocalTelemetry at user logon (silent, highest privileges).';
        Task.Principal.LogonType := 3;  // TASK_LOGON_INTERACTIVE_TOKEN
        Task.Principal.RunLevel := 1;   // TASK_RUNLEVEL_HIGHEST
        Task.Settings.DisallowStartIfOnBatteries := False;
        Task.Settings.StopIfGoingOnBatteries := False;
        Task.Settings.ExecutionTimeLimit := 'PT0S';
        Task.Settings.StartWhenAvailable := True;
        Trigger := Task.Triggers.Create(9); // TASK_TRIGGER_LOGON
        Trigger.Enabled := True;
        Action := Task.Actions.Create(0); // TASK_ACTION_EXEC
        Action.Path := ExpandConstant('{app}\LocalTelemetry.exe');
        Action.Arguments := '--minimized';
        Action.WorkingDirectory := ExpandConstant('{app}');
        RootFolder.RegisterTaskDefinition(StartupTaskName, Task, 6, '', '', 3, '');
        Result := True; finally Scheduler := Unassigned; RootFolder := Unassigned; Task := Unassigned; Trigger := Unassigned; Action := Unassigned; end; except
        Log('CreateStartupTask failed: ' + GetExceptionMessage);
    end;
end;

function IsDotNet10DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard;
begin
    RequiresReboot := False;
    DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'Downloading .NET 10 Desktop Runtime...', nil);
    DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
  Attempt: Integer;
  DownloadUrl: string;
begin
  Result := '';
  DotNetDownloaded := False;

  if not IsDotNet10DesktopInstalled then
  begin
    for Attempt := 1 to 2 do
    begin
      // Attempt 1 uses aka.ms (evergreen redirect); Attempt 2 falls back directly to builds.dotnet.microsoft.com CDN
      if Attempt = 1 then
        DownloadUrl := 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
      else
        DownloadUrl := 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.11/windowsdesktop-runtime-10.0.11-win-x64.exe';

      DownloadPage.Clear;
      DownloadPage.Add(DownloadUrl, 'dotnet_installer.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          DotNetDownloaded := True;
          Log('.NET 10 Desktop Runtime downloaded successfully on attempt ' + IntToStr(Attempt) + '.');
          Break;
        except
          Log('.NET 10 Desktop Runtime download failed on attempt ' + IntToStr(Attempt) + ': ' + GetExceptionMessage);
          if DownloadPage.AbortedByUser then
          begin
            Result := 'Installation cancelled by user.';
            Break;
          end;
          if Attempt = 2 then
          begin
            Result := 'Failed to download the required .NET 10 Desktop Runtime.' + #13#10 + #13#10 +
                      'Error: ' + GetExceptionMessage + #13#10 + #13#10 +
                      'Please ensure you have an active internet connection and run Setup again.';
          end;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;

    if DotNetDownloaded then
    begin
      WizardForm.StatusLabel.Caption := 'Installing .NET 10 Desktop Runtime...';
      if not Exec(ExpandConstant('{tmp}\dotnet_installer.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        Result := 'Failed to install the .NET 10 Desktop Runtime. Setup cannot continue.';
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
    MarkerPath, PawnIoExe: string;
    ResultCode: Integer;
begin
    if CurStep = ssPostInstall then
    begin
        // Create 'app.mode' marker file inside application directory.
        // AppSettings.cs checks for this file's existence to route settings to LocalAppData.
        MarkerPath := ExpandConstant('{app}\app.mode');
        SaveStringToFile(MarkerPath, 'standard', False);

        // Install PawnIO sensor driver if downloaded during setup.
        // This follows the bootstrapper pattern: clean stale state, install, retry, verify.
        PawnIoExe := ExpandConstant('{app}\PawnIO_setup.exe');
        if FileExists(PawnIoExe) then
        begin
            try
                WizardForm.StatusLabel.Caption := 'Preparing kernel hardware sensor driver...';

                // Step 1: Clean up any stale PawnIO service from a previous install/uninstall.
                // Without this, PawnIO_setup.exe fails with 1072 (service marked for deletion).
                Exec('sc.exe', 'stop PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
                Log('Pre-install: sc stop PawnIO exit code: ' + IntToStr(ResultCode));
                Exec('sc.exe', 'delete PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
                Log('Pre-install: sc delete PawnIO exit code: ' + IntToStr(ResultCode));

                // Give Windows SCM time to fully flush the deleted service handle
                if ResultCode = 0 then
                begin
                    Log('Pre-install: Waiting for Windows to flush stale service...');
                    Sleep(3000);
                end;

                // Step 2: Install PawnIO
                WizardForm.StatusLabel.Caption := 'Installing kernel hardware sensor driver...';
                Log('Installing PawnIO sensor driver from ' + PawnIoExe + '...');
                if not Exec(PawnIoExe, '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
                begin
                    Log('Failed to launch PawnIO setup executable');
                end
                else
                begin
                    Log('PawnIO installer finished with exit code: ' + IntToStr(ResultCode));

                    // If we still get 1072, wait longer and retry once
                    if ResultCode = 1072 then
                    begin
                        Log('PawnIO install returned 1072 (stale service). Waiting 5s and retrying...');
                        WizardForm.StatusLabel.Caption := 'Waiting for driver cleanup to complete...';
                        Sleep(5000);
                        Exec('sc.exe', 'delete PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
                        Sleep(2000);
                        WizardForm.StatusLabel.Caption := 'Retrying driver installation...';
                        if Exec(PawnIoExe, '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
                            Log('PawnIO retry finished with exit code: ' + IntToStr(ResultCode))
                        else
                            Log('PawnIO retry failed to launch');
                    end;

                    // Evaluate final result
                    if (ResultCode = 0) or (ResultCode = 183) or (ResultCode = 1056) then
                    begin
                        Log('PawnIO installation successful (exit code: ' + IntToStr(ResultCode) + ')');
                    end
                    else if ResultCode = 1072 then
                    begin
                        Log('PawnIO still returning 1072 after retry. Flagging for restart.');
                        RequiresReboot := True;
                    end
                    else
                    begin
                        Log('PawnIO installer returned unexpected exit code: ' + IntToStr(ResultCode));
                    end;
                end;

                // Step 3: Start the service immediately so the app can use it on first launch
                if not RequiresReboot then
                begin
                    WizardForm.StatusLabel.Caption := 'Starting hardware sensor driver...';
                    Sleep(1000);
                    Exec('sc.exe', 'start PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
                    Log('Post-install: sc start PawnIO exit code: ' + IntToStr(ResultCode));
                    if (ResultCode <> 0) and (ResultCode <> 1056) then
                    begin
                        Log('PawnIO service failed to start. Will be started by app at runtime.');
                    end;
                end;

            except
                Log('Exception during PawnIO installation: ' + GetExceptionMessage);
            end;
        end;

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
function NeedRestart(): Boolean;
begin
    Result := RequiresReboot;
end;


function InitializeSetup: Boolean;
var
    ResultCode: Integer;
begin
    Result := True;
    if IsAppRunning('LocalTelemetry.exe') or IsAppRunning('LocalTelemetry.Notifier.exe') then
    begin
        if MsgBox('LocalTelemetry is currently running in the background.' + #13#10 + #13#10 +
                  'Setup needs to close LocalTelemetry before updating or reinstalling.' + #13#10 + #13#10 +
                  'Do you want Setup to close it automatically and continue?',
                  mbConfirmation, MB_YESNO) = IDYES then
        begin
            Exec('taskkill.exe', '/f /im LocalTelemetry.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            Exec('taskkill.exe', '/f /im LocalTelemetry.Notifier.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            WaitForAppExit;
        end
        else
        begin
            Result := False; // Cancel installation gracefully
        end;
    end;
end;

procedure ToggleUninstallSettings(Sender: TObject);
begin
    if Assigned(UninstallChkSettings) then
        UninstallChkSettings.Checked := not UninstallChkSettings.Checked;
end;

procedure ToggleUninstallTraffic(Sender: TObject);
begin
    if Assigned(UninstallChkTraffic) then
        UninstallChkTraffic.Checked := not UninstallChkTraffic.Checked;
end;

procedure ToggleUninstallLogs(Sender: TObject);
begin
    if Assigned(UninstallChkLogs) then
        UninstallChkLogs.Checked := not UninstallChkLogs.Checked;
end;

function PromptUserDataCleanup(ProfileCount: Integer; out RemoveSettings, RemoveTraffic, RemoveLogs: Boolean): Boolean;
var
    Form: TSetupForm;
    LblTitle, LblPrompt: TLabel;
    LblSettingsDesc, LblTrafficDesc, LblLogsDesc: TLabel;
    BottomSep: TBevel;
    BtnRemove, BtnSkip: TNewButton;
    PadX, ContentW: Integer;
begin
    Result := False;
    RemoveSettings := False;
    RemoveTraffic := False;
    RemoveLogs := False;

    if UninstallSilent then
        Exit;

    Form := CreateCustomForm(ScaleX(480), ScaleY(260), False, False);
    try
        Form.Caption := 'LocalTelemetry Uninstall';
        Form.Position := poScreenCenter;
        Form.BorderStyle := bsDialog;
        Form.ClientWidth := ScaleX(480);
        Form.ClientHeight := ScaleY(260);

        PadX := ScaleX(20);
        ContentW := Form.ClientWidth - (PadX * 2);

        // Header Title
        LblTitle := TLabel.Create(Form);
        LblTitle.Parent := Form;
        LblTitle.AutoSize := False;
        LblTitle.Left := PadX;
        LblTitle.Top := ScaleY(16);
        LblTitle.Width := ContentW;
        LblTitle.Height := ScaleY(20);
        LblTitle.Font.Name := 'Segoe UI';
        LblTitle.Font.Size := 10;
        LblTitle.Font.Style := [fsBold];
        if ProfileCount > 1 then
            LblTitle.Caption := 'Clean up application data for all ' + IntToStr(ProfileCount) + ' user profiles?'
        else
            LblTitle.Caption := 'Clean up application data and preferences?';

        // Header Subtitle
        LblPrompt := TLabel.Create(Form);
        LblPrompt.Parent := Form;
        LblPrompt.AutoSize := False;
        LblPrompt.Left := PadX;
        LblPrompt.Top := ScaleY(38);
        LblPrompt.Width := ContentW;
        LblPrompt.Height := ScaleY(18);
        LblPrompt.Font.Name := 'Segoe UI';
        LblPrompt.Font.Size := 9;
        LblPrompt.Font.Color := $555555;
        LblPrompt.Caption := 'Select the items you want to permanently delete from this computer:';

        // 1. Settings Item
        UninstallChkSettings := TNewCheckBox.Create(Form);
        UninstallChkSettings.Parent := Form;
        UninstallChkSettings.Left := PadX;
        UninstallChkSettings.Top := ScaleY(68);
        UninstallChkSettings.Width := ContentW;
        UninstallChkSettings.Height := ScaleY(20);
        UninstallChkSettings.Font.Name := 'Segoe UI';
        UninstallChkSettings.Font.Size := 9;
        UninstallChkSettings.Font.Style := [fsBold];
        UninstallChkSettings.Caption := 'Application Settings && Preferences';
        UninstallChkSettings.Checked := True;

        LblSettingsDesc := TLabel.Create(Form);
        LblSettingsDesc.Parent := Form;
        LblSettingsDesc.AutoSize := False;
        LblSettingsDesc.Left := PadX + ScaleX(20);
        LblSettingsDesc.Top := ScaleY(88);
        LblSettingsDesc.Width := ContentW - ScaleX(20);
        LblSettingsDesc.Height := ScaleY(16);
        LblSettingsDesc.Font.Name := 'Segoe UI';
        LblSettingsDesc.Font.Size := 8;
        LblSettingsDesc.Font.Color := $666666;
        LblSettingsDesc.Cursor := crHandPoint;
        LblSettingsDesc.Caption := 'Overlay layout, custom themes, hardware sensor alerts, and preferences';
        LblSettingsDesc.OnClick := @ToggleUninstallSettings;

        // 2. Traffic History Item
        UninstallChkTraffic := TNewCheckBox.Create(Form);
        UninstallChkTraffic.Parent := Form;
        UninstallChkTraffic.Left := PadX;
        UninstallChkTraffic.Top := ScaleY(112);
        UninstallChkTraffic.Width := ContentW;
        UninstallChkTraffic.Height := ScaleY(20);
        UninstallChkTraffic.Font.Name := 'Segoe UI';
        UninstallChkTraffic.Font.Size := 9;
        UninstallChkTraffic.Font.Style := [fsBold];
        UninstallChkTraffic.Caption := 'Network Traffic History';
        UninstallChkTraffic.Checked := False; // Preserved by default

        LblTrafficDesc := TLabel.Create(Form);
        LblTrafficDesc.Parent := Form;
        LblTrafficDesc.AutoSize := False;
        LblTrafficDesc.Left := PadX + ScaleX(20);
        LblTrafficDesc.Top := ScaleY(132);
        LblTrafficDesc.Width := ContentW - ScaleX(20);
        LblTrafficDesc.Height := ScaleY(16);
        LblTrafficDesc.Font.Name := 'Segoe UI';
        LblTrafficDesc.Font.Size := 8;
        LblTrafficDesc.Font.Color := $666666;
        LblTrafficDesc.Cursor := crHandPoint;
        LblTrafficDesc.Caption := 'Recorded daily and monthly network bandwidth consumption databases';
        LblTrafficDesc.OnClick := @ToggleUninstallTraffic;

        // 3. Cache & Logs Item
        UninstallChkLogs := TNewCheckBox.Create(Form);
        UninstallChkLogs.Parent := Form;
        UninstallChkLogs.Left := PadX;
        UninstallChkLogs.Top := ScaleY(156);
        UninstallChkLogs.Width := ContentW;
        UninstallChkLogs.Height := ScaleY(20);
        UninstallChkLogs.Font.Name := 'Segoe UI';
        UninstallChkLogs.Font.Size := 9;
        UninstallChkLogs.Font.Style := [fsBold];
        UninstallChkLogs.Caption := 'Cache && Diagnostic Logs';
        UninstallChkLogs.Checked := True;

        LblLogsDesc := TLabel.Create(Form);
        LblLogsDesc.Parent := Form;
        LblLogsDesc.AutoSize := False;
        LblLogsDesc.Left := PadX + ScaleX(20);
        LblLogsDesc.Top := ScaleY(176);
        LblLogsDesc.Width := ContentW - ScaleX(20);
        LblLogsDesc.Height := ScaleY(16);
        LblLogsDesc.Font.Name := 'Segoe UI';
        LblLogsDesc.Font.Size := 8;
        LblLogsDesc.Font.Color := $666666;
        LblLogsDesc.Cursor := crHandPoint;
        LblLogsDesc.Caption := 'WebView2 runtime cache, temporary session files, and crash logs';
        LblLogsDesc.OnClick := @ToggleUninstallLogs;

        // Bottom Separator Line (full width edge-to-edge)
        BottomSep := TBevel.Create(Form);
        BottomSep.Parent := Form;
        BottomSep.Left := 0;
        BottomSep.Top := ScaleY(206);
        BottomSep.Width := Form.ClientWidth;
        BottomSep.Height := ScaleY(1);
        BottomSep.Shape := bsTopLine;

        // Action Buttons
        BtnSkip := TNewButton.Create(Form);
        BtnSkip.Parent := Form;
        BtnSkip.Width := ScaleX(85);
        BtnSkip.Height := ScaleY(26);
        BtnSkip.Left := Form.ClientWidth - PadX - BtnSkip.Width;
        BtnSkip.Top := ScaleY(220);
        BtnSkip.Font.Name := 'Segoe UI';
        BtnSkip.Font.Size := 9;
        BtnSkip.Caption := 'Keep All';
        BtnSkip.ModalResult := mrCancel;
        BtnSkip.Cancel := True;

        // Primary Action Button (Default Highlight): Remove Selected
        BtnRemove := TNewButton.Create(Form);
        BtnRemove.Parent := Form;
        BtnRemove.Width := ScaleX(125);
        BtnRemove.Height := ScaleY(26);
        BtnRemove.Left := BtnSkip.Left - ScaleX(8) - BtnRemove.Width;
        BtnRemove.Top := ScaleY(220);
        BtnRemove.Font.Name := 'Segoe UI';
        BtnRemove.Font.Size := 9;
        BtnRemove.Caption := 'Remove Selected';
        BtnRemove.ModalResult := mrOk;
        BtnRemove.Default := True;

        // Give initial focus to the primary button so Windows highlights it as default
        Form.ActiveControl := BtnRemove;

        if Form.ShowModal = mrOk then
        begin
            RemoveSettings := UninstallChkSettings.Checked;
            RemoveTraffic := UninstallChkTraffic.Checked;
            RemoveLogs := UninstallChkLogs.Checked;
            Result := RemoveSettings or RemoveTraffic or RemoveLogs;
        end;
    finally
        UninstallChkSettings := nil;
        UninstallChkTraffic := nil;
        UninstallChkLogs := nil;
        Form.Free;
    end;
end;

function DeleteSelectedUserData(const Dir: string; RemoveSettings, RemoveTraffic, RemoveLogs: Boolean): Boolean;
var
    Attempt: Integer;
begin
    Result := True;
    if not DirExists(Dir) then
        Exit;

    if RemoveSettings then
    begin
        DeleteFile(Dir + '\settings.json');
        DeleteFile(Dir + '\hardware_state.json');
        DeleteFile(Dir + '\settings.json.bak');
        DeleteFile(Dir + '\hardware_state.json.bak');
    end;

    if RemoveTraffic then
    begin
        DeleteFile(Dir + '\internet_usage.jsonl');
        DeleteFile(Dir + '\internet_usage.jsonl.bak');
        DeleteFile(Dir + '\internet_usage.dat');
    end;

    if RemoveLogs then
    begin
        if DirExists(Dir + '\logs') then
            DelTree(Dir + '\logs', True, True, True);
        if DirExists(Dir + '\webview2_data') then
            DelTree(Dir + '\webview2_data', True, True, True);
        if DirExists(Dir + '\temp') then
            DelTree(Dir + '\temp', True, True, True);
    end;

    // If all components were selected for removal, clean up the root directory
    if RemoveSettings and RemoveTraffic and RemoveLogs then
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
    end
    else
    begin
        // If directory is now empty, remove it cleanly
        RemoveDir(Dir);
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

function CollectLocalTelemetryDataDirs(const Dirs: TStringList): Integer;
var
    Profiles: TArrayOfString;
    I: Integer;
    ProfilePath, DataDir: string;
begin
    Result := 0;
    if RegGetSubkeyNames(HKLM, ProfileListKey, Profiles) then
        for I := 0 to GetArrayLength(Profiles) - 1 do
        begin
            ProfilePath := '';
            if RegQueryStringValue(HKLM, ProfileListKey + '\' + Profiles[I], 'ProfileImagePath', ProfilePath) then
            begin
                DataDir := ProfilePath + '\AppData\Local\LocalTelemetry';
                if DirExists(DataDir) and (Dirs.IndexOf(DataDir) = -1) then
                begin
                    Dirs.Add(DataDir);
                    Result := Result + 1;
                end;
            end;
        end;
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

var
    UninstallDriverNeedsReboot: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
    TempDir, AppDir, PawnIoScript, PreservedMsg: string;
    DataDirs, PendingDataDirs: TStringList;
    Count, I: Integer;
    ResultCode: Integer;
    RemoveSettings, RemoveTraffic, RemoveLogs, AnySelected: Boolean;
begin
    if CurUninstallStep = usUninstall then
    begin
        // Remove the startup scheduled task created during install
        Exec('schtasks.exe', '/Delete /TN "' + StartupTaskName + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Force kill any running instances of main app and background notifier before file deletion
        Exec('taskkill.exe', '/f /im LocalTelemetry.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('taskkill.exe', '/f /im LocalTelemetry.Notifier.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        WaitForAppExit;
        Sleep(2000); // Give the Windows kernel time to fully close the driver handles

        // If PawnIO uninstaller binary exists, invoke it cleanly first
        if FileExists(ExpandConstant('{app}\PawnIO_setup.exe')) then
            Exec(ExpandConstant('{app}\PawnIO_setup.exe'), '-uninstall -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
        else if FileExists('C:\Program Files\PawnIO\PawnIO_setup.exe') then
            Exec('C:\Program Files\PawnIO\PawnIO_setup.exe', '-uninstall -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Remove the PawnIO kernel driver service, uninstall registry keys and files
        Exec('sc.exe', 'stop PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('sc.exe', 'delete PawnIO', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Remove the PawnIO kernel driver package via script
        PawnIoScript := ExpandConstant('{tmp}\pawnio_cleanup.ps1');
        SaveStringToFile(PawnIoScript, PawnIoCleanupScript, False);
        Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + PawnIoScript + '"',
             '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

        // Clean up temporary script and logs
        DeleteFile(PawnIoScript);
        DeleteFile(ExpandConstant('{tmp}\pawnio_cleanup.log'));
        DeleteFile(ExpandConstant('{commonappdata}\LocalTelemetry\pawnio_cleanup.log'));
        RemoveDir(ExpandConstant('{commonappdata}\LocalTelemetry'));

        if ResultCode = 3010 then
            UninstallDriverNeedsReboot := True
        else
            UninstallDriverNeedsReboot := False;

        RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
        RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
        if DirExists('C:\Program Files\PawnIO') then
            DelTree('C:\Program Files\PawnIO', True, True, True);

        AppDir := ExpandConstant('{app}');
        if DirExists(AppDir) then
            DelTree(AppDir, True, True, True);
    end
    else if CurUninstallStep = usPostUninstall then
    begin
        // Driver uninstall status prompt deferred to end of uninstall
        if UninstallDriverNeedsReboot then
        begin
            MsgBox('The PawnIO kernel driver was uninstalled, but Windows requires a system restart to fully release it from memory.' + #13#10 + #13#10 + 'If you plan to reinstall LocalTelemetry immediately, please restart your computer first.', mbInformation, MB_OK);
        end;

        // Prompt user to remove AppData user settings deferred to end of uninstall
        PendingDataDirs := TStringList.Create;
        DataDirs := TStringList.Create;
        try
            Count := CollectLocalTelemetryDataDirs(DataDirs);
            if Count > 0 then
            begin
                AnySelected := PromptUserDataCleanup(Count, RemoveSettings, RemoveTraffic, RemoveLogs);
                if AnySelected then
                begin
                    for I := 0 to DataDirs.Count - 1 do
                        if not DeleteSelectedUserData(DataDirs[I], RemoveSettings, RemoveTraffic, RemoveLogs) then
                            PendingDataDirs.Add(DataDirs[I]);

                    if RemoveLogs then
                    begin
                        TempDir := GetShellFolderByCSIDL(CSIDL_LOCAL_APPDATA, False);
                        if TempDir <> '' then
                        begin
                            TempDir := TempDir + '\Temp\PawnIo';
                            if DirExists(TempDir) then
                                DelTree(TempDir, True, True, True);
                        end;
                    end;
                end;

                if (not UninstallSilent) and (not (RemoveSettings and RemoveTraffic and RemoveLogs)) then
                begin
                    PreservedMsg := 'Your preserved application data has been kept at:' + #13#10;
                    for I := 0 to DataDirs.Count - 1 do
                    begin
                        if I > 0 then
                            PreservedMsg := PreservedMsg + #13#10;
                        PreservedMsg := PreservedMsg + DataDirs[I];
                    end;
                    MsgBox(PreservedMsg, mbInformation, MB_OK);
                end;
            end;

            // Final retry for any user data folders that could not be deleted above
            for I := 0 to PendingDataDirs.Count - 1 do
                if DirExists(PendingDataDirs[I]) then
                    DeleteSelectedUserData(PendingDataDirs[I], RemoveSettings, RemoveTraffic, RemoveLogs);
        finally
            DataDirs.Free;
            PendingDataDirs.Free;
            PendingDataDirs := nil;
        end;
    end;
end;

