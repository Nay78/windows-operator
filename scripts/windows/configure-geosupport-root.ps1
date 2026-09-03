[CmdletBinding()]
param(
    [string]$Path = "C:\Users\Alejg\Geosupport S.A"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    throw "Geosupport root does not exist: $Path"
}

$uri = "http://127.0.0.1:43117/v1/files/onedrive/config"
$current = Invoke-RestMethod -Uri $uri -Method Get
$current.config.roots.geosupport.path = $Path
$payload = @{ config = $current.config } | ConvertTo-Json -Depth 8 -Compress
$updated = Invoke-RestMethod `
    -Uri $uri `
    -Method Put `
    -Headers @{ "If-Match" = [string]$current.eTag } `
    -ContentType "application/json" `
    -Body $payload

[pscustomobject]@{
    rootId = "geosupport"
    path = $updated.config.roots.geosupport.path
    etag = $updated.eTag
    actions = @($updated.actions)
    warnings = @($updated.warnings)
} | ConvertTo-Json -Depth 6
