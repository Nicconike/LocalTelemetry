$ErrorActionPreference = 'Stop'

$packageName = 'localtelemetry'
$url = 'https://github.com/Nicconike/LocalTelemetry/releases/download/v1.0.0-rc.2/LocalTelemetrySetup.exe'
$checksum = 'A132A61BD4CC8F88936494634739AB7BAAEAEF1F1471EC485C9225C802E452B8'
$packageArgs = @{
    packageName    = $packageName
    fileType       = 'exe'
    url64          = $url
    checksum64     = $checksum
    checksumType64 = 'sha256'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
    validExitCodes = @(0)
}
Install-ChocolateyPackage @packageArgs



