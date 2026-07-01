[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$InputReportPath = '',
    [string]$Commit = 'HEAD',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
}

$root = (Resolve-Path $ProjectRoot).Path
if (-not (Test-Path (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

if ([string]::IsNullOrWhiteSpace($InputReportPath)) {
    $InputReportPath = Join-Path $root 'docs\memory\generated\curated-retain-dry-run-report.json'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-import-report.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$dotnetPath = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnetPath -PathType Leaf)) {
    $dotnetPath = 'dotnet'
}

$projectPath = Join-Path $root 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
$cliOutput = & $dotnetPath run --project $projectPath -- retain-import --project-root $root --input-report $InputReportPath --commit $Commit --json 2>&1
$cliExitCode = $LASTEXITCODE
$cliText = ($cliOutput | Out-String).Trim()

try {
    $cliResult = $cliText | ConvertFrom-Json
}
catch {
    throw "retain-import did not return JSON. Exit=$cliExitCode Output=$cliText"
}

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-import.ps1'
    mode = 'retain-import'
    status = $cliResult.status
    commit = $Commit
    input_report_path = $cliResult.input_report_path
    output_path = 'docs/memory/generated/curated-retain-import-report.json'
    output_is_generated = $true
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

Write-Output "Generated docs/memory/generated/curated-retain-import-report.json"
Write-Output "Status: $($cliResult.status)"
Write-Output "Imported: $($cliResult.imported_count)"
Write-Output "External retain: disabled"

exit 0
