[CmdletBinding()]
param(
    [string]$ScriptPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
    $ScriptPath = Join-Path $PSScriptRoot "provision-operator-safe.ps1"
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
    throw "Profile script missing: $ScriptPath"
}

$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $ScriptPath,
    [ref]$tokens,
    [ref]$parseErrors
)
Assert-True -Condition ($parseErrors.Count -eq 0) -Message "PowerShell parser reported errors."

$testId = [Guid]::NewGuid().ToString("N")
$registryRoot = "HKCU:\Software\WindowsOperatorTests\OperatorSafe-$testId"
$stateRoot = Join-Path $env:TEMP "WindowsOperatorTests\OperatorSafe-$testId"
$seedPath = Join-Path $registryRoot "Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"

try {
    New-Item -Path $seedPath -ItemType RegistryKey -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $seedPath `
        -Name "EnableTransparency" `
        -PropertyType DWord `
        -Value 1 `
        -Force | Out-Null

    $initialAudit = (& $ScriptPath `
        -Action Audit `
        -RegistryRoot $registryRoot `
        -StateRoot $stateRoot | ConvertFrom-Json)
    Assert-True -Condition ($initialAudit.action -eq "audit") -Message "Audit action mismatch."
    Assert-True -Condition ($initialAudit.settings.Count -eq 5) -Message "Audit must expose five allowlisted settings."
    Assert-True -Condition (-not [bool]$initialAudit.compliant) -Message "Seed state must not be compliant."
    Assert-True -Condition (-not (Test-Path -LiteralPath $stateRoot)) -Message "Audit must not write state."

    $firstApply = (& $ScriptPath `
        -Action Apply `
        -RegistryRoot $registryRoot `
        -StateRoot $stateRoot `
        -Confirm:$false | ConvertFrom-Json)
    Assert-True -Condition ([bool]$firstApply.applied) -Message "Apply did not report success."
    Assert-True -Condition ([bool]$firstApply.compliant) -Message "Apply result is not compliant."
    Assert-True -Condition ($firstApply.changedCount -eq 5) -Message "First apply must change five settings."
    Assert-True -Condition (Test-Path -LiteralPath $firstApply.snapshotPath -PathType Leaf) -Message "Rollback snapshot missing."

    $secondApply = (& $ScriptPath `
        -Action Apply `
        -RegistryRoot $registryRoot `
        -StateRoot $stateRoot `
        -Confirm:$false | ConvertFrom-Json)
    Assert-True -Condition ([bool]$secondApply.applied) -Message "Second apply did not report success."
    Assert-True -Condition ($secondApply.changedCount -eq 0) -Message "Second apply must be idempotent."
    Assert-True -Condition ($null -eq $secondApply.snapshotPath) -Message "Compliant apply must not create a redundant snapshot."

    $rollback = (& $ScriptPath `
        -Action Rollback `
        -RegistryRoot $registryRoot `
        -StateRoot $stateRoot `
        -SnapshotPath $firstApply.snapshotPath `
        -Confirm:$false | ConvertFrom-Json)
    Assert-True -Condition ([bool]$rollback.rolledBack) -Message "Rollback did not report success."

    $restored = Get-ItemPropertyValue `
        -LiteralPath $seedPath `
        -Name "EnableTransparency" `
        -ErrorAction Stop
    Assert-True -Condition ($restored -eq 1) -Message "Rollback did not restore the seeded value."

    $startupPath = Join-Path $registryRoot "Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"
    $startupKey = Get-Item -LiteralPath $startupPath -ErrorAction SilentlyContinue
    $startupValueExists = $startupKey -and ($startupKey.GetValueNames() -contains "StartupDelayInMSec")
    Assert-True -Condition (-not $startupValueExists) -Message "Rollback did not remove an originally absent value."

    [ordered]@{
        status = "passed"
        scriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path
        settingCount = $initialAudit.settings.Count
        firstApplyChangedCount = $firstApply.changedCount
        secondApplyChangedCount = $secondApply.changedCount
        rollbackRestoredSeed = ($restored -eq 1)
    } | ConvertTo-Json -Compress
}
finally {
    if ($registryRoot -like "HKCU:\Software\WindowsOperatorTests\OperatorSafe-*") {
        Remove-Item -LiteralPath $registryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($stateRoot -like (Join-Path $env:TEMP "WindowsOperatorTests\OperatorSafe-*")) {
        Remove-Item -LiteralPath $stateRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
