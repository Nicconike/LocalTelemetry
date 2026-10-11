param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Tag
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

Write-Host "=========================================="
Write-Host "Publishing LocalTelemetry Feeds: $Tag ($Version)"
Write-Host "=========================================="

# Compute or retrieve hashes
$zipHash = (Get-FileHash -Path "./LocalTelemetry-win-x64.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
$setupHash = (Get-FileHash -Path "./LocalTelemetrySetup.exe" -Algorithm SHA256).Hash.ToUpperInvariant()

$repo = $env:GITHUB_REPOSITORY
if (-not $repo) { $repo = "Nicconike/LocalTelemetry" }

# 1. Update Scoop Manifest
Write-Host "Updating Scoop bucket manifest..."
$manifestFile = "bucket/localtelemetry.json"
if (Test-Path $manifestFile) {
    $zipUrl = "https://github.com/$repo/releases/download/$Tag/LocalTelemetry-win-x64.zip"
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

# 2. Build and Publish Chocolatey Package
Write-Host "Updating Chocolatey package specifications..."
$nuspec = "packaging/chocolatey/localtelemetry.nuspec"
if (Test-Path $nuspec) {
    $setupUrl = "https://github.com/$repo/releases/download/$Tag/LocalTelemetrySetup.exe"
    (Get-Content $nuspec -Raw) -replace '<version>.*</version>', "<version>$Version</version>" | Set-Content $nuspec -Encoding utf8

    $installScript = "packaging/chocolatey/tools/chocolateyinstall.ps1"
    (Get-Content $installScript -Raw) `
        -replace '__URL__', $setupUrl `
        -replace '__CHECKSUM__', $setupHash | Set-Content $installScript -Encoding utf8

    $verificationFile = "packaging/chocolatey/tools/VERIFICATION.txt"
    if (Test-Path $verificationFile) {
        (Get-Content $verificationFile -Raw) `
            -replace '__VERSION__', $Version `
            -replace '__URL__', $setupUrl `
            -replace '__CHECKSUM__', $setupHash | Set-Content $verificationFile -Encoding utf8
    }

    Write-Host "Packing Chocolatey nupkg..."
    choco pack $nuspec --version $Version --outputdirectory packaging/chocolatey

    if ($env:CHOCOLATEY_API_KEY) {
        $nupkg = Get-ChildItem -Path "packaging/chocolatey/*.nupkg" | Select-Object -First 1
        $pushed = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            Write-Host "Attempt ${attempt}: Pushing $nupkg to Chocolatey..."
            $output = & choco push $nupkg.FullName --api-key $env:CHOCOLATEY_API_KEY --source https://push.chocolatey.org/ --execution-timeout 600 2>&1
            $output | ForEach-Object { Write-Host $_ }
            if ($LASTEXITCODE -eq 0 -or ($output -match "Successfully published Chocolatey package") -or ($output -match "already exists")) {
                $pushed = $true
                Write-Host "Successfully published Chocolatey package $nupkg"
                break
            }
            Write-Warning "Chocolatey push failed (exit code $LASTEXITCODE). Retrying in 15 seconds..."
            Start-Sleep -Seconds 15
        }
        if (-not $pushed) {
            throw "Failed to push Chocolatey package after 3 attempts."
        }
    }
    else {
        Write-Warning "CHOCOLATEY_API_KEY secret not found; skipping Chocolatey publish."
    }
}
else {
    Write-Warning "Nuspec file $nuspec not found."
}

Write-Host "Package feeds update completed."
