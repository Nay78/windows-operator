[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [ValidateSet("None", "OperatorSafe")]
    [string]$ProvisionProfile = "None",

    [string]$OneDriveRecoveryAllowedComputer = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$bootstrapPath = Join-Path $RepoRoot "scripts\windows\bootstrap.ps1"
if (-not (Test-Path -LiteralPath $bootstrapPath -PathType Leaf)) {
    throw "Bootstrap script missing: $bootstrapPath"
}

$bootstrapArguments = @{
    RepoRoot = $RepoRoot
    ProvisionProfile = $ProvisionProfile
}
if (-not [string]::IsNullOrWhiteSpace($OneDriveRecoveryAllowedComputer)) {
    $bootstrapArguments.OneDriveRecoveryAllowedComputer = $OneDriveRecoveryAllowedComputer
}

& $bootstrapPath @bootstrapArguments
if ($LASTEXITCODE -ne 0) {
    throw "Windows Operator bootstrap failed with exit code $LASTEXITCODE."
}
