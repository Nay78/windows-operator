[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$OneDriveRecoveryAllowedComputer = "",

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [string]$DotnetPath = "dotnet.exe",

    [string]$ExchangeRoot = "",

    [string]$HostExchangeRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$requiredOneDriveRecoveryComputer = "WIN-UUKQS009K4J"

function Write-Step {
    param([string]$Message)
    Write-Host "[agent-autostart] $Message"
}

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Quote-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Test-DotnetSdk {
    param([string]$Candidate)

    if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
        return $false
    }

    $sdkList = & $Candidate --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or -not ($sdkList | Where-Object { $_ -match '^8\.' })) {
        return $false
    }

    $runtimes = & $Candidate --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0 -or
        -not ($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+8\.' }) -or
        -not ($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s+8\.' }) -or
        -not ($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+8\.' })) {
        return $false
    }

    $info = & $Candidate --info 2>$null
    return ($LASTEXITCODE -eq 0 -and $info -match 'Architecture:\s*x64')
}

function Resolve-Dotnet {
    param(
        [string]$Candidate,
        [string]$LocalStateRoot
    )

    $candidates = @()
    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        $candidates += (Resolve-Path -LiteralPath $Candidate).Path
    }

    $candidates += (Join-Path $LocalStateRoot "dotnet-sdk\dotnet.exe")
    $candidates += (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    $candidates += (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidatePath in $candidates | Select-Object -Unique) {
        if (Test-DotnetSdk -Candidate $candidatePath) {
            return $candidatePath
        }
    }

    throw ".NET 8 x64 SDK plus Core, ASP.NET Core, and Windows Desktop runtimes missing. Run bootstrap.ps1 first or pass -DotnetPath."
}

function Test-InteractiveDesktop {
    $computerSystem = Get-CimInstance Win32_ComputerSystem
    if (-not [string]::IsNullOrWhiteSpace($computerSystem.UserName)) {
        return $true
    }

    $explorer = Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" | Select-Object -First 1
    if (-not $explorer) {
        return $false
    }

    $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
    return ($owner.ReturnValue -eq 0 -and -not [string]::IsNullOrWhiteSpace($owner.User))
}

function Stop-ExistingAgent {
    $task = Get-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction SilentlyContinue
    if ($task) {
        Write-Step "Stopping task WindowsOperator.Agent. state=$($task.State)"
        Stop-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction SilentlyContinue

        $deadline = (Get-Date).AddSeconds(10)
        do {
            Start-Sleep -Milliseconds 250
            $task = Get-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction SilentlyContinue
        } while ($task -and $task.State -eq "Running" -and (Get-Date) -lt $deadline)
    }

    $processes = @(Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine -like "*WindowsOperator.Agent.exe*" -or
                $_.CommandLine -like "*WindowsOperator.Agent.dll*"
            )
        })

    $launcherPath = Join-Path $StateRoot "run\start-agent.ps1"
    $processes += @(Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -in @("powershell.exe", "pwsh.exe") -and
            $_.CommandLine -and
            $_.CommandLine.IndexOf($launcherPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })

    $listener = Get-NetTCPConnection -State Listen -LocalPort 43119 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) {
        Write-Step "Found port 43119 listener owner PID=$($listener.OwningProcess)."
        $listenerProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction SilentlyContinue
        if ($listenerProcess) {
            $processes += $listenerProcess
        }
    }

    foreach ($process in $processes | Sort-Object ProcessId -Unique) {
        Write-Step "Stopping Agent runtime or exact launcher process PID=$($process.ProcessId)."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "RepoRoot missing: $RepoRoot"
}

$agentProjectPath = Join-Path $RepoRoot "src\WindowsOperator.Agent\WindowsOperator.Agent.csproj"
if (-not (Test-Path -LiteralPath $agentProjectPath)) {
    throw "Agent project missing: $agentProjectPath"
}
$supervisorSourcePath = Join-Path $RepoRoot "scripts\windows\invoke-agent-supervisor.ps1"
if (-not (Test-Path -LiteralPath $supervisorSourcePath -PathType Leaf)) {
    throw "Agent supervisor missing: $supervisorSourcePath"
}

$resolvedStateRoot = (New-Item -ItemType Directory -Path $StateRoot -Force).FullName
$agentRoot = Join-Path $resolvedStateRoot "agent"
$runRoot = Join-Path $resolvedStateRoot "run"
$logRoot = Join-Path $resolvedStateRoot "logs"
$dotnetHome = Join-Path $resolvedStateRoot "dotnet-home"
$nugetPackages = Join-Path $resolvedStateRoot "nuget-packages"
@($runRoot, $logRoot, $dotnetHome, $nugetPackages) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
}

$resolvedDotnetPath = Resolve-Dotnet -Candidate $DotnetPath -LocalStateRoot $resolvedStateRoot
$recoveryEnvironment = "`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS = `$null`r`n"
if (-not [string]::IsNullOrWhiteSpace($OneDriveRecoveryAllowedComputer)) {
    if (-not [string]::Equals($OneDriveRecoveryAllowedComputer, $requiredOneDriveRecoveryComputer, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OneDrive recovery is restricted to $requiredOneDriveRecoveryComputer."
    }
    if (-not [string]::Equals($OneDriveRecoveryAllowedComputer, $env:COMPUTERNAME, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OneDrive recovery allowlist target '$OneDriveRecoveryAllowedComputer' does not match this computer '$env:COMPUTERNAME'."
    }

    $recoveryEnvironment = "`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS = $(Quote-PowerShellLiteral $OneDriveRecoveryAllowedComputer)`r`n"
}
$env:WINDOWS_OPERATOR_LOCAL_STATE_ROOT = $resolvedStateRoot
$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages
if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
    New-Item -ItemType Directory -Path $ExchangeRoot -Force | Out-Null
    $env:WINDOWS_OPERATOR_EXCHANGE_ROOT = $ExchangeRoot
}
if (-not [string]::IsNullOrWhiteSpace($HostExchangeRoot)) {
    $env:WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT = $HostExchangeRoot
}

Stop-ExistingAgent

Write-Step "Restoring WindowsOperator.Agent packages."
& $resolvedDotnetPath restore $agentProjectPath --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

if (Test-Path -LiteralPath $agentRoot) {
    Write-Step "Replacing disposable Agent runtime at $agentRoot."
    Remove-Item -LiteralPath $agentRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $agentRoot -Force | Out-Null

Write-Step "Publishing WindowsOperator.Agent Debug runtime."
& $resolvedDotnetPath publish $agentProjectPath -c Debug -o $agentRoot --no-self-contained --disable-build-servers --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$agentDll = Join-Path $agentRoot "WindowsOperator.Agent.dll"
if (-not (Test-Path -LiteralPath $agentDll -PathType Leaf)) {
    throw "Published Agent entry point missing: $agentDll"
}

$exchangeEnvironment = ""
if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
    $exchangeEnvironment += "`$env:WINDOWS_OPERATOR_EXCHANGE_ROOT = $(Quote-PowerShellLiteral $ExchangeRoot)`r`n"
}
if (-not [string]::IsNullOrWhiteSpace($HostExchangeRoot)) {
    $exchangeEnvironment += "`$env:WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT = $(Quote-PowerShellLiteral $HostExchangeRoot)`r`n"
}

$launcherPath = Join-Path $runRoot "start-agent.ps1"
$supervisorPath = Join-Path $runRoot "invoke-agent-supervisor.ps1"
Copy-Item -LiteralPath $supervisorSourcePath -Destination $supervisorPath -Force
$launcherContent = @"
`$ErrorActionPreference = "Stop"
`$env:WINDOWS_OPERATOR_LOCAL_STATE_ROOT = $(Quote-PowerShellLiteral $resolvedStateRoot)
`$env:DOTNET_CLI_HOME = $(Quote-PowerShellLiteral $dotnetHome)
`$env:NUGET_PACKAGES = $(Quote-PowerShellLiteral $nugetPackages)
$recoveryEnvironment$exchangeEnvironment`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_USER = 'Administrator'
`$logRoot = $(Quote-PowerShellLiteral $logRoot)
New-Item -ItemType Directory -Path `$logRoot -Force | Out-Null
Set-Location -LiteralPath $(Quote-PowerShellLiteral $agentRoot)
& $(Quote-PowerShellLiteral $supervisorPath) ``
    -ExecutablePath $(Quote-PowerShellLiteral $resolvedDotnetPath) ``
    -ApplicationArguments @($(Quote-PowerShellLiteral $agentDll)) ``
    -LogRoot `$logRoot ``
    -MaximumRestartCount 3 ``
    -RestartDelaySeconds 2
exit `$LASTEXITCODE
"@
$launcherContent | Set-Content -LiteralPath $launcherPath -Encoding UTF8

$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Quote-Argument $launcherPath)
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $agentRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
if ($trigger.PSObject.Properties.Name -contains "Delay") {
    $trigger.Delay = "PT30S"
}
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -StartWhenAvailable

$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName "WindowsOperator.Agent" -InputObject $task -Force | Out-Null
Disable-ScheduledTask -TaskName "WindowsOperator.Agent" | Out-Null
Write-Step "Registered disabled task WindowsOperator.Agent for $userId. Host is sole Agent lifecycle owner. AgentRoot=$agentRoot Launcher=$launcherPath"
