param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('launch', 'doctor', 'pin', 'snapshot', 'invoke', 'read', 'cleanup')]
    [string]$Command,

    [string]$Name
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$StatePath = Join-Path $env:LOCALAPPDATA 'verify-tc-dn-hofi3\state.json'
$RunsRoot = Join-Path $env:LOCALAPPDATA 'verify-tc-dn-hofi3\runs'
$SkillRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EvidenceRoot = Join-Path $SkillRoot 'evidence'
$WindowTitle = 'TC-DN-HOFI3 Dashboard'
$PinName = 'Keep window on top'
$GwlExStyle = -20
$WsExTopmost = 0x00000008

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

if (-not ('VerifyWin32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VerifyWin32 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
}

function Fail([string]$Message) {
    throw $Message
}

function Read-State {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        Fail "No verification instance. Run launch first."
    }

    return Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
}

function Get-DashboardProcess([object]$State) {
    $process = Get-Process -Id ([int]$State.pid) -ErrorAction SilentlyContinue
    if (-not $process -or $process.HasExited) {
        Fail "Recorded process $($State.pid) is not running."
    }

    $expected = [System.IO.Path]::GetFullPath($State.exePath)
    $actual = [System.IO.Path]::GetFullPath($process.Path)
    if (-not [string]::Equals($expected, $actual, [StringComparison]::OrdinalIgnoreCase)) {
        Fail "Process $($State.pid) is '$actual', not the launched exe '$expected'."
    }

    return $process
}

function Get-DashboardWindow([int]$ProcessId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = New-Object System.Windows.Automation.AndCondition($pidCondition, $typeCondition)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Wait-DashboardWindow([int]$ProcessId) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $window = Get-DashboardWindow $ProcessId
        if ($window -and $window.Current.Name -eq $WindowTitle) {
            return $window
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    Fail "Window '$WindowTitle' did not appear for process $ProcessId."
}

function Find-Named([System.Windows.Automation.AutomationElement]$Window, [string]$ElementName) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $ElementName)
    $element = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $element) {
        Fail "No element named '$ElementName' in the launched window."
    }

    return $element
}

function Get-ToggleState([System.Windows.Automation.AutomationElement]$Element) {
    $patternObject = $null
    $hasToggle = $Element.TryGetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern,
        [ref]$patternObject)
    if (-not $hasToggle) {
        return $null
    }

    return $patternObject.Current.ToggleState.ToString()
}

function Get-ElementReport([System.Windows.Automation.AutomationElement]$Element) {
    $toggleState = Get-ToggleState $Element
    $toggle = $(if ($null -eq $toggleState) { 'n/a' } else { $toggleState })

    return "{0}`tenabled={1}`ttoggle={2}`ttype={3}" -f @(
        $Element.Current.Name,
        $Element.Current.IsEnabled,
        $toggle,
        $Element.Current.ControlType.ProgrammaticName)
}

function Get-NamedElements([System.Windows.Automation.AutomationElement]$Window) {
    $collection = $Window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $named = @()
    for ($index = 0; $index -lt $collection.Count; $index++) {
        $element = $collection.Item($index)
        if (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) {
            $named += $element
        }
    }

    return $named
}

function Test-Topmost([IntPtr]$Handle) {
    $style = [VerifyWin32]::GetWindowLong($Handle, $GwlExStyle)
    return (($style -band $WsExTopmost) -eq $WsExTopmost)
}

function Show-Dashboard([System.Diagnostics.Process]$Process) {
    $handle = $Process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        Fail "Launched process $($Process.Id) has no main window handle."
    }

    [void][VerifyWin32]::ShowWindow($handle, 9)
    [void][VerifyWin32]::MoveWindow($handle, 40, 40, 1100, 760, $true)
    [void][VerifyWin32]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 400
    return $handle
}

function Write-Evidence([object]$State, [string]$FileName, [string]$Content) {
    New-Item -ItemType Directory -Force -Path $State.evidenceDir | Out-Null
    $path = Join-Path $State.evidenceDir $FileName
    Set-Content -LiteralPath $path -Value $Content -Encoding utf8
    return $path
}

function Save-WindowScreenshot([object]$State, [System.Windows.Automation.AutomationElement]$Window) {
    $bounds = $Window.Current.BoundingRectangle
    $width = [int]$bounds.Width
    $height = [int]$bounds.Height
    if ($width -le 0 -or $height -le 0) {
        Fail "Window rectangle is empty."
    }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    }
    finally {
        $graphics.Dispose()
    }

    New-Item -ItemType Directory -Force -Path $State.evidenceDir | Out-Null
    $path = Join-Path $State.evidenceDir 'window.png'
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $path
}

function Assert-IdleDashboard([System.Windows.Automation.AutomationElement]$Window) {
    if ($Window.Current.Name -ne $WindowTitle) {
        Fail "Window title is '$($Window.Current.Name)', expected '$WindowTitle'."
    }

    foreach ($buttonName in @('Refresh', 'Start', 'Restart')) {
        $button = Find-Named $Window $buttonName
        if (-not $button.Current.IsEnabled) {
            Fail "'$buttonName' is disabled on an idle dashboard."
        }
    }

    $stopButton = Find-Named $Window 'Stop'
    if ($stopButton.Current.IsEnabled) {
        Fail "'Stop' is enabled before a session starts."
    }

    $pin = Find-Named $Window $PinName
    $pinState = Get-ToggleState $pin
    if ($pinState -ne 'Off') {
        Fail "Pin toggle is '$pinState', expected Off on a fresh window."
    }
}

function Invoke-Launch {
    if (Test-Path -LiteralPath $StatePath) {
        $existing = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        $running = Get-Process -Id ([int]$existing.pid) -ErrorAction SilentlyContinue
        if ($running -and -not $running.HasExited) {
            Fail "Verification instance $($existing.pid) is already running. Refusing to start another. Run cleanup first."
        }
    }

    $project = Join-Path $RepoRoot 'CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj'
    & dotnet build $project -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet build exited $LASTEXITCODE."
    }

    $builtExe = Join-Path $RepoRoot 'CryptoIndicatorApp.Desktop\bin\Debug\net8.0-windows\CryptoIndicatorApp.Desktop.exe'
    if (-not (Test-Path -LiteralPath $builtExe)) {
        Fail "Build did not produce $builtExe."
    }

    $runId = Get-Date -Format 'yyyyMMdd-HHmmss'
    $appDir = Join-Path $RunsRoot "$runId\app"
    $evidenceDir = Join-Path $EvidenceRoot $runId
    New-Item -ItemType Directory -Force -Path $appDir, $evidenceDir | Out-Null
    Copy-Item -Path (Join-Path (Split-Path $builtExe -Parent) '*') -Destination $appDir -Recurse -Force
    $exePath = Join-Path $appDir 'CryptoIndicatorApp.Desktop.exe'

    $process = Start-Process -FilePath $exePath -WorkingDirectory $appDir -PassThru
    $state = [pscustomobject]@{
        runId = $runId
        pid = $process.Id
        appDir = $appDir
        evidenceDir = $evidenceDir
        exePath = $exePath
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $StatePath -Parent) | Out-Null
    $state | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding utf8

    try {
        $window = Wait-DashboardWindow $process.Id
        Assert-IdleDashboard $window
    }
    catch {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $runDir = Split-Path $appDir -Parent
        if (Test-Path -LiteralPath $runDir) {
            Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $evidenceDir) {
            Remove-Item -LiteralPath $evidenceDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $StatePath) {
            Remove-Item -LiteralPath $StatePath -Force -ErrorAction SilentlyContinue
        }

        throw
    }

    Write-Output "runId=$runId"
    Write-Output "pid=$($process.Id)"
    Write-Output "exe=$exePath"
    Write-Output "evidence=$evidenceDir"
    Write-Output 'ready=idle-dashboard'
}

function Invoke-Doctor {
    $state = Read-State
    $process = Get-DashboardProcess $state
    $window = Wait-DashboardWindow $process.Id
    $handle = Show-Dashboard $process
    $topmost = Test-Topmost $handle

    $startEnabled = (Find-Named $window 'Start').Current.IsEnabled
    $stopEnabled = (Find-Named $window 'Stop').Current.IsEnabled
    $pinState = Get-ToggleState (Find-Named $window $PinName)
    $idle = $startEnabled -and -not $stopEnabled -and $pinState -eq 'Off' -and -not $topmost
    $names = Get-NamedElements $window | ForEach-Object { Get-ElementReport $_ }
    $report = @(
        "pid=$($process.Id)"
        "exe=$($state.exePath)"
        "title=$($window.Current.Name)"
        "topmost=$topmost"
        "pin=$pinState"
        "idle=$idle"
        '---'
    ) + $names
    $path = Write-Evidence $state 'doctor.txt' ($report -join "`r`n")
    Write-Output "doctor=ok"
    Write-Output "evidence=$path"
}

function Invoke-Pin {
    $state = Read-State
    $process = Get-DashboardProcess $state
    $window = Wait-DashboardWindow $process.Id
    Assert-IdleDashboard $window
    $handle = Show-Dashboard $process
    if (Test-Topmost $handle) {
        Fail 'Window is already topmost.'
    }

    $pin = Find-Named $window $PinName
    $before = "toggle=Off`r`ntopmost=False`r`nname=$PinName"
    Write-Evidence $state 'pin-before.txt' $before | Out-Null

    $patternObject = $null
    if (-not $pin.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$patternObject)) {
        Fail "Element '$PinName' has no TogglePattern."
    }

    $patternObject.Toggle()
    Start-Sleep -Milliseconds 400
    $process.Refresh()
    $handle = $process.MainWindowHandle
    $toggle = Get-ToggleState (Find-Named $window $PinName)
    $topmost = Test-Topmost $handle
    if ($toggle -ne 'On' -or -not $topmost) {
        Fail "Pin result toggle=$toggle topmost=$topmost, expected On and True."
    }

    $after = "toggle=On`r`ntopmost=True`r`nname=$PinName"
    $afterPath = Write-Evidence $state 'pin-after.txt' $after
    $shot = Save-WindowScreenshot $state $window
    Write-Output "pin=on"
    Write-Output "evidence=$afterPath"
    Write-Output "screenshot=$shot"
}

function Invoke-Snapshot {
    $state = Read-State
    $process = Get-DashboardProcess $state
    $window = Wait-DashboardWindow $process.Id
    $handle = Show-Dashboard $process
    $names = Get-NamedElements $window | ForEach-Object { Get-ElementReport $_ }
    $listPath = Write-Evidence $state 'named-controls.txt' ($names -join "`r`n")
    $shot = Save-WindowScreenshot $state $window
    Write-Output "controls=$listPath"
    Write-Output "screenshot=$shot"
}

function Invoke-Invoke {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        Fail 'invoke requires -Name.'
    }

    $state = Read-State
    $process = Get-DashboardProcess $state
    $window = Wait-DashboardWindow $process.Id
    $element = Find-Named $window $Name
    $invokeObject = $null
    $toggleObject = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokeObject)) {
        $invokeObject.Invoke()
    }
    elseif ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$toggleObject)) {
        $toggleObject.Toggle()
    }
    else {
        Fail "Element '$Name' has neither InvokePattern nor TogglePattern."
    }

    Start-Sleep -Milliseconds 400
    Write-Output (Get-ElementReport (Find-Named $window $Name))
}

function Invoke-Read {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        Fail 'read requires -Name.'
    }

    $state = Read-State
    $process = Get-DashboardProcess $state
    $window = Wait-DashboardWindow $process.Id
    Write-Output (Get-ElementReport (Find-Named $window $Name))
}

function Invoke-Cleanup {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        Write-Output 'cleanup=no-state'
        return
    }

    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    $process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if ($process -and -not $process.HasExited) {
        $expected = [System.IO.Path]::GetFullPath($state.exePath)
        $actual = [System.IO.Path]::GetFullPath($process.Path)
        if (-not [string]::Equals($expected, $actual, [StringComparison]::OrdinalIgnoreCase)) {
            Fail "Refusing to kill process $($state.pid); its path is '$actual'."
        }

        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }

    if ($state.appDir -and (Test-Path -LiteralPath $state.appDir)) {
        Remove-Item -LiteralPath (Split-Path $state.appDir -Parent) -Recurse -Force
    }

    Remove-Item -LiteralPath $StatePath -Force
    Write-Output "cleanup=ok"
    Write-Output "evidenceKept=$($state.evidenceDir)"
}

switch ($Command) {
    'launch' { Invoke-Launch }
    'doctor' { Invoke-Doctor }
    'pin' { Invoke-Pin }
    'snapshot' { Invoke-Snapshot }
    'invoke' { Invoke-Invoke }
    'read' { Invoke-Read }
    'cleanup' { Invoke-Cleanup }
    default { Fail "Unknown command '$Command'." }
}
