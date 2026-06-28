[CmdletBinding()]
param(
    [string]$ProjectRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
}

$root = (Resolve-Path $ProjectRoot).Path
$generatedDirectory = Join-Path $root 'docs\memory\generated'
$indexPath = Join-Path $generatedDirectory 'project-memory-index.json'
$now = (Get-Date).ToUniversalTime().ToString('o')

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

function Get-ProjectFileHash {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $root $RelativePath
    return (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
}

function New-MemoryNode {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Summary,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [double]$Confidence = 0.95,
        [AllowNull()][object]$ValidFrom = '2026-06-28T00:00:00.0000000Z',
        [AllowNull()][object]$ValidUntil = $null
    )

    [ordered]@{
        id = $Id
        type = $Type
        status = $Status
        title = $Title
        summary = $Summary
        source_path = $SourcePath
        source_hash = Get-ProjectFileHash $SourcePath
        created_at = $now
        updated_at = $now
        confidence = $Confidence
        valid_from = $ValidFrom
        valid_until = $ValidUntil
    }
}

function New-MemoryEdge {
    param(
        [Parameter(Mandatory = $true)][string]$From,
        [Parameter(Mandatory = $true)][string]$Relation,
        [Parameter(Mandatory = $true)][string]$To,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [double]$Confidence = 0.95
    )

    [ordered]@{
        from = $From
        relation = $Relation
        to = $To
        source_path = $SourcePath
        source_hash = Get-ProjectFileHash $SourcePath
        confidence = $Confidence
    }
}

function Test-ProjectMemoryFile {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo]$File)

    $relativePath = Get-RelativeProjectPath $File.FullName
    $excludedPrefixes = @(
        '.git/',
        '.dotnet/',
        '.dotnet-home/',
        '.nuget/',
        '.superpowers/',
        '.tools/',
        'recordings/',
        'data/',
        'docs/memory/generated/'
    )

    foreach ($prefix in $excludedPrefixes) {
        if ($relativePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    $segments = $relativePath.Split('/')
    foreach ($excludedSegment in @('bin', 'obj', 'publish')) {
        if ($segments -contains $excludedSegment) {
            return $false
        }
    }

    $allowedExtensions = @('.cs', '.csproj', '.sln', '.xaml', '.json', '.md', '.ps1', '.editorconfig', '.gitignore', '.gitattributes')
    return $allowedExtensions -contains $File.Extension.ToLowerInvariant() -or $File.Name -in @('AGENTS.md', 'README.md')
}

if (-not (Test-Path (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

New-Item -ItemType Directory -Force -Path $generatedDirectory | Out-Null

$nodes = [System.Collections.Generic.List[object]]::new()
$nodes.Add((New-MemoryNode `
    -Id 'data-source.hot-path' `
    -Type 'data_source' `
    -Status 'current' `
    -Title 'Hot path market data' `
    -Summary 'Diff depth and aggregate trade streams feed subsecond calculations; REST is allowed only for initial snapshot and explicit resync.' `
    -SourcePath 'docs/data-sources.md'))
$nodes.Add((New-MemoryNode `
    -Id 'formula.canonical' `
    -Type 'formula' `
    -Status 'current' `
    -Title 'Canonical TC-DN-HOFI3 formula source' `
    -Summary 'TC-DN-HOFI3.md is the canonical formula source; docs/formulas.md summarizes implemented guardrails.' `
    -SourcePath 'docs/formulas.md'))
$nodes.Add((New-MemoryNode `
    -Id 'architecture.application-boundary' `
    -Type 'rule' `
    -Status 'current' `
    -Title 'Application layer boundary' `
    -Summary 'Application depends on Domain only; Infrastructure DTOs and adapters stay outside the indicator engine.' `
    -SourcePath 'docs/architecture.md'))
$nodes.Add((New-MemoryNode `
    -Id 'recordings.raw-jsonl' `
    -Type 'rule' `
    -Status 'current' `
    -Title 'Raw JSONL recordings are not memory facts' `
    -Summary 'Raw JSONL recordings are ignored by Git; only reviewed summaries should become memory facts.' `
    -SourcePath 'docs/data-sources.md'))
$nodes.Add((New-MemoryNode `
    -Id 'decision.agent-memory-contract' `
    -Type 'decision' `
    -Status 'current' `
    -Title 'Agent memory is contract-first tooling' `
    -Summary 'Agent memory starts as a contract, schema, tests, and manual refresh script, not as runtime integration.' `
    -SourcePath 'docs/decisions/0002-agent-memory-contract.md'))
$nodes.Add((New-MemoryNode `
    -Id 'memory.contract' `
    -Type 'rule' `
    -Status 'current' `
    -Title 'Memory retrieval and staleness contract' `
    -Summary 'Generated memory needs source grounding, freshness checks, staged retrieval, and explicit contradiction handling.' `
    -SourcePath 'docs/memory/contract.md'))

$edges = [System.Collections.Generic.List[object]]::new()
$edges.Add((New-MemoryEdge -From 'data-source.hot-path' -Relation 'guards' -To 'formula.canonical' -SourcePath 'docs/data-sources.md'))
$edges.Add((New-MemoryEdge -From 'architecture.application-boundary' -Relation 'guards' -To 'formula.canonical' -SourcePath 'docs/architecture.md'))
$edges.Add((New-MemoryEdge -From 'recordings.raw-jsonl' -Relation 'records' -To 'data-source.hot-path' -SourcePath 'docs/data-sources.md'))
$edges.Add((New-MemoryEdge -From 'recordings.raw-jsonl' -Relation 'replays' -To 'formula.canonical' -SourcePath 'docs/data-sources.md'))
$edges.Add((New-MemoryEdge -From 'decision.agent-memory-contract' -Relation 'owns' -To 'memory.contract' -SourcePath 'docs/decisions/0002-agent-memory-contract.md'))

$sourceFiles = Get-ChildItem -Path $root -File -Recurse |
    Where-Object { Test-ProjectMemoryFile $_ } |
    ForEach-Object {
        $relativePath = Get-RelativeProjectPath $_.FullName
        [ordered]@{
            path = $relativePath
            hash = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
        }
    } |
    Sort-Object -Property path

$toolAvailability = @('gbrain', 'graphify', 'mem0', 'graphiti') | ForEach-Object {
    $command = Get-Command $_ -ErrorAction SilentlyContinue
    [ordered]@{
        name = $_
        available = $null -ne $command
        path = if ($command) { $command.Source } else { $null }
    }
}

$index = [ordered]@{
    schema_version = 1
    generated_at = $now
    generator = 'scripts/memory-refresh.ps1'
    nodes = $nodes
    edges = $edges
    source_files = @($sourceFiles)
    tool_availability = @($toolAvailability)
}

$json = $index | ConvertTo-Json -Depth 8
Set-Content -Path $indexPath -Value $json -Encoding UTF8

Write-Output "Generated $(Get-RelativeProjectPath $indexPath)"
Write-Output "Nodes: $($nodes.Count); edges: $($edges.Count); indexed files: $(@($sourceFiles).Count)"
