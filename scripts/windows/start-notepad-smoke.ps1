[CmdletBinding()]
param(
    [string]$RunId = "notepad-smoke",

    [int]$WaitSeconds = 10,

    [string]$OutputPath,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [string]$TaskName,

    [string]$InteractiveUser,

    [switch]$RunInteractive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Ensure-StateDirectories {
    param([string]$Path)

    @(
        $Path,
        (Join-Path $Path "run")
    ) | ForEach-Object {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
    }
}

function Get-LoggedOnUser {
    $computerSystem = Get-CimInstance Win32_ComputerSystem
    if (-not [string]::IsNullOrWhiteSpace($computerSystem.UserName)) {
        return [string]$computerSystem.UserName
    }

    $explorer = Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" | Select-Object -First 1
    if ($explorer) {
        $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
        if ($owner.ReturnValue -eq 0 -and
            -not [string]::IsNullOrWhiteSpace($owner.User) -and
            -not [string]::IsNullOrWhiteSpace($owner.Domain)) {
            return "$($owner.Domain)\$($owner.User)"
        }
    }

    throw "No logged-in Windows desktop user found."
}

function Resolve-DefaultOutputPath {
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        return $OutputPath
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Join-Path (Split-Path -Parent $PSCommandPath) "notepad-launch.json")
    }

    Ensure-StateDirectories -Path $StateRoot
    return (Join-Path $StateRoot "run\notepad-launch.json")
}

function Invoke-InteractiveLaunch {
    $resolvedOutputPath = Resolve-DefaultOutputPath
    $outputRoot = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputRoot)) {
        New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    }

    $payload = [ordered]@{
        runId = $RunId
        processId = $null
        mainWindowHandle = 0
        hasExited = $true
        startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    try {
        $process = Start-Process -FilePath "notepad.exe" -PassThru
        $deadline = (Get-Date).AddSeconds($WaitSeconds)

        do {
            $process.Refresh()
            if ($process.MainWindowHandle -ne 0) {
                break
            }

            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)

        $payload.processId = $process.Id
        $payload.mainWindowHandle = [int64]$process.MainWindowHandle
        $payload.hasExited = $process.HasExited
    }
    catch {
        $payload.error = $_.Exception.Message
        throw
    }
    finally {
        $json = $payload | ConvertTo-Json -Compress
        $json | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
        $json

        if (-not [string]::IsNullOrWhiteSpace($TaskName)) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        }
    }
}

function Start-InteractiveTask {
    $resolvedOutputPath = Resolve-DefaultOutputPath
    Remove-Item -LiteralPath $resolvedOutputPath -Force -ErrorAction SilentlyContinue

    $targetUser = if ([string]::IsNullOrWhiteSpace($InteractiveUser)) {
        Get-LoggedOnUser
    }
    else {
        $InteractiveUser
    }

    $effectiveTaskName = if ([string]::IsNullOrWhiteSpace($TaskName)) {
        "WindowsOperator.NotepadSmoke.$RunId"
    }
    else {
        $TaskName
    }

    if ([string]::IsNullOrWhiteSpace($PSCommandPath) -or -not (Test-Path -LiteralPath $PSCommandPath)) {
        throw "Cannot resolve script path for scheduled task."
    }

    Ensure-StateDirectories -Path $StateRoot
    $resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
    $runRoot = Join-Path $resolvedStateRoot "run"
    $taskScriptPath = Join-Path $runRoot "start-notepad-smoke.ps1"
    Copy-Item -LiteralPath $PSCommandPath -Destination $taskScriptPath -Force

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-Argument $taskScriptPath),
        "-RunInteractive",
        "-RunId", (Quote-Argument $RunId),
        "-WaitSeconds", $WaitSeconds,
        "-OutputPath", (Quote-Argument $resolvedOutputPath),
        "-StateRoot", (Quote-Argument $resolvedStateRoot),
        "-TaskName", (Quote-Argument $effectiveTaskName)
    )

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument ($arguments -join " ") -WorkingDirectory $runRoot
    $principal = New-ScheduledTaskPrincipal -UserId $targetUser -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -MultipleInstances Parallel `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    $task = New-ScheduledTask -Action $action -Principal $principal -Settings $settings

    Register-ScheduledTask -TaskName $effectiveTaskName -InputObject $task -Force | Out-Null
    Start-ScheduledTask -TaskName $effectiveTaskName

    $deadline = (Get-Date).AddSeconds([Math]::Max(5, $WaitSeconds + 10))
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) {
            $json = Get-Content -LiteralPath $resolvedOutputPath -Raw
            $json
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for interactive Notepad launch result: $resolvedOutputPath"
}

if ($RunInteractive) {
    Invoke-InteractiveLaunch
}
else {
    Start-InteractiveTask
}
