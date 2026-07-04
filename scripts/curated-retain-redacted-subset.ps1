[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$InputReportPath = '',
    [Parameter(Mandatory = $true)][string[]]$SourcePath,
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

function Get-Sha256FileHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead((Resolve-Path $Path).Path)
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
    if ($segments -contains 'bin' -or $segments -contains 'obj' -or $segments -contains 'publish') {
        return $true
    }

    $fileName = [System.IO.Path]::GetFileName($lower)
    return $fileName -eq '.env' -or
        $fileName.StartsWith('.env.') -or
        $lower.EndsWith('.jsonl') -or
        $lower.Contains('secret') -or
        $lower.Contains('credential') -or
        $lower.Contains('api-key') -or
        $lower.Contains('apikey') -or
        $lower.Contains('token') -or
        $lower.Contains('local-proxy') -or
        $lower.Contains('local_proxy') -or
        $lower.Contains('raw-experiment') -or
        $lower.Contains('raw_experiment') -or
        $lower.Contains('experiment-dump') -or
        $lower.Contains('experiment_dump') -or
        $lower.Contains('raw-dump') -or
        $lower.Contains('raw_dump') -or
        $lower.Contains('shadowsocks') -or
        $lower.Contains('ss-local')
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
    $lines.Add('| Source | Status | Original Findings |')
    $lines.Add('| --- | --- | ---: |')
    foreach ($file in $Report.files) {
        $lines.Add("| $($file['path']) | $($file['redaction_status']) | $($file['original_finding_count']) |")
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

if (-not (Test-Path $InputReportPath -PathType Leaf)) {
    throw "Curated retain dry-run report is missing: $InputReportPath"
}

$inputReport = Get-Content -Raw $InputReportPath | ConvertFrom-Json
$blockingReasons = [System.Collections.Generic.List[string]]::new()
$selectedFiles = [System.Collections.Generic.List[object]]::new()
$requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in $SourcePath) {
    $normalized = $path.Replace('\', '/').Trim()
    if (-not [string]::IsNullOrWhiteSpace($normalized)) {
        [void]$requested.Add($normalized)
    }
}

if ($requested.Count -eq 0) {
    Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_source_path'
}

$inputFiles = @($inputReport.files)
$inputFindings = @($inputReport.findings)

foreach ($requestedPath in $requested) {
    if (Test-DeniedRetainPath $requestedPath) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'denied_source_path'
        continue
    }

    $file = @($inputFiles | Where-Object { ([string]$_.path) -eq $requestedPath }) | Select-Object -First 1
    if ($null -eq $file) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_not_in_dry_run_report'
        continue
    }

    $fullPath = Join-Path $root $requestedPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'source_missing'
        continue
    }

    $reportedHash = [string]$file.hash
    if ([string]::IsNullOrWhiteSpace($reportedHash)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'missing_source_hash'
        continue
    }

    $currentHash = Get-Sha256FileHash $fullPath
    if (-not $currentHash.Equals($reportedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-BlockingReason -Reasons $blockingReasons -Reason 'stale_source_metadata'
        continue
    }

    $lines = [System.IO.File]::ReadAllLines((Resolve-Path $fullPath).Path)
    $sourceFindings = @($inputFindings | Where-Object { ([string]$_.source_path) -eq $requestedPath })
    $findingsByLine = @{}
    foreach ($finding in $sourceFindings) {
        $line = [int]$finding.line
        if ($line -lt 1 -or $line -gt $lines.Length) {
            Add-BlockingReason -Reasons $blockingReasons -Reason 'invalid_finding_line'
            continue
        }

        if (-not $findingsByLine.ContainsKey($line)) {
            $findingsByLine[$line] = [System.Collections.Generic.List[string]]::new()
        }

        $type = [string]$finding.type
        if (-not $findingsByLine[$line].Contains($type)) {
            $findingsByLine[$line].Add($type)
        }
    }

    for ($index = 0; $index -lt $lines.Length; $index++) {
        $lineNumber = $index + 1
        if ($findingsByLine.ContainsKey($lineNumber)) {
            $types = @($findingsByLine[$lineNumber] | Sort-Object)
            $lines[$index] = "[REDACTED:$([string]::Join(',', $types))]"
        }
    }

    $redactedText = [string]::Join("`n", $lines) + "`n"
    $selectedFiles.Add([ordered]@{
        path = $requestedPath
        hash = [string]$file.hash
        size_bytes = [int64]$file.size_bytes
        redaction_status = if ($sourceFindings.Count -gt 0) { 'redacted' } else { 'candidate' }
        finding_count = 0
        original_finding_count = $sourceFindings.Count
        redacted_hash = Get-Sha256TextHash $redactedText
        redacted_text = $redactedText
    })
}

$relativeOutputPath = Get-RelativeProjectPath $OutputPath
$markdownOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
$relativeMarkdownOutputPath = Get-RelativeProjectPath $markdownOutputPath
$status = if ($blockingReasons.Count -gt 0 -or $selectedFiles.Count -eq 0) { 'blocked' } else { 'ready_for_import' }

$report = [ordered]@{
    schema_version = 1
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
    source_content_included = $false
    redacted_content_included = $true
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
