[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator"),

    [string]$DotnetPath = "dotnet.exe",

    [string]$PowerPointAddInBaseUrl = "https://localhost:3003",

    [string]$PowerPointAddInStaticRoot = "",

    [string]$ExchangeRoot = "",

    [string]$HostExchangeRoot = "",

    [string]$OneDriveRecoveryAllowedComputer = "",

    [switch]$DeferStart,

    [switch]$DisablePowerPointAddIn
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$requiredOneDriveRecoveryComputer = "WIN-UUKQS009K4J"

function Write-Step {
    param([string]$Message)
    Write-Host "[host-autostart] $Message"
}

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Quote-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function ConvertTo-TaskDuration {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [TimeSpan]) {
        return $Value
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    $parsed = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse($text, [ref]$parsed)) {
        return $parsed
    }

    try {
        return [System.Xml.XmlConvert]::ToTimeSpan($text)
    }
    catch {
        throw "Task duration '$text' is not a supported CIM/XML duration."
    }
}

function Get-HostScheduledTaskValidationErrors {
    param(
        $Task,
        [Parameter(Mandatory = $true)][string]$ExpectedExecute,
        [Parameter(Mandatory = $true)][string]$ExpectedArguments,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkingDirectory
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Task) {
        $errors.Add("WindowsOperator.Host task is missing after registration.")
        return @($errors)
    }

    $principal = $Task.Principal
    $isSystem = $principal.UserId -match '^(?i:SYSTEM|NT AUTHORITY\\SYSTEM)$'
    if (-not $isSystem) {
        $errors.Add("principal UserId must be SYSTEM; observed '$($principal.UserId)'.")
    }
    if ([string]$principal.LogonType -ne "ServiceAccount") {
        $errors.Add("principal LogonType must be ServiceAccount; observed '$($principal.LogonType)'.")
    }
    if ([string]$principal.RunLevel -ne "Highest") {
        $errors.Add("principal RunLevel must be Highest; observed '$($principal.RunLevel)'.")
    }

    $hasStartupTrigger = @($Task.Triggers | Where-Object {
        $_.CimClass.CimClassName -eq "MSFT_TaskBootTrigger"
    }).Count -gt 0
    if (-not $hasStartupTrigger) {
        $errors.Add("startup trigger MSFT_TaskBootTrigger is missing.")
    }

    $actions = @($Task.Actions)
    if ($actions.Count -ne 1) {
        $errors.Add("exactly one launcher action is required; observed $($actions.Count).")
    }
    else {
        $registeredAction = $actions[0]
        if (-not [string]::Equals([string]$registeredAction.Execute, $ExpectedExecute, [StringComparison]::OrdinalIgnoreCase)) {
            $errors.Add("action Execute mismatch; expected '$ExpectedExecute', observed '$($registeredAction.Execute)'.")
        }
        if (-not [string]::Equals([string]$registeredAction.Arguments, $ExpectedArguments, [StringComparison]::Ordinal)) {
            $errors.Add("action Arguments mismatch; expected launcher arguments were not read back.")
        }
        if (-not [string]::Equals([string]$registeredAction.WorkingDirectory, $ExpectedWorkingDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            $errors.Add("action WorkingDirectory mismatch; expected '$ExpectedWorkingDirectory', observed '$($registeredAction.WorkingDirectory)'.")
        }
    }

    $settings = $Task.Settings
    if ([int]$settings.RestartCount -ne 3) {
        $errors.Add("RestartCount must be 3; observed '$($settings.RestartCount)'.")
    }
    try {
        if ((ConvertTo-TaskDuration $settings.RestartInterval) -ne [TimeSpan]::FromMinutes(1)) {
            $errors.Add("RestartInterval must be 1 minute; observed '$($settings.RestartInterval)'.")
        }
    }
    catch {
        $errors.Add($_.Exception.Message)
    }
    try {
        if ((ConvertTo-TaskDuration $settings.ExecutionTimeLimit) -ne [TimeSpan]::Zero) {
            $errors.Add("ExecutionTimeLimit must be unlimited (PT0S); observed '$($settings.ExecutionTimeLimit)'.")
        }
    }
    catch {
        $errors.Add($_.Exception.Message)
    }
    if ([string]$settings.MultipleInstances -ne "IgnoreNew") {
        $errors.Add("MultipleInstances must be IgnoreNew; observed '$($settings.MultipleInstances)'.")
    }
    if (-not [bool]$settings.StartWhenAvailable) {
        $errors.Add("StartWhenAvailable must be enabled.")
    }

    return @($errors)
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

    $runtimes = & $DotnetPath --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $hasCoreRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+8\.' }
    $hasAspNetRuntime = $runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App\s+8\.' }
    if (-not $hasCoreRuntime -or -not $hasAspNetRuntime) {
        return $false
    }

    $info = & $DotnetPath --info 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    return ($info -match 'Architecture:\s*x64')
}

function Resolve-Dotnet {
    param(
        [string]$Candidate,
        [string]$StateRoot
    )

    $candidates = @()

    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        $candidates += (Resolve-Path -LiteralPath $Candidate).Path
    }

    $candidates += (Join-Path $StateRoot "dotnet-sdk\dotnet.exe")
    $candidates += (Join-Path $env:LOCALAPPDATA "WindowsOperator\dotnet-sdk\dotnet.exe")
    $candidates += (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    $candidates += (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidatePath in $candidates | Select-Object -Unique) {
        if (Test-DotnetSdk -DotnetPath $candidatePath) {
            return $candidatePath
        }
    }

    throw ".NET 8 x64 SDK plus Core and ASP.NET Core runtimes missing. Run bootstrap.ps1 first or pass -DotnetPath."
}

function Stop-ExistingHost {
    param([string]$HostRoot)

    $task = Get-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue
    if ($task) {
        Write-Step "Stopping existing WindowsOperator.Host task. state=$($task.State)"
        Disable-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue | Out-Null
        Stop-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue

        $deadline = (Get-Date).AddSeconds(10)
        do {
            Start-Sleep -Milliseconds 250
            $task = Get-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue
        } while ($task -and $task.State -eq "Running" -and (Get-Date) -lt $deadline)
    }

    $escapedHostRoot = [System.Management.Automation.WildcardPattern]::Escape($HostRoot)
    $hostProcesses = @(Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and
            (
                (
                    $_.CommandLine -like "*$escapedHostRoot*" -and
                    $_.CommandLine -like "*WindowsOperator.Host.dll*"
                ) -or
                $_.CommandLine -like "*WindowsOperator.Host.exe*"
            )
        })

    $listener = Get-NetTCPConnection -State Listen -LocalPort 43117 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) {
        Write-Step "Found port 43117 listener owner PID=$($listener.OwningProcess)."
        $listenerProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction SilentlyContinue
        if ($listenerProcess) {
            $hostProcesses += $listenerProcess
        }
    }

    foreach ($process in $hostProcesses | Sort-Object ProcessId -Unique) {
        Write-Step "Stopping existing WindowsOperator.Host process PID=$($process.ProcessId)."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }

    $remainingListener = Get-NetTCPConnection -State Listen -LocalPort 43117 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($remainingListener) {
        throw "WindowsOperator.Host listener PID=$($remainingListener.OwningProcess) did not stop."
    }
}

function Disable-OneDriveStartupTasks {
    $tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TaskName -like "OneDrive Startup Task-*" -and
            $_.Principal.UserId -match "(?i)(^|\\)Administrator$"
        })

    foreach ($task in $tasks) {
        Disable-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop | Out-Null
        Write-Step "Disabled duplicate OneDrive startup task $($task.TaskName); Host owns OneDrive lifecycle."
    }
}

function New-RandomPassword {
    $bytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function Convert-ToJsonString {
    param([hashtable]$Value)

    return ($Value | ConvertTo-Json -Depth 8)
}

function New-LocalhostCertificate {
    param(
        [string]$Path,
        [string]$Password
    )

    $friendlyName = "Windows Operator PowerPoint Add-in localhost"
    foreach ($storeName in @("My", "Root")) {
        Get-ChildItem -Path "Cert:\LocalMachine\$storeName" -ErrorAction SilentlyContinue |
            Where-Object { $_.FriendlyName -eq $friendlyName } |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }

    $certificate = New-SelfSignedCertificate `
        -DnsName "localhost" `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -FriendlyName $friendlyName `
        -NotAfter (Get-Date).AddYears(3)

    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    Export-PfxCertificate -Cert $certificate -FilePath $Path -Password $securePassword | Out-Null

    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $rootStore.Add($certificate)
    }
    finally {
        $rootStore.Close()
    }
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
$certRoot = Join-Path $resolvedStateRoot.FullName "certs"
New-Item -ItemType Directory -Path $hostRoot -Force | Out-Null
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
New-Item -ItemType Directory -Path $certRoot -Force | Out-Null

$resolvedDotnetPath = Resolve-Dotnet -Candidate $DotnetPath -StateRoot $resolvedStateRoot.FullName
$recoveryEnvironment = "`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS = `$null`r`n"
if (-not [string]::IsNullOrWhiteSpace($OneDriveRecoveryAllowedComputer)) {
    if (-not [string]::Equals($OneDriveRecoveryAllowedComputer, $requiredOneDriveRecoveryComputer, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OneDrive recovery is restricted to $requiredOneDriveRecoveryComputer."
    }
    if (-not [string]::Equals($OneDriveRecoveryAllowedComputer, $env:COMPUTERNAME, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OneDrive recovery allowlist target '$OneDriveRecoveryAllowedComputer' does not match this computer '$env:COMPUTERNAME'."
    }
    $recoveryEnvironment = @"
`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS = $(Quote-PowerShellLiteral $OneDriveRecoveryAllowedComputer)
"@
    $recoveryEnvironment += "`r`n"
}

Disable-OneDriveStartupTasks

$resolvedExchangeRoot = $null
if (-not [string]::IsNullOrWhiteSpace($ExchangeRoot)) {
    $resolvedExchangeRoot = (New-Item -ItemType Directory -Path $ExchangeRoot -Force).FullName
}

$resolvedHostExchangeRoot = $HostExchangeRoot
if ([string]::IsNullOrWhiteSpace($resolvedHostExchangeRoot)) {
    $resolvedHostExchangeRoot = $resolvedExchangeRoot
}

Stop-ExistingHost -HostRoot $hostRoot

Write-Step "Restoring WindowsOperator.Host packages."
& $resolvedDotnetPath restore $hostProjectPath --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

Write-Step "Cleaning incremental WindowsOperator.Host build outputs."
& $resolvedDotnetPath clean $hostProjectPath -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed."
}

Write-Step "Publishing WindowsOperator.Host."
& $resolvedDotnetPath publish $hostProjectPath -c Debug -o $hostRoot --no-self-contained --disable-build-servers --no-restore -t:Rebuild
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$hostDll = Join-Path $hostRoot "WindowsOperator.Host.dll"
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw "Published Host entry point missing: $hostDll"
}

$addInEnabled = $false
$publishedAddInRoot = Join-Path $hostRoot "powerpoint-addin"
$certPath = Join-Path $certRoot "localhost.pfx"
$certPasswordPath = Join-Path $certRoot "localhost.pfx.password"
$certPassword = $null
$sourceAddInRoot = $PowerPointAddInStaticRoot
if ([string]::IsNullOrWhiteSpace($sourceAddInRoot)) {
    $sourceAddInRoot = Join-Path $RepoRoot "src\WindowsOperator.PowerPointAddIn\dist"
}

if (-not $DisablePowerPointAddIn -and (Test-Path -LiteralPath (Join-Path $sourceAddInRoot "taskpane.html"))) {
    Write-Step "Publishing PowerPoint add-in static files."
    if (Test-Path -LiteralPath $publishedAddInRoot) {
        Remove-Item -LiteralPath $publishedAddInRoot -Recurse -Force
    }

    Copy-Item -LiteralPath $sourceAddInRoot -Destination $publishedAddInRoot -Recurse -Force

    try {
        $certPassword = New-RandomPassword
        New-LocalhostCertificate -Path $certPath -Password $certPassword
        if (-not (Test-Path -LiteralPath $certPath)) {
            throw "certificate export failed."
        }

        Set-Content -LiteralPath $certPasswordPath -Value $certPassword -Encoding UTF8
        $addInEnabled = $true
    }
    catch {
        Write-Step "PowerPoint add-in disabled because HTTPS certificate provisioning failed: $($_.Exception.Message)"
        $addInEnabled = $false
    }
}
else {
    Write-Step "PowerPoint add-in static files not found or disabled; Host REST will run without add-in HTTPS binding."
}

$localConfigPath = Join-Path $runRoot "host.appsettings.Local.json"
$localConfig = @{
    Operator = @{
        bindAddress = "127.0.0.1"
        restPort = 43117
        enableMcpStdio = $false
    }
    DesktopAgent = @{
        baseUrl = "http://127.0.0.1:43119"
    }
    PowerPointAddIn = @{
        enabled = $addInEnabled
        baseUrl = $PowerPointAddInBaseUrl
        staticRoot = $publishedAddInRoot
    }
}
if ($resolvedExchangeRoot) {
    $localConfig.Workbench = @{
        exchangeRoot = $resolvedExchangeRoot
        hostExchangeRoot = $resolvedHostExchangeRoot
    }
}
if ($addInEnabled) {
    $localConfig.Kestrel = @{
        Certificates = @{
            Default = @{
                Path = $certPath
                Password = $certPassword
            }
        }
    }
}

Convert-ToJsonString -Value $localConfig | Set-Content -LiteralPath $localConfigPath -Encoding UTF8

$hostDll = Join-Path $hostRoot "WindowsOperator.Host.dll"
$launcherPath = Join-Path $runRoot "start-host.ps1"
$launcherContent = @"
`$ErrorActionPreference = "Stop"
`$env:WINDOWS_OPERATOR_HOST_STATE_ROOT = $(Quote-PowerShellLiteral $resolvedStateRoot.FullName)
$recoveryEnvironment`$env:WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_USER = 'Administrator'
& $(Quote-PowerShellLiteral $resolvedDotnetPath) $(Quote-PowerShellLiteral $hostDll)
exit `$LASTEXITCODE
"@
$launcherContent | Set-Content -LiteralPath $launcherPath -Encoding UTF8

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Quote-Argument $launcherPath)
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $hostRoot
$trigger = New-ScheduledTaskTrigger -AtStartup
if ($trigger.PSObject.Properties.Name -contains "Delay") {
    $trigger.Delay = "PT30S"
}
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
$registeredTask = Get-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction Stop
$taskValidationErrors = @(Get-HostScheduledTaskValidationErrors `
    -Task $registeredTask `
    -ExpectedExecute $action.Execute `
    -ExpectedArguments $action.Arguments `
    -ExpectedWorkingDirectory $action.WorkingDirectory)
if ($taskValidationErrors.Count -gt 0) {
    throw "WindowsOperator.Host registration policy validation failed: $($taskValidationErrors -join ' ')"
}
if ($DeferStart) {
    Write-Step "Registered task WindowsOperator.Host as SYSTEM; start deferred until Agent publication completes. HostRoot=$hostRoot"
}
else {
    Start-ScheduledTask -TaskName "WindowsOperator.Host"
    Write-Step "Registered and started task WindowsOperator.Host as SYSTEM. HostRoot=$hostRoot"
}
