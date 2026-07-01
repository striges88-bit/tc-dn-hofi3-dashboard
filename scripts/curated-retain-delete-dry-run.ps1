[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$ExportReportPath = '',
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

if ([string]::IsNullOrWhiteSpace($ExportReportPath)) {
    $ExportReportPath = Join-Path $root 'docs\memory\generated\curated-retain-export-dry-run-report.json'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-delete-dry-run-report.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

function Get-RelativeProjectPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = if (Test-Path $Path) {
        (Resolve-Path $Path).Path
    }
    else {
        [System.IO.Path]::GetFullPath($Path)
    }

    $rootWithSeparator = $root
    if (-not $rootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootWithSeparator = $rootWithSeparator + [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [Uri]$rootWithSeparator
    $resolvedUri = [Uri]$resolved
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($resolvedUri).ToString()).Replace('\', '/')
}

function Get-Sha256FileHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = (Resolve-Path $Path).Path
    $stream = [System.IO.File]::OpenRead($resolvedPath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-DeniedRetainPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $lower = $RelativePath.Replace('\', '/').ToLowerInvariant()
    foreach ($prefix in @('.git/', '.hindsight/', '.gbrain/', '.graphify/', '.mem0/', '.graphiti/', 'recordings/', 'data/', 'docs/memory/generated/', 'secrets/')) {
        if ($lower.StartsWith($prefix)) {
            return $true
        }
    }

    $segments = $lower.Split('/')
    foreach ($segment in @('bin', 'obj', 'publish')) {
        if ($segments -contains $segment) {
            return $true
        }
    }

    $fileName = [System.IO.Path]::GetFileName($lower)
    if ($fileName -eq '.env' -or
        $fileName.StartsWith('.env.') -or
        $lower.EndsWith('.jsonl') -or
        $lower.Contains('secret') -or
        $lower.Contains('credential') -or
        $lower.Contains('api-key') -or
        $lower.Contains('apikey') -or
        $lower.Contains('token') -or
        $lower.Contains('local-proxy') -or
        $lower.Contains('local_proxy') -or
        $lower.Contains('proxy-local') -or
        $lower.Contains('proxy_local') -or
        $lower.Contains('raw-experiment') -or
        $lower.Contains('raw_experiment') -or
        $lower.Contains('experiment-dump') -or
        $lower.Contains('experiment_dump') -or
        $lower.Contains('raw-dump') -or
        $lower.Contains('raw_dump') -or
        $lower.Contains('shadowsocks') -or
        $lower.Contains('ss-local')) {
        return $true
    }

    return $false
}

function Add-BlockingReason {
    param(
        [System.Collections.Generic.List[string]]$Reasons,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    if (-not $Reasons.Contains($Reason)) {
        $Reasons.Add($Reason)
    }
}

function New-InvalidSource {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    return [ordered]@{
        source_path = $SourcePath
        reason = $Reason
    }
}

function Write-MarkdownReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Report
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Curated Retain Delete Dry-Run Report')
    $lines.Add('')
    $lines.Add("- Status: $($Report.status)")
    $lines.Add("- Sources: $($Report.summary.source_count)")
    $lines.Add("- Invalid sources: $($Report.summary.invalid_source_count)")
    $lines.Add("- Delete actions executed: false")
    $lines.Add("- External retain: disabled")
    $lines.Add("- Codex auto-retain: disabled")
    $lines.Add('')
    $lines.Add('## Blocking Reasons')
    $lines.Add('')
    if ($Report.blocking_reasons.Count -eq 0) {
        $lines.Add('No delete dry-run blockers. Retain still remains disabled until explicit approval.')
    }
    else {
        foreach ($reason in $Report.blocking_reasons) {
            $lines.Add("- $reason")
        }
    }
    $lines.Add('')
    $lines.Add('## Planned Selectors')
    $lines.Add('')
    foreach ($selector in $Report.planned_delete_selectors) {
        $lines.Add("- $selector")
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$relativeExportReportPath = Get-RelativeProjectPath $ExportReportPath
$relativeOutputPath = Get-RelativeProjectPath $OutputPath
$markdownOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
$relativeMarkdownOutputPath = Get-RelativeProjectPath $markdownOutputPath
$blockingReasons = [System.Collections.Generic.List[string]]::new()
$sources = [System.Collections.Generic.List[object]]::new()
$invalidSources = [System.Collections.Generic.List[object]]::new()
$exportReportPresent = Test-Path $ExportReportPath -PathType Leaf
$exportReport = $null

if (-not $exportReportPresent) {
    Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_curated_retain_export_report'
}
else {
    try {
        $exportReport = Get-Content -LiteralPath $ExportReportPath -Raw | ConvertFrom-Json
    }
    catch {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'unreadable_curated_retain_export_report'
    }
}

if ($exportReport -ne $null) {
    if ([string]$exportReport.status -eq 'blocked') {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'export_report_blocked'
    }

    foreach ($invalidSource in @($exportReport.invalid_sources)) {
        $sourcePath = [string]$invalidSource.source_path
        $reason = [string]$invalidSource.reason
        if ([string]::IsNullOrWhiteSpace($reason)) {
            $reason = 'invalid_source'
        }

        if (-not [string]::IsNullOrWhiteSpace($sourcePath)) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath.Replace('\', '/') -Reason $reason))
            if ($reason -eq 'denied_path') {
                Add-BlockingReason -Reasons $blockingReasons -Reason 'denied_sources_in_export_report'
            }
        }
    }

    foreach ($source in @($exportReport.sources)) {
        $sourcePath = ([string]$source.source_path).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $invalidSources.Add((New-InvalidSource -SourcePath '' -Reason 'missing_source_path'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'invalid_sources_in_export_report'
            continue
        }

        if (Test-DeniedRetainPath $sourcePath) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath -Reason 'denied_path'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'denied_sources_in_export_report'
            continue
        }

        $fullPath = Join-Path $root ($sourcePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path $fullPath -PathType Leaf)) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath -Reason 'missing_source_file'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_export_source_metadata'
            continue
        }

        $currentHash = Get-Sha256FileHash $fullPath
        $exportedHash = [string]$source.source_hash
        $hashMatchesExport = -not [string]::IsNullOrWhiteSpace($exportedHash) -and $currentHash -eq $exportedHash
        if (-not $hashMatchesExport) {
            Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_export_source_metadata'
        }

        $sources.Add([ordered]@{
            source_path = $sourcePath
            source_hash = $currentHash
            exported_source_hash = $exportedHash
            hash_matches_export = $hashMatchesExport
            source_metadata_only = $true
            would_delete = $false
        })
    }
}

$sortedSources = @($sources | Sort-Object -Property source_path)
$sortedInvalidSources = @($invalidSources | Sort-Object -Property source_path, reason)
$deniedSourceCount = @($sortedInvalidSources | Where-Object { $_['reason'] -eq 'denied_path' }).Count
$mismatchCount = @($sortedSources | Where-Object { -not $_['hash_matches_export'] }).Count
$reportStatus = if ($blockingReasons.Count -gt 0) { 'blocked' } else { 'ready_for_review' }

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-delete-dry-run.ps1'
    mode = 'delete-dry-run'
    purpose = 'Provider-neutral delete lifecycle dry-run. No retained items, files, provider data, hooks, or rebuilds are deleted.'
    status = $reportStatus
    export_report_path = $relativeExportReportPath
    output_path = $relativeOutputPath
    markdown_report_path = $relativeMarkdownOutputPath
    output_is_generated = $relativeOutputPath.StartsWith('docs/memory/generated/')
    output_should_be_ignored = $true
    markdown_output_should_be_ignored = $true
    export_report_present = $exportReportPresent
    stale_report = $mismatchCount -gt 0
    retain_enablement_candidate = $false
    external_retain_enabled = $false
    codex_auto_retain_enabled = $false
    cloud_enabled = $false
    calls_hindsight = $false
    calls_codex_retain = $false
    installs_hooks = $false
    runs_refresh_all = $false
    rebuilds_memory = $false
    deletes_items = $false
    removes_files = $false
    writes_report_only = $true
    planned_delete_selectors = @('retained_item_id', 'source_path', 'project_profile')
    blocking_reasons = @($blockingReasons)
    summary = [ordered]@{
        source_count = $sortedSources.Count
        invalid_source_count = $sortedInvalidSources.Count
        denied_source_count = $deniedSourceCount
        source_hash_mismatch_count = $mismatchCount
    }
    sources = $sortedSources
    invalid_sources = $sortedInvalidSources
}

$report | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding UTF8
Write-MarkdownReport -Path $markdownOutputPath -Report $report

Write-Output "Generated $relativeOutputPath"
Write-Output "Generated $relativeMarkdownOutputPath"
Write-Output "Status: $reportStatus"
Write-Output "Delete actions executed: false"
