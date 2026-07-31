[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$')]
    [string]$RunId,

    [string]$ExchangeRoot = (Join-Path $env:ProgramData "WindowsOperator\exchange")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runsRoot = Join-Path $ExchangeRoot "runs"
$runRoot = Join-Path $runsRoot $RunId
$artifactRoot = Join-Path $runRoot "contract-fixture"
$artifactPath = Join-Path $artifactRoot "proof.txt"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
[System.IO.File]::WriteAllText(
    $artifactPath,
    "Windows Operator v1 contract fixture: $RunId`n",
    [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    success = $true
    runId = $RunId
    relativePath = "runs/$RunId/contract-fixture/proof.txt"
    bytes = (Get-Item -LiteralPath $artifactPath).Length
} | ConvertTo-Json -Depth 4
