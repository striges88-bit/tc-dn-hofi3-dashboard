[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$InputReportPath = '',
    [Parameter(Mandatory = $true)][string[]]$SourcePath,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$toolRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
. (Join-Path $scriptRoot 'curated-retain-scanner-client.ps1')
. (Join-Path $scriptRoot 'curated-retain-redaction.ps1')
. (Join-Path $scriptRoot 'curated-retain-dry-run-contract.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = $toolRoot
}

$root = (Resolve-Path $ProjectRoot).Path
if (-not (Test-Path (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

if ([string]::IsNullOrWhiteSpace($InputReportPath)) {
    $InputReportPath = Join-Path $root 'docs\memory\generated\curated-retain-dry-run-report.json'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-redacted-subset-report.json'
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

function Get-Sha256TextHash {
    param([AllowEmptyString()][string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Test-PathWithinProjectRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/')
    $candidateFull = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
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

function Write-RedactedSubsetMarkdownReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Report
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Curated Retain Redacted Subset Report')
    $lines.Add('')
    $lines.Add("- Status: $($Report.status)")
    $lines.Add("- Files: $($Report.summary.file_count)")
    $lines.Add("- Original findings redacted: $($Report.summary.original_finding_count)")
    $lines.Add("- External retain: disabled")
    $lines.Add("- Codex auto-retain: disabled")
    $lines.Add('')
    $lines.Add('| Source | Status | Content | Original Findings |')
    $lines.Add('| --- | --- | --- | ---: |')
    foreach ($file in $Report.files) {
        $lines.Add("| $($file['path']) | $($file['redaction_status']) | $($file['content_kind']) | $($file['original_finding_count']) |")
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

if (-not (Test-Path $InputReportPath -PathType Leaf)) {
    throw "Curated retain dry-run report is missing: $InputReportPath"
}

$inputReport = Get-Content -Raw $InputReportPath | ConvertFrom-Json
$scan = Invoke-CuratedRetainScan -ToolRoot $toolRoot -ProjectRoot $root
$blockingReasons = [System.Collections.Generic.List[string]]::new()
$selectedFiles = [System.Collections.Generic.List[object]]::new()
$requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$hasDuplicateRequestedPath = $false
foreach ($path in $SourcePath) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        if (-not $requested.Add($path)) {
            Add-BlockingReason -Reasons $blockingReasons -Reason 'duplicate_source_path'
            $hasDuplicateRequestedPath = $true
        }
    }
}

if ($requested.Count -eq 0) {
    Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_source_path'
}
elseif ($hasDuplicateRequestedPath) {
    $requested.Clear()
}

$inputContractValid = Test-DryRunReportContract -Report $inputReport -Scan $scan
if (-not $inputContractValid) {
    Add-BlockingReason -Reasons $blockingReasons -Reason 'invalid_input_report_contract'
}

$inputFiles = if ($inputContractValid) { @($inputReport.files) } else { @() }
$inputFindings = if ($inputContractValid) { @($scan.findings) } else { @() }
$scannedFilesByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($scannedFile in @($scan.files)) {
    $scannedFilesByPath.Add([string]$scannedFile.path, $scannedFile)
}

foreach ($requestedPath in $requested) {
    $requestedPathSegments = $requestedPath.Split('/')
    if ([System.IO.Path]::IsPathRooted($requestedPath) -or $requestedPathSegments -contains '..') {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_outside_project'
        continue
    }

    $scannedFile = $null
    if (-not $scannedFilesByPath.TryGetValue($requestedPath, [ref]$scannedFile)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_not_allowlisted'
        continue
    }

    $file = @($inputFiles | Where-Object { ([string]$_.path) -ceq $requestedPath }) | Select-Object -First 1
    if ($null -eq $file) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_not_in_dry_run_report'
        continue
    }

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $requestedPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    if (-not (Test-PathWithinProjectRoot $fullPath)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_outside_project'
        continue
    }

    if (-not (Test-Path $fullPath -PathType Leaf)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_missing'
        continue
    }

    $fullPath = (Resolve-Path -LiteralPath $fullPath).Path
    if (-not (Test-PathWithinProjectRoot $fullPath)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_outside_project'
        continue
    }

    $reportedHash = [string]$file.hash
    if ([string]::IsNullOrWhiteSpace($reportedHash)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_source_hash'
        continue
    }

    $currentHash = [string]$scannedFile.hash
    if (-not $currentHash.Equals($reportedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_source_metadata'
        continue
    }

    $sourceFindings = @($inputFindings | Where-Object { ([string]$_.source_path) -ceq $requestedPath })
    $isRedacted = $sourceFindings.Count -gt 0
    $selectedFile = [ordered]@{
        path = $requestedPath
        hash = [string]$file.hash
        size_bytes = [int64]$scannedFile.size_bytes
        redaction_status = if ($isRedacted) { 'redacted' } else { 'candidate' }
        content_kind = if ($isRedacted) { 'reviewed-redacted-text' } else { 'commit-source-reference' }
        raw_source_text_included = $false
        source_derived_text_included = $isRedacted
        candidate_text_included = $false
        redacted_text_included = $isRedacted
        finding_count = 0
        original_finding_count = $sourceFindings.Count
    }

    if ($isRedacted) {
        $redaction = New-CuratedRetainRedactedText -Path $fullPath -Findings $sourceFindings
        if ([string]$redaction.status -cne 'redacted') {
            Add-BlockingReason -Reasons $blockingReasons -Reason ([string]$redaction.status)
            continue
        }

        $redactedText = [string]$redaction.text
        $selectedFile.redacted_hash = Get-Sha256TextHash $redactedText
        $selectedFile.redacted_text = $redactedText
    }

    $selectedFiles.Add($selectedFile)
}

$relativeOutputPath = Get-RelativeProjectPath $OutputPath
$markdownOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
$relativeMarkdownOutputPath = Get-RelativeProjectPath $markdownOutputPath
$status = if ($blockingReasons.Count -gt 0 -or $selectedFiles.Count -eq 0) { 'blocked' } else { 'ready_for_import' }
$candidateFileCount = @($selectedFiles | Where-Object { $_['redaction_status'] -eq 'candidate' }).Count
$redactedFileCount = @($selectedFiles | Where-Object { $_['redaction_status'] -eq 'redacted' }).Count

$report = [ordered]@{
    schema_version = 2
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-redacted-subset.ps1'
    mode = 'redacted-subset'
    purpose = 'Provider-neutral reviewed redacted subset report only; this script does not import or retain data.'
    status = $status
    input_report_path = Get-RelativeProjectPath $InputReportPath
    output_path = $relativeOutputPath
    markdown_report_path = $relativeMarkdownOutputPath
    output_is_generated = $relativeOutputPath.StartsWith('docs/memory/generated/')
    output_should_be_ignored = $true
    markdown_output_should_be_ignored = $true
    raw_source_text_included = $false
    source_derived_text_included = $redactedFileCount -gt 0
    candidate_text_included = $false
    redacted_text_included = $redactedFileCount -gt 0
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
    blocking_reasons = @($blockingReasons)
    summary = [ordered]@{
        file_count = $selectedFiles.Count
        candidate_file_count = $candidateFileCount
        redacted_file_count = $redactedFileCount
        original_finding_count = (@($selectedFiles | ForEach-Object { [int]($_['original_finding_count']) }) | Measure-Object -Sum).Sum
    }
    files = @($selectedFiles)
    findings = @()
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$report | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputPath -Encoding UTF8
Write-RedactedSubsetMarkdownReport -Path $markdownOutputPath -Report $report

Write-Output "Generated $relativeOutputPath"
Write-Output "Generated $relativeMarkdownOutputPath"
Write-Output "Status: $status"
Write-Output "Files: $($selectedFiles.Count)"
Write-Output "External retain: disabled"
