[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [switch]$PlanOnly,
    [switch]$AllowNetworkPreflight,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lanceDbPackagePin = 'lancedb==0.34.0'
$pyArrowPackagePin = 'pyarrow==24.0.0'
$fastEmbedPackagePin = 'fastembed==0.8.0'

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

function Join-ProcessArguments {
    param([string[]]$Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            '""'
        }
        elseif ($argument.Length -eq 0 -or $argument.IndexOfAny([char[]]@(' ', "`t", '"')) -ge 0) {
            '"' + $argument.Replace('"', '\"') + '"'
        }
        else {
            $argument
        }
    }

    return [string]::Join(' ', $quoted)
}

function Find-UvExecutable {
    $candidates = [System.Collections.Generic.List[object]]::new()

    foreach ($name in @('uv.exe', 'uv')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            $candidates.Add([ordered]@{
                source = 'PATH'
                path = $command.Source
                exists = $true
            })
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        $appDataCandidate = Join-Path $env:APPDATA 'Python\Python312\Scripts\uv.exe'
        $candidates.Add([ordered]@{
            source = '%APPDATA%/Python/Python312/Scripts/uv.exe'
            path = $appDataCandidate
            exists = Test-Path -LiteralPath $appDataCandidate
        })
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $wingetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
        if (Test-Path -LiteralPath $wingetRoot) {
            $matches = Get-ChildItem -Path $wingetRoot -Filter 'uv.exe' -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 5
            foreach ($match in $matches) {
                $candidates.Add([ordered]@{
                    source = '%LOCALAPPDATA%/Microsoft/WinGet/Packages/**/uv.exe'
                    path = $match.FullName
                    exists = $true
                })
            }
        }
    }

    $selected = $null
    foreach ($candidate in $candidates) {
        if ($candidate.exists) {
            $selected = $candidate.path
            break
        }
    }

    [ordered]@{
        selected_path = $selected
        candidates = @($candidates)
    }
}

function Test-PathInsideRoot {
    param(
        [string]$Root,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $false
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    return $candidateFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-CachePolicy {
    param([string]$Root)

    $environmentKeys = @(
        'UV_CACHE_DIR',
        'HF_HOME',
        'HF_HUB_CACHE',
        'FASTEMBED_CACHE_DIR',
        'FASTEMBED_CACHE_PATH'
    )

    $observations = [System.Collections.Generic.List[object]]::new()
    $issues = [System.Collections.Generic.List[object]]::new()

    foreach ($key in $environmentKeys) {
        $value = [Environment]::GetEnvironmentVariable($key)
        $insideRoot = Test-PathInsideRoot -Root $Root -Candidate $value
        $observations.Add([ordered]@{
            name = $key
            value_set = -not [string]::IsNullOrWhiteSpace($value)
            under_project_root = $insideRoot
        })

        if ($insideRoot) {
            $issues.Add([ordered]@{
                name = $key
                issue = 'cache-path-under-project-root'
            })
        }
    }

    [ordered]@{
        cache_scope = 'outside-repo-user-cache'
        repo_venv_policy = 'no-project-venv'
        cache_must_stay_outside_repo = $true
        model_cache_must_stay_outside_repo = $true
        executable_discovery_order = @(
            'PATH',
            '%APPDATA%/Python/Python312/Scripts/uv.exe',
            '%LOCALAPPDATA%/Microsoft/WinGet/Packages/**/uv.exe'
        )
        checked_environment = @($observations)
        issues = @($issues)
    }
}

function Invoke-DoctorProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-ProcessArguments $Arguments
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start $FilePath"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            try {
                $process.Kill()
            }
            catch {
                # The timeout result below is the useful diagnostic.
            }
        }

        $process.WaitForExit()
        $timer.Stop()

        return [ordered]@{
            command = "$FilePath $(Join-ProcessArguments $Arguments)"
            status = if ($completed -and $process.ExitCode -eq 0) { 'ok' } elseif ($completed) { 'failed' } else { 'timeout' }
            exit_code = if ($completed) { $process.ExitCode } else { 124 }
            duration_ms = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
            stdout = $stdoutTask.Result.Trim()
            stderr = $stderrTask.Result.Trim()
        }
    }
    finally {
        $process.Dispose()
    }
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

    $json = $Payload | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-semantic-doctor-report.json')

$uv = Find-UvExecutable
$cachePolicy = Get-CachePolicy -Root $root
$runtimeChecks = [System.Collections.Generic.List[object]]::new()
$status = if ($PlanOnly) { 'planned' } else { 'ok' }
$failureCode = ''

if (-not $PlanOnly) {
    if ($TimeoutSeconds -le 0) {
        $status = 'failed'
        $failureCode = 'invalid-timeout-seconds'
    }
    elseif ($null -eq $uv.selected_path) {
        $status = 'failed'
        $failureCode = 'uv-unavailable'
    }
    else {
        $runtimeChecks.Add((Invoke-DoctorProcess -FilePath $uv.selected_path -Arguments @('--version') -TimeoutSeconds $TimeoutSeconds))

        $runtimeArguments = @(
            'run'
        )
        if (-not $AllowNetworkPreflight) {
            $runtimeArguments += '--offline'
        }

        $runtimeArguments += @(
            '--python', '3.12',
            '--with', $lanceDbPackagePin,
            '--with', $pyArrowPackagePin,
            '--with', $fastEmbedPackagePin,
            'python',
            '-c',
            'import importlib.metadata as m; print("lancedb=" + m.version("lancedb")); print("pyarrow=" + m.version("pyarrow")); print("fastembed=" + m.version("fastembed"))'
        )

        $runtimeChecks.Add((Invoke-DoctorProcess -FilePath $uv.selected_path -Arguments $runtimeArguments -TimeoutSeconds $TimeoutSeconds))
        if (@($runtimeChecks | Where-Object { $_.status -ne 'ok' }).Count -gt 0) {
            $status = 'failed'
            $failureCode = if ($AllowNetworkPreflight) { 'runtime-preflight-failed' } else { 'offline-runtime-cache-missing-or-broken' }
        }
    }

    if (@($cachePolicy.issues).Count -gt 0 -and $status -eq 'ok') {
        $status = 'failed'
        $failureCode = 'cache-path-under-project-root'
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-semantic-doctor.ps1'
    mode = if ($PlanOnly) { 'plan-only' } elseif ($AllowNetworkPreflight) { 'explicit-network-preflight' } else { 'offline-doctor' }
    status = $status
    failure_code = $failureCode
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    manual_only = $true
    read_only = $true
    runs_refresh_all = $false
    rebuilds_memory = $false
    imports_curated_retain = $false
    installs_hooks = $false
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    uses_generated_exports_as_source = $false
    touches_build_artifacts = $false
    hidden_network_downloads_blocked = -not $AllowNetworkPreflight
    uv_offline_required_for_gate = $true
    explicit_preflight_required_for_downloads = $true
    network_download_allowed = [bool]$AllowNetworkPreflight
    dependency_pins = [ordered]@{
        lancedb = $lanceDbPackagePin
        pyarrow = $pyArrowPackagePin
        fastembed = $fastEmbedPackagePin
    }
    uv_policy = $cachePolicy
    uv = $uv
    runtime_checks = @($runtimeChecks)
}

Write-JsonReport -Path $reportPath -Payload $report

if ($status -eq 'failed') {
    exit 2
}

exit 0
