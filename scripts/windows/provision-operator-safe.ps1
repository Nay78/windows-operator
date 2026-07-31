[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [ValidateSet("Audit", "Apply", "Rollback")]
    [string]$Action = "Audit",

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator\provisioning\operator-safe"),

    [string]$SnapshotPath = "",

    [Parameter(DontShow = $true)]
    [string]$RegistryRoot = "HKCU:\"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $RegistryRoot.StartsWith("HKCU:\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "RegistryRoot must stay under HKCU:\."
}

$settings = @(
    [pscustomobject]@{
        id = "desktop.menu-delay"
        subKey = "Control Panel\Desktop"
        name = "MenuShowDelay"
        kind = "String"
        desired = "100"
        rationale = "Reduce shell menu latency without disabling shell features."
    },
    [pscustomobject]@{
        id = "desktop.window-animation"
        subKey = "Control Panel\Desktop\WindowMetrics"
        name = "MinAnimate"
        kind = "String"
        desired = "0"
        rationale = "Avoid window animation delay in local and remote desktop sessions."
    },
    [pscustomobject]@{
        id = "shell.taskbar-animation"
        subKey = "Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
        name = "TaskbarAnimations"
        kind = "DWord"
        desired = 0
        rationale = "Avoid taskbar animation delay while preserving taskbar behavior."
    },
    [pscustomobject]@{
        id = "shell.transparency"
        subKey = "Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        name = "EnableTransparency"
        kind = "DWord"
        desired = 0
        rationale = "Remove desktop composition work that provides no operator value."
    },
    [pscustomobject]@{
        id = "shell.startup-delay"
        subKey = "Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"
        name = "StartupDelayInMSec"
        kind = "DWord"
        desired = 0
        rationale = "Start registered logon applications without Explorer's artificial delay."
    }
)

function Resolve-SettingPath {
    param([Parameter(Mandatory = $true)]$Setting)

    return (Join-Path $RegistryRoot $Setting.subKey)
}

function Get-RegistryValueState {
    param([Parameter(Mandatory = $true)]$Setting)

    $path = Resolve-SettingPath -Setting $Setting
    $exists = $false
    $value = $null
    $kind = $null

    if (Test-Path -LiteralPath $path) {
        $key = Get-Item -LiteralPath $path
        if ($key.GetValueNames() -contains $Setting.name) {
            $exists = $true
            $value = $key.GetValue($Setting.name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $kind = [string]$key.GetValueKind($Setting.name)
        }
    }

    [pscustomobject]@{
        id = $Setting.id
        subKey = $Setting.subKey
        name = $Setting.name
        exists = $exists
        kind = $kind
        value = $value
    }
}

function Test-SettingCompliant {
    param(
        [Parameter(Mandatory = $true)]$Setting,
        [Parameter(Mandatory = $true)]$State
    )

    if (-not $State.exists -or $State.kind -ne $Setting.kind) {
        return $false
    }

    if ($Setting.kind -eq "DWord") {
        return ([int64]$State.value -eq [int64]$Setting.desired)
    }

    return ([string]$State.value -ceq [string]$Setting.desired)
}

function Get-ProfileAudit {
    @(
        foreach ($setting in $settings) {
            $state = Get-RegistryValueState -Setting $setting
            [pscustomobject]@{
                id = $setting.id
                path = Resolve-SettingPath -Setting $setting
                name = $setting.name
                kind = $setting.kind
                desired = $setting.desired
                exists = $state.exists
                currentKind = $state.kind
                current = $state.value
                compliant = Test-SettingCompliant -Setting $setting -State $state
                rationale = $setting.rationale
            }
        }
    )
}

function Set-RegistryValue {
    param(
        [Parameter(Mandatory = $true)]$Setting,
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    $allowedKinds = @("Binary", "DWord", "ExpandString", "MultiString", "QWord", "String")
    if ($Kind -notin $allowedKinds) {
        throw "Unsupported registry value kind for $($Setting.id): $Kind"
    }

    $path = Resolve-SettingPath -Setting $Setting
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -Path $path -ItemType RegistryKey -Force | Out-Null
    }

    $key = Get-Item -LiteralPath $path
    if ($key.GetValueNames() -contains $Setting.name) {
        Remove-ItemProperty -LiteralPath $path -Name $Setting.name -Force
    }

    New-ItemProperty `
        -LiteralPath $path `
        -Name $Setting.name `
        -PropertyType $Kind `
        -Value $Value `
        -Force | Out-Null
}

function Write-JsonResult {
    param([Parameter(Mandatory = $true)]$Value)

    $Value | ConvertTo-Json -Depth 8
}

function New-Snapshot {
    param([Parameter(Mandatory = $true)][object[]]$Before)

    New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ")
    $path = Join-Path $StateRoot "operator-safe-before-$stamp.json"
    $snapshot = [ordered]@{
        schemaVersion = 1
        profile = "operator-safe"
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        computerName = $env:COMPUTERNAME
        userName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        registryRoot = $RegistryRoot
        before = $Before
    }
    $snapshot | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    return (Resolve-Path -LiteralPath $path).Path
}

function Restore-ProfileValues {
    param([Parameter(Mandatory = $true)][object[]]$PriorSettings)

    foreach ($setting in $settings) {
        $prior = $PriorSettings | Where-Object { $_.id -eq $setting.id } | Select-Object -First 1
        if (-not $prior) {
            throw "Prior state missing operator-safe setting: $($setting.id)"
        }

        $path = Resolve-SettingPath -Setting $setting
        if ([bool]$prior.exists) {
            Set-RegistryValue -Setting $setting -Value $prior.value -Kind ([string]$prior.kind)
        }
        elseif (Test-Path -LiteralPath $path) {
            $key = Get-Item -LiteralPath $path
            if ($key.GetValueNames() -contains $setting.name) {
                Remove-ItemProperty -LiteralPath $path -Name $setting.name -Force
            }
        }
    }
}

if ($Action -eq "Audit") {
    $audit = @(Get-ProfileAudit)
    Write-JsonResult -Value ([ordered]@{
        action = "audit"
        profile = "operator-safe"
        compliant = (@($audit | Where-Object { -not $_.compliant }).Count -eq 0)
        settings = $audit
    })
    return
}

if ($Action -eq "Apply") {
    $before = @($settings | ForEach-Object { Get-RegistryValueState -Setting $_ })
    $beforeAudit = @(Get-ProfileAudit)
    $pending = @($beforeAudit | Where-Object { -not $_.compliant })
    $target = "$($settings.Count) per-user operator-safe registry settings under $RegistryRoot"

    if ($pending.Count -eq 0) {
        Write-JsonResult -Value ([ordered]@{
            action = "apply"
            profile = "operator-safe"
            applied = $true
            changedCount = 0
            snapshotPath = $null
            restartRequired = $null
            compliant = $true
            settings = $beforeAudit
        })
        return
    }

    if (-not $PSCmdlet.ShouldProcess($target, "Apply profile and save rollback snapshot")) {
        Write-JsonResult -Value ([ordered]@{
            action = "apply"
            profile = "operator-safe"
            applied = $false
            changedCount = 0
            pendingCount = $pending.Count
            snapshotPath = $null
            settings = $beforeAudit
        })
        return
    }

    $savedSnapshotPath = New-Snapshot -Before $before
    try {
        foreach ($setting in $settings) {
            $state = $before | Where-Object { $_.id -eq $setting.id } | Select-Object -First 1
            if (-not (Test-SettingCompliant -Setting $setting -State $state)) {
                Set-RegistryValue -Setting $setting -Value $setting.desired -Kind $setting.kind
            }
        }
    }
    catch {
        $applyFailure = $_.Exception.Message
        try {
            Restore-ProfileValues -PriorSettings $before
        }
        catch {
            throw "Operator-safe apply failed: $applyFailure Automatic rollback also failed: $($_.Exception.Message)"
        }

        throw "Operator-safe apply failed and was rolled back: $applyFailure"
    }

    $afterAudit = @(Get-ProfileAudit)
    Write-JsonResult -Value ([ordered]@{
        action = "apply"
        profile = "operator-safe"
        applied = $true
        changedCount = $pending.Count
        snapshotPath = $savedSnapshotPath
        restartRequired = "Explorer sign-out or reboot"
        compliant = (@($afterAudit | Where-Object { -not $_.compliant }).Count -eq 0)
        settings = $afterAudit
    })
    return
}

if ([string]::IsNullOrWhiteSpace($SnapshotPath)) {
    throw "SnapshotPath is required for Rollback."
}

$resolvedSnapshotPath = (Resolve-Path -LiteralPath $SnapshotPath -ErrorAction Stop).Path
$snapshot = Get-Content -LiteralPath $resolvedSnapshotPath -Raw | ConvertFrom-Json
if ($snapshot.schemaVersion -ne 1 -or $snapshot.profile -ne "operator-safe") {
    throw "Unsupported operator-safe snapshot: $resolvedSnapshotPath"
}
if ([string]$snapshot.registryRoot -ne $RegistryRoot) {
    throw "Snapshot RegistryRoot '$($snapshot.registryRoot)' does not match requested RegistryRoot '$RegistryRoot'."
}

$snapshotSettings = @($snapshot.before)
$knownIds = @($settings | ForEach-Object { $_.id })
$unknownIds = @($snapshotSettings | Where-Object { $_.id -notin $knownIds })
if ($unknownIds.Count -gt 0 -or $snapshotSettings.Count -ne $settings.Count) {
    throw "Snapshot setting set does not match the operator-safe profile."
}

$rollbackTarget = "$($settings.Count) per-user settings under $RegistryRoot from $resolvedSnapshotPath"
if (-not $PSCmdlet.ShouldProcess($rollbackTarget, "Restore exact pre-profile values")) {
    Write-JsonResult -Value ([ordered]@{
        action = "rollback"
        profile = "operator-safe"
        rolledBack = $false
        snapshotPath = $resolvedSnapshotPath
        settings = @(Get-ProfileAudit)
    })
    return
}

Restore-ProfileValues -PriorSettings $snapshotSettings

Write-JsonResult -Value ([ordered]@{
    action = "rollback"
    profile = "operator-safe"
    rolledBack = $true
    snapshotPath = $resolvedSnapshotPath
    restartRequired = "Explorer sign-out or reboot"
    settings = @(Get-ProfileAudit)
})
