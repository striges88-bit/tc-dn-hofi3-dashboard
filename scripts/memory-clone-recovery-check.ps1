[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [string]$CloneRoot = '',
    [int]$StepTimeoutSeconds = 900,
    [switch]$KeepClone,
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

function Invoke-Process {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds,
        [string]$PathPrefix = ''
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-ProcessArguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    if (-not [string]::IsNullOrWhiteSpace($PathPrefix)) {
        $existingPath = $startInfo.EnvironmentVariables['PATH']
        $startInfo.EnvironmentVariables['PATH'] = $PathPrefix + [System.IO.Path]::PathSeparator + $existingPath
    }

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
            try {
                $process.Kill()
            }
            catch {
                # The timeout status is the useful diagnostic.
            }
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

function Find-DotnetPathPrefix {
    param([string]$Root)

    $localDotnetDirectory = Join-Path $Root '.dotnet'
    $localDotnet = Join-Path $localDotnetDirectory 'dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        return $localDotnetDirectory
    }

    return ''
}

function Find-DotnetExecutable {
    param([string]$Root)

    $localDotnet = Join-Path $Root '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        return $localDotnet
    }

    return 'dotnet'
}

function Get-GitText {
    param(
        [string]$Root,
        [string[]]$Arguments
    )

    $result = Invoke-Process -FilePath 'git' -Arguments $Arguments -WorkingDirectory $Root -TimeoutSeconds 30
    if ([int]$result.exit_code -ne 0) {
        throw "Git command failed: git $(Join-ProcessArguments $Arguments)`n$($result.stderr)"
    }

    return ([string]$result.stdout).Trim()
}

function Assert-ClonePathSafe {
    param(
        [string]$Root,
        [string]$ClonePath
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $cloneFull = [System.IO.Path]::GetFullPath($ClonePath).TrimEnd('\', '/')
    $pathRoot = [System.IO.Path]::GetPathRoot($cloneFull).TrimEnd('\', '/')

    if ($cloneFull.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Clone path must not be the project root: $ClonePath"
    }

    if ($cloneFull.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Clone path must not be inside the project repository: $ClonePath"
    }

    if ($cloneFull.Equals($pathRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Clone path must not be a filesystem root: $ClonePath"
    }

    foreach ($forbidden in @('recordings', '.hindsight', 'docs\memory\generated', 'bin', 'obj', 'publish')) {
        if ($cloneFull.IndexOf($forbidden, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Clone path matches denied segment '$forbidden': $ClonePath"
        }
    }

    if ($cloneFull.IndexOf('secret', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Clone path mentions secret material: $ClonePath"
    }
}

function Get-OwnershipMarkerPath {
    param([string]$ClonePath)

    $gitDirectory = Join-Path $ClonePath '.git'
    return Join-Path $gitDirectory 'memory-clone-recovery-check.marker'
}

function Remove-OwnedClone {
    param([string]$ClonePath)

    $markerPath = Get-OwnershipMarkerPath -ClonePath $ClonePath
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw "Refusing to remove clone without ownership marker: $ClonePath"
    }

    Remove-Item -LiteralPath $ClonePath -Recurse -Force
}

function Write-JsonReport {
    param(
        [string]$Path,
        [object]$Payload
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Payload | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $Path -Value ($json + [Environment]::NewLine) -Encoding UTF8
    Write-Output $json
}

if ($StepTimeoutSeconds -le 0) {
    throw "StepTimeoutSeconds must be positive."
}

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$generatedRoot = Join-Path $root 'docs\memory\generated'
$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $generatedRoot 'memory-clone-recovery-check-report.json')

$head = Get-GitText -Root $root -Arguments @('rev-parse', 'HEAD')
$tree = Get-GitText -Root $root -Arguments @('rev-parse', 'HEAD^{tree}')
$branch = Get-GitText -Root $root -Arguments @('branch', '--show-current')
$dirtyText = Get-GitText -Root $root -Arguments @('status', '--porcelain')
$workingTreeDirty = -not [string]::IsNullOrWhiteSpace($dirtyText)

if ([string]::IsNullOrWhiteSpace($CloneRoot)) {
    $clonePath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'tc-dn-hofi3-memory-clone-check-' + [System.Guid]::NewGuid().ToString('N'))
}
elseif ([System.IO.Path]::IsPathRooted($CloneRoot)) {
    $clonePath = [System.IO.Path]::GetFullPath($CloneRoot)
}
else {
    $clonePath = [System.IO.Path]::GetFullPath((Join-Path $root $CloneRoot))
}

Assert-ClonePathSafe -Root $root -ClonePath $clonePath

$cloneArguments = @('clone', '--no-hardlinks', $root, $clonePath)
$checkoutArguments = @('-C', $clonePath, 'checkout', '--detach', $head)
$recoveryScript = Join-Path $clonePath 'scripts\memory-rebuild-from-head.ps1'
$recoveryArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $recoveryScript, '-ProjectRoot', $clonePath, '-StepTimeoutSeconds', ([string]$StepTimeoutSeconds))
$cloneMemoryProject = Join-Path $clonePath 'tools\Memory\CryptoIndicatorApp.Memory.csproj'
$statusArguments = @('run', '--project', $cloneMemoryProject, '--', 'status', '--project-root', $clonePath, '--json')
$dotnetPathPrefix = Find-DotnetPathPrefix -Root $root
$dotnetExecutable = Find-DotnetExecutable -Root $root

$status = if ($PlanOnly) { 'planned' } else { 'completed' }
$exitCode = 0
$cloneResult = $null
$checkoutResult = $null
$recoveryResult = $null
$statusResult = $null
$memoryStatus = $null
$cloneCreated = $false
$cloneDeleted = $true
$errorMessage = ''

if (-not $PlanOnly) {
    if ($workingTreeDirty) {
        $status = 'blocked'
        $exitCode = 2
        $errorMessage = 'Working tree is dirty. Commit durable source changes before proving clone-like recovery from HEAD.'
    }
    elseif (Test-Path -LiteralPath $clonePath) {
        $status = 'failed'
        $exitCode = 2
        $errorMessage = "Clone path already exists: $clonePath"
    }
    else {
        try {
            $cloneResult = Invoke-Process -FilePath 'git' -Arguments $cloneArguments -WorkingDirectory $root -TimeoutSeconds $StepTimeoutSeconds
            if ([int]$cloneResult.exit_code -ne 0) {
                $status = 'failed'
                $exitCode = [int]$cloneResult.exit_code
                $errorMessage = [string]$cloneResult.stderr
            }
            else {
                $cloneCreated = $true
                Set-Content -LiteralPath (Get-OwnershipMarkerPath -ClonePath $clonePath) -Value $head -Encoding ASCII

                $checkoutResult = Invoke-Process -FilePath 'git' -Arguments $checkoutArguments -WorkingDirectory $clonePath -TimeoutSeconds 120
                if ([int]$checkoutResult.exit_code -ne 0) {
                    $status = 'failed'
                    $exitCode = [int]$checkoutResult.exit_code
                    $errorMessage = [string]$checkoutResult.stderr
                }
                else {
                    $recoveryResult = Invoke-Process -FilePath 'powershell.exe' -Arguments $recoveryArguments -WorkingDirectory $clonePath -TimeoutSeconds $StepTimeoutSeconds -PathPrefix $dotnetPathPrefix
                    if ([int]$recoveryResult.exit_code -ne 0) {
                        $status = 'failed'
                        $exitCode = [int]$recoveryResult.exit_code
                        $errorMessage = [string]$recoveryResult.stderr
                    }
                    else {
                        $statusResult = Invoke-Process -FilePath $dotnetExecutable -Arguments $statusArguments -WorkingDirectory $clonePath -TimeoutSeconds 120 -PathPrefix $dotnetPathPrefix
                        if ([int]$statusResult.exit_code -ne 0) {
                            $status = 'failed'
                            $exitCode = [int]$statusResult.exit_code
                            $errorMessage = [string]$statusResult.stderr
                        }
                        else {
                            $memoryStatus = $statusResult.stdout | ConvertFrom-Json
                            if ([bool]$memoryStatus.needs_refresh) {
                                $status = 'failed'
                                $exitCode = 2
                                $errorMessage = 'Clone memory status still reports needs_refresh=true after recovery.'
                            }
                        }
                    }
                }
            }
        }
        finally {
            if ((Test-Path -LiteralPath $clonePath) -and -not $KeepClone) {
                Remove-OwnedClone -ClonePath $clonePath
                $cloneDeleted = -not (Test-Path -LiteralPath $clonePath)
            }
            elseif (Test-Path -LiteralPath $clonePath) {
                $cloneDeleted = $false
            }
        }
    }
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-clone-recovery-check.ps1'
    mode = if ($PlanOnly) { 'plan-only' } else { 'clone-like-recovery' }
    status = $status
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    started_at = (Get-Date).ToUniversalTime().ToString('o')
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    manual_only = $true
    requires_clean_working_tree = $true
    keep_clone = [bool]$KeepClone
    planned_clone = $true
    clone_created = $cloneCreated
    clone_deleted = $cloneDeleted
    runs_recovery = $null -ne $recoveryResult
    runs_refresh_all = $null -ne $recoveryResult
    error_message = $errorMessage
    git = [ordered]@{
        branch = $branch
        head = $head
        tree = $tree
        working_tree_dirty = $workingTreeDirty
    }
    clone = [ordered]@{
        path = $clonePath
        exists_after_run = Test-Path -LiteralPath $clonePath
    }
    planned_commands = [ordered]@{
        clone = "git $(Join-ProcessArguments $cloneArguments)"
        checkout = "git $(Join-ProcessArguments $checkoutArguments)"
        recovery = "powershell.exe $(Join-ProcessArguments $recoveryArguments)"
        status = "$dotnetExecutable $(Join-ProcessArguments $statusArguments)"
    }
    process_results = [ordered]@{
        clone_exit_code = if ($null -eq $cloneResult) { $null } else { $cloneResult.exit_code }
        checkout_exit_code = if ($null -eq $checkoutResult) { $null } else { $checkoutResult.exit_code }
        recovery_exit_code = if ($null -eq $recoveryResult) { $null } else { $recoveryResult.exit_code }
        memory_status_exit_code = if ($null -eq $statusResult) { $null } else { $statusResult.exit_code }
    }
    clone_memory_status = if ($null -eq $memoryStatus) { $null } else {
        [ordered]@{
            head = $memoryStatus.head
            indexed_commit = $memoryStatus.indexed_commit
            indexed_tree = $memoryStatus.indexed_tree
            needs_refresh = [bool]$memoryStatus.needs_refresh
            marker_exists = [bool]$memoryStatus.marker_exists
            working_tree_dirty = [bool]$memoryStatus.working_tree_dirty
        }
    }
    cloud_enabled = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    commit_hook_installed = $false
    installs_hooks = $false
    imports_raw_jsonl = $false
    imports_generated_exports = $false
    uses_generated_exports_as_source = $false
    imports_secrets = $false
    imports_build_artifacts = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    touches_build_artifacts = $false
    touches_source_files = $false
}

Write-JsonReport -Path $reportPath -Payload $report
exit $exitCode
