[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$HookPath = '',
    [string]$OutputPath = '',
    [int]$TimeoutSeconds = 15,
    [switch]$Confirm,
    [switch]$Disable,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$managedMarker = 'TC-DN-HOFI3 managed memory post-commit marker hook'
$managedByMarker = 'Managed-By: scripts/install-memory-post-commit-marker-hook.ps1'

function Resolve-ScriptRoot {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return Split-Path -Parent $PSCommandPath
    }

    return (Get-Location).Path
}

function Resolve-ProjectRoot {
    param([string]$Candidate)

    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $scriptRoot = Resolve-ScriptRoot
    return (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
}

function Resolve-RootedOrRelativePath {
    param(
        [string]$Root,
        [string]$Path,
        [string]$DefaultPath
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return [System.IO.Path]::GetFullPath($DefaultPath)
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Convert-ToRepoPath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $pathFull.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return '.'
        }

        return $relativePath
    }

    return $Path
}

function Write-JsonReport {
    param(
        [string]$Path,
        [object]$Payload
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Payload | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

function Test-ManagedHook {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $content = Get-Content -Raw -LiteralPath $Path
    return $content.Contains($managedMarker) -and $content.Contains($managedByMarker)
}

function Convert-ToHookPathLiteral {
    param([string]$Path)

    $value = ([System.IO.Path]::GetFullPath($Path) -replace '\\', '/')
    if ($value.Contains("'")) {
        throw "Hook path contains a single quote and cannot be safely embedded in a sh hook: $Path"
    }

    return "'$value'"
}

function New-HookContent {
    param(
        [string]$Root,
        [int]$Timeout
    )

    $rootLiteral = Convert-ToHookPathLiteral -Path $Root
    return (@(
        '#!/bin/sh'
        "# $managedMarker"
        "# $managedByMarker"
        '# Writes a marker only; memory rebuild stays manual.'
        "PROJECT_ROOT=$rootLiteral"
        'exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PROJECT_ROOT/scripts/memory-mark-needs-refresh.ps1" -ProjectRoot "$PROJECT_ROOT" -Reason post-commit -TimeoutSeconds ' + $Timeout
        ''
    ) -join "`n")
}

function New-Report {
    param(
        [string]$Status,
        [string]$FailureCode,
        [bool]$InstallsHooks,
        [bool]$PostCommitHookInstalled,
        [bool]$ManagedHookRemoved,
        [bool]$UnmanagedHookDetected
    )

    [ordered]@{
        schema_version = 1
        generator = 'scripts/install-memory-post-commit-marker-hook.ps1'
        mode = if ($PlanOnly) { 'plan-only' } elseif ($Disable) { 'disable' } else { 'install' }
        status = $Status
        failure_code = $FailureCode
        hook_type = 'post-commit'
        project_root = Convert-ToRepoPath -Root $root -Path $root
        hook_path = Convert-ToRepoPath -Root $root -Path $hookFilePath
        report_path = Convert-ToRepoPath -Root $root -Path $reportPath
        marker_script = 'scripts/memory-mark-needs-refresh.ps1'
        marker_script_exists = Test-Path -LiteralPath $markerScriptPath
        marker_path = 'docs/memory/generated/memory-needs-refresh.marker.json'
        manual_only = $true
        requires_confirm = $true
        confirm_provided = [bool]$Confirm
        would_install_hook = -not [bool]$Disable
        installs_hooks = $InstallsHooks
        post_commit_hook_installed = $PostCommitHookInstalled
        managed_hook_removed = $ManagedHookRemoved
        unmanaged_hook_detected = $UnmanagedHookDetected
        hook_invokes_marker_helper = $true
        hook_invokes_refresh_all = $false
        writes_marker = $true
        uses_lock = $true
        timeout_seconds = $TimeoutSeconds
        runs_refresh_all = $false
        rebuilds_memory = $false
        imports_curated_retain = $false
        cloud_enabled = $false
        codex_auto_retain_enabled = $false
        touches_raw_jsonl = $false
        touches_hindsight_store = $false
        touches_secret_storage = $false
        uses_generated_exports_as_source = $false
        touches_build_artifacts = $false
        started_at = $startedAt
        finished_at = (Get-Date).ToUniversalTime().ToString('o')
    }
}

$startedAt = (Get-Date).ToUniversalTime().ToString('o')
$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$defaultHookPath = Join-Path $root '.git\hooks\post-commit'
$hookFilePath = Resolve-RootedOrRelativePath -Root $root -Path $HookPath -DefaultPath $defaultHookPath
$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\install-memory-post-commit-marker-hook-report.json')
$markerScriptPath = Join-Path $root 'scripts\memory-mark-needs-refresh.ps1'

if ($PlanOnly) {
    $report = New-Report `
        -Status 'planned' `
        -FailureCode '' `
        -InstallsHooks $false `
        -PostCommitHookInstalled (Test-ManagedHook -Path $hookFilePath) `
        -ManagedHookRemoved $false `
        -UnmanagedHookDetected ((Test-Path -LiteralPath $hookFilePath) -and -not (Test-ManagedHook -Path $hookFilePath))
    Write-JsonReport -Path $reportPath -Payload $report
    exit 0
}

if (-not $Confirm) {
    $report = New-Report `
        -Status 'failed' `
        -FailureCode 'confirm-required' `
        -InstallsHooks $false `
        -PostCommitHookInstalled (Test-ManagedHook -Path $hookFilePath) `
        -ManagedHookRemoved $false `
        -UnmanagedHookDetected ((Test-Path -LiteralPath $hookFilePath) -and -not (Test-ManagedHook -Path $hookFilePath))
    Write-JsonReport -Path $reportPath -Payload $report
    exit 1
}

if (-not (Test-Path -LiteralPath $markerScriptPath)) {
    $report = New-Report `
        -Status 'failed' `
        -FailureCode 'marker-helper-missing' `
        -InstallsHooks $false `
        -PostCommitHookInstalled (Test-ManagedHook -Path $hookFilePath) `
        -ManagedHookRemoved $false `
        -UnmanagedHookDetected $false
    Write-JsonReport -Path $reportPath -Payload $report
    exit 1
}

$hookExists = Test-Path -LiteralPath $hookFilePath
$isManaged = Test-ManagedHook -Path $hookFilePath
if ($hookExists -and -not $isManaged) {
    $report = New-Report `
        -Status 'failed' `
        -FailureCode 'unmanaged-hook-exists' `
        -InstallsHooks $false `
        -PostCommitHookInstalled $false `
        -ManagedHookRemoved $false `
        -UnmanagedHookDetected $true
    Write-JsonReport -Path $reportPath -Payload $report
    exit 1
}

if ($Disable) {
    if ($isManaged) {
        Remove-Item -LiteralPath $hookFilePath -Force
        $managedHookRemoved = $true
    }
    else {
        $managedHookRemoved = $false
    }

    $report = New-Report `
        -Status 'disabled' `
        -FailureCode '' `
        -InstallsHooks $false `
        -PostCommitHookInstalled $false `
        -ManagedHookRemoved $managedHookRemoved `
        -UnmanagedHookDetected $false
    Write-JsonReport -Path $reportPath -Payload $report
    exit 0
}

$hookDirectory = Split-Path -Parent $hookFilePath
if (-not [string]::IsNullOrWhiteSpace($hookDirectory)) {
    New-Item -ItemType Directory -Path $hookDirectory -Force | Out-Null
}

Set-Content -LiteralPath $hookFilePath -Value (New-HookContent -Root $root -Timeout $TimeoutSeconds) -Encoding ASCII -NoNewline

$report = New-Report `
    -Status 'installed' `
    -FailureCode '' `
    -InstallsHooks $true `
    -PostCommitHookInstalled $true `
    -ManagedHookRemoved $false `
    -UnmanagedHookDetected $false
Write-JsonReport -Path $reportPath -Payload $report
exit 0
