[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [string]$RefreshAllReportPath = '',
    [string]$EvalJsonReportPath = '',
    [string]$EvalMarkdownReportPath = '',
    [string]$DatabasePath = '',
    [string]$LanceDbStorePath = '',
    [string]$IndexManifestPath = '',
    [int]$MinimumEvalCases = 9,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ScriptRoot {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return Split-Path -Parent $PSCommandPath
    }

    return (Get-Location).Path
}

$contractPath = Join-Path (Resolve-ScriptRoot) 'memory-pre-push-contract.ps1'
if (-not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
    throw "Missing memory pre-push contract helper: $contractPath"
}
. $contractPath

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

function New-CheckPlan {
    param(
        [string]$Name,
        [string]$Description
    )

    [ordered]@{
        name = $Name
        description = $Description
        status = 'planned'
        detail = ''
        uses_cloud = $false
        uses_hook = $false
        touches_denylist = $false
        uses_generated_exports_as_source = $false
    }
}

function New-CheckResult {
    param(
        [string]$Name,
        [string]$Description,
        [bool]$Passed,
        [string]$Detail,
        [object]$Evidence = $null
    )

    $result = [ordered]@{
        name = $Name
        description = $Description
        status = if ($Passed) { 'passed' } else { 'failed' }
        detail = $Detail
        uses_cloud = $false
        uses_hook = $false
        touches_denylist = $false
        uses_generated_exports_as_source = $false
    }

    if ($null -ne $Evidence) {
        $result['evidence'] = $Evidence
    }

    return $result
}

function Test-ManagedPrePushHookInstalled {
    param([string]$Root)

    $hookPath = Join-Path $Root '.git\hooks\pre-push'
    if (-not (Test-Path -LiteralPath $hookPath)) {
        return $false
    }

    $hook = Get-Content -Raw -LiteralPath $hookPath
    return $hook.Contains('TC-DN-HOFI3 managed memory pre-push hook') -and
        $hook.Contains('Managed-By: scripts/install-memory-pre-push-hook.ps1')
}

function Get-ExpectedChecks {
    return @(
        (New-CheckPlan -Name 'refresh-all-report-exists' -Description 'Find the refresh-all report'),
        (New-CheckPlan -Name 'refresh-all-status-completed' -Description 'Require completed full-local-rebuild status'),
        (New-CheckPlan -Name 'refresh-all-safety-flags' -Description 'Require refresh-all guardrail flags to stay disabled'),
        (New-CheckPlan -Name 'refresh-all-steps-completed' -Description 'Require all refresh-all steps to complete with clean stale-check'),
        (New-CheckPlan -Name 'lancedb-eval-json-exists' -Description 'Find LanceDB eval JSON report'),
        (New-CheckPlan -Name 'commit-addressed-freshness' -Description 'Require refresh and eval evidence to match Git HEAD and tree'),
        (New-CheckPlan -Name 'semantic-index-manifest' -Description 'Require physical SQLite/LanceDB stores and matching semantic index manifest'),
        (New-CheckPlan -Name 'lancedb-eval-passed' -Description 'Require LanceDB eval gate to pass'),
        (New-CheckPlan -Name 'lancedb-eval-markdown-exists' -Description 'Find LanceDB eval Markdown report')
    )
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$managedPrePushHookInstalled = Test-ManagedPrePushHookInstalled -Root $root

$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-pre-push-check-report.json')

$refreshReportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $RefreshAllReportPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-refresh-all-report.json')

$evalJsonPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $EvalJsonReportPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb-sidecar-report.json')

$evalMarkdownPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $EvalMarkdownReportPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb-eval-report.md')

$databasePath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $DatabasePath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\project-memory.sqlite')

$lanceDbStorePath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $LanceDbStorePath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb')

$indexManifestPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $IndexManifestPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb-manifest.json')

$startedAt = (Get-Date).ToUniversalTime().ToString('o')
$checks = [System.Collections.Generic.List[object]]::new()
$status = 'passed'

if ($PlanOnly) {
    $status = 'planned'
    foreach ($check in (Get-ExpectedChecks)) {
        $checks.Add($check)
    }
}
else {
    $refreshReport = $null
    $evalReport = $null

    $refreshExists = Test-Path -LiteralPath $refreshReportPath
    $checks.Add((New-CheckResult -Name 'refresh-all-report-exists' -Description 'Find the refresh-all report' -Passed $refreshExists -Detail (Convert-ToRepoPath -Root $root -Path $refreshReportPath)))
    if ($refreshExists) {
        $refreshReport = Read-JsonFile -Path $refreshReportPath
    }

    $refreshStatusOk = $false
    if ($null -ne $refreshReport) {
        $refreshStatusOk = $refreshReport.generator -eq 'scripts/memory-refresh-all.ps1' -and
            $refreshReport.mode -eq 'full-local-rebuild' -and
            $refreshReport.status -eq 'completed'
    }

    $checks.Add((New-CheckResult -Name 'refresh-all-status-completed' -Description 'Require completed full-local-rebuild status' -Passed $refreshStatusOk -Detail 'generator/mode/status'))

    $safetyDetail = if ($null -ne $refreshReport) { Test-RefreshAllReport -Report $refreshReport } else { 'refresh-all report missing' }
    $checks.Add((New-CheckResult -Name 'refresh-all-safety-flags' -Description 'Require refresh-all guardrail flags to stay disabled' -Passed ([string]::IsNullOrWhiteSpace($safetyDetail)) -Detail $safetyDetail))

    $stepsDetail = if ($null -ne $refreshReport) { Test-RefreshAllSteps -Report $refreshReport } else { 'refresh-all report missing' }
    $checks.Add((New-CheckResult -Name 'refresh-all-steps-completed' -Description 'Require all refresh-all steps to complete with clean stale-check' -Passed ([string]::IsNullOrWhiteSpace($stepsDetail)) -Detail $stepsDetail))

    $evalJsonExists = Test-Path -LiteralPath $evalJsonPath
    $checks.Add((New-CheckResult -Name 'lancedb-eval-json-exists' -Description 'Find LanceDB eval JSON report' -Passed $evalJsonExists -Detail (Convert-ToRepoPath -Root $root -Path $evalJsonPath)))
    if ($evalJsonExists) {
        $evalReport = Read-JsonFile -Path $evalJsonPath
    }

    $freshness = Test-CommitAddressedFreshness -Root $root -RefreshReport $refreshReport -EvalReport $evalReport
    $checks.Add((New-CheckResult -Name 'commit-addressed-freshness' -Description 'Require refresh and eval evidence to match Git HEAD and tree' -Passed ([bool]$freshness.passed) -Detail ([string]$freshness.detail) -Evidence $freshness.evidence))

    $manifestCheck = Test-SemanticIndexManifest `
        -DatabasePath $databasePath `
        -StorePath $lanceDbStorePath `
        -ManifestPath $indexManifestPath `
        -RefreshReport $refreshReport `
        -EvalReport $evalReport
    $checks.Add((New-CheckResult -Name 'semantic-index-manifest' -Description 'Require physical SQLite/LanceDB stores and matching semantic index manifest' -Passed ([bool]$manifestCheck.passed) -Detail ([string]$manifestCheck.detail)))

    $evalValidation = Test-LanceDbEvalReport -Report $evalReport -MinimumEvalCases $MinimumEvalCases
    $checks.Add((New-CheckResult -Name 'lancedb-eval-passed' -Description 'Require LanceDB eval gate to pass' -Passed ([bool]$evalValidation.passed) -Detail ([string]$evalValidation.detail)))

    $evalMarkdownExists = Test-Path -LiteralPath $evalMarkdownPath
    $checks.Add((New-CheckResult -Name 'lancedb-eval-markdown-exists' -Description 'Find LanceDB eval Markdown report' -Passed $evalMarkdownExists -Detail (Convert-ToRepoPath -Root $root -Path $evalMarkdownPath)))

    foreach ($check in $checks) {
        if ($check.status -ne 'passed') {
            $status = 'failed'
            break
        }
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-pre-push-check.ps1'
    mode = if ($PlanOnly) { 'plan-only' } else { 'manual-pre-push-check' }
    status = $status
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    refresh_all_report_path = Convert-ToRepoPath -Root $root -Path $refreshReportPath
    eval_json_report_path = Convert-ToRepoPath -Root $root -Path $evalJsonPath
    eval_markdown_report_path = Convert-ToRepoPath -Root $root -Path $evalMarkdownPath
    sqlite_database_path = Convert-ToRepoPath -Root $root -Path $databasePath
    lancedb_store_path = Convert-ToRepoPath -Root $root -Path $lanceDbStorePath
    index_manifest_path = Convert-ToRepoPath -Root $root -Path $indexManifestPath
    started_at = $startedAt
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    manual_only = $true
    requires_existing_refresh_all_report = $true
    runs_refresh_all = $false
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    commit_hook_installed = $false
    pre_push_hook_installed = $managedPrePushHookInstalled
    installs_hooks = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    uses_generated_exports_as_source = $false
    touches_build_artifacts = $false
    checks = @($checks)
}

Write-JsonReport -Path $reportPath -Payload $report

if ($status -eq 'failed') {
    exit 1
}

exit 0
