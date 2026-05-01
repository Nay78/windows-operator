[CmdletBinding()]
param(
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "Codex"),

    [switch]$EnableAutostart,

    [string]$ListenUrl = "ws://127.0.0.1:43118"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[codex-bootstrap] $Message"
}

function Ensure-StateDirectories {
    param([string]$Path)

    @(
        $Path,
        (Join-Path $Path "npm-global"),
        (Join-Path $Path "npm-cache"),
        (Join-Path $Path "logs"),
        (Join-Path $Path "run")
    ) | ForEach-Object {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
    }
}

function Find-CommandPath {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Path
        }
    }

    return $null
}

function Find-NodePaths {
    param([string]$Path)

    $nodeCandidates = @(
        (Join-Path $env:ProgramFiles "nodejs\node.exe")
    )
    $npmCandidates = @(
        (Join-Path $env:ProgramFiles "nodejs\npm.cmd")
    )

    $nodeCommand = Find-CommandPath -Names @("node.exe", "node")
    if ($nodeCommand) {
        $nodeCandidates += $nodeCommand
    }

    $npmCommand = Find-CommandPath -Names @("npm.cmd", "npm")
    if ($npmCommand) {
        $npmCandidates += $npmCommand
    }

    foreach ($nodePath in $nodeCandidates | Select-Object -Unique) {
        foreach ($npmPath in $npmCandidates | Select-Object -Unique) {
            if ((Test-Path -LiteralPath $nodePath) -and (Test-Path -LiteralPath $npmPath)) {
                return @{
                    Node = $nodePath
                    Npm = $npmPath
                }
            }
        }
    }

    return $null
}

function Install-NodeWithWinget {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        return $false
    }

    Write-Step "Node.js LTS missing. Installing with winget."
    & $winget.Path install `
        --id OpenJS.NodeJS.LTS `
        --exact `
        --architecture x64 `
        --accept-package-agreements `
        --accept-source-agreements `
        --disable-interactivity

    return ($LASTEXITCODE -eq 0)
}

function Install-NodeWithOfficialMsi {
    param([string]$Path)

    Write-Step "winget unavailable or failed. Falling back to official Node.js MSI."
    $installerPath = Join-Path $Path "run\node-lts-x64.msi"
    Invoke-WebRequest -Uri "https://nodejs.org/dist/v20.18.1/node-v20.18.1-x64.msi" -OutFile $installerPath
    $process = Start-Process msiexec.exe -ArgumentList @("/i", "`"$installerPath`"", "/qn", "/norestart") -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Node.js MSI installer failed with exit code $($process.ExitCode)."
    }
}

function Ensure-Node {
    param([string]$Path)

    $nodePaths = Find-NodePaths -Path $Path
    if ($nodePaths) {
        return $nodePaths
    }

    $wingetInstalled = Install-NodeWithWinget
    $nodePaths = Find-NodePaths -Path $Path
    if ($wingetInstalled -and $nodePaths) {
        return $nodePaths
    }

    Install-NodeWithOfficialMsi -Path $Path
    $nodePaths = Find-NodePaths -Path $Path
    if ($nodePaths) {
        return $nodePaths
    }

    throw "Node.js and npm still missing after install attempts."
}

function Find-CodexPath {
    param([string]$Path)

    $candidates = @(
        (Join-Path $Path "npm-global\codex.cmd")
    )

    $command = Find-CommandPath -Names @("codex.cmd", "codex")
    if ($command) {
        $candidates += $command
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Set-CodexEnvironment {
    param(
        [string]$Path,
        [hashtable]$NodePaths
    )

    $npmPrefix = Join-Path $Path "npm-global"
    $npmCache = Join-Path $Path "npm-cache"
    $nodeDir = Split-Path -Parent $NodePaths.Node

    $env:npm_config_prefix = $npmPrefix
    $env:npm_config_cache = $npmCache

    foreach ($pathEntry in @($npmPrefix, $nodeDir)) {
        if (-not $env:Path.Split(';').Contains($pathEntry)) {
            $env:Path = "$pathEntry;$env:Path"
        }
    }
}

function Ensure-UserPathEntry {
    param([string]$PathEntry)

    $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $segments = @()
    if (-not [string]::IsNullOrWhiteSpace($currentUserPath)) {
        $segments = $currentUserPath.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    if ($segments -notcontains $PathEntry) {
        $updatedUserPath = @($segments + $PathEntry) -join ';'
        [Environment]::SetEnvironmentVariable("Path", $updatedUserPath, "User")
    }

    if (-not $env:Path.Split(';').Contains($PathEntry)) {
        $env:Path = "$PathEntry;$env:Path"
    }
}

function Publish-EnvironmentChange {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult
    );
}
"@ -ErrorAction SilentlyContinue

    $result = [UIntPtr]::Zero
    [void][NativeMethods]::SendMessageTimeout(
        [IntPtr]0xffff,
        0x001A,
        [UIntPtr]::Zero,
        "Environment",
        0x0002,
        5000,
        [ref]$result
    )
}

function Ensure-CodexShims {
    param([string]$StateRoot)

    $shimDir = Join-Path $env:APPDATA "npm"
    $codexCmdTarget = Join-Path $StateRoot "npm-global\codex.cmd"
    $codexPs1Target = Join-Path $StateRoot "npm-global\codex.ps1"

    New-Item -ItemType Directory -Path $shimDir -Force | Out-Null

    $cmdShimPath = Join-Path $shimDir "codex.cmd"
    $cmdShimContent = @"
@echo off
"$codexCmdTarget" %*
"@
    Set-Content -LiteralPath $cmdShimPath -Value $cmdShimContent -Encoding ASCII

    $ps1ShimPath = Join-Path $shimDir "codex.ps1"
    $ps1ShimContent = @'
$target = '__CODEX_PS1_TARGET__'
& $target @args
exit $LASTEXITCODE
'@.Replace('__CODEX_PS1_TARGET__', $codexPs1Target)
    Set-Content -LiteralPath $ps1ShimPath -Value $ps1ShimContent -Encoding ASCII
}

function Invoke-NativeCapture {
    param(
        [string]$CommandPath,
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $CommandPath @Arguments 2>&1
        return @{
            ExitCode = $LASTEXITCODE
            Output = $output
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedStateRoot = $StateRoot
Ensure-StateDirectories -Path $resolvedStateRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $resolvedStateRoot).Path

$nodePaths = Ensure-Node -Path $resolvedStateRoot
Set-CodexEnvironment -Path $resolvedStateRoot -NodePaths $nodePaths

$npmPrefix = Join-Path $resolvedStateRoot "npm-global"
$npmCache = Join-Path $resolvedStateRoot "npm-cache"

Write-Step "Persisting Codex npm prefix on user PATH."
Ensure-UserPathEntry -PathEntry $npmPrefix
Publish-EnvironmentChange
Ensure-CodexShims -StateRoot $resolvedStateRoot

Write-Step "Configuring npm local prefix and cache."
& $nodePaths.Npm config set prefix $npmPrefix --location=user
if ($LASTEXITCODE -ne 0) {
    throw "npm prefix configuration failed."
}

& $nodePaths.Npm config set cache $npmCache --location=user
if ($LASTEXITCODE -ne 0) {
    throw "npm cache configuration failed."
}

Write-Step "Installing OpenAI Codex CLI with npm."
& $nodePaths.Npm install --global "@openai/codex"
if ($LASTEXITCODE -ne 0) {
    throw "Codex npm install failed."
}

$codexPath = Find-CodexPath -Path $resolvedStateRoot
if (-not $codexPath) {
    throw "codex command missing after npm install."
}

$versionResult = Invoke-NativeCapture -CommandPath $codexPath -Arguments @("--version")
if ($versionResult.ExitCode -ne 0) {
    throw "codex --version failed."
}
Write-Step "Installed $($versionResult.Output)"

if ($EnableAutostart) {
    Write-Step "Registering Codex app-server logon task."
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "register-codex-app-server.ps1") `
        -StateRoot $resolvedStateRoot `
        -ListenUrl $ListenUrl

    if ($LASTEXITCODE -ne 0) {
        throw "Codex app-server autostart registration failed."
    }
}

$loginStatus = Invoke-NativeCapture -CommandPath $codexPath -Arguments @("login", "status")
if ($loginStatus.ExitCode -eq 0 -and ($loginStatus.Output -match "Logged in")) {
    Write-Step "Codex login present. App server will listen on $ListenUrl at logon."
} else {
    Write-Step "Codex login missing. Run 'codex login' in the Windows desktop session, then start task Codex.AppServer."
}

Write-Step "Codex bootstrap complete. StateRoot=$resolvedStateRoot"
