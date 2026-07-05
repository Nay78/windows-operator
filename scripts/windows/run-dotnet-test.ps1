[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Project,

    [string]$Filter,

    [switch]$NoRestore,

    [switch]$NoBuild,

    [int]$MaxCpuCount = 1,

    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WindowsOperator")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-Dotnet {
    param([Parameter(Mandatory = $true)][string]$Path)

    $candidates = @(
        (Join-Path $Path "dotnet-sdk\dotnet.exe"),
        (Join-Path $env:LOCALAPPDATA "WindowsOperator\dotnet-sdk\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf) -and (Test-DotnetCandidate -Path $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-DotnetCandidate -Path $command.Source)) {
        return $command.Source
    }

    throw "No usable dotnet.exe with installed runtimes was found."
}

function Test-DotnetCandidate {
    param([Parameter(Mandatory = $true)][string]$Path)

    $runtimes = & $Path --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    return [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s' })
}

function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $Root $Path }
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pathFull = [System.IO.Path]::GetFullPath($candidate)
    if (-not $pathFull.StartsWith("$rootFull\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Project path escapes repo root. Root=$rootFull Path=$pathFull"
    }

    return $pathFull
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
    }
    else {
        throw "RepoRoot is required when this script is run from a staged copy."
    }
}

$repoRootFull = (Resolve-Path -LiteralPath $RepoRoot).Path
$stateRootFull = [System.IO.Path]::GetFullPath($StateRoot)
New-Item -ItemType Directory -Force -Path $stateRootFull | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $stateRootFull "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $stateRootFull "nuget-packages"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null

$dotnet = Resolve-Dotnet -Path $stateRootFull
$projectPath = Resolve-ProjectPath -Root $repoRootFull -Path $Project

$arguments = @("test", $projectPath, "-m:$MaxCpuCount")
if ($NoRestore) {
    $arguments += "--no-restore"
}

if ($NoBuild) {
    $arguments += "--no-build"
}

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @("--filter", $Filter)
}

& $dotnet @arguments
exit $LASTEXITCODE
