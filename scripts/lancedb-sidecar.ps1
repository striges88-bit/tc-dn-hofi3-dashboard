param(
    [string]$ProjectRoot = '',
    [ValidateSet('probe', 'rebuild', 'search', 'explain', 'eval', 'cleanup')]
    [string]$Command = 'probe',
    [string]$DatabasePath = '',
    [string]$StorePath = '',
    [string]$OutputPath = '',
    [string]$EvalMarkdownOutputPath = '',
    [string]$Query = '',
    [int]$Limit = 10,
    [ValidateSet('fastembed', 'token-hash')]
    [string]$EmbeddingProvider = 'fastembed',
    [string]$EmbeddingModel = 'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2'
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

function Convert-ToRepoPath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $pathFull.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
    }

    return $Path
}

function Find-Executable {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $wingetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path -LiteralPath $wingetRoot) {
        $match = Get-ChildItem -Path $wingetRoot -Filter $Name -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    return $null
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

function Get-DefaultOutputPath {
    param(
        [string]$Root,
        [string]$Command
    )

    $fileName = switch ($Command) {
        'probe' { 'lancedb-probe-report.json' }
        'search' { 'lancedb-search-report.json' }
        'explain' { 'lancedb-explain-report.json' }
        'cleanup' { 'lancedb-cleanup-report.json' }
        'rebuild' { 'lancedb-rebuild-report.json' }
        'eval' { 'lancedb-sidecar-report.json' }
        default { 'lancedb-sidecar-report.json' }
    }

    return (Join-Path $Root (Join-Path 'docs\memory\generated' $fileName))
}

function Write-JsonReport {
    param(
        [string]$Path,
        [hashtable]$Payload
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Payload | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
$database = Resolve-RootedOrRelativePath -Root $root -Path $DatabasePath -DefaultPath (Join-Path $root 'docs\memory\generated\project-memory.sqlite')
$store = Resolve-RootedOrRelativePath -Root $root -Path $StorePath -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb')
$output = Resolve-RootedOrRelativePath -Root $root -Path $OutputPath -DefaultPath (Get-DefaultOutputPath -Root $root -Command $Command)
$evalJsonReport = Resolve-RootedOrRelativePath -Root $root -Path '' -DefaultPath (Get-DefaultOutputPath -Root $root -Command 'eval')
$evalMarkdownOutput = Resolve-RootedOrRelativePath -Root $root -Path $EvalMarkdownOutputPath -DefaultPath (Join-Path $root 'docs\memory\generated\lancedb-eval-report.md')

$scriptPath = Join-Path $root 'tools\MemorySemantic\lancedb_sidecar.py'
$uvPath = Find-Executable -Name 'uv.exe'
$supportedCommands = @('probe', 'rebuild', 'search', 'explain', 'eval', 'cleanup')
$fastEmbedPackagePin = 'fastembed==0.8.0'
$fastEmbedDefaultModel = 'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2'
$fastEmbedRuntimeModel = 'tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean'
$embeddingPackagePin = if ($EmbeddingProvider -eq 'token-hash') { 'builtin' } else { $fastEmbedPackagePin }
$embeddingRuntimeModel = if ($EmbeddingProvider -eq 'token-hash') {
    'local-token-hash'
} elseif ($EmbeddingModel -eq $fastEmbedDefaultModel) {
    $fastEmbedRuntimeModel
} else {
    $EmbeddingModel
}
$embeddingPooling = if ($EmbeddingProvider -eq 'token-hash') { 'not-applicable' } elseif ($EmbeddingModel -eq $fastEmbedDefaultModel) { 'mean' } else { 'unknown' }
$embeddingPoolingBaseline = if ($EmbeddingProvider -eq 'token-hash') { 'not-applicable' } else { 'mean-pooling' }
$embeddingBaselineStatus = if ($EmbeddingProvider -eq 'token-hash') { 'fallback-test-only' } else { 'accepted-if-eval-passes' }
$embeddingBaselineEvalGate = if ($EmbeddingProvider -eq 'token-hash') { 'not-semantic-quality-evidence' } else { 'lancedb-eval-9-of-9' }
$embeddingBaselineChangePolicy = if ($EmbeddingProvider -eq 'token-hash') {
    'do not use token-hash as semantic quality evidence'
} else {
    'rerun cleanup/rebuild/eval and update docs before changing package, model, or pooling'
}
$embeddingWarningPolicy = if ($EmbeddingProvider -eq 'token-hash') {
    'not-applicable'
} else {
    'production-custom-alias-no-suppression'
}

if ($Command -eq 'probe') {
    Write-JsonReport -Path $output -Payload @{
        schema_version = 1
        generator = 'scripts/lancedb-sidecar.ps1'
        mode = 'local-python-embedded'
        source_store = 'sqlite-fts5'
        lancedb_store_path = Convert-ToRepoPath -Root $root -Path $store
        sqlite_database_path = Convert-ToRepoPath -Root $root -Path $database
        report_path = Convert-ToRepoPath -Root $root -Path $output
        eval_json_report_path = Convert-ToRepoPath -Root $root -Path $evalJsonReport
        eval_markdown_report_path = Convert-ToRepoPath -Root $root -Path $evalMarkdownOutput
        python_script = 'tools/MemorySemantic/lancedb_sidecar.py'
        uv_path = $uvPath
        cloud_enabled = $false
        auto_commit_refresh_enabled = $false
        direct_project_crawl_enabled = $false
        import_executed = $false
        commit_hook_installed = $false
        supported_commands = $supportedCommands
        embedding_provider = $EmbeddingProvider
        embedding_model = if ($EmbeddingProvider -eq 'token-hash') { 'local-token-hash' } else { $EmbeddingModel }
        embedding_package_pin = $embeddingPackagePin
        embedding_runtime_model = $embeddingRuntimeModel
        embedding_pooling = $embeddingPooling
        embedding_pooling_baseline = $embeddingPoolingBaseline
        embedding_baseline_status = $embeddingBaselineStatus
        embedding_baseline_eval_gate = $embeddingBaselineEvalGate
        embedding_baseline_change_policy = $embeddingBaselineChangePolicy
        embedding_warning_policy = $embeddingWarningPolicy
        status = if ($null -eq $uvPath) { 'uv-unavailable' } else { 'ready-to-run' }
        next_action = 'Run rebuild after SQLite memory refresh; do not install hooks or crawl project files directly.'
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Missing Python sidecar script: $scriptPath"
}

if ($null -eq $uvPath) {
    throw 'uv.exe was not found in PATH or the WinGet package directory.'
}

$arguments = @(
    'run',
    '--python', '3.12',
    '--with', 'lancedb',
    '--with', 'pyarrow',
    '--with', $fastEmbedPackagePin,
    'python',
    $scriptPath,
    '--command', $Command,
    '--project-root', $root,
    '--sqlite', $database,
    '--store', $store,
    '--output', $output,
    '--limit', ([string]$Limit),
    '--embedding-provider', $EmbeddingProvider,
    '--embedding-model', $EmbeddingModel,
    '--eval-markdown-output', $evalMarkdownOutput
)

if (-not [string]::IsNullOrWhiteSpace($Query)) {
    $arguments += @('--query', $Query)
}

& $uvPath @arguments
exit $LASTEXITCODE
