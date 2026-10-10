$ErrorActionPreference = 'Stop'

$packageName = 'localtelemetry'
$silentArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'

[array]$key = Get-UninstallRegistryKey -SoftwareName 'LocalTelemetry'

if ($key.Count -eq 1) {
    $key | ForEach-Object {
        $packageArgs = @{
            packageName    = $packageName
            fileType       = 'exe'
            file           = ($_.UninstallString -replace '"', '')
            silentArgs     = $silentArgs
            validExitCodes = @(0)
        }
        Uninstall-ChocolateyPackage @packageArgs
    }
}
elseif (Test-Path "${env:ProgramFiles}\LocalTelemetry\unins000.exe") {
    $packageArgs = @{
        packageName    = $packageName
        fileType       = 'exe'
        file           = "${env:ProgramFiles}\LocalTelemetry\unins000.exe"
        silentArgs     = $silentArgs
        validExitCodes = @(0)
    }
    Uninstall-ChocolateyPackage @packageArgs
}
else {
    Write-Warning "Uninstaller for $packageName could not be located in registry or default install directory."
}
