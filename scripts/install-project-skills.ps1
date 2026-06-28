param(
    [string]$ProjectRoot = "",
    [string]$SkillName = "binance-indicator-dev",
    [string]$DestinationRoot = (Join-Path $env:USERPROFILE ".codex\skills")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
}

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

$sourceRoot = Get-FullPath (Join-Path $ProjectRoot "skills\$SkillName")
$sourceSkill = Join-Path $sourceRoot "SKILL.md"

if (-not (Test-Path -LiteralPath $sourceSkill)) {
    throw "Project skill source is missing: $sourceSkill"
}

$destinationRootFull = Get-FullPath $DestinationRoot
$destination = Get-FullPath (Join-Path $destinationRootFull $SkillName)

if (-not $destination.StartsWith($destinationRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install outside destination root: $destination"
}

New-Item -ItemType Directory -Force -Path $destinationRootFull | Out-Null

if (Test-Path -LiteralPath $destination) {
    $resolvedDestination = Get-FullPath (Resolve-Path -LiteralPath $destination).Path

    if (-not $resolvedDestination.StartsWith($destinationRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside destination root: $resolvedDestination"
    }

    Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
}

Copy-Item -LiteralPath $sourceRoot -Destination $destinationRootFull -Recurse -Force

$installedSkill = Join-Path $destination "SKILL.md"
if (-not (Test-Path -LiteralPath $installedSkill)) {
    throw "Install failed; missing installed SKILL.md: $installedSkill"
}

Write-Host "Installed $SkillName to $destination"
Write-Host "Restart Codex if the active skills list does not refresh automatically."
