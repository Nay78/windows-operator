[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [int]$WaitSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$wasRunning = $false
$stopped = $false

$process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($null -ne $process) {
    $wasRunning = $true
    try {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            [void]$process.CloseMainWindow()
            [void]$process.WaitForExit($WaitSeconds * 1000)
        }

        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $ProcessId -Force -ErrorAction Stop
            Start-Sleep -Milliseconds 250
        }
    }
    finally {
        $process.Dispose()
    }
}

$remaining = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($null -eq $remaining) {
    $stopped = $true
}
else {
    $remaining.Dispose()
}

[ordered]@{
    processId = $ProcessId
    wasRunning = $wasRunning
    stopped = $stopped
} | ConvertTo-Json -Compress
