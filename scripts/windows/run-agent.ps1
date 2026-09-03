[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [string]$ExchangeRoot = "",

    [string]$HostExchangeRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

    $hasCoreRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+8\.' }
    $hasAspNetRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s+8\.' }
    $hasWindowsDesktopRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+8\.' }
    if (-not $hasCoreRuntime -or -not $hasAspNetRuntime -or -not $hasWindowsDesktopRuntime) {
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
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-DotnetSdk -DotnetPath $candidate) {
            return $candidate
        }
    }

    throw ".NET 8 x64 SDK plus Core, ASP.NET Core, and Windows Desktop runtimes missing. Run bootstrap.ps1 first."
}

function Set-LocalStateEnvironment {
    param(
        [string]$Path,
        [string]$DotnetPath,
        [string]$ExchangeRoot,
        [string]$HostExchangeRoot
    )

    $env:WINDOWS_OPERATOR_LOCAL_STATE_ROOT = $Path
    $env:DOTNET_CLI_HOME = (Join-Path $Path "dotnet-home")
    $env:NUGET_PACKAGES = (Join-Path $Path "nuget-packages")
    if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
        New-Item -ItemType Directory -Path $ExchangeRoot -Force | Out-Null
        $env:WINDOWS_OPERATOR_EXCHANGE_ROOT = $ExchangeRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($HostExchangeRoot)) {
        $env:WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT = $HostExchangeRoot
    }

    $dotnetDir = Split-Path -Parent $DotnetPath
    if (-not $env:Path.Split(';').Contains($dotnetDir)) {
        $env:Path = "$dotnetDir;$env:Path"
    }
}

function Wait-ForRepoRoot {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $agentProjectPath = Join-Path $Path "src\\WindowsOperator.Agent\\WindowsOperator.Agent.csproj"
            if (Test-Path -LiteralPath $agentProjectPath) {
                return (Resolve-Path -LiteralPath $Path).Path
            }
        }

        Start-Sleep -Seconds 3
    }

    throw "RepoRoot unavailable after $TimeoutSeconds seconds: $Path"
}

Ensure-StateDirectories -Path $StateRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
$logRoot = Join-Path $resolvedStateRoot "logs"
$logPath = Join-Path $logRoot ("agent-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-Log {
    param([string]$Message)

    $line = "[{0}] {1}" -f (Get-Date -Format "s"), $Message
    $line | Tee-Object -FilePath $logPath -Append | Out-Host
}

try {
    Write-Log "Waiting for RepoRoot."
    $resolvedRepoRoot = Wait-ForRepoRoot -Path $RepoRoot -TimeoutSeconds 120

    $dotnetPath = Find-DotnetPath -Path $resolvedStateRoot
    Set-LocalStateEnvironment -Path $resolvedStateRoot -DotnetPath $dotnetPath -ExchangeRoot $ExchangeRoot -HostExchangeRoot $HostExchangeRoot

    $agentProjectPath = Join-Path $resolvedRepoRoot "src\\WindowsOperator.Agent\\WindowsOperator.Agent.csproj"
    $runDir = Join-Path $resolvedStateRoot "run"

    Write-Log "Building Agent project into local artifacts."
    & $dotnetPath build $agentProjectPath 2>&1 | Tee-Object -FilePath $logPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }

    Write-Log "Starting agent."
    Push-Location $runDir
    try {
        & $dotnetPath run --project $agentProjectPath --no-build --no-launch-profile 2>&1 | Tee-Object -FilePath $logPath -Append
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet run failed."
        }
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Log $_.Exception.Message
    throw
}
