[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
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

    $normalized = $RelativePath.Replace('\', '/')
    $lower = $normalized.ToLowerInvariant()

    $deniedPrefixes = @(
        '.git/',
        '.hindsight/',
        '.gbrain/',
        '.graphify/',
        '.mem0/',
        '.graphiti/',
        'recordings/',
        'data/',
        'docs/memory/generated/',
        'secrets/'
    )

    foreach ($prefix in $deniedPrefixes) {
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

function Add-RedactionFinding {
    param(
        [System.Collections.Generic.List[object]]$Findings,
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][int]$Line,
        [Parameter(Mandatory = $true)][string]$Rule,
        [string]$Severity = 'review'
    )

    $Findings.Add([ordered]@{
        type = $Type
        severity = $Severity
        source_path = $SourcePath
        line = $Line
        rule = $Rule
    })
}

function Get-RedactionFindings {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $findings = [System.Collections.Generic.List[object]]::new()
    $lines = [System.IO.File]::ReadAllLines($File.FullName)

    for ($index = 0; $index -lt $lines.Length; $index++) {
        $lineNumber = $index + 1
        $line = $lines[$index]

        if ($line -match '(?i)(OPENAI_API_KEY|BINANCE_API_KEY|API[_ -]?KEY|SECRET|TOKEN|CREDENTIAL|PASSWORD|sk-[A-Za-z0-9_-]{8,})') {
            Add-RedactionFinding -Findings $findings -Type 'secret_reference' -SourcePath $RelativePath -Line $lineNumber -Rule 'secret/token/api key marker'
        }

        if ($line -match '(?i)(^|[\s`''"])\.env($|[\s`''".])|env contents|env file') {
            Add-RedactionFinding -Findings $findings -Type 'env_reference' -SourcePath $RelativePath -Line $lineNumber -Rule '.env marker'
        }

        if ($line -match '(?i)([A-Z]:\\Users\\|C:\\Users\\|/Users/|/home/)') {
            Add-RedactionFinding -Findings $findings -Type 'absolute_local_path' -SourcePath $RelativePath -Line $lineNumber -Rule 'machine-local absolute path'
        }

        if ($line -match '(?i)(local proxy|local-proxy|local_proxy|shadowsocks|ss-local|socks5|127\.0\.0\.1:\d+|localhost:\d+)') {
            Add-RedactionFinding -Findings $findings -Type 'local_proxy_detail' -SourcePath $RelativePath -Line $lineNumber -Rule 'local proxy detail'
        }

        if ($line -match '(?i)(raw JSONL|JSONL dump|raw dump|raw experiment|experiment dump|recordings/.*\.jsonl)') {
            Add-RedactionFinding -Findings $findings -Type 'raw_jsonl_or_dump' -SourcePath $RelativePath -Line $lineNumber -Rule 'raw recording or dump reference'
        }

        if ($line -match '(?i)(docs/memory/generated/|generated export|generated exports|memory export)') {
            Add-RedactionFinding -Findings $findings -Type 'generated_export_reference' -SourcePath $RelativePath -Line $lineNumber -Rule 'generated export reference'
        }
    }

    return $findings
}

function Add-RetainFile {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [System.Collections.Generic.List[object]]$Files,
        [System.Collections.Generic.List[object]]$Findings,
        [System.Collections.Generic.HashSet[string]]$Seen
    )

    $relativePath = Get-RelativeProjectPath $File.FullName
    if (Test-DeniedRetainPath $relativePath) {
        return
    }

    if (-not $Seen.Add($relativePath)) {
        return
    }

    $fileFindings = @(Get-RedactionFindings -File $File -RelativePath $relativePath)
    foreach ($finding in $fileFindings) {
        $Findings.Add($finding)
    }

    $Files.Add([ordered]@{
        path = $relativePath
        hash = Get-Sha256FileHash $File.FullName
        size_bytes = $File.Length
        redaction_status = if ($fileFindings.Count -gt 0) { 'review_required' } else { 'candidate' }
        finding_count = $fileFindings.Count
    })
}

$files = [System.Collections.Generic.List[object]]::new()
$findings = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($relativePath in @('AGENTS.md', 'TC-DN-HOFI3.md', 'docs/formulas.md', 'tasks/lessons.md')) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Required dry-run source is missing: $relativePath"
    }

    Add-RetainFile -File (Get-Item $path) -Files $files -Findings $findings -Seen $seen
}

foreach ($directoryPath in @('docs\decisions', 'docs\memory')) {
    $fullDirectoryPath = Join-Path $root $directoryPath
    if (Test-Path $fullDirectoryPath -PathType Container) {
        Get-ChildItem -Path $fullDirectoryPath -File -Filter '*.md' |
            ForEach-Object { Add-RetainFile -File $_ -Files $files -Findings $findings -Seen $seen }
    }
}

$sortedFiles = @($files | Sort-Object -Property path)
$sortedFindings = @($findings | Sort-Object -Property source_path, line, type)
$relativeOutputPath = Get-RelativeProjectPath $OutputPath

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/curated-retain-dry-run.ps1'
    mode = 'dry-run'
    purpose = 'Curated retain preflight report only; this script does not call retain/import APIs.'
    status = if ($sortedFindings.Count -gt 0) { 'review_required' } else { 'ready_for_review' }
    output_path = $relativeOutputPath
    output_is_generated = $relativeOutputPath.StartsWith('docs/memory/generated/')
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
    allowed_patterns = $allowedPatterns
    denied_patterns = $deniedPatterns
    summary = [ordered]@{
        file_count = $sortedFiles.Count
        finding_count = $sortedFindings.Count
        files_requiring_redaction_review = @($sortedFiles | Where-Object { $_.redaction_status -ne 'candidate' }).Count
    }
    files = $sortedFiles
    findings = $sortedFindings
}

$json = $report | ConvertTo-Json -Depth 10
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Output "Generated $relativeOutputPath"
Write-Output "Files: $($sortedFiles.Count)"
Write-Output "Findings: $($sortedFindings.Count)"
Write-Output "External retain: disabled"
