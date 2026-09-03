[CmdletBinding()]
param(
    [string]$RootId = "geosupport",
    [string]$RelativePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$body = @{ rootId = $RootId; relativePath = $RelativePath } | ConvertTo-Json -Compress
$entries = $null
$errorMessage = $null
$agentEntries = $null
$agentErrorMessage = $null
try {
    $entries = Invoke-RestMethod `
        -Uri "http://127.0.0.1:43117/v1/files/onedrive/list" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body
}
catch {
    $errorMessage = $_.Exception.Message
}
try {
    $agentEntries = Invoke-RestMethod `
        -Uri "http://127.0.0.1:43119/v1/files/onedrive/list" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body
}
catch {
    $agentErrorMessage = $_.Exception.Message
}

$openapi = Invoke-RestMethod -Uri "http://127.0.0.1:43117/openapi.json" -Method Get
$config = Invoke-RestMethod -Uri "http://127.0.0.1:43117/v1/files/onedrive/config" -Method Get
$hostSource = Join-Path $env:ProgramData "WindowsOperator\host\WindowsOperator.Host.dll"
$agentEndpointSource = Join-Path "C:\src\windows-operator" "src\WindowsOperator.Agent\Api\OperatorEndpoints.cs"

[pscustomobject]@{
    rootId = $RootId
    relativePath = $RelativePath
    entries = @($entries)
    error = $errorMessage
    agentEntries = @($agentEntries)
    agentError = $agentErrorMessage
    liveOpenApiHasList = $openapi.paths.PSObject.Properties.Name -contains "/v1/files/onedrive/list"
    liveOpenApiHasDownload = $openapi.paths.PSObject.Properties.Name -contains "/v1/files/onedrive/download"
    config = $config
    configuredRootExists = Test-Path -LiteralPath "C:\Users\Administrator\Geosupport S.A" -PathType Container
    candidateDirectories = @(
        Get-ChildItem -LiteralPath "C:\Users" -Directory -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                Get-ChildItem -LiteralPath $_.FullName -Directory -Force -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like "OneDrive*" -or $_.Name -like "Geosupport*" } |
                    Select-Object FullName, Name
            }
    )
    oneDriveChildren = @(
        Get-ChildItem -LiteralPath "C:\Users\Alejg\OneDrive - Grupo Minero Antofagasta Minerals" -Directory -Force -ErrorAction SilentlyContinue |
            Select-Object FullName, Name
    )
    essChildren = @(
        Get-ChildItem -LiteralPath "C:\Users\Alejg\OneDrive - Grupo Minero Antofagasta Minerals\ESS" -Directory -Force -ErrorAction SilentlyContinue |
            Select-Object FullName, Name
    )
    sem36Children = @(
        Get-ChildItem -LiteralPath "C:\Users\Alejg\OneDrive - Grupo Minero Antofagasta Minerals\ESS\SEM36" -Directory -Force -ErrorAction SilentlyContinue |
            Select-Object FullName, Name
    )
    sem36Files = @(
        Get-ChildItem -LiteralPath "C:\Users\Alejg\OneDrive - Grupo Minero Antofagasta Minerals\ESS\SEM36" -File -Force -ErrorAction SilentlyContinue |
            Select-Object FullName, Name, Length
    )
    syncedAgentSourceHasList = (Get-Content -LiteralPath $agentEndpointSource -Raw) -match "/files/onedrive/list"
    syncedAgentSourceHasDownload = (Get-Content -LiteralPath $agentEndpointSource -Raw) -match "/files/onedrive/download"
    agentAssembly = Get-Item -LiteralPath (Join-Path $env:LOCALAPPDATA "WindowsOperator\agent\WindowsOperator.Agent.dll") | Select-Object FullName, Length, LastWriteTimeUtc
    hostAssembly = Get-Item -LiteralPath $hostSource | Select-Object FullName, Length, LastWriteTimeUtc
} | ConvertTo-Json -Depth 6
