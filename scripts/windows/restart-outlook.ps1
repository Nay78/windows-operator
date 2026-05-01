[CmdletBinding()]
param(
    [int]$GraceSeconds = 30,
    [switch]$NoStart,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "restart-outlook: $Message"
}

function Stop-OutlookGracefully {
    param([int]$TimeoutSeconds)

    $processes = @(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        Write-Step "Outlook not running."
        return
    }

    Write-Step "Requesting Outlook close for $($processes.Count) process(es)."
    foreach ($process in $processes) {
        try {
            if ($process.MainWindowHandle -ne 0) {
                [void]$process.CloseMainWindow()
            }
        }
        catch {
            Write-Step "CloseMainWindow failed for PID $($process.Id): $($_.Exception.Message)"
        }
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $remaining = @(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) {
            Write-Step "Outlook closed."
            return
        }
    } while ((Get-Date) -lt $deadline)

    if (-not $Force) {
        $ids = ($remaining | ForEach-Object { $_.Id }) -join ","
        throw "Outlook still running after ${TimeoutSeconds}s. Use -Force to kill remaining PID(s): $ids"
    }

    Write-Step "Force-killing Outlook after timeout."
    foreach ($process in $remaining) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-Step "Stop-Process failed for PID $($process.Id): $($_.Exception.Message)"
        }
    }
}

Stop-OutlookGracefully -TimeoutSeconds $GraceSeconds

if ($NoStart) {
    Write-Step "Start skipped."
    exit 0
}

Write-Step "Starting Outlook."
Start-Process -FilePath "outlook.exe" | Out-Null
Start-Sleep -Seconds 3

$started = @(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue)
if ($started.Count -eq 0) {
    throw "Outlook did not start."
}

Write-Step "Outlook running. PID(s): $(($started | ForEach-Object { $_.Id }) -join ',')"
