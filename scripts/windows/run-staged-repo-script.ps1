[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RunRoot,

    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$request = $null
$scriptStartedAtUtc = $null

function Convert-ToJsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $InputObject |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-Result {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [int]$ExitCode,

        [Parameter(Mandatory = $true)]
        [string]$Message,

        [object]$Request = $null
    )

    $payload = [ordered]@{
        runId = if ($Request -and $Request.runId) { $Request.runId } else { Split-Path -Leaf $RunRoot }
        status = $Status
        exitCode = $ExitCode
        message = $Message
        startedAtUtc = if ($script:scriptStartedAtUtc) { $script:scriptStartedAtUtc.ToString("o") } else { $null }
        completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    if ($script:scriptStartedAtUtc) {
        $payload.durationSeconds = [Math]::Round(((Get-Date).ToUniversalTime() - $script:scriptStartedAtUtc).TotalSeconds, 3)
    }

    if ($Request) {
        $payload.sourcePath = $Request.sourcePath
        $payload.sourcePathWindows = $Request.sourcePathWindows
        $payload.scriptSha256 = $Request.scriptSha256
    }

    Convert-ToJsonFile -InputObject $payload -Path (Join-Path $RunRoot "result.json")
}

function Resolve-UnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith("$rootFull\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes root. Root=$rootFull Path=$pathFull"
    }

    return $pathFull
}

try {
    if (-not (Test-Path -LiteralPath $RunRoot -PathType Container)) {
        throw "RunRoot missing: $RunRoot"
    }

    $requestPath = Join-Path $RunRoot "request.json"
    $commandPath = Join-Path $RunRoot "command.ps1"
    $transcriptPath = Join-Path $RunRoot "transcript.txt"

    if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf)) {
        throw "request.json missing: $requestPath"
    }

    if (-not (Test-Path -LiteralPath $commandPath -PathType Leaf)) {
        throw "command.ps1 missing: $commandPath"
    }

    $request = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
    $repoRootFull = (Resolve-Path -LiteralPath $RepoRoot).Path
    $scriptRoot = Join-Path $repoRootFull "scripts\windows"
    $sourcePath = [string]$request.sourcePath

    if ([string]::IsNullOrWhiteSpace($sourcePath) -or
        [System.IO.Path]::IsPathRooted($sourcePath) -or
        -not $sourcePath.EndsWith(".ps1", [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $sourcePath.Replace('/', '\').StartsWith("scripts\windows\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourcePath.Contains("..")) {
        throw "Request sourcePath is not an approved repo script: $sourcePath"
    }

    $sourceAbsolute = Resolve-UnderRoot -Root $scriptRoot -Path (Join-Path $repoRootFull $sourcePath)
    if (-not (Test-Path -LiteralPath $sourceAbsolute -PathType Leaf)) {
        throw "Repo source script missing: $sourceAbsolute"
    }

    $sourceHash = (Get-FileHash -LiteralPath $sourceAbsolute -Algorithm SHA256).Hash.ToLowerInvariant()
    $commandHash = (Get-FileHash -LiteralPath $commandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = ([string]$request.scriptSha256).ToLowerInvariant()

    if ($sourceHash -ne $expectedHash) {
        throw "Repo source hash mismatch. Expected=$expectedHash Actual=$sourceHash"
    }

    if ($commandHash -ne $expectedHash) {
        throw "Staged command hash mismatch. Expected=$expectedHash Actual=$commandHash"
    }

    $scriptArguments = @()
    if ($null -ne $request.arguments) {
        if ($request.arguments -is [array]) {
            $scriptArguments = @($request.arguments)
        }
        else {
            $scriptArguments = @($request.arguments)
        }
    }

    Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null
    try {
        $script:scriptStartedAtUtc = (Get-Date).ToUniversalTime()
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $commandPath @scriptArguments
        $exitCode = if ($global:LASTEXITCODE -is [int]) { $global:LASTEXITCODE } else { 0 }
    }
    finally {
        Stop-Transcript | Out-Null
    }

    if ($exitCode -eq 0) {
        Write-Result -Status "succeeded" -ExitCode $exitCode -Message "Script completed." -Request $request
    }
    else {
        Write-Result -Status "failed" -ExitCode $exitCode -Message "Script exited nonzero." -Request $request
    }

    exit $exitCode
}
catch {
    try {
        Write-Result -Status "failed" -ExitCode 1 -Message $_.Exception.Message -Request $request
    }
    catch {
        Write-Error $_.Exception.Message
    }

    Write-Error $_.Exception.Message
    exit 1
}
