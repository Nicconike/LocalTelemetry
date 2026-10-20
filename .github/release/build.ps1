param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Tag
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$actualTag = if ($Tag) { $Tag } else { "v$Version" }
$repo = $env:GITHUB_REPOSITORY
if (-not $repo) { $repo = "Nicconike/LocalTelemetry" }

Write-Host "=========================================="
Write-Host "Building LocalTelemetry Release: $Version ($actualTag)"
Write-Host "=========================================="

# 1. Build Frontend
Write-Host "Building Svelte frontend assets with Bun..."
Push-Location "src/LocalTelemetry.App/Settings/wwwroot"
try {
    bun install --frozen-lockfile --ignore-scripts
    bun run build
}
finally {
    Pop-Location
}

# 2. Build .NET Executable
Write-Host "Compiling .NET application binary..."
$numVer = ($Version -split '-')[0]
if ($numVer.Split('.').Count -eq 3) { $numVer = "$numVer.0" }

dotnet publish src/LocalTelemetry.App/LocalTelemetry.App.csproj `
    -c Release `
    -r win-x64 `
    --no-self-contained `
    -p:PublishSingleFile=true `
    -p:MinVerVersionOverride=$Version `
    -o ./publish

# 3. Download PawnIO Kernel Driver Setup
Write-Host "Downloading PawnIO driver setup..."
$pawnUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"
Invoke-WebRequest -Uri $pawnUrl -OutFile "./publish/PawnIO_setup.exe"

# 4. Locate ISCC.exe and Compile Inno Setup Windows Installer
Write-Host "Compiling Inno Setup Windows installer with Inno Setup 7..."
$inno7Paths = @(
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe"
)
$iscc = $null
foreach ($p in $inno7Paths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}

if (-not $iscc -or -not (Test-Path $iscc)) {
    throw "Inno Setup 7 compiler (ISCC.exe) not found. Inno Setup 7 must be installed prior to running build.ps1."
}

Write-Host "Using Inno Setup 7 compiler: $iscc"

& $iscc /DAppVersion=$Version /DAppVersionInfoVersion=$numVer setup.iss

# 5. Package Portable Zip Archive and SHA-256 Checksums
Write-Host "Packaging portable zip archive and computing checksums..."
Compress-Archive -Path ./publish/* -DestinationPath ./LocalTelemetry-win-x64.zip -Force

$hashes = Get-FileHash -Path ./LocalTelemetrySetup.exe, ./LocalTelemetry-win-x64.zip -Algorithm SHA256
$hashes | ForEach-Object { "{0}  {1}" -f $_.Hash, (Split-Path $_.Path -Leaf) } | Set-Content -Path ./checksums.txt -Encoding utf8

$setupHash = ($hashes | Where-Object { $_.Path -like "*LocalTelemetrySetup.exe" }).Hash.ToUpperInvariant()
$zipHash = ($hashes | Where-Object { $_.Path -like "*LocalTelemetry-win-x64.zip" }).Hash.ToLowerInvariant()

if ($env:GITHUB_ENV) {
    "SETUP_HASH=$setupHash" | Out-File -FilePath $env:GITHUB_ENV -Append
    "ZIP_HASH=$zipHash" | Out-File -FilePath $env:GITHUB_ENV -Append
    "RELEASE_VERSION=$Version" | Out-File -FilePath $env:GITHUB_ENV -Append
    "RELEASE_TAG=$actualTag" | Out-File -FilePath $env:GITHUB_ENV -Append
}

# 6. Update Scoop Bucket Manifest
Write-Host "Updating Scoop bucket manifest..."
$manifestFile = "bucket/localtelemetry.json"
if (Test-Path $manifestFile) {
    $zipUrl = "https://github.com/$repo/releases/download/$actualTag/LocalTelemetry-win-x64.zip"
    $node = [System.Text.Json.Nodes.JsonNode]::Parse((Get-Content $manifestFile -Raw))
    $node["version"] = $Version
    $node["architecture"]["64bit"]["url"] = $zipUrl
    $node["architecture"]["64bit"]["hash"] = $zipHash
    $jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
    $jsonOptions.WriteIndented = $true
    $jsonOptions.IndentSize = 4
    $node.ToJsonString($jsonOptions) | Set-Content $manifestFile -Encoding utf8
    Write-Host "Updated $manifestFile with version $Version."
}
else {
    Write-Warning "Manifest file $manifestFile not found."
}

# 7. Update WinGet Package Manifest
Write-Host "Updating WinGet package manifest..."
$wingetManifest = "packaging/winget/localtelemetry.yaml"
if (Test-Path $wingetManifest) {
    $setupUrl = "https://github.com/$repo/releases/download/$actualTag/LocalTelemetrySetup.exe"
    (Get-Content $wingetManifest -Raw) `
        -replace '(?m)^PackageVersion:\s*.*$', "PackageVersion: $Version" `
        -replace '(?m)^\s*InstallerUrl:\s*.*$', "      InstallerUrl: $setupUrl" `
        -replace '(?m)^\s*InstallerSha256:\s*.*$', "      InstallerSha256: `"$setupHash`"" |
        Set-Content $wingetManifest -Encoding utf8
    Write-Host "Updated $wingetManifest with version $Version."
}
else {
    Write-Warning "WinGet manifest $wingetManifest not found."
}

# 8. Synchronize Package Versions (Docs and Frontend UI)
Write-Host "Synchronizing package.json versions..."
$packageFiles = @(
    "docs/package.json",
    "src/LocalTelemetry.App/Settings/wwwroot/package.json"
)
foreach ($pkgFile in $packageFiles) {
    if (Test-Path $pkgFile) {
        (Get-Content $pkgFile -Raw) -replace '(?m)^(\s*"version":\s*")[^"]*(")', "`${1}$Version`${2}" |
            Set-Content $pkgFile -Encoding utf8
        Write-Host "Updated $pkgFile with version $Version."
    }
}

Write-Host "Build and packaging complete for version $Version."
