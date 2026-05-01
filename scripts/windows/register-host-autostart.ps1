[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator"),

    [string]$DotnetPath = "dotnet.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[host-autostart] $Message"
}

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Resolve-Dotnet {
    param([string]$Candidate)

    if (Test-Path -LiteralPath $Candidate) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "dotnet executable not found: $Candidate"
}

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "RepoRoot missing: $RepoRoot"
}

$hostProjectPath = Join-Path $RepoRoot "src\WindowsOperator.Host\WindowsOperator.Host.csproj"
if (-not (Test-Path -LiteralPath $hostProjectPath)) {
    throw "Host project missing: $hostProjectPath"
}

$resolvedStateRoot = New-Item -ItemType Directory -Path $StateRoot -Force
$hostRoot = Join-Path $resolvedStateRoot.FullName "host"
$runRoot = Join-Path $resolvedStateRoot.FullName "run"
New-Item -ItemType Directory -Path $hostRoot -Force | Out-Null
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$resolvedDotnetPath = Resolve-Dotnet -Candidate $DotnetPath

Write-Step "Publishing WindowsOperator.Host."
& $resolvedDotnetPath publish $hostProjectPath -c Debug -o $hostRoot --no-self-contained
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$localConfigPath = Join-Path $runRoot "host.appsettings.Local.json"
@"
{
  "Operator": {
    "bindAddress": "127.0.0.1",
    "restPort": 43117,
    "enableMcpStdio": false
  },
  "DesktopAgent": {
    "baseUrl": "http://127.0.0.1:43119"
  }
}
"@ | Set-Content -LiteralPath $localConfigPath -Encoding UTF8

$hostDll = Join-Path $hostRoot "WindowsOperator.Host.dll"
$arguments = @(
    (Quote-Argument $hostDll)
) -join " "

$action = New-ScheduledTaskAction -Execute $resolvedDotnetPath -Argument $arguments -WorkingDirectory $hostRoot
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -StartWhenAvailable

$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName "WindowsOperator.Host" -InputObject $task -Force | Out-Null

Write-Step "Registered task WindowsOperator.Host as SYSTEM. HostRoot=$hostRoot"
