[CmdletBinding()]
param(
    [string]$SupervisorPath = (Join-Path $PSScriptRoot "invoke-agent-supervisor.ps1")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$supervisor = [System.IO.Path]::GetFullPath($SupervisorPath)
if (-not (Test-Path -LiteralPath $supervisor -PathType Leaf)) {
    throw "Supervisor script missing: $supervisor"
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("windows-operator-agent-supervisor-{0}" -f [Guid]::NewGuid().ToString("N"))
$counterPath = Join-Path $testRoot "counter.txt"
$childPath = Join-Path $testRoot "child.ps1"
$logRoot = Join-Path $testRoot "logs"

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    Set-Content -LiteralPath $counterPath -Value "0" -Encoding ASCII
    @"
`$count = [int](Get-Content -LiteralPath '$counterPath' -Raw)
`$count++
Set-Content -LiteralPath '$counterPath' -Value `$count -Encoding ASCII
if (`$count -lt 2) { exit 23 }
exit 0
"@ | Set-Content -LiteralPath $childPath -Encoding ASCII

    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $supervisor `
        -ExecutablePath "powershell.exe" `
        -ApplicationArguments $childPath `
        -LogRoot $logRoot `
        -MaximumRestartCount 2 `
        -RestartDelaySeconds 1
    if ($LASTEXITCODE -ne 0) {
        throw "Supervisor did not recover. ExitCode=$LASTEXITCODE"
    }

    $count = [int](Get-Content -LiteralPath $counterPath -Raw)
    if ($count -ne 2) {
        throw "Expected two child attempts. Actual=$count"
    }

    $log = Get-ChildItem -LiteralPath $logRoot -Filter "agent-supervisor-*.log" |
        Select-Object -First 1 |
        Get-Content -Raw
    if ($log -notmatch "child exited code=[1-9][0-9]*" -or
        $log -notmatch "restart scheduled" -or
        $log -notmatch "child exited code=0") {
        throw "Supervisor log omitted failure/recovery evidence. Log=$log"
    }

    [ordered]@{
        success = $true
        attempts = $count
        failureExitCode = "nonzero"
        recoveredExitCode = 0
    } | ConvertTo-Json -Compress
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
