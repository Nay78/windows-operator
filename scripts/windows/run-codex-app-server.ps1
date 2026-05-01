[CmdletBinding()]
param(
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "Codex"),

    [string]$ListenUrl = "ws://127.0.0.1:43118"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
    param([string]$Path)

    $npmPrefix = Join-Path $Path "npm-global"
    $npmCache = Join-Path $Path "npm-cache"
    $env:npm_config_prefix = $npmPrefix
    $env:npm_config_cache = $npmCache

    $pathEntries = @($npmPrefix)
    $nodeCommand = Find-CommandPath -Names @("node.exe", "node")
    if ($nodeCommand) {
        $pathEntries += (Split-Path -Parent $nodeCommand)
    }

    $programFilesNode = Join-Path $env:ProgramFiles "nodejs"
    if (Test-Path -LiteralPath $programFilesNode) {
        $pathEntries += $programFilesNode
    }

    foreach ($pathEntry in $pathEntries | Select-Object -Unique) {
        if (-not $env:Path.Split(';').Contains($pathEntry)) {
            $env:Path = "$pathEntry;$env:Path"
        }
    }
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

Ensure-StateDirectories -Path $StateRoot
$resolvedStateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
$logRoot = Join-Path $resolvedStateRoot "logs"
$logPath = Join-Path $logRoot ("codex-app-server-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-Log {
    param([string]$Message)

    $line = "[{0}] {1}" -f (Get-Date -Format "s"), $Message
    $line | Tee-Object -FilePath $logPath -Append | Out-Host
}

try {
    Set-CodexEnvironment -Path $resolvedStateRoot
    $codexPath = Find-CodexPath -Path $resolvedStateRoot
    if (-not $codexPath) {
        Write-Log "Codex CLI missing. Run bootstrap-codex.ps1 first."
        exit 0
    }

    Write-Log "Checking Codex login status."
    $loginStatus = Invoke-NativeCapture -CommandPath $codexPath -Arguments @("login", "status")
    if ($loginStatus.ExitCode -ne 0 -or -not ($loginStatus.Output -match "Logged in")) {
        Write-Log "Codex login missing. Run 'codex login' in the Windows desktop session."
        exit 0
    }

    Write-Log "Starting Codex app-server on $ListenUrl."
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $codexPath app-server --listen $ListenUrl 2>&1 | Tee-Object -FilePath $logPath -Append
        $appServerExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($appServerExitCode -ne 0) {
        throw "codex app-server exited with code $appServerExitCode."
    }
}
catch {
    Write-Log $_.Exception.Message
    throw
}
