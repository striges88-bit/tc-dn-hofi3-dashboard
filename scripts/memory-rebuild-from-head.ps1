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

function New-DeleteTarget {
    param(
        [string]$Root,
        [string]$GeneratedRoot,
        [string]$RelativePath,
        [bool]$Recursive
    )

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $generatedFull = [System.IO.Path]::GetFullPath($GeneratedRoot).TrimEnd('\', '/')
    $underGenerated = $fullPath.StartsWith($generatedFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)

    [ordered]@{
        path = Convert-ToRepoPath -Root $Root -Path $fullPath
        full_path = $fullPath
        recursive = $Recursive
        exists = Test-Path -LiteralPath $fullPath
        under_generated_memory = $underGenerated
    }
}

function Assert-DeleteTargetsAreSafe {
    param([System.Collections.IEnumerable]$Targets)

    foreach ($target in $Targets) {
        if (-not [bool]$target.under_generated_memory) {
            throw "Unsafe recovery delete target outside docs/memory/generated: $($target.full_path)"
        }

        $path = [string]$target.path
        foreach ($forbidden in @('recordings/', '.hindsight/', '/bin/', '/obj/', 'publish/')) {
            if ($path.IndexOf($forbidden, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Unsafe recovery delete target matches denylist '$forbidden': $path"
            }
        }

        if ($path.IndexOf('secret', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Unsafe recovery delete target mentions secret material: $path"
        }
    }
}

function Remove-GeneratedMemoryTarget {
    param([System.Collections.IDictionary]$Target)

    if (-not (Test-Path -LiteralPath $Target.full_path)) {
        return $false
    }

    $item = Get-Item -LiteralPath $Target.full_path -Force
    if ($item.PSIsContainer -and -not [bool]$Target.recursive) {
        throw "Refusing non-recursive delete of directory: $($Target.full_path)"
    }

    if ($item.PSIsContainer) {
        Remove-Item -LiteralPath $Target.full_path -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $Target.full_path -Force
    }

    return $true
}

function Invoke-Process {
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
            throw "Failed to start process: $FilePath"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            $process.Kill()
        }

        $process.WaitForExit()
        $timer.Stop()

        [ordered]@{
            command = "$FilePath $(Join-ProcessArguments $Arguments)"
            status = if ($completed -and $process.ExitCode -eq 0) { 'completed' } elseif ($completed) { 'failed' } else { 'timeout' }
            exit_code = if ($completed) { $process.ExitCode } else { 124 }
            duration_ms = [math]::Round($timer.Elapsed.TotalMilliseconds, 3)
            stdout = $stdoutTask.Result
            stderr = $stderrTask.Result
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

    $json = $Payload | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$generatedRoot = Join-Path $root 'docs\memory\generated'
$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $generatedRoot 'memory-rebuild-from-head-report.json')

$deleteTargets = @(
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\project-memory.sqlite' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\project-memory.sqlite-shm' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\project-memory.sqlite-wal' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\project-memory-index.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\memory-refresh-all-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\memory-pre-push-check-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\memory-daily-check-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\memory-needs-refresh.marker.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-probe-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-search-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-explain-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-cleanup-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-rebuild-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-sidecar-report.json' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb-eval-report.md' -Recursive $false),
    (New-DeleteTarget -Root $root -GeneratedRoot $generatedRoot -RelativePath 'docs\memory\generated\lancedb' -Recursive $true)
)
Assert-DeleteTargetsAreSafe -Targets $deleteTargets

$refreshAllScript = Join-Path $root 'scripts\memory-refresh-all.ps1'
$refreshAllArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $refreshAllScript, '-ProjectRoot', $root, '-OutputPath', (Join-Path $generatedRoot 'memory-refresh-all-report.json'), '-StepTimeoutSeconds', ([string]$StepTimeoutSeconds))
$dotnetPath = Find-Dotnet -Root $root
$memoryProject = Join-Path $root 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
$statusArguments = @('run', '--no-restore', '--project', $memoryProject, '--', 'status', '--project-root', $root, '--json')
$startedAt = (Get-Date).ToUniversalTime().ToString('o')
$deletedPaths = [System.Collections.Generic.List[string]]::new()
$refreshResult = $null
$statusResult = $null
$memoryStatus = $null
$status = if ($PlanOnly) { 'planned' } else { 'completed' }
$exitCode = 0

if (-not $PlanOnly) {
    foreach ($target in $deleteTargets) {
        if (Remove-GeneratedMemoryTarget -Target $target) {
            $deletedPaths.Add([string]$target.path)
        }
    }

    $refreshResult = Invoke-Process -FilePath 'powershell.exe' -Arguments $refreshAllArguments -TimeoutSeconds $StepTimeoutSeconds
    if ([int]$refreshResult.exit_code -ne 0) {
        $status = 'failed'
        $exitCode = [int]$refreshResult.exit_code
    }
    else {
        $statusResult = Invoke-Process -FilePath $dotnetPath -Arguments $statusArguments -TimeoutSeconds 120
        if ([int]$statusResult.exit_code -ne 0) {
            $status = 'failed'
            $exitCode = [int]$statusResult.exit_code
        }
        else {
            $memoryStatus = $statusResult.stdout | ConvertFrom-Json
            if ([bool]$memoryStatus.needs_refresh) {
                $status = 'failed'
                $exitCode = 2
            }
        }
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-rebuild-from-head.ps1'
    mode = if ($PlanOnly) { 'plan-only' } else { 'full-local-recovery' }
    status = $status
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    started_at = $startedAt
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    planned_refresh_all = $true
    runs_refresh_all = -not [bool]$PlanOnly
    refresh_all_command = "powershell.exe $(Join-ProcessArguments $refreshAllArguments)"
    refresh_all_exit_code = if ($null -eq $refreshResult) { $null } else { $refreshResult.exit_code }
    memory_status_exit_code = if ($null -eq $statusResult) { $null } else { $statusResult.exit_code }
    memory_status_needs_refresh = if ($null -eq $memoryStatus) { $null } else { [bool]$memoryStatus.needs_refresh }
    delete_plan = @($deleteTargets | ForEach-Object {
        [ordered]@{
            path = $_.path
            recursive = $_.recursive
            exists = $_.exists
            under_generated_memory = $_.under_generated_memory
        }
    })
    deleted_paths = @($deletedPaths)
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    commit_hook_installed = $false
    installs_hooks = $false
    deletes_raw_jsonl = $false
    deletes_generated_exports = $false
    deletes_secrets = $false
    deletes_build_artifacts = $false
    deletes_hindsight_store = $false
    deletes_source_files = $false
}

Write-JsonReport -Path $reportPath -Payload $report
exit $exitCode
