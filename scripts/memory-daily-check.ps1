[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
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

function Invoke-ProcessText {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds = 30,
        [string]$LockPath = '',
        [int]$LockTimeoutSeconds = 30
    )

    $lockAcquired = $false
    if (-not [string]::IsNullOrWhiteSpace($LockPath)) {
        $lockAcquired = Acquire-DirectoryLock -Path $LockPath -TimeoutSeconds $LockTimeoutSeconds
        if (-not $lockAcquired) {
            return [ordered]@{
                exit_code = 124
                stdout = ''
                stderr = "Timed out waiting for Memory CLI lock: $LockPath"
                timed_out = $true
                lock_timed_out = $true
            }
        }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-ProcessArguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
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
            try {
                $process.Kill()
            }
            catch {
                # The returned timeout status is the useful diagnostic.
            }

            $process.WaitForExit()
            return [ordered]@{
                exit_code = 124
                stdout = $stdoutTask.Result
                stderr = $stderrTask.Result
                timed_out = $true
                lock_timed_out = $false
            }
        }

        $process.WaitForExit()
        return [ordered]@{
            exit_code = $process.ExitCode
            stdout = $stdoutTask.Result
            stderr = $stderrTask.Result
            timed_out = $false
            lock_timed_out = $false
        }
    }
    finally {
        $process.Dispose()
        if ($lockAcquired) {
            Remove-Item -LiteralPath $LockPath -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
}

function Find-Dotnet {
    param([string]$Root)

    $localDotnet = Join-Path $Root '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        return $localDotnet
    }

    return 'dotnet'
}

function Read-JsonFileOrNull {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
}

function New-Observation {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Detail
    )

    [ordered]@{
        name = $Name
        status = $Status
        detail = $Detail
        uses_cloud = $false
        uses_hook = $false
        runs_rebuild = $false
        touches_denylist = $false
    }
}

function New-GeneratedReportInfo {
    param(
        [string]$Root,
        [string]$Name,
        [string]$Path,
        [switch]$Json
    )

    $exists = Test-Path -LiteralPath $Path
    $status = if ($exists) { 'present' } else { 'missing' }
    $generator = $null
    $mode = $null
    $lastWriteTimeUtc = $null

    if ($exists) {
        $item = Get-Item -LiteralPath $Path
        $lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
        if ($Json) {
            $jsonReport = Read-JsonFileOrNull -Path $Path
            if ($null -ne $jsonReport) {
                if ($null -ne $jsonReport.PSObject.Properties['status']) {
                    $status = [string]$jsonReport.status
                }
                if ($null -ne $jsonReport.PSObject.Properties['generator']) {
                    $generator = [string]$jsonReport.generator
                }
                if ($null -ne $jsonReport.PSObject.Properties['mode']) {
                    $mode = [string]$jsonReport.mode
                }
            }
        }
    }

    [ordered]@{
        name = $Name
        path = Convert-ToRepoPath -Root $Root -Path $Path
        exists = $exists
        status = $status
        generator = $generator
        mode = $mode
        last_write_time_utc = $lastWriteTimeUtc
    }
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

$root = Resolve-ProjectRoot -Candidate $ProjectRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CryptoIndicatorApp.sln'))) {
    throw "ProjectRoot does not look like the repository root: $root"
}

$reportPath = Resolve-RootedOrRelativePath `
    -Root $root `
    -Path $OutputPath `
    -DefaultPath (Join-Path $root 'docs\memory\generated\memory-daily-check-report.json')

$generatedRoot = Join-Path $root 'docs\memory\generated'
$sqlitePath = Join-Path $generatedRoot 'project-memory.sqlite'
$markerPath = Join-Path $generatedRoot 'memory-needs-refresh.marker.json'
$memoryCliLockPath = Join-Path $generatedRoot 'memory-cli.lock'
$memoryProject = Join-Path $root 'tools\Memory\CryptoIndicatorApp.Memory.csproj'

$gitBranchResult = Invoke-ProcessText -FilePath 'git' -Arguments @('branch', '--show-current') -WorkingDirectory $root
$gitHeadResult = Invoke-ProcessText -FilePath 'git' -Arguments @('rev-parse', 'HEAD') -WorkingDirectory $root
$gitDirtyResult = Invoke-ProcessText -FilePath 'git' -Arguments @('status', '--porcelain') -WorkingDirectory $root

$branch = if ($gitBranchResult.exit_code -eq 0) { $gitBranchResult.stdout.Trim() } else { '' }
$head = if ($gitHeadResult.exit_code -eq 0) { $gitHeadResult.stdout.Trim() } else { $null }
$workingTreeDirty = $gitDirtyResult.exit_code -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitDirtyResult.stdout)

$memoryStatus = [ordered]@{
    available = $false
    status = if (Test-Path -LiteralPath $sqlitePath) { 'cli-unavailable' } else { 'store-missing' }
    head = $head
    indexed_commit = $null
    indexed_tree = $null
    indexed_at = $null
    marker_exists = Test-Path -LiteralPath $markerPath
    needs_refresh = if (Test-Path -LiteralPath $markerPath) { $true } elseif (Test-Path -LiteralPath $sqlitePath) { $null } else { $true }
    needs_refresh_is_known = -not (Test-Path -LiteralPath $sqlitePath) -or (Test-Path -LiteralPath $markerPath)
    working_tree_dirty = $workingTreeDirty
    marker_path = Convert-ToRepoPath -Root $root -Path $markerPath
    source = 'git-and-marker-fallback'
    error_message = ''
    error_kind = ''
}

if (Test-Path -LiteralPath $sqlitePath) {
    $dotnetPath = Find-Dotnet -Root $root
    try {
        $statusResult = Invoke-ProcessText `
            -FilePath $dotnetPath `
            -Arguments @(
                'run',
                '--no-restore',
                '--project',
                $memoryProject,
                '--',
                'status',
                '--project-root',
                $root,
                '--db',
                $sqlitePath,
                '--json'
            ) `
            -WorkingDirectory $root `
            -TimeoutSeconds 120 `
            -LockPath $memoryCliLockPath `
            -LockTimeoutSeconds 30
    }
    catch {
        $statusResult = [ordered]@{
            exit_code = 127
            stdout = ''
            stderr = $_.Exception.Message
            timed_out = $false
            lock_timed_out = $false
        }
    }

    if ($statusResult.exit_code -eq 0 -and -not [string]::IsNullOrWhiteSpace($statusResult.stdout)) {
        try {
            $statusJson = $statusResult.stdout | ConvertFrom-Json
            $memoryStatus = [ordered]@{
                available = $true
                status = 'reported'
                head = $statusJson.head
                indexed_commit = $statusJson.indexed_commit
                indexed_tree = $statusJson.indexed_tree
                indexed_at = $statusJson.indexed_at
                marker_exists = [bool]$statusJson.marker_exists
                needs_refresh = [bool]$statusJson.needs_refresh
                needs_refresh_is_known = $true
                working_tree_dirty = [bool]$statusJson.working_tree_dirty
                marker_path = [string]$statusJson.marker_path
                source = 'tools/Memory status'
                error_message = ''
                error_kind = ''
            }
        }
        catch {
            $memoryStatus.error_message = ("Memory CLI status did not return JSON. " + $_.Exception.Message + " Output=" + $statusResult.stdout).Trim()
            $memoryStatus.error_kind = 'memory-cli-invalid-json'
        }
    }
    else {
        $memoryStatus.error_message = ($statusResult.stderr + $statusResult.stdout).Trim()
        $memoryStatus.error_kind = if ([bool]$statusResult.lock_timed_out) { 'memory-cli-lock-timeout' } else { 'memory-cli-unavailable' }
    }
}

$evalJsonPath = Join-Path $generatedRoot 'lancedb-sidecar-report.json'
$evalMarkdownPath = Join-Path $generatedRoot 'lancedb-eval-report.md'
$evalReport = Read-JsonFileOrNull -Path $evalJsonPath
$evalStatus = 'missing'
$evalPassed = $null
$evalPassedCount = $null
$evalFailedCount = $null
$evalCommand = $null
$evalBaseline = $null

if ($null -ne $evalReport) {
    $evalStatus = if ($null -ne $evalReport.PSObject.Properties['status']) { [string]$evalReport.status } else { 'unknown' }
    $evalPassed = if ($null -ne $evalReport.PSObject.Properties['passed']) { [bool]$evalReport.passed } else { $null }
    $evalPassedCount = if ($null -ne $evalReport.PSObject.Properties['passed_count']) { [int]$evalReport.passed_count } else { $null }
    $evalFailedCount = if ($null -ne $evalReport.PSObject.Properties['failed_count']) { [int]$evalReport.failed_count } else { $null }
    $evalCommand = if ($null -ne $evalReport.PSObject.Properties['command']) { [string]$evalReport.command } else { $null }
    $evalBaseline = if ($null -ne $evalReport.PSObject.Properties['embedding_pooling_baseline']) { [string]$evalReport.embedding_pooling_baseline } else { $null }
}

$generatedReports = @(
    (New-GeneratedReportInfo -Root $root -Name 'memory-refresh-all' -Path (Join-Path $generatedRoot 'memory-refresh-all-report.json') -Json),
    (New-GeneratedReportInfo -Root $root -Name 'lancedb-eval-json' -Path $evalJsonPath -Json),
    (New-GeneratedReportInfo -Root $root -Name 'lancedb-eval-markdown' -Path $evalMarkdownPath),
    (New-GeneratedReportInfo -Root $root -Name 'memory-pre-push-check' -Path (Join-Path $generatedRoot 'memory-pre-push-check-report.json') -Json),
    (New-GeneratedReportInfo -Root $root -Name 'curated-retain-dry-run' -Path (Join-Path $generatedRoot 'curated-retain-dry-run-report.json') -Json),
    (New-GeneratedReportInfo -Root $root -Name 'curated-retain-export-dry-run' -Path (Join-Path $generatedRoot 'curated-retain-export-dry-run-report.json') -Json),
    (New-GeneratedReportInfo -Root $root -Name 'curated-retain-delete-dry-run' -Path (Join-Path $generatedRoot 'curated-retain-delete-dry-run-report.json') -Json)
)

$gitHeadObservationStatus = 'reported'
if ([string]::IsNullOrWhiteSpace($head)) {
    $gitHeadObservationStatus = 'missing'
}

$memoryStatusObservationStatus = 'unavailable'
if ($memoryStatus.available) {
    $memoryStatusObservationStatus = 'reported'
}
elseif ($memoryStatus.status -eq 'store-missing') {
    $memoryStatusObservationStatus = 'store-missing'
}
elseif ($memoryStatus.status -eq 'cli-unavailable') {
    $memoryStatusObservationStatus = 'cli-unavailable'
}

$observations = @(
    (New-Observation -Name 'git-head' -Status $gitHeadObservationStatus -Detail 'Read current branch and HEAD from Git.'),
    (New-Observation -Name 'memory-status' -Status $memoryStatusObservationStatus -Detail 'Read tools/Memory status only when the generated SQLite store exists.'),
    (New-Observation -Name 'marker-status' -Status 'reported' -Detail 'Checked marker file presence only.'),
    (New-Observation -Name 'generated-reports' -Status 'reported' -Detail 'Read generated report metadata as evidence only.'),
    (New-Observation -Name 'lancedb-eval' -Status $evalStatus -Detail 'Read latest LanceDB eval report metadata when present.')
)

$existingReportCount = @($generatedReports | Where-Object { $_.exists -eq $true }).Count
$missingReportCount = @($generatedReports | Where-Object { $_.exists -eq $false }).Count
$mode = 'daily-check'
if ($PlanOnly) {
    $mode = 'plan-only'
}

$report = [ordered]@{
    schema_version = 1
    generator = 'scripts/memory-daily-check.ps1'
    mode = $mode
    status = 'reported'
    project_root = Convert-ToRepoPath -Root $root -Path $root
    report_path = Convert-ToRepoPath -Root $root -Path $reportPath
    started_at = (Get-Date).ToUniversalTime().ToString('o')
    finished_at = (Get-Date).ToUniversalTime().ToString('o')
    manual_only = $true
    read_only = $true
    runs_refresh_all = $false
    rebuilds_memory = $false
    imports_curated_retain = $false
    installs_hooks = $false
    cloud_enabled = $false
    calls_hindsight = $false
    calls_codex_retain = $false
    codex_auto_retain_enabled = $false
    post_commit_auto_refresh_enabled = $false
    touches_raw_jsonl = $false
    touches_hindsight_store = $false
    touches_secret_storage = $false
    uses_generated_exports_as_source = $false
    touches_build_artifacts = $false
    memory_cli_checks_serialized = $true
    memory_cli_lock_path = Convert-ToRepoPath -Root $root -Path $memoryCliLockPath
    git = [ordered]@{
        branch = $branch
        head = $head
        working_tree_dirty = $workingTreeDirty
    }
    memory_status = $memoryStatus
    marker = [ordered]@{
        path = Convert-ToRepoPath -Root $root -Path $markerPath
        exists = Test-Path -LiteralPath $markerPath
    }
    lancedb_eval = [ordered]@{
        json_report_path = Convert-ToRepoPath -Root $root -Path $evalJsonPath
        markdown_report_path = Convert-ToRepoPath -Root $root -Path $evalMarkdownPath
        markdown_report_exists = Test-Path -LiteralPath $evalMarkdownPath
        status = $evalStatus
        command = $evalCommand
        passed = $evalPassed
        passed_count = $evalPassedCount
        failed_count = $evalFailedCount
        embedding_pooling_baseline = $evalBaseline
    }
    summary = [ordered]@{
        needs_refresh = $memoryStatus.needs_refresh
        needs_refresh_is_known = [bool]$memoryStatus.needs_refresh_is_known
        marker_exists = [bool]$memoryStatus.marker_exists
        lancedb_eval_status = $evalStatus
        lancedb_eval_passed = $evalPassed
        generated_reports_present = $existingReportCount
        generated_reports_missing = $missingReportCount
    }
    generated_reports = @($generatedReports)
    observations = @($observations)
}

Write-JsonReport -Path $reportPath -Payload $report
exit 0
