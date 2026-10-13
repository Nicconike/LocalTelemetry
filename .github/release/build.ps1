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
Write-Host "Compiling Inno Setup Windows installer..."
$possiblePaths = @(
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
$iscc = "iscc"
foreach ($p in $possiblePaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}
if ($iscc -eq "iscc" -and -not (Get-Command iscc -ErrorAction SilentlyContinue)) {
    winget install --id JRSoftware.InnoSetup.7 --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) {
            $iscc = $p
            break
        }
    }
    if ($iscc -eq "iscc") {
        $found = Get-ChildItem -Path "C:\Program Files*" -Filter "ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $iscc = $found.FullName }
    }
}
if (-not (Test-Path $iscc) -and -not (Get-Command $iscc -ErrorAction SilentlyContinue)) {
    throw "ISCC.exe compiler not found on system."
}

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
}

# 6. Update Scoop Bucket Manifest
Write-Host "Updating Scoop bucket manifest..."
$manifestFile = "bucket/localtelemetry.json"
if (Test-Path $manifestFile) {
    $zipUrl = "https://github.com/$repo/releases/download/$actualTag/LocalTelemetry-win-x64.zip"
    $manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
    $manifest.version = $Version
    $manifest.architecture.'64bit'.url = $zipUrl
    $manifest.architecture.'64bit'.hash = $zipHash
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestFile -Encoding utf8
    Write-Host "Updated $manifestFile with version $Version."
}
else {
    Write-Warning "Manifest file $manifestFile not found."
}

# 7. Update Chocolatey Package Specifications and Pack
Write-Host "Updating Chocolatey package specifications..."
$nuspec = "packaging/chocolatey/localtelemetry.nuspec"
if (Test-Path $nuspec) {
    $setupUrl = "https://github.com/$repo/releases/download/$actualTag/LocalTelemetrySetup.exe"
    (Get-Content $nuspec -Raw) -replace '<version>.*</version>', "<version>$Version</version>" | Set-Content $nuspec -Encoding utf8

    $installScript = "packaging/chocolatey/tools/chocolateyinstall.ps1"
    if (Test-Path $installScript) {
        (Get-Content $installScript -Raw) `
            -replace "(?m)^\$url\s*=\s*'.*'", "`$url = '$setupUrl'" `
            -replace "(?m)^\$checksum\s*=\s*'.*'", "`$checksum = '$setupHash'" |
        Set-Content $installScript -Encoding utf8
    }

    $verificationFile = "packaging/chocolatey/tools/VERIFICATION.txt"
    if (Test-Path $verificationFile) {
        (Get-Content $verificationFile -Raw) `
            -replace "(?m)(^The installer has been downloaded from the official GitHub Release:\r?\n).*(\r?\n)", "`$1$setupUrl`$2" `
            -replace "releases/download/[^/]+/checksums\.txt", "releases/download/$actualTag/checksums.txt" `
            -replace "(?m)(^Checksum \(SHA256\):\r?\n).*(\r?\n)", "`$1$setupHash`$2" |
        Set-Content $verificationFile -Encoding utf8
    }
}
else {
    Write-Warning "Nuspec file $nuspec not found."
}

Write-Host "Build and packaging complete for version $Version."
