[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [ValidateSet("Audit", "Apply", "Rollback")]
    [string]$Action = "Audit",

    [ValidateRange(1, 365)]
    [int]$ThresholdDays = 1,

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator\maintenance\onedrive-storage-sense"),

    [string]$SnapshotPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# These are Microsoft Storage Sense machine policies. They affect local
# cloud-backed content only; they do not delete or modify cloud files.
$registryPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\StorageSense"
$settings = @(
    [pscustomobject]@{
        Id = "storage-sense.enabled"
        Name = "AllowStorageSenseGlobal"
        Desired = 1
        Description = "Allow Storage Sense to manage local storage."
    },
    [pscustomobject]@{
        Id = "storage-sense.cadence"
        Name = "ConfigStorageSenseGlobalCadence"
        Desired = 1
        Description = "Run Storage Sense daily."
    },
    [pscustomobject]@{
        Id = "onedrive.cloud-dehydration-threshold"
        Name = "ConfigStorageSenseCloudContentDehydrationThreshold"
        Desired = $ThresholdDays
        Description = "Dehydrate cloud-backed content unopened for at least the configured number of days."
    }
)

function Write-JsonResult {
    param([Parameter(Mandatory = $true)][object]$Value)

    $Value | ConvertTo-Json -Depth 8
}

function Get-RegistryValueState {
    param([Parameter(Mandatory = $true)]$Setting)

    $exists = $false
    $value = $null
    $kind = $null
    if (Test-Path -LiteralPath $registryPath) {
        $key = Get-Item -LiteralPath $registryPath
        if ($key.GetValueNames() -contains $Setting.Name) {
            $exists = $true
            $value = $key.GetValue(
                $Setting.Name,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $kind = [string]$key.GetValueKind($Setting.Name)
        }
    }

    return [pscustomobject]@{
        id = $Setting.Id
        name = $Setting.Name
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

    return $State.exists -and $State.kind -eq "DWord" -and [int64]$State.value -eq [int64]$Setting.Desired
}

function Get-Audit {
    $rows = @(
        foreach ($setting in $settings) {
            $state = Get-RegistryValueState -Setting $setting
            [pscustomobject]@{
                id = $setting.Id
                path = $registryPath
                name = $setting.Name
                desired = $setting.Desired
                exists = $state.exists
                currentKind = $state.kind
                current = $state.value
                compliant = Test-SettingCompliant -Setting $setting -State $state
                description = $setting.Description
            }
        }
    )
    return $rows
}

function Set-PolicyValue {
    param(
        [Parameter(Mandatory = $true)]$Setting,
        [Parameter(Mandatory = $true)][int]$Value
    )

    if (-not (Test-Path -LiteralPath $registryPath)) {
        New-Item -Path $registryPath -ItemType RegistryKey -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $registryPath -Name $Setting.Name -PropertyType DWord -Value $Value -Force | Out-Null
}

function Write-Snapshot {
    param([Parameter(Mandatory = $true)][object[]]$Before)

    New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ")
    $path = Join-Path $StateRoot "before-$stamp.json"
    [ordered]@{
        schemaVersion = 1
        profile = "onedrive-daily-dehydration"
        registryPath = $registryPath
        thresholdDays = $ThresholdDays
        computerName = $env:COMPUTERNAME
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        before = $Before
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    return (Resolve-Path -LiteralPath $path).Path
}

function Restore-Snapshot {
    param([Parameter(Mandatory = $true)][object[]]$Before)

    foreach ($setting in $settings) {
        $prior = $Before | Where-Object { $_.id -eq $setting.Id } | Select-Object -First 1
        if ($null -eq $prior) {
            throw "Snapshot is missing setting $($setting.Id)."
        }

        if ([bool]$prior.exists) {
            if ([string]$prior.kind -ne "DWord") {
                throw "Snapshot setting $($setting.Id) is not a DWord."
            }
            Set-PolicyValue -Setting $setting -Value ([int]$prior.value)
        }
        elseif (Test-Path -LiteralPath $registryPath) {
            $key = Get-Item -LiteralPath $registryPath
            if ($key.GetValueNames() -contains $setting.Name) {
                Remove-ItemProperty -LiteralPath $registryPath -Name $setting.Name -Force
            }
        }
    }
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Apply and Rollback require an elevated PowerShell process because the target is $registryPath."
    }
}

if ($Action -eq "Audit") {
    $audit = @(Get-Audit)
    Write-JsonResult -Value ([ordered]@{
        action = "audit"
        profile = "onedrive-daily-dehydration"
        registryPath = $registryPath
        cloudFilesModified = $false
        compliant = (@($audit | Where-Object { -not $_.compliant }).Count -eq 0)
        settings = $audit
    })
    return
}

Assert-Administrator

if ($Action -eq "Apply") {
    $before = @($settings | ForEach-Object { Get-RegistryValueState -Setting $_ })
    $beforeAudit = @(Get-Audit)
    $pending = @($beforeAudit | Where-Object { -not $_.compliant })
    $target = "$registryPath (daily Storage Sense and cloud-content dehydration threshold)"

    if ($pending.Count -eq 0) {
        Write-JsonResult -Value ([ordered]@{
            action = "apply"
            profile = "onedrive-daily-dehydration"
            applied = $true
            changedCount = 0
            snapshotPath = $null
            cloudFilesModified = $false
            compliant = $true
            settings = $beforeAudit
        })
        return
    }

    if (-not $PSCmdlet.ShouldProcess($target, "Apply machine Storage Sense policy and save rollback snapshot")) {
        Write-JsonResult -Value ([ordered]@{
            action = "apply"
            profile = "onedrive-daily-dehydration"
            applied = $false
            changedCount = 0
            pendingCount = $pending.Count
            cloudFilesModified = $false
            settings = $beforeAudit
        })
        return
    }

    $savedSnapshotPath = Write-Snapshot -Before $before
    try {
        foreach ($setting in $settings) {
            $state = $before | Where-Object { $_.id -eq $setting.Id } | Select-Object -First 1
            if (-not (Test-SettingCompliant -Setting $setting -State $state)) {
                Set-PolicyValue -Setting $setting -Value ([int]$setting.Desired)
            }
        }
    }
    catch {
        $applyFailure = $_.Exception.Message
        try {
            Restore-Snapshot -Before $before
        }
        catch {
            throw "Storage Sense policy apply failed: $applyFailure. Automatic rollback also failed: $($_.Exception.Message)"
        }
        throw "Storage Sense policy apply failed and was rolled back: $applyFailure"
    }

    $afterAudit = @(Get-Audit)
    Write-JsonResult -Value ([ordered]@{
        action = "apply"
        profile = "onedrive-daily-dehydration"
        applied = $true
        changedCount = $pending.Count
        snapshotPath = $savedSnapshotPath
        cloudFilesModified = $false
        policyRefresh = "gpupdate /target:computer /force"
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
if ($snapshot.schemaVersion -ne 1 -or $snapshot.profile -ne "onedrive-daily-dehydration") {
    throw "Unsupported OneDrive daily dehydration snapshot: $resolvedSnapshotPath"
}
if ([string]$snapshot.registryPath -ne $registryPath) {
    throw "Snapshot registry path '$($snapshot.registryPath)' does not match '$registryPath'."
}

$before = @($snapshot.before)
$target = "$registryPath from $resolvedSnapshotPath"
if (-not $PSCmdlet.ShouldProcess($target, "Restore exact pre-policy values")) {
    Write-JsonResult -Value ([ordered]@{
        action = "rollback"
        profile = "onedrive-daily-dehydration"
        rolledBack = $false
        snapshotPath = $resolvedSnapshotPath
        cloudFilesModified = $false
        settings = @(Get-Audit)
    })
    return
}

Restore-Snapshot -Before $before
Write-JsonResult -Value ([ordered]@{
    action = "rollback"
    profile = "onedrive-daily-dehydration"
    rolledBack = $true
    snapshotPath = $resolvedSnapshotPath
    cloudFilesModified = $false
    policyRefresh = "gpupdate /target:computer /force"
    settings = @(Get-Audit)
})
