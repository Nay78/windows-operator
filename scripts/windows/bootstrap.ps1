[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [string]$ExchangeRoot = "",

    [string]$HostExchangeRoot = "",

    [ValidateSet("None", "OperatorSafe")]
    [string]$ProvisionProfile = "OperatorSafe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[bootstrap] $Message"
}

function Assert-RepoRoot {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "RepoRoot missing: $Path"
    }

    $solutionPath = Join-Path $Path "WindowsOperator.sln"
    if (-not (Test-Path -LiteralPath $solutionPath)) {
        throw "WindowsOperator.sln missing under RepoRoot: $solutionPath"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Ensure-StateDirectories {
    param([string]$Path)

    @(
        $Path,
        (Join-Path $Path "dotnet-home"),
        (Join-Path $Path "nuget-packages"),
        (Join-Path $Path "artifacts"),
        (Join-Path $Path "artifacts\\obj"),
        (Join-Path $Path "artifacts\\bin"),
        (Join-Path $Path "logs"),
        (Join-Path $Path "run")
    ) | ForEach-Object {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
    }
}

function Test-DotnetSdk {
    param([string]$DotnetPath)

    if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
        return $false
    }

    $sdkList = & $DotnetPath --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $hasSdk8 = $sdkList | Where-Object { $_ -match '^8\.' }
    if (-not $hasSdk8) {
        return $false
    }

    $info = & $DotnetPath --info 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $runtimes = & $DotnetPath --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $hasCoreRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s' }
    $hasAspNetRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s' }
    if (-not $hasCoreRuntime -or -not $hasAspNetRuntime) {
        return $false
    }

    return ($info -match 'Architecture:\s*x64')
}

function Find-DotnetPath {
    param([string]$Path)

    $candidates = @(
        (Join-Path $Path "dotnet-sdk\\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\\dotnet.exe")
    )

    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Path
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-DotnetSdk -DotnetPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Install-DotnetWithWinget {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        return $false
    }

    Write-Step ".NET 8 SDK x64 missing. Installing with winget."
    & $winget.Source install `
        --id Microsoft.DotNet.SDK.8 `
        --exact `
        --architecture x64 `
        --accept-package-agreements `
        --accept-source-agreements `
        --disable-interactivity

    return ($LASTEXITCODE -eq 0)
}

function Install-DotnetWithMicrosoftScript {
    param([string]$Path)

    Write-Step "winget unavailable or failed. Falling back to Microsoft installer."
    $installerPath = Join-Path $Path "run\\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerPath
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath -Channel 8.0 -Architecture x64 -InstallDir (Join-Path $Path "dotnet-sdk") | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw ".NET installer failed."
    }
}

function Ensure-DotnetSdk {
    param([string]$Path)

    $dotnetPath = Find-DotnetPath -Path $Path
    if ($dotnetPath) {
        return $dotnetPath
    }

    $wingetInstalled = Install-DotnetWithWinget
    $dotnetPath = Find-DotnetPath -Path $Path
    if ($wingetInstalled -and $dotnetPath) {
        return $dotnetPath
    }

    Install-DotnetWithMicrosoftScript -Path $Path
    $dotnetPath = Find-DotnetPath -Path $Path
    if ($dotnetPath) {
        return $dotnetPath
    }

    throw ".NET 8 SDK x64 still missing after install attempts."
}

function Set-LocalStateEnvironment {
    param(
        [string]$Path,
        [string]$DotnetPath
    )

    $env:WINDOWS_OPERATOR_LOCAL_STATE_ROOT = $Path
    $env:DOTNET_CLI_HOME = (Join-Path $Path "dotnet-home")
    $env:NUGET_PACKAGES = (Join-Path $Path "nuget-packages")

    if (-not (Test-Path -LiteralPath $DotnetPath)) {
        throw "Resolved dotnet path missing: $DotnetPath"
    }

    $dotnetDir = Split-Path -Parent $DotnetPath
    if (-not $env:Path.Split(';').Contains($dotnetDir)) {
        $env:Path = "$dotnetDir;$env:Path"
    }
}

function Ensure-AlwaysOnPowerPolicy {
    $powercfg = Get-Command powercfg.exe -ErrorAction SilentlyContinue
    if (-not $powercfg) {
        Write-Step "powercfg.exe unavailable. Skipping power policy guard."
        return
    }

    Write-Step "Disabling idle sleep and hibernate."
    $commands = @(
        @("/hibernate", "off"),
        @("/change", "standby-timeout-ac", "0"),
        @("/change", "hibernate-timeout-ac", "0"),
        @("/change", "disk-timeout-ac", "0"),
        @("/change", "monitor-timeout-ac", "0")
    )

    foreach ($arguments in $commands) {
        & $powercfg.Source @arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Step "powercfg $($arguments -join ' ') exited with code $LASTEXITCODE."
        }
    }
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

function Wait-OperatorRuntime {
    param(
        [string]$HealthUrl = "http://127.0.0.1:43117/v1/health",
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastObservation = "Host health endpoint not reachable."

    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 5
            $status = [string]$health.status
            $runtimeMode = [string]$health.runtimeMode
            $interactiveDesktop = Test-InteractiveDesktop

            if ($runtimeMode -ne "headless-host") {
                $lastObservation = "Unexpected runtimeMode=$runtimeMode status=$status."
            }
            elseif ($interactiveDesktop -and $status -eq "ok") {
                Write-Step "Runtime healthy. status=ok runtimeMode=headless-host desktop=interactive"
                return
            }
            elseif (-not $interactiveDesktop) {
                $agentTask = Get-ScheduledTask -TaskName "WindowsOperator.Agent" -ErrorAction SilentlyContinue
                if ($agentTask -and $status -in @("ok", "degraded")) {
                    Write-Step "Runtime healthy. status=$status runtimeMode=headless-host desktop=absent agentTask=$($agentTask.State)"
                    return
                }

                $taskState = if ($agentTask) { [string]$agentTask.State } else { "missing" }
                $lastObservation = "status=$status runtimeMode=$runtimeMode desktop=absent agentTask=$taskState."
            }
            else {
                $lastObservation = "status=$status runtimeMode=$runtimeMode desktop=interactive; waiting for Agent."
            }
        }
        catch {
            $lastObservation = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    }

    throw "Runtime health gate failed after $TimeoutSeconds seconds. Last observation: $lastObservation"
}

$resolvedRepoRoot = Assert-RepoRoot -Path $RepoRoot
Ensure-StateDirectories -Path $StateRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path

if ($ProvisionProfile -eq "OperatorSafe") {
    $profileScript = Join-Path $PSScriptRoot "provision-operator-safe.ps1"
    if (-not (Test-Path -LiteralPath $profileScript -PathType Leaf)) {
        throw "Operator-safe provision profile missing: $profileScript"
    }

    Write-Step "Applying reversible operator-safe desktop profile."
    & $profileScript `
        -Action Apply `
        -StateRoot (Join-Path $resolvedStateRoot "provisioning\operator-safe") `
        -Confirm:$false | Out-Host
}

Ensure-AlwaysOnPowerPolicy

$dotnetPath = Ensure-DotnetSdk -Path $resolvedStateRoot
Set-LocalStateEnvironment -Path $resolvedStateRoot -DotnetPath $dotnetPath

Write-Step "Registering Host runtime and autostart task."
$hostAutostartArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "register-host-autostart.ps1"),
    "-RepoRoot", $resolvedRepoRoot,
    "-DotnetPath", $dotnetPath
)
if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
    $hostAutostartArguments += @("-ExchangeRoot", $ExchangeRoot)
}
if (-not [string]::IsNullOrWhiteSpace($HostExchangeRoot)) {
    $hostAutostartArguments += @("-HostExchangeRoot", $HostExchangeRoot)
}

& powershell.exe @hostAutostartArguments
if ($LASTEXITCODE -ne 0) {
    throw "Host runtime registration failed."
}

Write-Step "Registering Agent runtime and logon task."
$agentAutostartArguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "register-agent-autostart.ps1"),
    "-RepoRoot", $resolvedRepoRoot,
    "-StateRoot", $resolvedStateRoot,
    "-DotnetPath", $dotnetPath
)
if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
    $agentAutostartArguments += @("-ExchangeRoot", $ExchangeRoot)
}
if (-not [string]::IsNullOrWhiteSpace($HostExchangeRoot)) {
    $agentAutostartArguments += @("-HostExchangeRoot", $HostExchangeRoot)
}

& powershell.exe @agentAutostartArguments
if ($LASTEXITCODE -ne 0) {
    throw "Agent runtime registration failed."
}

Write-Step "Waiting for Host and Agent runtime health."
Wait-OperatorRuntime

Write-Step "Bootstrap complete. RepoRoot=$resolvedRepoRoot StateRoot=$resolvedStateRoot"
