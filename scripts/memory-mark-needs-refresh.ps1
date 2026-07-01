[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$MarkerPath = '',
    [string]$OutputPath = '',
    [string]$Reason = 'post-commit',
    [int]$TimeoutSeconds = 15
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

function Find-Git {
    $commonGit = 'C:\Program Files\Git\cmd\git.exe'
    if (Test-Path -LiteralPath $commonGit) {
        return $commonGit
    }

    return 'git'
}

function Read-HeadCommit {
    param([string]$Root)

    $git = Find-Git
    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    $process = Start-Process `
        -FilePath $git `
        -ArgumentList @('-C', $Root, 'rev-parse', '--verify', 'HEAD') `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -Wait

    try {
        if ($process.ExitCode -ne 0) {
            return ''
        }

        return (Get-Content -Raw -LiteralPath $stdoutPath).Trim()
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Write-Json {
    param(
        [string]$Path,
        [object]$Payload
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Payload | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

function Acquire-Lock {
    param(
        [string]$Path,
        [int]$Timeout
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $Timeout) {
        try {
            New-Item -ItemType Directory -Path $Path -ErrorAction Stop | Out-Null
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 200
        }
    }

    return $false
}

$startedAt = (Get-Date).ToUniversalTime().ToString('o')
$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$markerFilePath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $MarkerPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-needs-refresh.marker.json')
$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-mark-needs-refresh-report.json')
$lockPath = Join-Path (Split-Path -Parent $markerFilePath) 'memory-needs-refresh.lock'

$status = 'marked'
$failureCode = ''
$lockAcquired = $false
if ($TimeoutSeconds -le 0) {
    $status = 'failed'
    $failureCode = 'invalid-timeout-seconds'
}
else {
    $lockAcquired = Acquire-Lock -Path $lockPath -Timeout $TimeoutSeconds
}

if ($status -ne 'failed' -and -not $lockAcquired) {
    $status = 'timeout'
    $failureCode = 'lock-timeout'
}
elseif ($status -ne 'failed') {
    try {
        $head = Read-HeadCommit -Root $root
        $marker = [ordered]@{
            schema_version = 1
            generator = 'scripts/memory-mark-needs-refresh.ps1'
            reason = $Reason
            head = $head
            created_at = (Get-Date).ToUniversalTime().ToString('o')
            refresh_command = 'tools/Memory refresh-from-commit --commit HEAD'
            runs_refresh_all = $false
            rebuilds_memory = $false
            imports_curated_retain = $false
            cloud_enabled = $false
            codex_auto_retain_enabled = $false
        }

        Write-Json -Path $markerFilePath -Payload $marker | Out-Null
    }
    finally {
        Remove-Item -LiteralPath $lockPath -Force -Recurse -ErrorAction SilentlyContinue
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-mark-needs-refresh.ps1'
    status = $status
    failure_code = $failureCode
    reason = $Reason
    project_root = Convert-ToRepoPath -Root $root -Path $root
    marker_path = Convert-ToRepoPath -Root $root -Path $markerFilePath
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    timeout_seconds = $TimeoutSeconds
    uses_lock = $true
    lock_path = Convert-ToRepoPath -Root $root -Path $lockPath
    lock_acquired = $lockAcquired
    writes_marker = $lockAcquired
    started_at = $startedAt
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    runs_refresh_all = $false
    hook_invokes_refresh_all = $false
    rebuilds_memory = $false
    imports_curated_retain = $false
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    uses_generated_exports_as_source = $false
    touches_build_artifacts = $false
}

Write-Json -Path $reportPath -Payload $report

if ($status -eq 'timeout') {
    exit 0
}

if ($status -eq 'failed') {
    exit 1
}

exit 0
