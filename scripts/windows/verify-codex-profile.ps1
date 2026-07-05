[CmdletBinding()]
param(
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex"),

    [string[]]$ExpectedSkills = @(
        "parallel-work",
        "drill-area-delivery",
        "mining-engineering-analysis",
        "visual-plan"
    ),

    [int]$MinimumSkillCount = 48
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-CodexCommand {
    $command = Get-Command codex.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Path
    }

    $configPath = Join-Path $env:CODEX_HOME "config.toml"
    if (Test-Path -LiteralPath $configPath) {
        $cliPathLine = Select-String -LiteralPath $configPath -Pattern "^CODEX_CLI_PATH\s*=\s*'([^']+)'" | Select-Object -First 1
        if ($cliPathLine -and $cliPathLine.Matches[0].Groups.Count -gt 1) {
            $candidate = $cliPathLine.Matches[0].Groups[1].Value
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    return $null
}

$resolvedCodexHome = $CodexHome
New-Item -ItemType Directory -Path $resolvedCodexHome -Force | Out-Null
$resolvedCodexHome = (Resolve-Path -LiteralPath $resolvedCodexHome).Path
$env:CODEX_HOME = $resolvedCodexHome

$skillsPath = Join-Path $resolvedCodexHome "skills"
if (-not (Test-Path -LiteralPath $skillsPath)) {
    throw "Skills directory missing: $skillsPath"
}

$skillFiles = @(Get-ChildItem -LiteralPath $skillsPath -Recurse -Filter "SKILL.md" -File)
$missingSkillFiles = @(
    $ExpectedSkills | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $skillsPath "$_\SKILL.md"))
    }
)
if ($skillFiles.Count -lt $MinimumSkillCount) {
    throw "Skill count too low. Expected at least $MinimumSkillCount, got $($skillFiles.Count)."
}
if ($missingSkillFiles.Count -gt 0) {
    throw "Missing expected skills: $($missingSkillFiles -join ', ')"
}

$codexCommand = Find-CodexCommand
if (-not $codexCommand) {
    throw "codex.exe not found."
}

$promptInput = & $codexCommand debug prompt-input "noop" 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "codex debug prompt-input failed with exit code $LASTEXITCODE."
}

$promptText = $promptInput | Out-String
$missingPromptSkills = @($ExpectedSkills | Where-Object { $promptText -notmatch [regex]::Escape($_) })
if ($promptText -notmatch "Available skills") {
    throw "Fresh Codex prompt input did not include Available skills."
}
if ($missingPromptSkills.Count -gt 0) {
    throw "Fresh Codex prompt input missing expected skills: $($missingPromptSkills -join ', ')"
}

$summary = [pscustomobject]@{
    status = "verified"
    codexHome = $resolvedCodexHome
    codexCommand = $codexCommand
    skillCount = $skillFiles.Count
    expectedSkills = $ExpectedSkills
    promptInputChars = $promptText.Length
    promptHasAvailableSkills = $true
}

Write-Host "[codex-profile-verify] $($summary | ConvertTo-Json -Compress)"
