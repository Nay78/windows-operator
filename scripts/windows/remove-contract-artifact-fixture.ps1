[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$')]
    [string]$RunId,

    [string]$ExchangeRoot = (Join-Path $env:ProgramData "WindowsOperator\exchange")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runRoot = Join-Path (Join-Path $ExchangeRoot "runs") $RunId
if (Test-Path -LiteralPath $runRoot) {
    Remove-Item -LiteralPath $runRoot -Recurse -Force
}

[pscustomobject]@{
    success = -not (Test-Path -LiteralPath $runRoot)
    runId = $RunId
    removedPath = $runRoot
} | ConvertTo-Json -Depth 4
