[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [string]$RefreshAllReportPath = '',
    [string]$EvalJsonReportPath = '',
    [string]$EvalMarkdownReportPath = '',
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
        [string]$Detail
    )

    [ordered]@{
        name = $Name
        description = $Description
        status = if ($Passed) { 'passed' } else { 'failed' }
        detail = $Detail
        uses_cloud = $false
        uses_hook = $false
        touches_denylist = $false
        uses_generated_exports_as_source = $false
    }
}

function Test-JsonPropertyFalse {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    return $null -ne $property -and $property.Value -eq $false
}

function Test-JsonPropertyExists {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object.PSObject.Properties[$Name]
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Read-JsonFile {
    param([string]$Path)

    return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
}

function Get-ExpectedChecks {
    return @(
        (New-CheckPlan -Name 'refresh-all-report-exists' -Description 'Find the refresh-all report'),
        (New-CheckPlan -Name 'refresh-all-status-completed' -Description 'Require completed full-local-rebuild status'),
        (New-CheckPlan -Name 'refresh-all-safety-flags' -Description 'Require refresh-all guardrail flags to stay disabled'),
        (New-CheckPlan -Name 'refresh-all-steps-completed' -Description 'Require all refresh-all steps to complete with clean stale-check'),
        (New-CheckPlan -Name 'lancedb-eval-json-exists' -Description 'Find LanceDB eval JSON report'),
        (New-CheckPlan -Name 'lancedb-eval-passed' -Description 'Require LanceDB eval gate to pass'),
        (New-CheckPlan -Name 'lancedb-eval-markdown-exists' -Description 'Find LanceDB eval Markdown report')
    )
}

function Test-RefreshAllReport {
    param([object]$Report)

    $falseFlags = @(
        'cloud_enabled',
        'codex_auto_retain_enabled',
        'auto_commit_refresh_enabled',
        'commit_hook_installed',
        'installs_hooks',
        'direct_project_crawl_enabled',
        'imports_raw_jsonl',
        'imports_generated_exports',
        'uses_generated_exports_as_source',
        'imports_secrets',
        'imports_local_proxy_details',
        'imports_build_artifacts',
        'touches_raw_jsonl',
        'touches_hindsight_store',
        'touches_secret_storage',
        'touches_build_artifacts'
    )

    foreach ($flag in $falseFlags) {
        if (-not (Test-JsonPropertyFalse -Object $Report -Name $flag)) {
            return "Unexpected refresh-all flag: $flag"
        }
    }

    foreach ($step in @($Report.steps)) {
        if ($step.uses_cloud -ne $false -or $step.uses_hook -ne $false) {
            return "Unexpected refresh-all step automation flag: $($step.name)"
        }
    }

    return ''
}

function Test-RefreshAllSteps {
    param([object]$Report)

    $expectedSteps = @(
        'legacy-json-refresh',
        'sqlite-refresh',
        'sqlite-stale-check',
        'lancedb-cleanup',
        'lancedb-rebuild',
        'lancedb-eval'
    )

    $steps = @($Report.steps)
    $actualSteps = @($steps | ForEach-Object { $_.name })
    if (($actualSteps -join '|') -ne ($expectedSteps -join '|')) {
        return "Unexpected refresh-all step order: $($actualSteps -join ', ')"
    }

    foreach ($step in $steps) {
        if ($step.status -ne 'completed' -or $step.exit_code -ne 0) {
            return "Refresh-all step failed: $($step.name)"
        }
    }

    $staleStep = $steps | Where-Object { $_.name -eq 'sqlite-stale-check' } | Select-Object -First 1
    if ($null -eq $staleStep -or ($staleStep.stdout_tail -notmatch '"issues"\s*:\s*\[\s*\]')) {
        return 'SQLite stale-check did not report an empty issue list'
    }

    return ''
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

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

    $evalPassed = $false
    $evalDetail = 'eval report missing'
    if ($null -ne $evalReport) {
        $requiredEvalProperties = @(
            'generator',
            'command',
            'status',
            'source_store',
            'cloud_enabled',
            'auto_commit_refresh_enabled',
            'direct_project_crawl_enabled',
            'commit_hook_installed',
            'passed',
            'failed_count',
            'passed_count'
        )

        $missingEvalProperties = @($requiredEvalProperties | Where-Object { -not (Test-JsonPropertyExists -Object $evalReport -Name $_) })
        if ($missingEvalProperties.Count -gt 0) {
            $statusValue = Get-JsonPropertyValue -Object $evalReport -Name 'status'
            $commandValue = Get-JsonPropertyValue -Object $evalReport -Name 'command'
            $evalDetail = "missing properties: $($missingEvalProperties -join ', '); status=$statusValue; command=$commandValue"
        }
        else {
            $generatorValue = Get-JsonPropertyValue -Object $evalReport -Name 'generator'
            $commandValue = Get-JsonPropertyValue -Object $evalReport -Name 'command'
            $statusValue = Get-JsonPropertyValue -Object $evalReport -Name 'status'
            $sourceStoreValue = Get-JsonPropertyValue -Object $evalReport -Name 'source_store'
            $cloudEnabledValue = Get-JsonPropertyValue -Object $evalReport -Name 'cloud_enabled'
            $autoCommitRefreshValue = Get-JsonPropertyValue -Object $evalReport -Name 'auto_commit_refresh_enabled'
            $directProjectCrawlValue = Get-JsonPropertyValue -Object $evalReport -Name 'direct_project_crawl_enabled'
            $commitHookInstalledValue = Get-JsonPropertyValue -Object $evalReport -Name 'commit_hook_installed'
            $passedValue = Get-JsonPropertyValue -Object $evalReport -Name 'passed'
            $failedCountValue = Get-JsonPropertyValue -Object $evalReport -Name 'failed_count'
            $passedCountValue = Get-JsonPropertyValue -Object $evalReport -Name 'passed_count'

            $evalPassed = $generatorValue -eq 'tools/MemorySemantic/lancedb_sidecar.py' -and
                $commandValue -eq 'eval' -and
                $statusValue -eq 'ok' -and
                $sourceStoreValue -eq 'sqlite-fts5' -and
                $cloudEnabledValue -eq $false -and
                $autoCommitRefreshValue -eq $false -and
                $directProjectCrawlValue -eq $false -and
                $commitHookInstalledValue -eq $false -and
                $passedValue -eq $true -and
                $failedCountValue -eq 0 -and
                $passedCountValue -ge $MinimumEvalCases

            $evalDetail = "passed_count=$passedCountValue; failed_count=$failedCountValue"
        }
    }

    $checks.Add((New-CheckResult -Name 'lancedb-eval-passed' -Description 'Require LanceDB eval gate to pass' -Passed $evalPassed -Detail $evalDetail))

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
    started_at = $startedAt
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    manual_only = $true
    requires_existing_refresh_all_report = $true
    runs_refresh_all = $false
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    commit_hook_installed = $false
    pre_push_hook_installed = $false
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
