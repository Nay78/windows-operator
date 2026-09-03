[CmdletBinding()]
param(
    [switch]$Keep,

    [string]$InvokeScriptPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$invokeScript = if ([string]::IsNullOrWhiteSpace($InvokeScriptPath)) {
    Join-Path $PSScriptRoot "invoke-storage-cleanup.ps1"
}
else {
    $InvokeScriptPath
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "windows-operator-storage-cleanup-$([Guid]::NewGuid().ToString('N'))"
$stateRoot = Join-Path $testRoot "ProgramData\WindowsOperator"
$userStateRoot = Join-Path $testRoot "UserState\WindowsOperator"
$maintenanceRoot = Join-Path $stateRoot "maintenance"

function Assert-True {
    param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function New-TestFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$Bytes,
        [Parameter(Mandatory = $true)][DateTime]$LastWriteTimeUtc
    )
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllBytes($Path, (New-Object byte[] $Bytes))
    [IO.File]::SetLastWriteTimeUtc($Path, $LastWriteTimeUtc)
}

function Invoke-CleanupProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$RunId,
        [string]$RestoreRunId = ""
    )

    $arguments = @(
        "-Mode", $Mode,
        "-RunId", $RunId,
        "-StateRoot", $stateRoot,
        "-MaintenanceRoot", $maintenanceRoot,
        "-UserStateRoots", $userStateRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($RestoreRunId)) {
        $arguments += @("-RestoreRunId", $RestoreRunId)
    }
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $invokeScript @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not ($Mode -eq "Execute" -and $exitCode -eq 2)) {
        throw "Cleanup process failed with exit code $LASTEXITCODE. $($output -join ' ')"
    }
    return (($output -join [Environment]::NewLine) | ConvertFrom-Json)
}

try {
    New-Item -ItemType Directory -Force -Path $stateRoot, $userStateRoot | Out-Null
    $old = (Get-Date).ToUniversalTime().AddDays(-8)
    $fresh = (Get-Date).ToUniversalTime()

    $oldSync = Join-Path $stateRoot "sync\old-run"
    $freshSync = Join-Path $stateRoot "sync\fresh-run"
    $activeSync = Join-Path $stateRoot "sync\active-run"
    $oldStability = Join-Path $stateRoot "stability-preflight"
    $protectedHost = Join-Path $stateRoot "host"
    $oldLog = Join-Path $userStateRoot "logs\agent-old.log"
    $newLog = Join-Path $userStateRoot "logs\agent-new.log"

    New-TestFile -Path (Join-Path $oldSync "payload.bin") -Bytes 1024 -LastWriteTimeUtc $old
    New-TestFile -Path (Join-Path $freshSync "payload.bin") -Bytes 1024 -LastWriteTimeUtc $fresh
    New-TestFile -Path (Join-Path $activeSync "payload.bin") -Bytes 1024 -LastWriteTimeUtc $old
    New-TestFile -Path (Join-Path $activeSync ".windows-operator-active") -Bytes 1 -LastWriteTimeUtc $fresh
    New-TestFile -Path (Join-Path $oldStability "payload.bin") -Bytes 1024 -LastWriteTimeUtc $old
    New-TestFile -Path (Join-Path $protectedHost "payload.bin") -Bytes 1024 -LastWriteTimeUtc $old
    New-TestFile -Path $oldLog -Bytes 1024 -LastWriteTimeUtc $old
    New-TestFile -Path $newLog -Bytes 1024 -LastWriteTimeUtc $fresh

    $audit = Invoke-CleanupProcess -Mode Audit -RunId "audit"
    Assert-True ($audit.status -eq "audited") "audit status"
    Assert-True (Test-Path -LiteralPath (Join-Path $oldSync "payload.bin")) "audit leaves old sync data"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $maintenanceRoot "quarantine\audit"))) "audit does not quarantine"
    $activeCandidate = @($audit.candidates | Where-Object { $_.Path -eq [IO.Path]::GetFullPath($activeSync) })
    Assert-True ($activeCandidate.Count -eq 1 -and $activeCandidate[0].Reason -eq "active_marker") "active marker blocks cleanup"
    $protectedCandidate = @($audit.candidates | Where-Object { $_.Path -eq [IO.Path]::GetFullPath($protectedHost) })
    Assert-True ($protectedCandidate.Count -eq 0) "protected Host root is not a candidate"

    $execute = Invoke-CleanupProcess -Mode Execute -RunId "execute"
    Assert-True ($execute.status -in @("succeeded", "capacity_unresolved")) "execute status"
    Assert-True (-not (Test-Path -LiteralPath $oldSync)) "old sync moved"
    Assert-True (-not (Test-Path -LiteralPath $oldStability)) "old stability data moved"
    Assert-True (Test-Path -LiteralPath $freshSync) "fresh sync retained"
    Assert-True (Test-Path -LiteralPath $activeSync) "active sync retained"
    Assert-True (Test-Path -LiteralPath (Join-Path $maintenanceRoot "quarantine\execute\manifest.json")) "quarantine manifest written"

    if ($execute.status -eq "succeeded") {
        $restore = Invoke-CleanupProcess -Mode Restore -RunId "restore" -RestoreRunId "execute"
        Assert-True ($restore.status -eq "succeeded") "restore status"
        $restoreEvidence = @($restore.restored | ConvertTo-Json -Compress)
        Assert-True (Test-Path -LiteralPath (Join-Path $oldSync "payload.bin")) "old sync restored: $($restoreEvidence -join ' ')"
        Assert-True (Test-Path -LiteralPath (Join-Path $oldStability "payload.bin")) "old stability restored: $($restoreEvidence -join ' ')"
    }

    Write-Output "storage cleanup synthetic tests passed"
}
finally {
    if (-not $Keep -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Force -Recurse -ErrorAction SilentlyContinue
    }
}
