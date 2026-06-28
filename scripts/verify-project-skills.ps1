param(
    [string]$ProjectRoot = "",
    [string]$SkillName = "binance-indicator-dev",
    [switch]$CheckInstalled
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Label
    )

    if ($Text.IndexOf($Expected, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Missing required skill guardrail [$Label]: $Expected"
    }
}

$skillRoot = Join-Path $ProjectRoot "skills\$SkillName"
$skillPath = Join-Path $skillRoot "SKILL.md"
$metadataPath = Join-Path $skillRoot "agents\openai.yaml"

if (-not (Test-Path -LiteralPath $skillPath)) {
    throw "Missing project skill: $skillPath"
}

if (-not (Test-Path -LiteralPath $metadataPath)) {
    throw "Missing skill UI metadata: $metadataPath"
}

$skill = Get-Content -Raw -LiteralPath $skillPath
$metadata = Get-Content -Raw -LiteralPath $metadataPath

if (-not $skill.StartsWith("---`r`nname: $SkillName", [System.StringComparison]::Ordinal) -and
    -not $skill.StartsWith("---`nname: $SkillName", [System.StringComparison]::Ordinal)) {
    throw "Invalid or missing skill frontmatter name in $skillPath"
}

Assert-Contains $skill "description:" "frontmatter description"
Assert-Contains $skill "Do not use REST in the hot path" "no REST hot path"
Assert-Contains $skill "Live and replay must feed the same internal event types" "shared live/replay events"
Assert-Contains $skill "Do not change formula, thresholds, filters, or sampling cadence" "formula approval gate"
Assert-Contains $skill 'Keep `Application` dependent on `Domain` only' "Application boundary"
Assert-Contains $skill "Version JSONL event envelopes" "JSONL versioning"
Assert-Contains $skill "%USERPROFILE%\.codex\skills" "local install is cache"
Assert-Contains $metadata '$binance-indicator-dev' "default prompt"

if ($skill -match "\[TODO:" -or
    $skill -match "Complete and informative explanation" -or
    $skill -match "Replace with the first main section") {
    throw "Skill still contains template placeholder text."
}

if ($CheckInstalled) {
    $installedPath = Join-Path $env:USERPROFILE ".codex\skills\$SkillName\SKILL.md"

    if (-not (Test-Path -LiteralPath $installedPath)) {
        throw "Installed skill is missing: $installedPath"
    }

    $installed = Get-Content -Raw -LiteralPath $installedPath
    if ($installed -ne $skill) {
        throw "Installed skill differs from repository source: $installedPath"
    }
}

Write-Host "Project skill verification passed: $skillPath"
