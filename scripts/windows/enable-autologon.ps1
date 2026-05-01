[CmdletBinding()]
param(
    [string]$UserName = "Administrator",

    [string]$Domain = $env:COMPUTERNAME,

    [string]$Password,

    [switch]$PasswordFromStdin,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PasswordFromStdin) {
    $Password = [Console]::In.ReadToEnd().TrimEnd("`r", "`n")
}

if ([string]::IsNullOrEmpty($Password)) {
    throw "Password missing. Pass -Password or -PasswordFromStdin."
}

$winlogonPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
if (-not (Test-Path -LiteralPath $winlogonPath)) {
    throw "Winlogon registry path missing: $winlogonPath"
}

Set-ItemProperty -LiteralPath $winlogonPath -Name "AutoAdminLogon" -Type String -Value "1"
Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultUserName" -Type String -Value $UserName
Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultDomainName" -Type String -Value $Domain
Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultPassword" -Type String -Value $Password

if ($Force) {
    Set-ItemProperty -LiteralPath $winlogonPath -Name "ForceAutoLogon" -Type String -Value "1"
} else {
    Set-ItemProperty -LiteralPath $winlogonPath -Name "ForceAutoLogon" -Type String -Value "0"
}

$terminalServerPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server"
if (Test-Path -LiteralPath $terminalServerPath) {
    Set-ItemProperty -LiteralPath $terminalServerPath -Name "fSingleSessionPerUser" -Type DWord -Value 0
}

Write-Host "[autologon] Enabled for $Domain\$UserName"
