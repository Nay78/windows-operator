[CmdletBinding()]
param(
    [string]$DeviceCode,

    [string]$RequestPath,

    [string]$LoginUrl = "https://microsoft.com/devicelogin",

    [int]$PageLoadSeconds = 6,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [string]$TaskName = "WindowsOperator.MicrosoftDeviceLogin",

    [string]$InteractiveUser,

    [switch]$InPrivate,

    [switch]$RunInteractive,

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "microsoft-device-login: $Message"
}

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Ensure-StateDirectories {
    param([string]$Path)

    @(
        $Path,
        (Join-Path $Path "logs"),
        (Join-Path $Path "run")
    ) | ForEach-Object {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
    }
}

function Find-EdgePath {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe")
    )

    $command = Get-Command msedge.exe -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Microsoft Edge not found."
}

function Get-LoggedOnUser {
    $computerSystem = Get-CimInstance Win32_ComputerSystem
    if (-not [string]::IsNullOrWhiteSpace($computerSystem.UserName)) {
        return [string]$computerSystem.UserName
    }

    $explorer = Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" | Select-Object -First 1
    if ($explorer) {
        $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
        if ($owner.ReturnValue -eq 0 -and
            -not [string]::IsNullOrWhiteSpace($owner.User) -and
            -not [string]::IsNullOrWhiteSpace($owner.Domain)) {
            return "$($owner.Domain)\$($owner.User)"
        }
    }

    throw "No logged-in Windows desktop user found."
}

function Invoke-InteractiveDeviceLogin {
    Ensure-StateDirectories -Path $StateRoot
    $resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
    $logPath = Join-Path $resolvedStateRoot "logs\microsoft-device-login.log"

    function Write-Log {
        param([string]$Message)

        $line = "[{0}] {1}" -f (Get-Date -Format "s"), $Message
        $line | Tee-Object -FilePath $logPath -Append | Out-Host
    }

    $effectiveDeviceCode = $DeviceCode
    $effectiveLoginUrl = $LoginUrl
    $effectivePageLoadSeconds = $PageLoadSeconds
    $effectiveInPrivate = [bool]$InPrivate

    if (-not [string]::IsNullOrWhiteSpace($RequestPath)) {
        $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
        $effectiveDeviceCode = [string]$request.deviceCode
        $effectiveLoginUrl = [string]$request.loginUrl
        $effectivePageLoadSeconds = [int]$request.pageLoadSeconds
        $effectiveInPrivate = [bool]$request.inPrivate
    }

    if ([string]::IsNullOrWhiteSpace($effectiveDeviceCode)) {
        throw "DeviceCode is required."
    }

    $edgePath = Find-EdgePath
    $edgeArgs = @("--new-window")
    if ($effectiveInPrivate) {
        $edgeArgs += "--inprivate"
    }

    $edgeArgs += $effectiveLoginUrl
    try {
        if ($DryRun) {
            Write-Log "Dry run. Would start Edge: $edgePath $($edgeArgs -join ' ')"
            Write-Log "Dry run. Would paste device code length: $($effectiveDeviceCode.Length)"
            return
        }

        Write-Log "Opening Microsoft device login in Edge."
        Start-Process -FilePath $edgePath -ArgumentList $edgeArgs | Out-Null
        Start-Sleep -Seconds ([Math]::Max(1, $effectivePageLoadSeconds))

        Set-Clipboard -Value $effectiveDeviceCode
        $shell = New-Object -ComObject WScript.Shell
        $activated = $false
        foreach ($title in @("Microsoft Edge", "Sign in to your account", "Enter code")) {
            if ($shell.AppActivate($title)) {
                $activated = $true
                break
            }
        }

        if (-not $activated) {
            Write-Log "Edge window not activated by title. Sending keys to current foreground window."
        }

        Start-Sleep -Milliseconds 500
        $shell.SendKeys("^v")
        Start-Sleep -Milliseconds 500
        $shell.SendKeys("{ENTER}")
        Write-Log "Device code submitted. Complete Microsoft account and MFA prompts in Edge."
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($RequestPath)) {
            Remove-Item -LiteralPath $RequestPath -Force -ErrorAction SilentlyContinue
        }

        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function Start-InteractiveTask {
    if ([string]::IsNullOrWhiteSpace($DeviceCode)) {
        throw "DeviceCode is required."
    }

    $targetUser = if ([string]::IsNullOrWhiteSpace($InteractiveUser)) {
        Get-LoggedOnUser
    }
    else {
        $InteractiveUser
    }

    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath) -or -not (Test-Path -LiteralPath $scriptPath)) {
        throw "Cannot resolve script path for scheduled task."
    }

    Ensure-StateDirectories -Path $StateRoot
    $resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
    $runRoot = Join-Path $resolvedStateRoot "run"
    $taskScriptPath = Join-Path $runRoot "login-microsoft-device-code.ps1"
    $requestPath = Join-Path $runRoot ("microsoft-device-login-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-Argument $taskScriptPath),
        "-RunInteractive",
        "-RequestPath", (Quote-Argument $requestPath),
        "-StateRoot", (Quote-Argument $resolvedStateRoot),
        "-TaskName", (Quote-Argument $TaskName)
    )

    if ($DryRun) {
        $arguments += "-DryRun"
        Write-Step "Dry run. Would register task '$TaskName' for $targetUser."
        Write-Step "Action: powershell.exe $($arguments -join ' ')"
        return
    }

    Copy-Item -LiteralPath $scriptPath -Destination $taskScriptPath -Force
    [ordered]@{
        deviceCode = $DeviceCode
        loginUrl = $LoginUrl
        pageLoadSeconds = $PageLoadSeconds
        inPrivate = [bool]$InPrivate
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $requestPath -Encoding UTF8

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument ($arguments -join " ") -WorkingDirectory $runRoot
    $principal = New-ScheduledTaskPrincipal -UserId $targetUser -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -MultipleInstances Parallel `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
    $task = New-ScheduledTask -Action $action -Principal $principal -Settings $settings

    Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    Write-Step "Started interactive Edge login task '$TaskName' for $targetUser."
}

if ($RunInteractive) {
    Invoke-InteractiveDeviceLogin
}
else {
    Start-InteractiveTask
}
