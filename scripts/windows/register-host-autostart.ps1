[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$StateRoot = (Join-Path $env:ProgramData "WindowsOperator"),

    [string]$DotnetPath = "dotnet.exe",

    [string]$PowerPointAddInBaseUrl = "https://localhost:3003",

    [string]$PowerPointAddInStaticRoot = "",

    [switch]$DisablePowerPointAddIn
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

function Quote-PowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
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

function Stop-ExistingHost {
    param([string]$HostRoot)

    $task = Get-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue
    if ($task) {
        Write-Step "Stopping existing WindowsOperator.Host task."
        Stop-ScheduledTask -TaskName "WindowsOperator.Host" -ErrorAction SilentlyContinue
    }

    $escapedHostRoot = [System.Management.Automation.WildcardPattern]::Escape($HostRoot)
    $hostProcesses = Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -like "*$escapedHostRoot*" -and
            $_.CommandLine -like "*WindowsOperator.Host.dll*"
        }

    foreach ($process in $hostProcesses) {
        Write-Step "Stopping existing WindowsOperator.Host process PID=$($process.ProcessId)."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
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

$resolvedDotnetPath = Resolve-Dotnet -Candidate $DotnetPath

Stop-ExistingHost -HostRoot $hostRoot

Write-Step "Publishing WindowsOperator.Host."
& $resolvedDotnetPath publish $hostProjectPath -c Debug -o $hostRoot --no-self-contained
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
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
