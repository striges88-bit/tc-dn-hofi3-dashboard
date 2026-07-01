[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$InputReportPath = '',
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
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-export-dry-run-report.json'
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

function Test-AllowlistedRetainPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = $RelativePath.Replace('\', '/')
    if ($path -eq 'AGENTS.md' -or
        $path -eq 'TC-DN-HOFI3.md' -or
        $path -eq 'docs/formulas.md' -or
        $path -eq 'tasks/lessons.md') {
        return $true
    }

    if ($path.StartsWith('docs/decisions/') -and $path.EndsWith('.md')) {
        return $true
    }

    if ($path.StartsWith('docs/memory/') -and
        -not $path.StartsWith('docs/memory/generated/') -and
        $path.EndsWith('.md')) {
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
    $lines.Add('# Curated Retain Export Dry-Run Report')
    $lines.Add('')
    $lines.Add("- Status: $($Report.status)")
    $lines.Add("- Sources: $($Report.summary.source_count)")
    $lines.Add("- Invalid sources: $($Report.summary.invalid_source_count)")
    $lines.Add("- Stale source hashes: $($Report.summary.source_hash_mismatch_count)")
    $lines.Add("- External retain: disabled")
    $lines.Add("- Codex auto-retain: disabled")
    $lines.Add('')
    $lines.Add('## Blocking Reasons')
    $lines.Add('')
    if ($Report.blocking_reasons.Count -eq 0) {
        $lines.Add('No export dry-run blockers. Retain still remains disabled until explicit approval.')
    }
    else {
        foreach ($reason in $Report.blocking_reasons) {
            $lines.Add("- $reason")
        }
    }
    $lines.Add('')
    $lines.Add('## Sources')
    $lines.Add('')
    $lines.Add('| Source | Hash Matches Report | Redaction Status |')
    $lines.Add('| --- | --- | --- |')
    foreach ($source in $Report.sources) {
        $lines.Add("| $($source['source_path']) | $($source['hash_matches_report']) | $($source['redaction_status']) |")
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$relativeInputReportPath = Get-RelativeProjectPath $InputReportPath
$relativeOutputPath = Get-RelativeProjectPath $OutputPath
$markdownOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
$relativeMarkdownOutputPath = Get-RelativeProjectPath $markdownOutputPath
$blockingReasons = [System.Collections.Generic.List[string]]::new()
$sources = [System.Collections.Generic.List[object]]::new()
$invalidSources = [System.Collections.Generic.List[object]]::new()
$curatedReportPresent = Test-Path $InputReportPath -PathType Leaf
$curatedReport = $null

if (-not $curatedReportPresent) {
    Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_curated_retain_report'
}
else {
    try {
        $curatedReport = Get-Content -LiteralPath $InputReportPath -Raw | ConvertFrom-Json
    }
    catch {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'unreadable_curated_retain_report'
    }
}

if ($curatedReport -ne $null) {
    foreach ($file in @($curatedReport.files)) {
        $sourcePath = [string]$file.path
        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $sourcePath = [string]$file.source_path
        }

        $sourcePath = $sourcePath.Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $invalidSources.Add((New-InvalidSource -SourcePath '' -Reason 'missing_source_path'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'invalid_sources_in_input_report'
            continue
        }

        if (Test-DeniedRetainPath $sourcePath) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath -Reason 'denied_path'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'denied_sources_in_input_report'
            continue
        }

        if (-not (Test-AllowlistedRetainPath $sourcePath)) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath -Reason 'not_allowlisted'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'invalid_sources_in_input_report'
            continue
        }

        $fullPath = Join-Path $root ($sourcePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path $fullPath -PathType Leaf)) {
            $invalidSources.Add((New-InvalidSource -SourcePath $sourcePath -Reason 'missing_source_file'))
            Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_source_metadata'
            continue
        }

        $currentHash = Get-Sha256FileHash $fullPath
        $reportedHash = [string]$file.hash
        if ([string]::IsNullOrWhiteSpace($reportedHash)) {
            $reportedHash = [string]$file.source_hash
        }

        $hashMatchesReport = -not [string]::IsNullOrWhiteSpace($reportedHash) -and $currentHash -eq $reportedHash
        if (-not $hashMatchesReport) {
            Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_source_metadata'
        }

        $redactionStatus = [string]$file.redaction_status
        if ([string]::IsNullOrWhiteSpace($redactionStatus)) {
            $redactionStatus = 'unknown'
        }

        $findingCount = 0
        if ($file.PSObject.Properties.Name -contains 'finding_count') {
            $findingCount = [int]$file.finding_count
        }

        if ($redactionStatus -ne 'candidate' -or $findingCount -gt 0) {
            Add-BlockingReason -Reasons $blockingReasons -Reason 'redaction_review_required'
        }

        $fileInfo = Get-Item $fullPath
        $sources.Add([ordered]@{
            source_path = $sourcePath
            source_hash = $currentHash
            reported_source_hash = $reportedHash
            hash_matches_report = $hashMatchesReport
            source_size_bytes = $fileInfo.Length
            redaction_status = $redactionStatus
            finding_count = $findingCount
            source_metadata_only = $true
            would_export = $false
        })
    }
}

$sortedSources = @($sources | Sort-Object -Property source_path)
$sortedInvalidSources = @($invalidSources | Sort-Object -Property source_path, reason)
$mismatchCount = @($sortedSources | Where-Object { -not $_['hash_matches_report'] }).Count
$redactionReviewCount = @($sortedSources | Where-Object { $_['redaction_status'] -ne 'candidate' -or $_['finding_count'] -gt 0 }).Count
$deniedSourceCount = @($sortedInvalidSources | Where-Object { $_['reason'] -eq 'denied_path' }).Count
$reportStatus = if ($blockingReasons.Count -gt 0) { 'blocked' } else { 'ready_for_delete_dry_run' }

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-export-dry-run.ps1'
    mode = 'export-dry-run'
    purpose = 'Provider-neutral export lifecycle dry-run. No retained text, provider calls, imports, hooks, or rebuilds.'
    status = $reportStatus
    input_report_path = $relativeInputReportPath
    output_path = $relativeOutputPath
    markdown_report_path = $relativeMarkdownOutputPath
    output_is_generated = $relativeOutputPath.StartsWith('docs/memory/generated/')
    output_should_be_ignored = $true
    markdown_output_should_be_ignored = $true
    curated_report_present = $curatedReportPresent
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
    imports_denylist = $false
    writes_report_only = $true
    source_content_included = $false
    blocking_reasons = @($blockingReasons)
    summary = [ordered]@{
        source_count = $sortedSources.Count
        invalid_source_count = $sortedInvalidSources.Count
        denied_source_count = $deniedSourceCount
        source_hash_mismatch_count = $mismatchCount
        redaction_review_source_count = $redactionReviewCount
    }
    sources = $sortedSources
    invalid_sources = $sortedInvalidSources
}

$report | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding UTF8
Write-MarkdownReport -Path $markdownOutputPath -Report $report

Write-Output "Generated $relativeOutputPath"
Write-Output "Generated $relativeMarkdownOutputPath"
Write-Output "Status: $reportStatus"
Write-Output "External retain: disabled"
