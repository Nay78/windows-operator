[CmdletBinding()]
param(
    [string]$HostBaseUrl = "http://127.0.0.1:43117",

    [string]$AgentBaseUrl = "http://127.0.0.1:43119",

    [string]$SessionId = ("v1-contract-raw-js-{0}" -f (Get-Date -Format "yyyyMMddHHmmss"))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Wait-OperatorHealth {
    param(
        [string]$BaseUrl,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $health = Invoke-RestMethod -Method Get -Uri "$BaseUrl/v1/health" -TimeoutSec 3
            if ($health.status -eq "ok") {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    throw "Operator health did not recover at $BaseUrl within $TimeoutSeconds seconds."
}

function Restart-AgentTask {
    $task = Get-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction Stop
    Write-Host "Restarting WindowsOperator.Agent. state=$($task.State) principal=$($task.Principal.UserId)"
    Stop-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $listener = Get-NetTCPConnection -State Listen -LocalPort 43119 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction Stop
        if (-not $process.CommandLine -or $process.CommandLine -notmatch 'WindowsOperator\.Agent') {
            throw "Refusing to stop unexpected port 43119 owner PID=$($listener.OwningProcess)."
        }
        Write-Host "Stopping verified Agent listener PID=$($listener.OwningProcess)."
        Stop-Process -Id $listener.OwningProcess -Force -ErrorAction Stop
        Wait-Process -Id $listener.OwningProcess -Timeout 10 -ErrorAction SilentlyContinue
    }

    Start-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction Stop
    Wait-OperatorHealth -BaseUrl $AgentBaseUrl
}

function Invoke-JsonPost {
    param(
        [string]$Uri,
        [object]$Body
    )

    $json = $Body | ConvertTo-Json -Depth 12 -Compress
    try {
        return Invoke-RestMethod -Method Post -Uri $Uri -Body $json -ContentType "application/json" -TimeoutSec 60
    }
    catch {
        $response = $_.Exception.Response
        if (-not $response) {
            throw
        }
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        try {
            $responseBody = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        throw "POST $Uri returned HTTP $([int]$response.StatusCode): $responseBody"
    }
}

function Resolve-AgentLauncher {
    $task = Get-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction Stop
    $arguments = [string]$task.Actions[0].Arguments
    $match = [regex]::Match($arguments, '(?i)(?:^|\s)-File\s+(?:"([^"]+)"|(\S+))')
    if (-not $match.Success) {
        throw "WindowsOperator.Agent action does not contain a -File launcher."
    }

    $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "WindowsOperator.Agent launcher missing: $path"
    }
    if ($path -notmatch '(?i)\\WindowsOperator\\run\\start-agent\.ps1$') {
        throw "Refusing unexpected Agent launcher path: $path"
    }
    return (Resolve-Path -LiteralPath $path).Path
}

$launcherPath = Resolve-AgentLauncher
Write-Host "Temporary machine-local target: $launcherPath"
$originalBytes = [System.IO.File]::ReadAllBytes($launcherPath)
$sessionStarted = $false
$launcherChanged = $false
$successResult = $null
$cleanupResult = $null
$disabledCode = $null

try {
    $launcherText = [System.IO.File]::ReadAllText($launcherPath)
    if ($launcherText -match 'WINDOWS_OPERATOR_DEV_(?:AUTOMATION|RAW_JS)') {
        throw "Agent launcher already contains a development-automation override."
    }

    $gateLines = @"
`$env:WINDOWS_OPERATOR_DEV_AUTOMATION = "1"
`$env:WINDOWS_OPERATOR_DEV_RAW_JS = "1"
"@
    $updatedText = "$gateLines`r`n$launcherText"

    [System.IO.File]::WriteAllText($launcherPath, $updatedText, [System.Text.UTF8Encoding]::new($false))
    $launcherChanged = $true
    Restart-AgentTask

    $start = Invoke-JsonPost -Uri "$HostBaseUrl/v1/browser/edge/session/start" -Body @{
        sessionId = $SessionId
        startUrl = "https://example.com"
        profileMode = "temp"
        pageLoadSeconds = 5
    }
    if (-not $start.success) {
        throw "Owned Edge session did not start."
    }
    $sessionStarted = $true

    $successResult = Invoke-JsonPost `
        -Uri "$HostBaseUrl/v1/dev/browser/edge/sessions/$SessionId/eval" `
        -Body @{
            source = "document.title"
            allowUnsafeRawJs = $true
            timeoutSeconds = 5
        }
    if (-not $successResult.success -or $successResult.status -ne "succeeded") {
        throw "Raw-JavaScript contract operation did not succeed."
    }
}
finally {
    if ($sessionStarted) {
        try {
            $cleanupResult = Invoke-JsonPost `
                -Uri "$HostBaseUrl/v1/browser/edge/session/$SessionId/cleanup" `
                -Body @{}
        }
        catch {
            Write-Warning "Owned Edge cleanup failed: $($_.Exception.Message)"
        }
    }

    if ($launcherChanged) {
        [System.IO.File]::WriteAllBytes($launcherPath, $originalBytes)
        Restart-AgentTask
    }
}

if (-not $cleanupResult -or -not $cleanupResult.success) {
    throw "Owned Edge session cleanup was not confirmed."
}

try {
    $disabledJson = @{
        source = "document.title"
        allowUnsafeRawJs = $true
        timeoutSeconds = 5
    } | ConvertTo-Json -Compress
    Invoke-RestMethod `
        -Method Post `
        -Uri "$HostBaseUrl/v1/dev/browser/edge/sessions/$SessionId/eval" `
        -Body $disabledJson `
        -ContentType "application/json" `
        -TimeoutSec 60 | Out-Null
    throw "Raw JavaScript remained enabled after launcher restoration."
}
catch {
    $response = $_.Exception.Response
    if (-not $response) {
        throw
    }
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    try {
        $errorBody = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }
    $disabledCode = $errorBody.code
    if ([int]$response.StatusCode -ne 422 -or
        $disabledCode -notin @("dev_automation_disabled", "dev_raw_js_disabled")) {
        throw "Disabled-state proof returned HTTP $([int]$response.StatusCode) code=$disabledCode."
    }
}

[pscustomobject]@{
    success = $true
    operationId = "evaluateEdgeBrowserDevScript"
    endpoint = "POST /v1/dev/browser/edge/sessions/{sessionId}/eval"
    sessionId = $SessionId
    resultStatus = $successResult.status
    resultText = $successResult.resultText
    devScriptResult = $successResult
    cleanupSuccess = $cleanupResult.success
    launcherRestored = $true
    disabledStatus = 422
    disabledCode = $disabledCode
    observedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Depth 6
