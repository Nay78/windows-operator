[CmdletBinding()]
param(
    [switch]$ClearConfiguration,

    [string]$HostBaseUrl = "http://127.0.0.1:43117",

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$uri = "$($HostBaseUrl.TrimEnd('/'))/v1/files/onedrive/runtime/recover"
$payload = [ordered]@{ clearConfiguration = [bool]$ClearConfiguration }
$json = $payload | ConvertTo-Json -Compress

if (-not $ClearConfiguration) {
    Write-Host "Starting or confirming OneDrive in the dynamically resolved Administrator RDP session without changing authentication configuration."
}
else {
    throw "ClearConfiguration is not supported. OneDrive authentication configuration is operator-controlled."
}

if ($DryRun) {
    [pscustomobject]@{
        method = "POST"
        uri = $uri
        body = $payload
        wouldChange = $true
    } | ConvertTo-Json -Depth 5
    exit 0
}

Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body $json
