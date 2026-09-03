[CmdletBinding()]
param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot "register-host-autostart.ps1")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $ScriptPath,
    [ref]$tokens,
    [ref]$parseErrors
)
Assert-True -Condition ($parseErrors.Count -eq 0) -Message "PowerShell parser reported errors."

$functionNames = @("ConvertTo-TaskDuration", "Get-HostScheduledTaskValidationErrors")
foreach ($functionName in $functionNames) {
    $definition = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName
    }, $true) | Select-Object -First 1
    Assert-True -Condition ($null -ne $definition) -Message "Missing $functionName."
    Invoke-Expression $definition.Extent.Text
}

$expectedExecute = "powershell.exe"
$expectedArguments = '-NoProfile -ExecutionPolicy Bypass -File "C:\ProgramData\WindowsOperator\run\start-host.ps1"'
$expectedWorkingDirectory = "C:\ProgramData\WindowsOperator\host"
$validTask = [pscustomobject]@{
    Principal = [pscustomobject]@{ UserId = "NT AUTHORITY\SYSTEM"; LogonType = "ServiceAccount"; RunLevel = "Highest" }
    Triggers = @([pscustomobject]@{ CimClass = [pscustomobject]@{ CimClassName = "MSFT_TaskBootTrigger" } })
    Actions = @([pscustomobject]@{ Execute = $expectedExecute; Arguments = $expectedArguments; WorkingDirectory = $expectedWorkingDirectory })
    Settings = [pscustomobject]@{ RestartCount = 3; RestartInterval = "PT1M"; ExecutionTimeLimit = "PT0S"; MultipleInstances = "IgnoreNew"; StartWhenAvailable = $true }
}

Assert-True -Condition ((ConvertTo-TaskDuration "PT1M") -eq [TimeSpan]::FromMinutes(1)) -Message "XML restart duration did not normalize."
Assert-True -Condition ((ConvertTo-TaskDuration ([TimeSpan]::FromMinutes(1))) -eq [TimeSpan]::FromMinutes(1)) -Message "CIM TimeSpan restart duration did not normalize."
Assert-True -Condition ((ConvertTo-TaskDuration "00:01:00") -eq [TimeSpan]::FromMinutes(1)) -Message "CIM string restart duration did not normalize."
Assert-True -Condition ((ConvertTo-TaskDuration "PT0S") -eq [TimeSpan]::Zero) -Message "Unlimited execution duration did not normalize."

$validErrors = @(Get-HostScheduledTaskValidationErrors -Task $validTask -ExpectedExecute $expectedExecute -ExpectedArguments $expectedArguments -ExpectedWorkingDirectory $expectedWorkingDirectory)
Assert-True -Condition ($validErrors.Count -eq 0) -Message "Valid task rejected: $($validErrors -join ' ')"

$missingErrors = @(Get-HostScheduledTaskValidationErrors -Task $null -ExpectedExecute $expectedExecute -ExpectedArguments $expectedArguments -ExpectedWorkingDirectory $expectedWorkingDirectory)
Assert-True -Condition ($missingErrors -match "missing after registration") -Message "Missing task was not rejected."

$validTask.Settings.RestartCount = 0
$validTask.Settings.RestartInterval = "PT5M"
$validTask.Settings.ExecutionTimeLimit = "PT5M"
$validTask.Settings.MultipleInstances = "Parallel"
$validTask.Settings.StartWhenAvailable = $false
$validTask.Principal.UserId = "Administrator"
$validTask.Principal.LogonType = "Interactive"
$validTask.Principal.RunLevel = "Limited"
$validTask.Triggers = @()
$validTask.Actions[0].Execute = "cmd.exe"
$validTask.Actions[0].WorkingDirectory = "C:\Temp"
$mismatchErrors = @(Get-HostScheduledTaskValidationErrors -Task $validTask -ExpectedExecute $expectedExecute -ExpectedArguments $expectedArguments -ExpectedWorkingDirectory $expectedWorkingDirectory)
Assert-True -Condition ($mismatchErrors -match "principal UserId") -Message "Principal user mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "principal LogonType") -Message "Principal logon mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "principal RunLevel") -Message "Principal run level mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "startup trigger") -Message "Startup trigger mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "action Execute") -Message "Action execute mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "action WorkingDirectory") -Message "Action working directory mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "RestartCount") -Message "RestartCount mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "RestartInterval") -Message "RestartInterval mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "ExecutionTimeLimit") -Message "ExecutionTimeLimit mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "MultipleInstances") -Message "MultipleInstances mismatch was not rejected."
Assert-True -Condition ($mismatchErrors -match "StartWhenAvailable") -Message "StartWhenAvailable mismatch was not rejected."

"host autostart registration tests passed"
