$ErrorActionPreference = 'Stop'

$packageName = 'localtelemetry'
$url = '__URL__'
$checksum = '__CHECKSUM__'
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
