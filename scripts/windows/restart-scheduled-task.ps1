[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("WindowsOperator.Agent", "WindowsOperator.Host", "Codex.AppServer")]
    [string]$TaskName,

    [int]$WaitSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
$beforeState = [string]$task.State
$before = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue

if ($task.State -eq "Running") {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    } while ($task.State -eq "Running" -and (Get-Date) -lt $deadline)
}

Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds ([Math]::Min([Math]::Max($WaitSeconds, 1), 30))

$afterTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
$after = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue

[ordered]@{
    taskName = $TaskName
    beforeState = $beforeState
    beforeLastRunTime = if ($before) { $before.LastRunTime } else { $null }
    afterState = [string]$afterTask.State
    afterLastRunTime = if ($after) { $after.LastRunTime } else { $null }
    lastTaskResult = if ($after) { $after.LastTaskResult } else { $null }
} | ConvertTo-Json -Compress
