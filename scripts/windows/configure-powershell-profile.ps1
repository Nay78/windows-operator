[CmdletBinding()]
param(
    [string]$RepoRoot = "",

    [string]$ProfileSourceRelativePath = "profiles\powershell\profile.ps1",

    [string[]]$ProfileTargets = @("WindowsPowerShell", "PowerShell"),

    [string]$ProfileTargetsText = "",

    [string]$ProfilePath = "",

    [string]$BackupRoot = "",

    [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[powershell-profile] $Message"
}

function Quote-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-TextFileNoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Resolve-RepoRoot {
    param([string]$RequestedRepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedRepoRoot)) {
        return (Resolve-Path -LiteralPath $RequestedRepoRoot).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WINDOWS_OPERATOR_REPO_ROOT)) {
        return (Resolve-Path -LiteralPath $env:WINDOWS_OPERATOR_REPO_ROOT).Path
    }

    $candidate = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..") -ErrorAction SilentlyContinue
    if ($candidate) {
        $candidatePath = $candidate.Path
        if (Test-Path -LiteralPath (Join-Path $candidatePath "profiles\powershell\profile.ps1") -PathType Leaf) {
            return $candidatePath
        }
    }

    throw "RepoRoot required when script is executed from a staged copy."
}

function Resolve-RepoFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains("..")) {
        throw "ProfileSourceRelativePath must be repo-relative and must not contain '..'."
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pathFull = [System.IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if (-not $pathFull.StartsWith("$rootFull\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Profile source escapes repo root. Root=$rootFull Path=$pathFull"
    }

    if (-not (Test-Path -LiteralPath $pathFull -PathType Leaf)) {
        throw "Profile source missing: $pathFull"
    }

    return $pathFull
}

function Get-ProfileTargetPaths {
    param(
        [string[]]$Targets,
        [string]$ExplicitProfilePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitProfilePath)) {
        [pscustomobject]@{
            Name = "Custom"
            Path = $ExplicitProfilePath
        }
        return
    }

    $documents = [Environment]::GetFolderPath("MyDocuments")
    foreach ($target in $Targets | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        if ($target -match "^(WindowsPowerShell|powershell\.exe)$") {
            [pscustomobject]@{
                Name = "WindowsPowerShell"
                Path = (Join-Path $documents "WindowsPowerShell\profile.ps1")
            }
            continue
        }

        if ($target -match "^(PowerShell|pwsh|pwsh\.exe)$") {
            [pscustomobject]@{
                Name = "PowerShell"
                Path = (Join-Path $documents "PowerShell\profile.ps1")
            }
            continue
        }

        throw "Unsupported profile target: $target"
    }
}

function Backup-Profile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$TargetName,

        [Parameter(Mandatory = $true)]
        [string]$DestinationRoot
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMddTHHmmssfff"
    $backupPath = Join-Path $DestinationRoot ("{0}.profile.ps1.{1}.bak" -f $TargetName, $stamp)
    Copy-Item -LiteralPath $Path -Destination $backupPath -Force
    return $backupPath
}

function Update-ManagedBlock {
    param(
        [string]$ExistingContent,
        [string]$Block,
        [switch]$Remove
    )

    $begin = "# >>> windows-operator powershell profile >>>"
    $end = "# <<< windows-operator powershell profile <<<"
    $pattern = "(?ms)^" + [regex]::Escape($begin) + ".*?^" + [regex]::Escape($end) + "\r?\n?"

    if ($Remove) {
        $removed = [regex]::Replace($ExistingContent, $pattern, "")
        return (($removed -replace "(\r?\n){3,}", [Environment]::NewLine + [Environment]::NewLine).TrimEnd() + [Environment]::NewLine)
    }

    $blockWithNewline = $Block.TrimEnd() + [Environment]::NewLine
    if ($ExistingContent -match $pattern) {
        return [regex]::Replace($ExistingContent, $pattern, $blockWithNewline)
    }

    $trimmed = $ExistingContent -replace "(\r?\n)+$", ""
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $blockWithNewline
    }

    return $trimmed + [Environment]::NewLine + [Environment]::NewLine + $blockWithNewline
}

$resolvedRepoRoot = Resolve-RepoRoot -RequestedRepoRoot $RepoRoot
$profileSourcePath = Resolve-RepoFile -Root $resolvedRepoRoot -RelativePath $ProfileSourceRelativePath

$targets = @($ProfileTargets)
if (-not [string]::IsNullOrWhiteSpace($ProfileTargetsText)) {
    $targets = @($ProfileTargetsText.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
    $BackupRoot = Join-Path $env:LOCALAPPDATA "WindowsOperator\profile-backups"
}

$managedBlock = @"
# >>> windows-operator powershell profile >>>
`$env:WINDOWS_OPERATOR_REPO_ROOT = $(Quote-PowerShellLiteral $resolvedRepoRoot)
`$windowsOperatorProfile = $(Quote-PowerShellLiteral $profileSourcePath)
if (Test-Path -LiteralPath `$windowsOperatorProfile -PathType Leaf) {
    . `$windowsOperatorProfile
}
# <<< windows-operator powershell profile <<<
"@

$resolvedProfileTargets = @(Get-ProfileTargetPaths -Targets $targets -ExplicitProfilePath $ProfilePath)
foreach ($profileTarget in $resolvedProfileTargets) {
    $targetPath = [string]$profileTarget.Path
    $existing = ""
    if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
        $existing = Get-Content -LiteralPath $targetPath -Raw
    }

    $next = Update-ManagedBlock -ExistingContent $existing -Block $managedBlock -Remove:$Remove
    if ($existing -ceq $next) {
        Write-Step "No change for $($profileTarget.Name): $targetPath"
        continue
    }

    $backupPath = Backup-Profile -Path $targetPath -TargetName ([string]$profileTarget.Name) -DestinationRoot $BackupRoot
    Write-TextFileNoBom -Path $targetPath -Content $next

    if ($backupPath) {
        Write-Step "Updated $($profileTarget.Name): $targetPath Backup=$backupPath"
    }
    else {
        Write-Step "Created $($profileTarget.Name): $targetPath"
    }
}

Write-Step "Profile source: $profileSourcePath"
