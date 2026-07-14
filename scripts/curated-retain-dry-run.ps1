[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$toolRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
. (Join-Path $scriptRoot 'curated-retain-scanner-client.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = $toolRoot
}

$root = (Resolve-Path $ProjectRoot).Path
if (-not (Test-Path (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'docs\memory\generated\curated-retain-dry-run-report.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$allowedPatterns = @(
    'AGENTS.md',
    'docs/decisions/*.md',
    'docs/formulas.md',
    'TC-DN-HOFI3.md',
    'docs/memory/*.md',
    'tasks/lessons.md'
)

$deniedPatterns = @(
    'recordings/*.jsonl',
    'docs/memory/generated/',
    '.hindsight/',
    'secrets/',
    'bin/',
    'obj/',
    'publish/',
    'local proxy details',
    'raw experiment dumps'
)

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

function Get-CountMap {
    param(
        [AllowEmptyCollection()][object[]]$Items,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [string[]]$KnownValues = @()
    )

    $counts = [ordered]@{}
    foreach ($knownValue in $KnownValues) {
        $counts[$knownValue] = 0
    }

    foreach ($item in $Items) {
        $value = [string]$item[$PropertyName]
        if (-not $counts.Contains($value)) {
            $counts[$value] = 0
        }

        $counts[$value]++
    }

    return $counts
}

function Write-CuratedRetainMarkdownReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Report
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Curated Retain Dry-Run Report')
    $lines.Add('')
    $lines.Add("- Status: $($Report.status)")
    $lines.Add("- Files: $($Report.summary.file_count)")
    $lines.Add("- Findings: $($Report.summary.finding_count)")
    $lines.Add("- Files requiring review: $($Report.summary.files_requiring_redaction_review)")
    $lines.Add("- External retain: disabled")
    $lines.Add("- Codex auto-retain: disabled")
    $lines.Add('')
    $lines.Add('## Findings By Severity')
    $lines.Add('')
    $lines.Add('| Severity | Count |')
    $lines.Add('| --- | ---: |')
    foreach ($severity in $Report.summary.findings_by_severity.Keys) {
        $lines.Add("| $severity | $($Report.summary.findings_by_severity[$severity]) |")
    }
    $lines.Add('')
    $lines.Add('## Findings By Type')
    $lines.Add('')
    $lines.Add('| Type | Count |')
    $lines.Add('| --- | ---: |')
    foreach ($type in $Report.summary.findings_by_type.Keys) {
        $lines.Add("| $type | $($Report.summary.findings_by_type[$type]) |")
    }
    $lines.Add('')
    $lines.Add('## Review Findings')
    $lines.Add('')
    if ($Report.findings.Count -eq 0) {
        $lines.Add('No findings.')
    }
    else {
        $lines.Add('| Severity | Type | Source | Line | Policy Reference | Rule |')
        $lines.Add('| --- | --- | --- | ---: | --- | --- |')
        foreach ($finding in $Report.findings) {
            $lines.Add("| $($finding['severity']) | $($finding['type']) | $($finding['source_path']) | $($finding['line']) | $($finding['policy_reference']) | $($finding['rule']) |")
        }
    }

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$scan = Invoke-CuratedRetainScan -ToolRoot $toolRoot -ProjectRoot $root
$sortedFiles = @($scan.files | ForEach-Object {
    [ordered]@{
        path = [string]$_.path
        hash = [string]$_.hash
        size_bytes = [int64]$_.size_bytes
        redaction_status = [string]$_.redaction_status
        finding_count = [int]$_.finding_count
    }
})
$sortedFindings = @($scan.findings | ForEach-Object {
    [ordered]@{
        type = [string]$_.type
        severity = [string]$_.severity
        policy_reference = [bool]$_.policy_reference
        source_path = [string]$_.source_path
        line = [int]$_.line
        rule = [string]$_.rule
    }
})
$relativeOutputPath = Get-RelativeProjectPath $OutputPath
$markdownOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
$relativeMarkdownOutputPath = Get-RelativeProjectPath $markdownOutputPath

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-dry-run.ps1'
    mode = 'dry-run'
    purpose = 'Curated retain preflight report only; this script does not call retain/import APIs.'
    status = if ($sortedFindings.Count -gt 0) { 'review_required' } else { 'ready_for_review' }
    blocking_reasons = @()
    output_path = $relativeOutputPath
    markdown_report_path = $relativeMarkdownOutputPath
    output_is_generated = $relativeOutputPath.StartsWith('docs/memory/generated/')
    output_should_be_ignored = $true
    markdown_output_should_be_ignored = $true
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
    allowed_patterns = $allowedPatterns
    denied_patterns = $deniedPatterns
    summary = [ordered]@{
        file_count = $sortedFiles.Count
        finding_count = $sortedFindings.Count
        files_requiring_redaction_review = @($sortedFiles | Where-Object { $_.redaction_status -ne 'candidate' }).Count
        findings_by_severity = Get-CountMap -Items $sortedFindings -PropertyName 'severity' -KnownValues @('critical', 'review', 'info')
        findings_by_type = Get-CountMap -Items $sortedFindings -PropertyName 'type'
    }
    files = $sortedFiles
    findings = $sortedFindings
}

$json = $report | ConvertTo-Json -Depth 10
Set-Content -Path $OutputPath -Value $json -Encoding UTF8
Write-CuratedRetainMarkdownReport -Path $markdownOutputPath -Report $report

Write-Output "Generated $relativeOutputPath"
Write-Output "Generated $relativeMarkdownOutputPath"
Write-Output "Files: $($sortedFiles.Count)"
Write-Output "Findings: $($sortedFindings.Count)"
Write-Output "External retain: disabled"
