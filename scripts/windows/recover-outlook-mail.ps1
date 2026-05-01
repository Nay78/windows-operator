[CmdletBinding()]
param(
    [ValidateSet("Basic", "Profile", "Force")]
    [string]$Mode = "Basic"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$actions = New-Object System.Collections.Generic.List[string]
$errors = New-Object System.Collections.Generic.List[string]

function Has-MainWindow {
    param([System.Diagnostics.Process]$Process)
    try {
        $Process.Refresh()
        return $Process.MainWindowHandle -ne [IntPtr]::Zero
    }
    catch {
        return $false
    }
}

function Stop-Outlook {
    param([bool]$IncludeVisible)

    foreach ($process in @(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue)) {
        $visible = Has-MainWindow -Process $process
        if ($visible -and -not $IncludeVisible) {
            continue
        }

        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $kind = if ($visible) { "killed_visible_outlook" } else { "killed_headless_outlook" }
            $actions.Add("$kind`:$($process.Id)")
        }
        catch {
            $errors.Add("failed_to_kill_outlook:$($process.Id):$($_.Exception.Message)")
        }
    }
}

function Clear-OutlookTempFiles {
    if (@(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue).Count -gt 0) {
        return
    }

    $outlookDataPath = Join-Path $env:LOCALAPPDATA "Microsoft\Outlook"
    if (-not (Test-Path -LiteralPath $outlookDataPath -PathType Container)) {
        return
    }

    foreach ($path in @(Get-ChildItem -LiteralPath $outlookDataPath -Filter "~*.tmp" -File -ErrorAction SilentlyContinue)) {
        try {
            Remove-Item -LiteralPath $path.FullName -Force -ErrorAction Stop
            $actions.Add("deleted_temp:$($path.Name)")
        }
        catch {
            $errors.Add("failed_to_delete_temp:$($path.FullName):$($_.Exception.Message)")
        }
    }
}

function Invoke-OutlookSwitch {
    param([string]$Argument)

    try {
        Start-Process -FilePath "outlook.exe" -ArgumentList $Argument | Out-Null
        $actions.Add("started_outlook:$Argument")
        Start-Sleep -Seconds 8
    }
    catch {
        $errors.Add("failed_outlook_switch:${Argument}:$($_.Exception.Message)")
    }
}

function Close-VisibleOutlook {
    foreach ($process in @(Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue)) {
        if (-not (Has-MainWindow -Process $process)) {
            continue
        }

        try {
            [void]$process.CloseMainWindow()
            $actions.Add("close_requested:$($process.Id)")
            [void]$process.WaitForExit(10000)
        }
        catch {
            $errors.Add("failed_to_close_outlook:$($process.Id):$($_.Exception.Message)")
        }
    }
}

$normalized = $Mode.ToLowerInvariant()
if ($normalized -eq "force") {
    Stop-Outlook -IncludeVisible $true
}
else {
    Stop-Outlook -IncludeVisible $false
}

Clear-OutlookTempFiles

if ($normalized -in @("profile", "force")) {
    $visibleCount = @(
        Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue |
            Where-Object { Has-MainWindow -Process $_ }
    ).Count
    if ($visibleCount -gt 0 -and $normalized -ne "force") {
        $errors.Add("visible_outlook_open")
    }
    else {
        Invoke-OutlookSwitch -Argument "/cleanreminders"
        Invoke-OutlookSwitch -Argument "/resetnavpane"
        Close-VisibleOutlook
        Stop-Outlook -IncludeVisible ($normalized -eq "force")
        Clear-OutlookTempFiles
    }
}

$visibleFinal = @(
    Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue |
        Where-Object { Has-MainWindow -Process $_ }
).Count
$headlessFinal = @(
    Get-Process -Name OUTLOOK -ErrorAction SilentlyContinue |
        Where-Object { -not (Has-MainWindow -Process $_) }
).Count

[ordered]@{
    mode = $normalized
    success = ($errors.Count -eq 0)
    actions = @($actions)
    errors = @($errors)
    visibleOutlookCount = $visibleFinal
    headlessOutlookCount = $headlessFinal
    completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Depth 6

if ($errors.Count -gt 0) {
    exit 1
}
