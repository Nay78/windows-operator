[CmdletBinding()]
param(
    [ValidateSet("Configure", "Status", "Disable")]
    [string]$Action = "Status",

    [string]$AuditRoot = (Join-Path $env:ProgramData "WindowsOperator\autologon")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$targetComputer = "WIN-UUKQS009K4J"
$targetUser = "Administrator"
$winlogonPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$terminalServerPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server"
$agentTaskName = "WindowsOperator.Agent"

function Assert-TargetComputer {
    if (-not [string]::Equals($env:COMPUTERNAME, $targetComputer, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Auto-logon is restricted to $targetComputer; current computer is $env:COMPUTERNAME."
    }
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Configure and Disable require an elevated Administrator shell."
    }
}

function Get-RegistryValue {
    param(
        [string]$Path,
        [string]$Name
    )

    try {
        $item = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop
        return $item.$Name
    }
    catch [System.Management.Automation.PSArgumentException] {
        return $null
    }
}

function Test-RegistryFlag {
    param(
        [object]$Value
    )

    return [string]::Equals([string]$Value, "1", [StringComparison]::OrdinalIgnoreCase) -or
        ($Value -is [int] -and $Value -eq 1)
}

function Get-SafeUserName {
    param([object]$Value)

    if ($null -eq $Value) {
        return $null
    }

    return [string]$Value
}

function Get-SessionEvidence {
    $sessions = [System.Collections.Generic.List[object]]::new()
    $quser = Get-Command quser.exe -ErrorAction SilentlyContinue
    if ($null -eq $quser) {
        return @($sessions)
    }

    $lines = & $quser.Source 2>$null
    foreach ($line in $lines) {
        $match = [regex]::Match(
            [string]$line,
            '^\s*>?(?<user>\S+)\s+(?:(?<session>\S+)\s+)?(?<id>\d+)\s+(?<state>\S+)')
        if (-not $match.Success -or
            -not [string]::Equals($match.Groups["user"].Value, $targetUser, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $sessionName = $match.Groups["session"].Value
        $protocol = if ($sessionName -like "rdp-*") { "RDP" } elseif ($sessionName -eq "console") { "console" } else { "unknown" }
        $sessions.Add([ordered]@{
            user = $match.Groups["user"].Value
            sessionName = $sessionName
            sessionId = [int]$match.Groups["id"].Value
            state = $match.Groups["state"].Value
            protocol = $protocol
        })
    }

    return @($sessions)
}

function Get-ProcessEvidence {
    $processes = [System.Collections.Generic.List[object]]::new()
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    foreach ($process in $all) {
        $kind = $null
        if ($process.Name -ieq "OneDrive.exe") {
            $kind = "OneDrive"
        }
        elseif ($process.Name -ieq "WindowsOperator.Agent.exe" -or
            ($process.Name -ieq "dotnet.exe" -and $process.CommandLine -like "*WindowsOperator.Agent.dll*")) {
            $kind = "Agent"
        }
        elseif (($process.Name -ieq "WindowsOperator.Host.exe" -or
                $process.Name -ieq "dotnet.exe") -and
            $process.CommandLine -like "*WindowsOperator.Host.dll*") {
            $kind = "Host"
        }

        if ($null -eq $kind) {
            continue
        }

        $owner = $null
        try {
            $ownerResult = Invoke-CimMethod -InputObject $process -MethodName GetOwner -ErrorAction Stop
            if ($ownerResult.ReturnValue -eq 0) {
                $owner = $ownerResult.User
            }
        }
        catch {
            $owner = $null
        }

        $processes.Add([ordered]@{
            kind = $kind
            processId = [int]$process.ProcessId
            sessionId = [int]$process.SessionId
            owner = $owner
        })
    }

    return @($processes)
}

function Get-TaskEvidence {
    param([object]$Task)

    if ($null -eq $Task) {
        return [ordered]@{ exists = $false }
    }

    return [ordered]@{
        exists = $true
        name = $Task.TaskName
        state = [string]$Task.State
        enabled = -not [string]::Equals([string]$Task.State, "Disabled", [StringComparison]::OrdinalIgnoreCase)
        principal = $Task.Principal.UserId
    }
}

function Get-LauncherEvidence {
    $agentTask = Get-ScheduledTask -TaskName $agentTaskName -ErrorAction SilentlyContinue
    $oneDriveTasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TaskName -like "OneDrive Startup Task-*" -and
            $_.Principal.UserId -match "(?i)(^|\\)Administrator$"
        } |
        ForEach-Object { Get-TaskEvidence -Task $_ })

    return [ordered]@{
        agentTask = Get-TaskEvidence -Task $agentTask
        oneDriveStartupTasks = $oneDriveTasks
        hostOwnsLifecycle = $true
    }
}

function Get-SessionIds {
    param([object[]]$Items)

    return @($Items |
        Where-Object { $null -ne $_ -and $null -ne $_.PSObject.Properties["sessionId"] } |
        ForEach-Object { [int]$_.sessionId } |
        Select-Object -Unique)
}

function Get-Status {
    if (-not (Test-Path -LiteralPath $winlogonPath)) {
        throw "Winlogon registry path missing: $winlogonPath"
    }

    $processes = Get-ProcessEvidence
    $hostProcesses = @($processes | Where-Object { $_.kind -eq "Host" })
    $agentProcesses = @($processes | Where-Object { $_.kind -eq "Agent" })
    $oneDriveProcesses = @($processes | Where-Object { $_.kind -eq "OneDrive" })
    $administratorSessions = Get-SessionEvidence

    return [ordered]@{
        action = "Status"
        computerName = $env:COMPUTERNAME
        targetComputer = $targetComputer
        targetAccount = "$env:COMPUTERNAME\$targetUser"
        policy = [ordered]@{
            autoAdminLogonEnabled = Test-RegistryFlag (Get-RegistryValue $winlogonPath "AutoAdminLogon")
            forceAutoLogonEnabled = Test-RegistryFlag (Get-RegistryValue $winlogonPath "ForceAutoLogon")
            defaultUserName = Get-SafeUserName (Get-RegistryValue $winlogonPath "DefaultUserName")
            defaultDomainName = Get-SafeUserName (Get-RegistryValue $winlogonPath "DefaultDomainName")
            defaultPasswordPresent = $null -ne (Get-RegistryValue $winlogonPath "DefaultPassword")
            singleSessionPerUser = Test-RegistryFlag (Get-RegistryValue $terminalServerPath "fSingleSessionPerUser")
        }
        sessionEvidence = [ordered]@{
            administratorSessions = $administratorSessions
            hostProcesses = $hostProcesses
            agentProcesses = $agentProcesses
            oneDriveProcesses = $oneDriveProcesses
            hostRunsInSession0 = @($hostProcesses | Where-Object { $_.sessionId -eq 0 }).Count -gt 0
            agentSessionIds = Get-SessionIds $agentProcesses
            oneDriveSessionIds = Get-SessionIds $oneDriveProcesses
        }
        launchers = Get-LauncherEvidence
        auditRoot = $AuditRoot
        observedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
}

function Protect-AuditPath {
    New-Item -ItemType Directory -Path $AuditRoot -Force | Out-Null
    $acl = Get-Acl -LiteralPath $AuditRoot
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($accessRule in @($acl.Access)) {
        $acl.RemoveAccessRule($accessRule) | Out-Null
    }
    foreach ($identity in @(
            [System.Security.Principal.SecurityIdentifier]::new("S-1-5-18"),
            [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544"))) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $AuditRoot -AclObject $acl
}

function Write-AuditRecord {
    param(
        [string]$ActionName,
        [object]$Before,
        [object]$After,
        [string[]]$LauncherActions
    )

    Protect-AuditPath
    $record = [ordered]@{
        action = $ActionName
        computerName = $env:COMPUTERNAME
        targetAccount = "$env:COMPUTERNAME\$targetUser"
        beforePolicy = $Before.policy
        afterPolicy = $After.policy
        launcherActions = $LauncherActions
        recordedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    $auditPath = Join-Path $AuditRoot "actions.jsonl"
    $record | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath $auditPath -Encoding UTF8
    $fileAcl = Get-Acl -LiteralPath $auditPath
    $fileAcl.SetAccessRuleProtection($true, $false)
    foreach ($accessRule in @($fileAcl.Access)) {
        $fileAcl.RemoveAccessRule($accessRule) | Out-Null
    }
    foreach ($identity in @(
            [System.Security.Principal.SecurityIdentifier]::new("S-1-5-18"),
            [System.Security.Principal.SecurityIdentifier]::new("S-1-5-32-544"))) {
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $fileAcl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $auditPath -AclObject $fileAcl
}

function Disable-DuplicateLaunchers {
    $actions = [System.Collections.Generic.List[string]]::new()
    $agentTask = Get-ScheduledTask -TaskName $agentTaskName -ErrorAction SilentlyContinue
    if ($null -ne $agentTask -and -not [string]::Equals([string]$agentTask.State, "Disabled", [StringComparison]::OrdinalIgnoreCase)) {
        Disable-ScheduledTask -TaskName $agentTaskName -ErrorAction Stop | Out-Null
        $actions.Add("disabled_WindowsOperator.Agent_task")
    }

    $oneDriveTasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TaskName -like "OneDrive Startup Task-*" -and
            $_.Principal.UserId -match "(?i)(^|\\)Administrator$"
        })
    foreach ($task in $oneDriveTasks) {
        if (-not [string]::Equals([string]$task.State, "Disabled", [StringComparison]::OrdinalIgnoreCase)) {
            Disable-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop | Out-Null
            $actions.Add("disabled_$($task.TaskName)")
        }
    }

    if ($actions.Count -eq 0) {
        $actions.Add("duplicate_launchers_already_disabled_or_absent")
    }
    return @($actions)
}

function Get-InteractiveAdministratorCredential {
    $credential = Get-Credential -UserName "$env:COMPUTERNAME\$targetUser" -Message "Enter the existing Administrator password for Windows auto-logon. It is used only in this local process and is never recorded."
    if ($null -eq $credential -or $null -eq $credential.Password -or $credential.UserName -notmatch "(?i)(^|\\)Administrator$") {
        throw "Interactive credential must be for the existing Administrator account."
    }

    return $credential
}

function Set-DefaultPasswordFromCredential {
    param([System.Management.Automation.PSCredential]$Credential)

    $password = $null
    $passwordPointer = [IntPtr]::Zero
    try {
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Credential.Password)
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultPassword" -Type String -Value $password
    }
    finally {
        if ($passwordPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
        $password = $null
    }
}

function Invoke-Configure {
    Assert-Administrator
    $before = Get-Status
    $existingUser = $before.policy.defaultUserName
    if ($before.policy.autoAdminLogonEnabled -and
        $null -ne $existingUser -and
        -not [string]::Equals($existingUser, $targetUser, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Auto-logon is already enabled for a different account; refusing to overwrite it."
    }

    $credential = Get-InteractiveAdministratorCredential
    try {
        $launcherActions = Disable-DuplicateLaunchers
        Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultUserName" -Type String -Value $targetUser
        Set-ItemProperty -LiteralPath $winlogonPath -Name "DefaultDomainName" -Type String -Value $env:COMPUTERNAME
        Set-ItemProperty -LiteralPath $winlogonPath -Name "ForceAutoLogon" -Type String -Value "0"
        Set-ItemProperty -LiteralPath $terminalServerPath -Name "fSingleSessionPerUser" -Type DWord -Value 1
        Set-DefaultPasswordFromCredential -Credential $credential
        Set-ItemProperty -LiteralPath $winlogonPath -Name "AutoAdminLogon" -Type String -Value "1"
    }
    finally {
        $credential = $null
    }

    $after = Get-Status
    Write-AuditRecord -ActionName "Configure" -Before $before -After $after -LauncherActions $launcherActions
    $after.action = "Configure"
    $after.actions = @("configured_interactive_administrator_auto_logon") + $launcherActions
    return $after
}

function Invoke-Disable {
    Assert-Administrator
    $before = Get-Status
    $existingUser = $before.policy.defaultUserName
    if ($before.policy.autoAdminLogonEnabled -and
        $null -ne $existingUser -and
        -not [string]::Equals($existingUser, $targetUser, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Auto-logon is enabled for a different account; refusing to modify it."
    }

    $launcherActions = Disable-DuplicateLaunchers
    Set-ItemProperty -LiteralPath $winlogonPath -Name "AutoAdminLogon" -Type String -Value "0"
    Set-ItemProperty -LiteralPath $winlogonPath -Name "ForceAutoLogon" -Type String -Value "0"
    Remove-ItemProperty -LiteralPath $winlogonPath -Name "DefaultPassword" -ErrorAction SilentlyContinue
    Set-ItemProperty -LiteralPath $terminalServerPath -Name "fSingleSessionPerUser" -Type DWord -Value 1

    $after = Get-Status
    Write-AuditRecord -ActionName "Disable" -Before $before -After $after -LauncherActions $launcherActions
    $after.action = "Disable"
    $after.actions = @("disabled_interactive_administrator_auto_log_on") + $launcherActions
    return $after
}

Assert-TargetComputer
if (-not (Test-Path -LiteralPath $winlogonPath)) {
    throw "Winlogon registry path missing: $winlogonPath"
}

$result = switch ($Action) {
    "Configure" { Invoke-Configure }
    "Disable" { Invoke-Disable }
    default { Get-Status }
}

$result | ConvertTo-Json -Depth 10
