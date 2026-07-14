function Invoke-CuratedRetainScan {
    param(
        [Parameter(Mandatory = $true)][string]$ToolRoot,
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $dotnetPath = Join-Path $ToolRoot '.dotnet\dotnet.exe'
    if (-not (Test-Path $dotnetPath -PathType Leaf)) {
        $dotnetPath = 'dotnet'
    }

    $toolDll = [string]$env:CRYPTO_MEMORY_TOOL_DLL
    if (-not [string]::IsNullOrWhiteSpace($toolDll)) {
        if (-not (Test-Path $toolDll -PathType Leaf)) {
            throw "CRYPTO_MEMORY_TOOL_DLL does not exist: $toolDll"
        }

        $scanOutput = & $dotnetPath $toolDll retain-scan --project-root $ProjectRoot --json 2>&1
    }
    else {
        $projectPath = Join-Path $ToolRoot 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
        $scanOutput = & $dotnetPath run --no-restore --project $projectPath -- retain-scan --project-root $ProjectRoot --json 2>&1
    }

    $scanExitCode = $LASTEXITCODE
    $scanText = ($scanOutput | Out-String).Trim()
    if ($scanExitCode -ne 0) {
        throw "retain-scan failed with exit code $scanExitCode. Output=$scanText"
    }

    try {
        $scan = $scanText | ConvertFrom-Json
    }
    catch {
        throw "retain-scan did not return JSON. Output=$scanText"
    }

    if ([int]$scan.schema_version -ne 1 -or
        [string]$scan.scanner -cne 'CryptoIndicatorApp.Memory.CuratedRetainScanner/v1' -or
        [string]$scan.status -cne 'scanned' -or
        $null -eq $scan.files -or
        $null -eq $scan.findings) {
        throw 'retain-scan returned an unsupported scanner contract.'
    }

    return $scan
}
