[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator"),

    [switch]$EnableAutostart
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

    if (-not (Test-Path -LiteralPath $DotnetPath)) {
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

$resolvedRepoRoot = Assert-RepoRoot -Path $RepoRoot
Ensure-StateDirectories -Path $StateRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path

Ensure-AlwaysOnPowerPolicy

$dotnetPath = Ensure-DotnetSdk -Path $resolvedStateRoot
Set-LocalStateEnvironment -Path $resolvedStateRoot -DotnetPath $dotnetPath

Push-Location $resolvedRepoRoot
try {
    Write-Step "Restoring solution."
    & $dotnetPath restore ".\\WindowsOperator.sln"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    Write-Step "Building solution."
    & $dotnetPath build ".\\WindowsOperator.sln"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }

    Write-Step "Running tests."
    & $dotnetPath test ".\\WindowsOperator.sln"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed."
    }
}
finally {
    Pop-Location
}

if ($EnableAutostart) {
    Write-Step "Registering host autostart task."
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "register-host-autostart.ps1") `
        -RepoRoot $resolvedRepoRoot `
        -DotnetPath $dotnetPath

    if ($LASTEXITCODE -ne 0) {
        throw "Host autostart registration failed."
    }

    Write-Step "Registering logon autostart task."
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "register-autostart.ps1") `
        -RepoRoot $resolvedRepoRoot `
        -StateRoot $resolvedStateRoot

    if ($LASTEXITCODE -ne 0) {
        throw "Autostart registration failed."
    }
}

Write-Step "Bootstrap complete. RepoRoot=$resolvedRepoRoot StateRoot=$resolvedStateRoot"
