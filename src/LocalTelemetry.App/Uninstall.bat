@echo off
:: Request Admin Privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :run
) else (
    echo Requesting administrative privileges...
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

:run
echo ===================================================
echo   LocalTelemetry Portable Uninstallation Tool
echo ===================================================
echo.
echo Closing LocalTelemetry processes...
taskkill /F /IM LocalTelemetry.exe >nul 2>&1
taskkill /F /IM LocalTelemetry.Notifier.exe >nul 2>&1

echo Waiting for Windows to release hardware handles...
timeout /t 3 /nobreak >nul

echo.
echo Stopping and removing PawnIO kernel service...
if exist "%~dp0PawnIO_setup.exe" (
    "%~dp0PawnIO_setup.exe" -uninstall -silent >nul 2>&1
) else if exist "C:\Program Files\PawnIO\PawnIO_setup.exe" (
    "C:\Program Files\PawnIO\PawnIO_setup.exe" -uninstall -silent >nul 2>&1
)
sc stop PawnIO >nul 2>&1
sc delete PawnIO >nul 2>&1

echo.
echo Removing PawnIO from Windows DriverStore...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$pnp = if ($env:PROCESSOR_ARCHITECTURE -eq 'x86' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { Join-Path $env:windir 'Sysnative\pnputil.exe' } else { Join-Path $env:windir 'System32\pnputil.exe' }; if (-not (Test-Path $pnp)) { $pnp = Join-Path $env:windir 'System32\pnputil.exe' }; $pnpLines = & $pnp /enum-drivers 2>&1; $curOem = ''; $pawnioOems = @(); foreach ($line in $pnpLines) { if ($line -match 'Published Name:\s+(oem\d+\.inf)') { $curOem = $matches[1] } elseif ($line -match 'Original Name:\s+pawnio\.inf') { if ($curOem) { $pawnioOems += $curOem } } }; $reboot = $false; foreach ($oem in $pawnioOems) { Write-Host ('Uninstalling driver package ' + $oem + '...') -ForegroundColor Cyan; & $pnp /delete-driver $oem /uninstall /force | Out-Null; if ($LASTEXITCODE -eq 3010) { $reboot = $true } }; if ($reboot) { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('The PawnIO kernel driver was uninstalled, but Windows requires a system restart to fully release it from memory.' + [Environment]::NewLine + [Environment]::NewLine + 'If you plan to run LocalTelemetry again, please restart your computer first.', 'LocalTelemetry Cleanup', 'OK', 'Information') | Out-Null }"

echo.
echo Cleaning up Registry keys and startup entries...
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" /f >nul 2>&1
schtasks /Delete /TN "LocalTelemetry Startup" /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "LocalTelemetry" /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v "LocalTelemetry" /f >nul 2>&1

echo.
echo Cleaning up leftover data folders...
if exist "C:\Program Files\PawnIO" rmdir /s /q "C:\Program Files\PawnIO"
if exist "%ProgramData%\LocalTelemetry" rmdir /s /q "%ProgramData%\LocalTelemetry"
if exist "%LocalAppData%\Temp\PawnIo" rmdir /s /q "%LocalAppData%\Temp\PawnIo"

echo.
echo ===================================================
echo Cleanup complete!
echo You can now safely delete the LocalTelemetry folder.
echo ===================================================
pause
