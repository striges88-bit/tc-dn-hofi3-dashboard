[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$InputReportPath = '',
    [string]$Commit = 'HEAD',
    [string]$OutputPath = '',
    [string]$DatabasePath = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$toolRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = $toolRoot
}

$root = (Resolve-Path $ProjectRoot).Path
if (-not (Test-Path (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

if ([string]::IsNullOrWhiteSpace($InputReportPath)) {
    $InputReportPath = Join-Path $root 'docs\memory\generated\curated-retain-redacted-subset-report.json'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-import-report.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$dotnetPath = Join-Path $toolRoot '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnetPath -PathType Leaf)) {
    $dotnetPath = 'dotnet'
}

$memoryArgs = @(
    'retain-import',
    '--project-root', $root,
    '--input-report', $InputReportPath,
    '--commit', $Commit,
    '--json'
)
if (-not [string]::IsNullOrWhiteSpace($DatabasePath)) {
    $memoryArgs += @('--db', $DatabasePath)
}

$toolDll = [string]$env:CRYPTO_MEMORY_TOOL_DLL
if (-not [string]::IsNullOrWhiteSpace($toolDll)) {
    if (-not (Test-Path $toolDll -PathType Leaf)) {
        throw "CRYPTO_MEMORY_TOOL_DLL does not exist: $toolDll"
    }

    $cliOutput = & $dotnetPath $toolDll @memoryArgs 2>&1
}
else {
    $projectPath = Join-Path $toolRoot 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
    $cliOutput = & $dotnetPath run --no-restore --project $projectPath -- @memoryArgs 2>&1
}

$cliExitCode = $LASTEXITCODE
$cliText = ($cliOutput | Out-String).Trim()

try {
    $cliResult = $cliText | ConvertFrom-Json
}
catch {
    throw "retain-import did not return JSON. Exit=$cliExitCode Output=$cliText"
}

$fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$rootFullPath = [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/')
$rootPrefix = $rootFullPath + [System.IO.Path]::DirectorySeparatorChar
$outputInsideProject = $fullOutputPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
$reportedOutputPath = if ($outputInsideProject) {
    $fullOutputPath.Substring($rootPrefix.Length).Replace('\', '/')
}
else {
    $fullOutputPath
}
$outputIsGenerated = $outputInsideProject -and
    $reportedOutputPath.StartsWith('docs/memory/generated/', [System.StringComparison]::Ordinal)

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-import.ps1'
    mode = 'retain-import'
    status = $cliResult.status
    commit = $Commit
    input_report_path = $cliResult.input_report_path
    output_path = $reportedOutputPath
    output_is_generated = $outputIsGenerated
    output_should_be_ignored = $true
    external_retain_enabled = $false
    codex_auto_retain_enabled = $false
    cloud_enabled = $false
    calls_hindsight = $false
    calls_codex_retain = $false
    installs_hooks = $false
    runs_refresh_all = $false
    rebuilds_memory = $false
    imports_denylist = $false
    writes_report_only = $true
    cli_exit_code = $cliExitCode
    result = $cliResult
}

$report | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputPath -Encoding UTF8

Write-Output "Generated $reportedOutputPath"
Write-Output "Status: $($cliResult.status)"
Write-Output "Imported: $($cliResult.imported_count)"
Write-Output "External retain: disabled"

exit $cliExitCode
