[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [string]$RequiredText = "",

    [int]$TimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$statusCode = $null
$contentLength = $null
$contentType = $null
$containsRequiredText = $true
$errorMessage = $null

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec $TimeoutSeconds
    $statusCode = [int]$response.StatusCode
    $contentLength = [int64]$response.RawContentLength
    $contentType = [string]$response.Headers["Content-Type"]
    if (-not [string]::IsNullOrWhiteSpace($RequiredText)) {
        $containsRequiredText = ([string]$response.Content).Contains($RequiredText)
    }
}
catch {
    $errorMessage = $_.Exception.Message
}

$success = $statusCode -ge 200 -and $statusCode -lt 300 -and $containsRequiredText

[ordered]@{
    success = $success
    url = $Url
    statusCode = $statusCode
    contentLength = $contentLength
    contentType = $contentType
    containsRequiredText = $containsRequiredText
    error = $errorMessage
} | ConvertTo-Json -Compress

if (-not $success) {
    exit 1
}
