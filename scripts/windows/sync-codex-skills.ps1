[CmdletBinding()]
param(
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex"),

    [Parameter(Mandatory = $true)]
    [string]$SkillsArchivePath,

    [switch]$NoBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[codex-skills-sync] $Message"
}

$resolvedCodexHome = $CodexHome
New-Item -ItemType Directory -Path $resolvedCodexHome -Force | Out-Null
$resolvedCodexHome = (Resolve-Path -LiteralPath $resolvedCodexHome).Path

if (-not (Test-Path -LiteralPath $SkillsArchivePath)) {
    throw "Skills archive missing: $SkillsArchivePath"
}
$resolvedArchivePath = (Resolve-Path -LiteralPath $SkillsArchivePath).Path

$skillsPath = Join-Path $resolvedCodexHome "skills"
$backupPath = $null
if ((Test-Path -LiteralPath $skillsPath) -and -not $NoBackup) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
    $backupPath = Join-Path $resolvedCodexHome "skills.backup-sync-$timestamp"
    Copy-Item -LiteralPath $skillsPath -Destination $backupPath -Recurse -Force
    Write-Step "Backed up skills to $backupPath."
}

New-Item -ItemType Directory -Path $skillsPath -Force | Out-Null
& tar.exe -xzf $resolvedArchivePath -C $resolvedCodexHome
if ($LASTEXITCODE -ne 0) {
    throw "tar extract failed with exit code $LASTEXITCODE."
}

$skillFiles = @(Get-ChildItem -LiteralPath $skillsPath -Recurse -Filter "SKILL.md" -File)
$summary = [pscustomobject]@{
    status = "synced"
    codexHome = $resolvedCodexHome
    skillsPath = $skillsPath
    archivePath = $resolvedArchivePath
    backupPath = $backupPath
    skillCount = $skillFiles.Count
    hasParallelWork = (Test-Path -LiteralPath (Join-Path $skillsPath "parallel-work\SKILL.md"))
    hasDrillAreaDelivery = (Test-Path -LiteralPath (Join-Path $skillsPath "drill-area-delivery\SKILL.md"))
    hasMiningEngineering = (Test-Path -LiteralPath (Join-Path $skillsPath "mining-engineering-analysis\SKILL.md"))
    hasVisualPlan = (Test-Path -LiteralPath (Join-Path $skillsPath "visual-plan\SKILL.md"))
}

Write-Step ($summary | ConvertTo-Json -Compress)
