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
    $OutputPath = Join-Path $root 'docs\memory\generated\hindsight-curated-import-manifest.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$allowedPatterns = @(
    'docs/memory/*.md',
    'docs/decisions/*.md',
    'docs/formulas.md',
    'AGENTS.md',
    'tasks/lessons.md'
)

$deniedPatterns = @(
    'recordings/*.jsonl',
    'docs/memory/generated/',
    'secrets',
    'build artifacts',
    'local proxy details'
)

function Get-RelativeProjectPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path $Path).Path
    $rootWithSeparator = $root
    if (-not $rootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootWithSeparator = $rootWithSeparator + [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [Uri]$rootWithSeparator
    $resolvedUri = [Uri]$resolved
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($resolvedUri).ToString()).Replace('\', '/')
}

function Test-DeniedImportPath {
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
        'docs/memory/generated/'
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
        $lower.Contains('shadowsocks') -or
        $lower.Contains('ss-local')) {
        return $true
    }

    return $false
}

function Add-ImportFile {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [System.Collections.Generic.List[object]]$Files,
        [System.Collections.Generic.HashSet[string]]$Seen
    )

    $relativePath = Get-RelativeProjectPath $File.FullName
    if (Test-DeniedImportPath $relativePath) {
        return
    }

    if (-not $Seen.Add($relativePath)) {
        return
    }

    $Files.Add([ordered]@{
        path = $relativePath
        hash = (Get-FileHash -Algorithm SHA256 -Path $File.FullName).Hash.ToLowerInvariant()
        size_bytes = $File.Length
    })
}

$files = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($relativePath in @('AGENTS.md', 'docs/formulas.md', 'tasks/lessons.md')) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Required import source is missing: $relativePath"
    }

    Add-ImportFile -File (Get-Item $path) -Files $files -Seen $seen
}

foreach ($directoryPath in @('docs\memory', 'docs\decisions')) {
    $fullDirectoryPath = Join-Path $root $directoryPath
    if (Test-Path $fullDirectoryPath -PathType Container) {
        Get-ChildItem -Path $fullDirectoryPath -File -Filter '*.md' |
            ForEach-Object { Add-ImportFile -File $_ -Files $files -Seen $seen }
    }
}

$sortedFiles = @($files | Sort-Object -Property path)
$manifest = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/hindsight-curated-import.ps1'
    purpose = 'Pre-install curated Hindsight import file manifest only; this script does not call Hindsight.'
    install_required = $false
    codex_auto_retain_enabled = $false
    allowed_patterns = $allowedPatterns
    denied_patterns = $deniedPatterns
    files = $sortedFiles
}

$json = $manifest | ConvertTo-Json -Depth 8
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Output "Generated $(Get-RelativeProjectPath $OutputPath)"
Write-Output "Files: $($sortedFiles.Count)"
Write-Output "Codex auto-retain: disabled"
