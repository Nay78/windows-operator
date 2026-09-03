[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$tasks = foreach ($taskName in @("WindowsOperator.Host", "WindowsOperator.Agent")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        [pscustomobject]@{
            taskName = $taskName
            exists = $false
            state = $null
            enabled = $null
            principal = $null
            principalDetails = $null
            triggers = @()
            settings = $null
            actions = @()
            lastRunTime = $null
            lastTaskResult = $null
        }
        continue
    }

    $info = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction SilentlyContinue
    [pscustomobject]@{
        taskName = $taskName
        exists = $true
        state = [string]$task.State
        enabled = [bool]$task.Settings.Enabled
        principal = $task.Principal.UserId
        principalDetails = [pscustomobject]@{
            userId = $task.Principal.UserId
            logonType = [string]$task.Principal.LogonType
            runLevel = [string]$task.Principal.RunLevel
        }
        triggers = @($task.Triggers | ForEach-Object {
            [pscustomobject]@{
                type = $_.CimClass.CimClassName
                enabled = [bool]$_.Enabled
                startBoundary = $_.StartBoundary
                delay = $_.Delay
            }
        })
        settings = [pscustomobject]@{
            restartCount = $task.Settings.RestartCount
            restartInterval = [string]$task.Settings.RestartInterval
            executionTimeLimit = [string]$task.Settings.ExecutionTimeLimit
            multipleInstances = [string]$task.Settings.MultipleInstances
            startWhenAvailable = [bool]$task.Settings.StartWhenAvailable
        }
        actions = @($task.Actions | ForEach-Object {
            [pscustomobject]@{
                execute = $_.Execute
                arguments = $_.Arguments
                workingDirectory = $_.WorkingDirectory
            }
        })
        lastRunTime = $info.LastRunTime
        lastTaskResult = $info.LastTaskResult
    }
}

function Get-OperatorRuntimeProcess {
    param(
        [int[]]$Ports = @(43117, 43119)
    )

    $runtimeProcessIds = foreach ($port in $Ports) {
        Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess
    }

    foreach ($runtimeProcessId in $runtimeProcessIds | Sort-Object -Unique) {
        $process = Get-CimInstance `
            -ClassName Win32_Process `
            -Filter ("ProcessId = {0}" -f $runtimeProcessId) `
            -Property ProcessId, Name, ExecutablePath, CommandLine `
            -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            continue
        }

        [pscustomobject]@{
            processId = $process.ProcessId
            name = $process.Name
            executablePath = $process.ExecutablePath
            commandLine = $process.CommandLine
        }
    }
}

$processes = @(Get-OperatorRuntimeProcess)

$hostConfigPath = Join-Path $env:ProgramData "WindowsOperator\run\host.appsettings.Local.json"
$hostWorkbench = $null
if (Test-Path -LiteralPath $hostConfigPath -PathType Leaf) {
    $hostConfig = Get-Content -LiteralPath $hostConfigPath -Raw | ConvertFrom-Json
    if ($hostConfig.Workbench) {
        $hostWorkbench = [pscustomobject]@{
            exchangeRoot = $hostConfig.Workbench.exchangeRoot
            hostExchangeRoot = $hostConfig.Workbench.hostExchangeRoot
        }
    }
}

function Invoke-LocalJson {
    param([Parameter(Mandatory)][string]$Uri)

    try {
        return [pscustomobject]@{
            uri = $Uri
            success = $true
            body = Invoke-RestMethod -Uri $Uri -TimeoutSec 10
            error = $null
        }
    }
    catch {
        return [pscustomobject]@{
            uri = $Uri
            success = $false
            body = $null
            error = $_.Exception.Message
        }
    }
}

[pscustomobject]@{
    observedAtUtc = [DateTimeOffset]::UtcNow
    tasks = @($tasks)
    processes = $processes
    hostWorkbench = $hostWorkbench
    health = Invoke-LocalJson -Uri "http://127.0.0.1:43117/v1/health"
    capabilities = Invoke-LocalJson -Uri "http://127.0.0.1:43117/v1/capabilities"
} | ConvertTo-Json -Depth 12
