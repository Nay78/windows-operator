[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string[]]$ApplicationArguments,

    [Parameter(Mandatory = $true)]
    [string]$LogRoot,

    [ValidateRange(0, 10)]
    [int]$MaximumRestartCount = 3,

    [ValidateRange(1, 30)]
    [int]$RestartDelaySeconds = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
$supervisorLog = Join-Path $LogRoot ("agent-supervisor-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$maximumAttempts = $MaximumRestartCount + 1
$lastExitCode = 1

for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
    $startedAt = Get-Date
    $stamp = $startedAt.ToString("yyyyMMdd-HHmmss")
    $outputLog = Join-Path $LogRoot ("agent-{0}.log" -f $stamp)
    $errorLog = Join-Path $LogRoot ("agent-error-{0}.log" -f $stamp)
    Add-Content -LiteralPath $supervisorLog -Value (
        "[{0}] starting child attempt={1}/{2}" -f $startedAt.ToString("o"), $attempt, $maximumAttempts)

    # Windows PowerShell surfaces native stderr as non-terminating error records.
    # Keep those in the child error log without terminating supervision.
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $ExecutablePath @ApplicationArguments 1>> $outputLog 2>> $errorLog
        $lastExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $exitedAt = Get-Date
    Add-Content -LiteralPath $supervisorLog -Value (
        "[{0}] child exited code={1} runtimeSeconds={2}" -f `
            $exitedAt.ToString("o"),
            $lastExitCode,
            [Math]::Round(($exitedAt - $startedAt).TotalSeconds, 3))

    if ($lastExitCode -eq 0) {
        exit 0
    }

    if ($attempt -lt $maximumAttempts) {
        Add-Content -LiteralPath $supervisorLog -Value (
            "[{0}] restart scheduled delaySeconds={1}" -f (Get-Date).ToString("o"), $RestartDelaySeconds)
        Start-Sleep -Seconds $RestartDelaySeconds
    }
}

Add-Content -LiteralPath $supervisorLog -Value (
    "[{0}] restart budget exhausted; task scheduler restart policy may retry" -f (Get-Date).ToString("o"))
exit $lastExitCode
