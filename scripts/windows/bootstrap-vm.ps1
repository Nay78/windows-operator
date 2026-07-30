[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bootstrapPath = Join-Path $PSScriptRoot "bootstrap.ps1"
$codexBootstrapPath = Join-Path $PSScriptRoot "bootstrap-codex.ps1"

if (-not (Test-Path -LiteralPath $bootstrapPath)) {
    throw "Bootstrap script missing: $bootstrapPath"
}

if (-not (Test-Path -LiteralPath $codexBootstrapPath)) {
    throw "Codex bootstrap script missing: $codexBootstrapPath"
}

& $bootstrapPath -RepoRoot $repoRoot -EnableAutostart
if (-not $?) {
    throw "Windows Operator VM bootstrap failed."
}

& $codexBootstrapPath -EnableAutostart
if (-not $?) {
    throw "Codex VM bootstrap failed."
}
