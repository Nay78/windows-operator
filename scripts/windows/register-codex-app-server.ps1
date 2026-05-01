[CmdletBinding()]
param(
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "Codex"),

    [string]$ListenUrl = "ws://127.0.0.1:43118"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

if (-not (Test-Path -LiteralPath $StateRoot)) {
    New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
}

$resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
$runDir = Join-Path $resolvedStateRoot "run"
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$scriptPath = Join-Path $PSScriptRoot "run-codex-app-server.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Launcher script missing: $scriptPath"
}

$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Quote-Argument $scriptPath),
    "-StateRoot", (Quote-Argument $resolvedStateRoot),
    "-ListenUrl", (Quote-Argument $ListenUrl)
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $runDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
if ($trigger.PSObject.Properties.Name -contains "Delay") {
    $trigger.Delay = "PT30S"
}
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -StartWhenAvailable

$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName "Codex.AppServer" -InputObject $task -Force | Out-Null

Write-Host "[codex-autostart] Registered task Codex.AppServer for $userId"
