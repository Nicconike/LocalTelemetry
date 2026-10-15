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

if ($env:CHOCOLATEY_API_KEY) {
    $nuspec = "packaging/chocolatey/localtelemetry.nuspec"
    Write-Host "Packing Chocolatey nupkg..."
    choco pack $nuspec --version $Version --outputdirectory packaging/chocolatey

    $nupkg = Get-ChildItem -Path "packaging/chocolatey/*.nupkg" | Select-Object -First 1
    if (-not $nupkg) {
        throw "Chocolatey package (.nupkg) not found in packaging/chocolatey."
    }
    $pushed = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Write-Host "Attempt ${attempt}: Pushing $nupkg to Chocolatey..."
        $prevPref = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
        try {
            $output = & choco push $nupkg.FullName --api-key $env:CHOCOLATEY_API_KEY --source https://push.chocolatey.org/ --execution-timeout 600 2>&1
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $prevPref
        }
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

Write-Host "Feed publishing completed."
