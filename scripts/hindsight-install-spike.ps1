[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputPath = '',
    [switch]$ProbeUvxHelp
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
    $OutputPath = Join-Path $root 'docs\memory\generated\hindsight-install-spike-report.json'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

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

function Invoke-ProcessCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [string[]]$Arguments = @(),
        [int]$TimeoutSeconds = 10
    )

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $FileName
    $process.StartInfo.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_.Replace('"', '\"')) + '"'
        }
        else {
            $_
        }
    }) -join ' '

    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true

    [void]$process.Start()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        }
        catch {
            $process.Kill()
        }

        return [ordered]@{
            exit_code = $null
            timed_out = $true
            stdout = ''
            stderr = "Timed out after $TimeoutSeconds seconds."
        }
    }

    return [ordered]@{
        exit_code = $process.ExitCode
        timed_out = $false
        stdout = $process.StandardOutput.ReadToEnd().Trim()
        stderr = $process.StandardError.ReadToEnd().Trim()
    }
}

function Find-WinGetUvExecutable {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($Name -notin @('uv', 'uvx')) {
        return @()
    }

    $packagesRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (-not (Test-Path $packagesRoot -PathType Container)) {
        return @()
    }

    $executableName = "$Name.exe"
    return @(Get-ChildItem -Path $packagesRoot -Filter $executableName -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*astral-sh.uv*' } |
        Sort-Object -Property LastWriteTimeUtc -Descending |
        Select-Object -ExpandProperty FullName)
}

function Get-CommandProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$VersionArguments = @('--version')
    )

    $whereResult = Invoke-ProcessCapture -FileName 'where.exe' -Arguments @($Name)
    $paths = @()
    if ($whereResult.exit_code -eq 0 -and -not [string]::IsNullOrWhiteSpace($whereResult.stdout)) {
        $paths = @($whereResult.stdout -split "(`r`n|`n)" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($paths.Count -eq 0) {
        $paths = @(Find-WinGetUvExecutable -Name $Name)
    }

    $version = $null
    $status = if ($paths.Count -gt 0) { 'found' } else { 'not_found' }
    $note = $null

    if ($paths.Count -gt 0) {
        $isWindowsStorePythonAlias = $Name -eq 'python' -and ($paths | Where-Object { $_ -like '*\WindowsApps\python.exe' }).Count -gt 0
        if ($isWindowsStorePythonAlias) {
            $status = 'windows_store_alias'
            $note = 'Skipping python --version because the visible python.exe is the Windows Store alias, not a usable Python runtime.'
        }
        else {
            $versionResult = Invoke-ProcessCapture -FileName $paths[0] -Arguments $VersionArguments
            $version = [ordered]@{
                exit_code = $versionResult.exit_code
                timed_out = $versionResult.timed_out
                stdout = $versionResult.stdout
                stderr = $versionResult.stderr
            }
        }
    }

    return [ordered]@{
        name = $Name
        status = $status
        paths = $paths
        version = $version
        note = $note
    }
}

$checks = [System.Collections.Generic.List[object]]::new()
$checks.Add((Get-CommandProbe -Name 'python'))
$checks.Add((Get-CommandProbe -Name 'py' -VersionArguments @('--version')))
$checks.Add((Get-CommandProbe -Name 'uv' -VersionArguments @('--version')))
$checks.Add((Get-CommandProbe -Name 'uvx' -VersionArguments @('--version')))
$checks.Add((Get-CommandProbe -Name 'hindsight' -VersionArguments @('--version')))
$checks.Add((Get-CommandProbe -Name 'hindsight-embed' -VersionArguments @('--help')))

$uvxCheck = $checks | Where-Object { $_.name -eq 'uvx' } | Select-Object -First 1
$uvxHelp = $null
$networkInstallExecuted = $false
if ($ProbeUvxHelp.IsPresent) {
    if ($uvxCheck.status -eq 'found') {
        $networkInstallExecuted = $true
        $uvxHelpResult = Invoke-ProcessCapture -FileName $uvxCheck.paths[0] -Arguments @('hindsight-embed', '--help') -TimeoutSeconds 120
        $uvxHelp = [ordered]@{
            command = 'uvx hindsight-embed --help'
            executable = $uvxCheck.paths[0]
            exit_code = $uvxHelpResult.exit_code
            timed_out = $uvxHelpResult.timed_out
            stdout = $uvxHelpResult.stdout
            stderr = $uvxHelpResult.stderr
        }
    }
    else {
        $uvxHelp = [ordered]@{
            command = 'uvx hindsight-embed --help'
            skipped = $true
            reason = 'uvx is not available in PATH.'
        }
    }
}

$openAiApiKeyPresent = [bool](Test-Path Env:OPENAI_API_KEY)
$hindsightApiTokenPresent = [bool](Test-Path Env:HINDSIGHT_API_TOKEN)
$hindsightLlmApiKeyPresent = [bool](Test-Path Env:HINDSIGHT_API_LLM_API_KEY)

$nextActions = [System.Collections.Generic.List[string]]::new()
if ($uvxCheck.status -ne 'found') {
    $nextActions.Add('Install or expose uv/uvx before running uvx hindsight-embed. Prefer a user-scoped uv install; do not add it as a WPF/.NET dependency.')
}
else {
    $nextActions.Add('Run scripts/hindsight-install-spike.ps1 -ProbeUvxHelp to verify uvx hindsight-embed --help before starting any daemon.')
}

if (-not $openAiApiKeyPresent -and -not $hindsightLlmApiKeyPresent) {
    $nextActions.Add('Define a secret-backed LLM key such as OPENAI_API_KEY or HINDSIGHT_API_LLM_API_KEY before daemon/retain tests; do not commit it.')
}

$nextActions.Add('Keep Codex auto-retain disabled until curated import, retention, export, and delete policies are approved.')
$nextActions.Add('Do not import curated files or run retain-files until the embedded daemon endpoint and CLI command surface are confirmed.')

$report = [ordered]@{
    schema_version = 1
    generated_at = (Get-Date).ToUniversalTime().ToString('o')
    generator = 'scripts/hindsight-install-spike.ps1'
    mode = 'python-uvx-embedded-daemon'
    purpose = 'Safe local install-spike report only. The default run does not install Hindsight, call Hindsight APIs, start daemons, retain memory, or enable Codex hooks.'
    codex_auto_retain_enabled = $false
    curated_import_executed = $false
    network_install_executed = $networkInstallExecuted
    daemon_started = $false
    project_root = $root
    checks = @($checks)
    uvx_hindsight_embed_help = $uvxHelp
    environment_presence = [ordered]@{
        OPENAI_API_KEY = $openAiApiKeyPresent
        HINDSIGHT_API_TOKEN = $hindsightApiTokenPresent
        HINDSIGHT_API_LLM_API_KEY = $hindsightLlmApiKeyPresent
    }
    documented_risks = @(
        'Upstream docs mention uvx hindsight-embed for local Codex mode, but documented local daemon ports differ between 9077 and 8888.',
        'The Rust hindsight CLI documents retain-files, while the embedded Python daemon surface must be verified before using file import.',
        'uvx can download package/runtime dependencies, so package probing is explicit instead of default.'
    )
    next_actions = @($nextActions)
}

$json = $report | ConvertTo-Json -Depth 10
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Output "Generated $(Get-RelativeProjectPath $OutputPath)"
Write-Output "Mode: python-uvx-embedded-daemon"
Write-Output "Codex auto-retain: disabled"
Write-Output "Daemon started: false"
