[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [int]$StepTimeoutSeconds = 900,
    [switch]$PlanOnly
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

function Find-Dotnet {
    param([string]$Root)

    $localDotnet = Join-Path $Root '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        return $localDotnet
    }

    return 'dotnet'
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

function Find-Git {
    $commonGitPath = 'C:\Program Files\Git\cmd\git.exe'
    if (Test-Path -LiteralPath $commonGitPath) {
        return $commonGitPath
    }

    return 'git'
}

function Invoke-GitText {
    param(
        [string]$Root,
        [string[]]$Arguments,
        [int]$TimeoutMilliseconds = 10000
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Find-Git
    $startInfo.Arguments = Join-ProcessArguments (@('-C', $Root) + $Arguments)
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'git process did not start'
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            try {
                $process.Kill()
            }
            catch {
                # The timeout remains the primary failure evidence.
            }

            throw 'git command timed out'
        }

        $process.WaitForExit()
        $stdout = $stdoutTask.Result.Trim()
        $stderr = $stderrTask.Result.Trim()
        if ($process.ExitCode -ne 0) {
            throw "git exit $($process.ExitCode): $stderr"
        }
        if ([string]::IsNullOrWhiteSpace($stdout)) {
            throw 'git command returned empty output'
        }

        return $stdout
    }
    finally {
        $process.Dispose()
    }
}

function New-Step {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [bool]$UsesMemoryCliLock = $false
    )

    [ordered]@{
        name = $Name
        file_path = $FilePath
        arguments = $Arguments
        command = "$FilePath $(Join-ProcessArguments $Arguments)"
        uses_cloud = $false
        uses_hook = $false
        uses_memory_cli_lock = $UsesMemoryCliLock
    }
}

function Acquire-DirectoryLock {
    param(
        [string]$Path,
        [int]$TimeoutSeconds
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
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

function Invoke-RefreshStep {
    param(
        [System.Collections.IDictionary]$Step,
        [int]$TimeoutSeconds,
        [string]$MemoryCliLockPath
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $lockAcquired = $false

    if ([bool]$Step.uses_memory_cli_lock) {
        $lockAcquired = Acquire-DirectoryLock -Path $MemoryCliLockPath -TimeoutSeconds $TimeoutSeconds
        if (-not $lockAcquired) {
            $timer.Stop()
            return [ordered]@{
                name = $Step.name
                command = $Step.command
                status = 'timeout'
                exit_code = 124
                duration_ms = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
                stdout_tail = ''
                stderr_tail = "Timed out waiting for Memory CLI lock: $MemoryCliLockPath"
                uses_cloud = $Step.uses_cloud
                uses_hook = $Step.uses_hook
                uses_memory_cli_lock = $Step.uses_memory_cli_lock
            }
        }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [string]$Step.file_path
    $startInfo.Arguments = Join-ProcessArguments ([string[]]$Step.arguments)
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $killError = ''

    try {
        if (-not $process.Start()) {
            throw "Failed to start refresh step: $($Step.name)"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            try {
                $process.Kill()
            }
            catch {
                $killError = $_.Exception.Message
            }
        }

        $process.WaitForExit()
        $stdoutText = $stdoutTask.Result
        $stderrText = $stderrTask.Result
        if (-not [string]::IsNullOrWhiteSpace($killError)) {
            $stderrText = ($stderrText.TrimEnd() + [Environment]::NewLine + $killError).Trim()
        }

        $timer.Stop()
        if (-not $completed) {
            return [ordered]@{
                name = $Step.name
                command = $Step.command
                status = 'timeout'
                exit_code = 124
                duration_ms = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
                stdout_tail = Get-OutputTail $stdoutText
                stderr_tail = Get-OutputTail $stderrText
                uses_cloud = $Step.uses_cloud
                uses_hook = $Step.uses_hook
                uses_memory_cli_lock = $Step.uses_memory_cli_lock
            }
        }

        $exitCode = $process.ExitCode
        return [ordered]@{
            name = $Step.name
            command = $Step.command
            status = if ($exitCode -eq 0) { 'completed' } else { 'failed' }
            exit_code = $exitCode
            duration_ms = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
            stdout_tail = Get-OutputTail $stdoutText
            stderr_tail = Get-OutputTail $stderrText
            uses_cloud = $Step.uses_cloud
            uses_hook = $Step.uses_hook
            uses_memory_cli_lock = $Step.uses_memory_cli_lock
        }
    }
    finally {
        $process.Dispose()
        if ($lockAcquired) {
            Remove-Item -LiteralPath $MemoryCliLockPath -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
}

function Get-OutputTail {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $normalized = $Text.Trim()
    if ($normalized.Length -le 4000) {
        return $normalized
    }

    return $normalized.Substring($normalized.Length - 4000)
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
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-refresh-all-report.json')
$memoryCliLockPath = Join-Path $root 'docs\memory\generated\memory-cli.lock'

$dotnetPath = Find-Dotnet -Root $root
$memoryProject = Join-Path $root 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
$legacyRefreshScript = Join-Path $root 'scripts\memory-refresh.ps1'
$lanceDbScript = Join-Path $root 'scripts\lancedb-sidecar.ps1'
$requestedCommit = 'HEAD'
$commitSha = Invoke-GitText -Root $root -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')
$treeSha = Invoke-GitText -Root $root -Arguments @('rev-parse', "$commitSha^{tree}")

$steps = @(
    (New-Step -Name 'legacy-json-refresh' -FilePath 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $legacyRefreshScript, '-ProjectRoot', $root)),
    (New-Step -Name 'sqlite-refresh' -FilePath $dotnetPath -Arguments @('run', '--no-restore', '--project', $memoryProject, '--', 'refresh-from-commit', '--commit', $commitSha, '--project-root', $root, '--json') -UsesMemoryCliLock $true),
    (New-Step -Name 'sqlite-stale-check' -FilePath $dotnetPath -Arguments @('run', '--no-restore', '--project', $memoryProject, '--', 'stale-check', '--project-root', $root, '--json') -UsesMemoryCliLock $true),
    (New-Step -Name 'lancedb-cleanup' -FilePath 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $lanceDbScript, '-ProjectRoot', $root, '-Command', 'cleanup')),
    (New-Step -Name 'lancedb-rebuild' -FilePath 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $lanceDbScript, '-ProjectRoot', $root, '-Command', 'rebuild')),
    (New-Step -Name 'lancedb-eval' -FilePath 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $lanceDbScript, '-ProjectRoot', $root, '-Command', 'eval'))
)

$startedAt = (Get-Date).ToUniversalTime().ToString('o')
$results = [System.Collections.Generic.List[object]]::new()
$status = 'completed'
$exitCode = 0

if ($PlanOnly) {
    $status = 'planned'
    foreach ($step in $steps) {
        $results.Add([ordered]@{
            name = $step.name
            command = $step.command
            status = 'planned'
            exit_code = $null
            duration_ms = 0
            stdout_tail = ''
            stderr_tail = ''
            uses_cloud = $step.uses_cloud
            uses_hook = $step.uses_hook
            uses_memory_cli_lock = $step.uses_memory_cli_lock
        })
    }
}
else {
    foreach ($step in $steps) {
        $result = Invoke-RefreshStep -Step $step -TimeoutSeconds $StepTimeoutSeconds -MemoryCliLockPath $memoryCliLockPath
        $results.Add($result)

        if ($result.exit_code -ne 0) {
            $status = 'failed'
            $exitCode = [int]$result.exit_code
            break
        }
    }

    if ($status -eq 'failed') {
        $completedNames = @($results | ForEach-Object { $_.name })
        foreach ($step in $steps) {
            if ($completedNames -notcontains $step.name) {
                $results.Add([ordered]@{
                    name = $step.name
                    command = $step.command
                    status = 'skipped'
                    exit_code = $null
                    duration_ms = 0
                    stdout_tail = ''
                    stderr_tail = ''
                    uses_cloud = $step.uses_cloud
                    uses_hook = $step.uses_hook
                    uses_memory_cli_lock = $step.uses_memory_cli_lock
                })
            }
        }
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-refresh-all.ps1'
    mode = if ($PlanOnly) { 'plan-only' } else { 'full-local-rebuild' }
    status = $status
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    requested_commit = $requestedCommit
    commit_sha = $commitSha
    tree_sha = $treeSha
    started_at = $startedAt
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    auto_commit_refresh_enabled = $false
    commit_hook_installed = $false
    installs_hooks = $false
    direct_project_crawl_enabled = $false
    imports_raw_jsonl = $false
    imports_generated_exports = $false
    uses_generated_exports_as_source = $false
    imports_secrets = $false
    imports_local_proxy_details = $false
    imports_build_artifacts = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    touches_build_artifacts = $false
    memory_cli_checks_serialized = $true
    memory_cli_lock_path = Convert-ToRepoPath -Root $root -Path $memoryCliLockPath
    steps = @($results)
}

Write-JsonReport -Path $reportPath -Payload $report

if (-not $PlanOnly -and $exitCode -ne 0) {
    exit $exitCode
}

exit 0
