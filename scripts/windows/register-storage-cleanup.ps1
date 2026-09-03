[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator"),

    [ValidateSet("Audit", "Execute")]
    [string]$Mode = "Audit",

    [switch]$Unregister
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$taskName = "WindowsOperator.StorageCleanup"
$maintenanceRoot = Join-Path $StateRoot "maintenance"
$stagedRoot = Join-Path $maintenanceRoot "executor"
$stagedScript = Join-Path $stagedRoot "invoke-storage-cleanup.ps1"
$sourceScript = Join-Path $RepoRoot "scripts\windows\invoke-storage-cleanup.ps1"
$installRecord = Join-Path $maintenanceRoot "registration.json"

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        ConvertTo-Json -InputObject $InputObject -Depth 8 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Unregister) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Unregistered $taskName. State retained under $StateRoot."
    exit 0
}

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot missing: $RepoRoot"
}
if (-not (Test-Path -LiteralPath $sourceScript -PathType Leaf)) {
    throw "Cleanup source script missing: $sourceScript"
}

New-Item -ItemType Directory -Force -Path $maintenanceRoot, $stagedRoot, (Join-Path $maintenanceRoot "plans"), (Join-Path $maintenanceRoot "reports"), (Join-Path $maintenanceRoot "quarantine") | Out-Null
Copy-Item -LiteralPath $sourceScript -Destination $stagedScript -Force
$scriptHash = (Get-FileHash -LiteralPath $stagedScript -Algorithm SHA256).Hash.ToLowerInvariant()

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ('"{0}"' -f $stagedScript),
    "-Mode", $Mode,
    "-StateRoot", ('"{0}"' -f $StateRoot)
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $stagedRoot
$trigger = New-ScheduledTaskTrigger -Daily -At (Get-Date "03:15") -RandomDelay (New-TimeSpan -Minutes 30)
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
    -StartWhenAvailable

$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

$record = [ordered]@{
    taskName = $taskName
    mode = $Mode
    stateRoot = $StateRoot
    stagedScript = $stagedScript
    stagedScriptSha256 = $scriptHash
    registeredAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    trigger = "daily 03:15 with up to 30 minute random delay"
    principal = "SYSTEM"
}
Write-JsonAtomic -InputObject $record -Path $installRecord
Write-Host "Registered $taskName as SYSTEM in $Mode mode. StagedScript=$stagedScript Sha256=$scriptHash"
