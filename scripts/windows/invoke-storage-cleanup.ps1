[CmdletBinding()]
param(
    [ValidateSet("Audit", "Execute", "Restore")]
    [string]$Mode = "Audit",

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator"),

    [string]$MaintenanceRoot = "",

    [string[]]$UserStateRoots = @(),

    [string]$RunId = "",

    [string]$RestoreRunId = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:NormalRetention = @{
    sync = [TimeSpan]::FromHours(72)
    stability = [TimeSpan]::FromDays(7)
    onedriveTest = [TimeSpan]::FromDays(7)
    agentLog = [TimeSpan]::FromDays(14)
    maintenanceReport = [TimeSpan]::FromDays(30)
}
$script:LowRetention = @{
    sync = [TimeSpan]::FromHours(24)
    stability = [TimeSpan]::FromHours(48)
    onedriveTest = [TimeSpan]::FromHours(48)
    agentLog = [TimeSpan]::FromDays(3)
    maintenanceReport = [TimeSpan]::FromDays(7)
}
$script:LowSpaceBytes = [int64](20GB)
$script:LowSpacePercent = 15.0
$script:RecoveryBytes = [int64](30GB)
$script:RecoveryPercent = 20.0
$script:NormalDeleteLimit = [int64](10GB)
$script:LowDeleteLimit = [int64](25GB)
$script:QuarantineRetention = [TimeSpan]::FromHours(24)
$script:NewestLogCount = 5

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        ConvertTo-Json -InputObject $InputObject -Depth 12 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $pathFull = Get-CanonicalPath -Path $Path
    $rootFull = Get-CanonicalPath -Path $Root
    return $pathFull.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $pathFull.StartsWith("$rootFull\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ReparseTree {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $true
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $true
    }

    if ($item.PSIsContainer) {
        $scanErrors = @()
        $descendants = @(Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction SilentlyContinue -ErrorVariable scanErrors)
        if (@($scanErrors).Count -gt 0) {
            return $true
        }
        foreach ($descendant in $descendants) {
            if (($descendant.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }
    }

    return $false
}

function Get-PathMetrics {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    $scanErrors = @()
    $files = @()
    if ($item.PSIsContainer) {
        $files = @(Get-ChildItem -LiteralPath $Path -Force -Recurse -File -ErrorAction SilentlyContinue -ErrorVariable scanErrors)
    }
    else {
        $files = @($item)
    }
    if (@($scanErrors).Count -gt 0) {
        return $null
    }
    $bytes = [int64]0
    if ($files.Count -gt 0) {
        $sum = ($files | Measure-Object -Property Length -Sum).Sum
        if ($null -ne $sum) {
            $bytes = [int64]$sum
        }
    }

    $newest = $item.LastWriteTimeUtc
    if ($item.PSIsContainer -and $files.Count -gt 0) {
        $newest = $files[0].LastWriteTimeUtc
    }
    foreach ($child in $files) {
        if ($child.LastWriteTimeUtc -gt $newest) {
            $newest = $child.LastWriteTimeUtc
        }
    }

    return [pscustomobject]@{
        Bytes = $bytes
        NewestWriteTimeUtc = $newest
        FileCount = $files.Count
    }
}

function Get-FreeSpace {
    param([Parameter(Mandatory = $true)][string]$Path)

    $drive = [System.IO.Path]::GetPathRoot((Get-CanonicalPath -Path $Path)).TrimEnd('\')
    $volume = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='$drive'" -ErrorAction Stop
    if ($null -eq $volume) {
        throw "Unable to inspect volume $drive."
    }

    $freeBytes = [int64]$volume.FreeSpace
    $totalBytes = [int64]$volume.Size
    return [pscustomobject]@{
        Drive = $drive
        FreeBytes = $freeBytes
        TotalBytes = $totalBytes
        FreeGiB = [math]::Round($freeBytes / 1GB, 2)
        FreePercent = [math]::Round(100.0 * $freeBytes / $totalBytes, 2)
    }
}

function Get-ProtectedRoots {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedStateRoot,
        [Parameter(Mandatory = $true)][string[]]$ResolvedUserStateRoots
    )

    $roots = @(
        (Join-Path $ResolvedStateRoot "host"),
        (Join-Path $ResolvedStateRoot "run"),
        (Join-Path $ResolvedStateRoot "certs"),
        (Join-Path $ResolvedStateRoot "maintenance"),
        (Join-Path $ResolvedStateRoot "exchange")
    )
    foreach ($userRoot in $ResolvedUserStateRoots) {
        $roots += @(
            (Join-Path $userRoot "agent"),
            (Join-Path $userRoot "run"),
            (Join-Path $userRoot "dotnet-sdk"),
            (Join-Path $userRoot "dotnet-home"),
            (Join-Path $userRoot "nuget-packages"),
            (Join-Path $userRoot "artifacts"),
            (Join-Path $userRoot "provisioning"),
            (Join-Path $userRoot "files-on-demand")
        )
    }
    return @($roots | ForEach-Object { Get-CanonicalPath -Path $_ })
}

function New-Candidate {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][TimeSpan]$Retention,
        [Parameter(Mandatory = $true)][TimeSpan]$LowRetention,
        [int]$PreserveNewest = 0
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    $metrics = Get-PathMetrics -Path $Path
    if ($null -eq $metrics) {
        return $null
    }

    $markerPath = if ($item.PSIsContainer) { Join-Path $Path ".windows-operator-active" } else { $null }
    $markerAge = $null
    if ($markerPath -and (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        $marker = Get-Item -LiteralPath $markerPath -Force
        $markerAge = ((Get-Date).ToUniversalTime() - $marker.LastWriteTimeUtc)
    }

    $isReparse = Test-ReparseTree -Path $Path
    $age = ((Get-Date).ToUniversalTime() - $metrics.NewestWriteTimeUtc)
    $eligible = $age -ge $Retention
    $reason = if ($isReparse) {
        "reparse_point"
    }
    elseif ($markerAge -and $markerAge -lt $LowRetention) {
        "active_marker"
    }
    elseif (-not $eligible) {
        "retention"
    }
    else {
        "eligible"
    }

    return [pscustomobject]@{
        Path = (Get-CanonicalPath -Path $Path)
        Category = $Category
        Bytes = $metrics.Bytes
        FileCount = $metrics.FileCount
        NewestWriteTimeUtc = $metrics.NewestWriteTimeUtc
        AgeHours = [math]::Round($age.TotalHours, 2)
        RetentionHours = [math]::Round($Retention.TotalHours, 2)
        LowRetentionHours = [math]::Round($LowRetention.TotalHours, 2)
        PreserveNewest = $PreserveNewest
        MarkerPath = $markerPath
        MarkerAgeHours = if ($markerAge) { [math]::Round($markerAge.TotalHours, 2) } else { $null }
        IsReparse = $isReparse
        Eligible = ($reason -eq "eligible")
        Reason = $reason
    }
}

function Get-Candidates {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedStateRoot,
        [Parameter(Mandatory = $true)][string]$ResolvedMaintenanceRoot,
        [Parameter(Mandatory = $true)][string[]]$ResolvedUserStateRoots,
        [Parameter(Mandatory = $true)][bool]$LowSpace
    )

    $retention = if ($LowSpace) { $script:LowRetention } else { $script:NormalRetention }
    $candidates = @()

    $syncRoot = Join-Path $ResolvedStateRoot "sync"
    if (Test-Path -LiteralPath $syncRoot -PathType Container) {
        foreach ($child in @(Get-ChildItem -LiteralPath $syncRoot -Force -Directory -ErrorAction SilentlyContinue)) {
            $candidate = New-Candidate -Path $child.FullName -Category "sync" -Retention $retention.sync -LowRetention $script:LowRetention.sync
            if ($candidate) { $candidates += $candidate }
        }
    }

    foreach ($definition in @(
        @{ Relative = "stability-preflight"; Category = "stability" },
        @{ Relative = "onedrive-module-live-test"; Category = "onedriveTest" }
    )) {
        $path = Join-Path $ResolvedStateRoot $definition.Relative
        if (Test-Path -LiteralPath $path -PathType Container) {
            $candidate = New-Candidate -Path $path -Category $definition.Category -Retention $retention[$definition.Category] -LowRetention $script:LowRetention[$definition.Category]
            if ($candidate) { $candidates += $candidate }
        }
    }

    foreach ($userRoot in $ResolvedUserStateRoots) {
        $logRoot = Join-Path $userRoot "logs"
        $logs = @(Get-ChildItem -LiteralPath $logRoot -Filter "agent-*.log" -File -Force -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
        for ($index = 0; $index -lt $logs.Count; $index++) {
            $candidate = New-Candidate -Path $logs[$index].FullName -Category "agentLog" -Retention $retention.agentLog -LowRetention $script:LowRetention.agentLog -PreserveNewest $script:NewestLogCount
            if ($candidate) {
                if ($index -lt $script:NewestLogCount) {
                    $candidate.Eligible = $false
                    $candidate.Reason = "newest_logs"
                }
                $candidates += $candidate
            }
        }
    }

    $reportRoot = Join-Path $ResolvedMaintenanceRoot "reports"
    $reports = @(Get-ChildItem -LiteralPath $reportRoot -Filter "*.json" -File -Force -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
    for ($index = 0; $index -lt $reports.Count; $index++) {
        $candidate = New-Candidate -Path $reports[$index].FullName -Category "maintenanceReport" -Retention $retention.maintenanceReport -LowRetention $script:LowRetention.maintenanceReport -PreserveNewest 30
        if ($candidate) {
            if ($index -lt 30) {
                $candidate.Eligible = $false
                $candidate.Reason = "newest_reports"
            }
            $candidates += $candidate
        }
    }

    return @($candidates)
}

function Get-UserStateRoots {
    param([string[]]$RequestedRoots)

    if ($RequestedRoots -and $RequestedRoots.Count -gt 0) {
        return @($RequestedRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Get-CanonicalPath -Path $_ })
    }

    $profiles = @(Get-CimInstance -ClassName Win32_UserProfile -ErrorAction SilentlyContinue | Where-Object {
        -not $_.Special -and -not [string]::IsNullOrWhiteSpace($_.LocalPath)
    })
    return @($profiles | ForEach-Object { Get-CanonicalPath -Path (Join-Path $_.LocalPath "AppData\Local\WindowsOperator") })
}

function Get-QuarantineDirectories {
    param([Parameter(Mandatory = $true)][string]$QuarantineRoot)

    if (-not (Test-Path -LiteralPath $QuarantineRoot -PathType Container)) {
        return @()
    }
    return @(Get-ChildItem -LiteralPath $QuarantineRoot -Force -Directory -ErrorAction SilentlyContinue)
}

function Remove-Quarantine {
    param(
        [Parameter(Mandatory = $true)][string]$QuarantineRoot,
        [Parameter(Mandatory = $true)][TimeSpan]$MinimumAge,
        [Parameter(Mandatory = $true)][int64]$LimitBytes
    )

    $removed = @()
    $removedBytes = [int64]0
    foreach ($directory in @(Get-QuarantineDirectories -QuarantineRoot $QuarantineRoot | Sort-Object LastWriteTimeUtc)) {
        if (((Get-Date).ToUniversalTime() - $directory.LastWriteTimeUtc) -lt $MinimumAge) {
            continue
        }
        $metrics = Get-PathMetrics -Path $directory.FullName
        if ($null -eq $metrics -or ($removedBytes + $metrics.Bytes) -gt $LimitBytes) {
            continue
        }
        Remove-Item -LiteralPath $directory.FullName -Force -Recurse
        $removed += [pscustomobject]@{ Path = $directory.FullName; Bytes = $metrics.Bytes }
        $removedBytes += $metrics.Bytes
    }
    return [pscustomobject]@{ Items = @($removed); Bytes = $removedBytes }
}

function Move-CandidateToQuarantine {
    param(
        [Parameter(Mandatory = $true)][pscustomobject]$Candidate,
        [Parameter(Mandatory = $true)][string]$QuarantineRunRoot
    )

    $leaf = Split-Path -Leaf $Candidate.Path
    $destination = Join-Path $QuarantineRunRoot ("{0}-{1}-{2}" -f $Candidate.Category, $leaf, ([Guid]::NewGuid().ToString("N")))
    New-Item -ItemType Directory -Force -Path $QuarantineRunRoot | Out-Null
    Move-Item -LiteralPath $Candidate.Path -Destination $destination
    return $destination
}

function Invoke-Restore {
    param(
        [Parameter(Mandatory = $true)][string]$QuarantineRoot,
        [Parameter(Mandatory = $true)][string]$RequestedRunId
    )

    if ([string]::IsNullOrWhiteSpace($RequestedRunId)) {
        throw "Restore requires -RestoreRunId."
    }
    $runRoot = Join-Path $QuarantineRoot $RequestedRunId
    $manifestPath = Join-Path $runRoot "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Quarantine manifest not found: $manifestPath"
    }

    $rawManifest = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
    $manifest = @($rawManifest)
    $restored = @()
    foreach ($entry in $manifest) {
        if ($entry.disposition -ne "quarantined" -or -not (Test-Path -LiteralPath $entry.quarantinePath)) {
            continue
        }
        if (Test-Path -LiteralPath $entry.originalPath) {
            $restored += [pscustomobject]@{ OriginalPath = $entry.originalPath; Status = "blocked_existing_path" }
            continue
        }
        $parent = Split-Path -Parent $entry.originalPath
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        Move-Item -LiteralPath $entry.quarantinePath -Destination $entry.originalPath
        $restored += [pscustomobject]@{ OriginalPath = $entry.originalPath; Status = "restored" }
    }
    return $restored
}

$resolvedStateRoot = Get-CanonicalPath -Path $StateRoot
if ([string]::IsNullOrWhiteSpace($MaintenanceRoot)) {
    $MaintenanceRoot = Join-Path $resolvedStateRoot "maintenance"
}
$resolvedMaintenanceRoot = Get-CanonicalPath -Path $MaintenanceRoot
$resolvedUserStateRoots = @(Get-UserStateRoots -RequestedRoots $UserStateRoots)
$reportRoot = Join-Path $resolvedMaintenanceRoot "reports"
$quarantineRoot = Join-Path $resolvedMaintenanceRoot "quarantine"
$runId = if ([string]::IsNullOrWhiteSpace($RunId)) { "storage-$(Get-Date -Format yyyyMMdd-HHmmss)-$([Guid]::NewGuid().ToString('N').Substring(0,8))" } else { $RunId }
$reportPath = Join-Path $reportRoot "$runId.json"
$startedAt = (Get-Date).ToUniversalTime()
$mutex = New-Object System.Threading.Mutex($false, "Global\WindowsOperator.StorageCleanup")
$mutexHeld = $false

try {
    if ($Mode -eq "Restore") {
        $mutexHeld = $mutex.WaitOne(0)
        if (-not $mutexHeld) { throw "Another storage cleanup is running." }
        $restoreResult = Invoke-Restore -QuarantineRoot $quarantineRoot -RequestedRunId $RestoreRunId
        $result = [ordered]@{
            runId = $runId
            mode = $Mode
            status = "succeeded"
            restored = @($restoreResult)
            startedAtUtc = $startedAt.ToString("o")
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        Write-JsonAtomic -InputObject $result -Path $reportPath
        $result | ConvertTo-Json -Depth 12
        exit 0
    }

    New-Item -ItemType Directory -Force -Path $resolvedMaintenanceRoot, $reportRoot, $quarantineRoot | Out-Null
    $freeBefore = Get-FreeSpace -Path $resolvedStateRoot
    $lowSpace = $freeBefore.FreeBytes -lt $script:LowSpaceBytes -or $freeBefore.FreePercent -lt $script:LowSpacePercent
    $candidates = @(Get-Candidates -ResolvedStateRoot $resolvedStateRoot -ResolvedMaintenanceRoot $resolvedMaintenanceRoot -ResolvedUserStateRoots $resolvedUserStateRoots -LowSpace $lowSpace)
    $eligible = @($candidates | Where-Object Eligible | Sort-Object Bytes -Descending)
    $plan = [ordered]@{
        runId = $runId
        mode = $Mode
        startedAtUtc = $startedAt.ToString("o")
        stateRoot = $resolvedStateRoot
        maintenanceRoot = $resolvedMaintenanceRoot
        userStateRoots = @($resolvedUserStateRoots)
        lowSpace = $lowSpace
        freeBefore = $freeBefore
        candidates = @($candidates)
        protectedRoots = @(Get-ProtectedRoots -ResolvedStateRoot $resolvedStateRoot -ResolvedUserStateRoots $resolvedUserStateRoots)
    }
    $planPath = Join-Path $resolvedMaintenanceRoot "plans\$runId.json"
    Write-JsonAtomic -InputObject $plan -Path $planPath

    if ($Mode -eq "Audit") {
        $result = [ordered]@{
            runId = $runId
            mode = $Mode
            status = "audited"
            planPath = $planPath
            eligibleBytes = if ($eligible.Count -eq 0) { [int64]0 } else { [int64](($eligible | Measure-Object -Property Bytes -Sum).Sum) }
            freeBefore = $freeBefore
            lowSpace = $lowSpace
            candidates = @($candidates)
            startedAtUtc = $startedAt.ToString("o")
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        Write-JsonAtomic -InputObject $result -Path $reportPath
        $result | ConvertTo-Json -Depth 12
        exit 0
    }

    $mutexHeld = $mutex.WaitOne(0)
    if (-not $mutexHeld) { throw "Another storage cleanup is running." }
    $deleteLimit = if ($lowSpace) { $script:LowDeleteLimit } else { $script:NormalDeleteLimit }
    $quarantineResult = Remove-Quarantine -QuarantineRoot $quarantineRoot -MinimumAge $script:QuarantineRetention -LimitBytes $deleteLimit
    $quarantineRunRoot = Join-Path $quarantineRoot $runId
    $actions = @()
    $actionBytes = [int64]0
    foreach ($candidate in $eligible) {
        if (($actionBytes + $candidate.Bytes) -gt $deleteLimit) { continue }
        if (-not (Test-Path -LiteralPath $candidate.Path)) { continue }
        if (Test-ReparseTree -Path $candidate.Path) { continue }
        $quarantinePath = Move-CandidateToQuarantine -Candidate $candidate -QuarantineRunRoot $quarantineRunRoot
        $disposition = "quarantined"
        if ($lowSpace) {
            Remove-Item -LiteralPath $quarantinePath -Force -Recurse
            $disposition = "purged"
        }
        $actionBytes += $candidate.Bytes
        $actions += [pscustomobject]@{
            category = $candidate.Category
            originalPath = $candidate.Path
            quarantinePath = $quarantinePath
            bytes = $candidate.Bytes
            disposition = $disposition
        }
        $freeNow = Get-FreeSpace -Path $resolvedStateRoot
        if ($lowSpace -and $freeNow.FreeBytes -ge $script:RecoveryBytes -and $freeNow.FreePercent -ge $script:RecoveryPercent) { break }
    }

    $manifest = @($actions)
    if ($manifest.Count -gt 0) {
        Write-JsonAtomic -InputObject $manifest -Path (Join-Path $quarantineRunRoot "manifest.json")
    }
    $freeAfter = Get-FreeSpace -Path $resolvedStateRoot
    $result = [ordered]@{
        runId = $runId
        mode = $Mode
        status = if ($lowSpace -and ($freeAfter.FreeBytes -lt $script:RecoveryBytes -or $freeAfter.FreePercent -lt $script:RecoveryPercent)) { "capacity_unresolved" } else { "succeeded" }
        planPath = $planPath
        freeBefore = $freeBefore
        freeAfter = $freeAfter
        lowSpace = $lowSpace
        quarantinePurged = @($quarantineResult.Items)
        actions = @($actions)
        actionBytes = $actionBytes
        startedAtUtc = $startedAt.ToString("o")
        completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    Write-JsonAtomic -InputObject $result -Path $reportPath
    $result | ConvertTo-Json -Depth 12
    if ($result.status -eq "capacity_unresolved") { exit 2 }
    exit 0
}
catch {
    $failure = [ordered]@{
        runId = $runId
        mode = $Mode
        status = "failed"
        error = $_.Exception.Message
        startedAtUtc = $startedAt.ToString("o")
        completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    try { Write-JsonAtomic -InputObject $failure -Path $reportPath } catch { }
    $failure | ConvertTo-Json -Depth 12
    exit 1
}
finally {
    if ($mutexHeld) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
