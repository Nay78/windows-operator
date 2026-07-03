[CmdletBinding()]
param(
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[codex-config-sync] $Message"
}

function Escape-TomlBasicString {
    param([string]$Value)
    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function Add-Line {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Line = ""
    )

    [void]$Lines.Add($Line)
}

function Get-SectionBlock {
    param(
        [string[]]$Content,
        [string]$Header
    )

    $start = -1
    for ($i = 0; $i -lt $Content.Count; $i++) {
        if ($Content[$i].Trim() -eq $Header) {
            $start = $i
            break
        }
    }

    if ($start -lt 0) {
        return @()
    }

    $end = $Content.Count
    for ($i = $start + 1; $i -lt $Content.Count; $i++) {
        if ($Content[$i] -match "^\s*\[") {
            $end = $i
            break
        }
    }

    return @($Content[$start..($end - 1)])
}

function Get-TopLevelAssignment {
    param(
        [string[]]$Content,
        [string]$Name
    )

    foreach ($line in $Content) {
        if ($line -match "^\s*\[") {
            break
        }

        if ($line -match ("^\s*" + [regex]::Escape($Name) + "\s*=")) {
            return $line
        }
    }

    return $null
}

function Add-Project {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Projects,
        [string]$Path,
        [bool]$NetworkAccess
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $key = $Path.ToLowerInvariant()
    if (-not $Projects.Contains($key)) {
        $Projects.Add($key, [pscustomobject]@{
                Path = $Path
                NetworkAccess = $NetworkAccess
            })
    }
    elseif ($NetworkAccess) {
        $Projects[$key].NetworkAccess = $true
    }
}

function Add-ExistingProjects {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Projects,
        [string[]]$Content
    )

    foreach ($line in $Content) {
        if ($line -match "^\[projects\.(?:`"([^`"]+)`"|'([^']+)')\]") {
            $projectPath = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
            Add-Project -Projects $Projects -Path $projectPath -NetworkAccess:$false
        }
    }
}

function Add-MiniMappedProjects {
    param([System.Collections.Specialized.OrderedDictionary]$Projects)

    $userProfile = $env:USERPROFILE
    Add-Project -Projects $Projects -Path "C:\src" -NetworkAccess:$true
    Add-Project -Projects $Projects -Path (Join-Path $userProfile "projects") -NetworkAccess:$true
    Add-Project -Projects $Projects -Path "C:\src\windows-operator" -NetworkAccess:$true
    Add-Project -Projects $Projects -Path $userProfile -NetworkAccess:$false
    Add-Project -Projects $Projects -Path (Join-Path $userProfile "nixos") -NetworkAccess:$true
    Add-Project -Projects $Projects -Path (Join-Path $userProfile "proj") -NetworkAccess:$false

    foreach ($relativePath in @(
            "proj\hubris",
            "proj\mpas",
            "proj\pit-operator",
            "proj\pit-viz",
            "proj\pitviz-cs",
            "proj\pitviz-odin",
            "proj\pitviz-three",
            "proj\windows-operator",
            "proj\windows-teams-recorder",
            "proj\servers",
            "proj\servers\drive",
            "proj\servers\forms",
            "proj\servers\jams",
            "proj\servers\whatsapp",
            "Desktop\Centinela",
            "archive\win-automation",
            ".pi\agent"
        )) {
        Add-Project -Projects $Projects -Path (Join-Path $userProfile $relativePath) -NetworkAccess:$false
    }
}

$resolvedCodexHome = $CodexHome
New-Item -ItemType Directory -Path $resolvedCodexHome -Force | Out-Null
$resolvedCodexHome = (Resolve-Path -LiteralPath $resolvedCodexHome).Path
$configPath = Join-Path $resolvedCodexHome "config.toml"
if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Codex config missing: $configPath"
}

$existing = @(Get-Content -LiteralPath $configPath)
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$backupPath = Join-Path $resolvedCodexHome "config.toml.backup-sync-$timestamp"
Copy-Item -LiteralPath $configPath -Destination $backupPath -Force
Write-Step "Backed up config to $backupPath."

$projects = [System.Collections.Specialized.OrderedDictionary]::new()
Add-ExistingProjects -Projects $projects -Content $existing
Add-MiniMappedProjects -Projects $projects

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($line in @(
        'model = "gpt-5.5"',
        'model_auto_compact_token_limit = 200000',
        'model_reasoning_effort = "xhigh"',
        'personality = "pragmatic"',
        'approval_policy = "never"',
        'sandbox_mode = "danger-full-access"',
        'plan_mode_reasoning_effort = "xhigh"',
        'service_tier = "default"'
    )) {
    Add-Line -Lines $lines -Line $line
}

$notify = Get-TopLevelAssignment -Content $existing -Name "notify"
if ($notify) {
    Add-Line -Lines $lines
    Add-Line -Lines $lines -Line $notify
}

Add-Line -Lines $lines
foreach ($entry in $projects.Values) {
    Add-Line -Lines $lines -Line ('[projects."{0}"]' -f (Escape-TomlBasicString -Value $entry.Path))
    Add-Line -Lines $lines -Line 'trust_level = "trusted"'
    if ($entry.NetworkAccess) {
        Add-Line -Lines $lines -Line 'network_access = true'
    }
    Add-Line -Lines $lines
}

foreach ($line in @(
        '[notice]',
        'hide_rate_limit_model_nudge = true',
        'fast_default_opt_out = true',
        '',
        '[notice.model_migrations]',
        '',
        '[features]',
        'multi_agent = true',
        'apps = true',
        'shell_snapshot = true',
        'js_repl = false',
        'terminal_resize_reflow = true',
        'goals = true',
        'prevent_idle_sleep = true',
        '',
        '[agents]',
        'max_depth = 2',
        '',
        '[tui]',
        'alternate_screen = "always"',
        'status_line = ["model-with-reasoning", "context-remaining", "current-dir", "weekly-limit", "codex-version"]',
        'theme = "monokai-extended-origin"',
        'status_line_use_colors = true',
        '',
        '[tui.keymap.pager]',
        'page_up = "page-up"',
        'page_down = "page-down"',
        ''
    )) {
    Add-Line -Lines $lines -Line $line
}

$modelNux = Get-SectionBlock -Content $existing -Header '[tui.model_availability_nux]'
if ($modelNux.Count -gt 0) {
    foreach ($line in $modelNux) {
        Add-Line -Lines $lines -Line $line
    }
}
else {
    Add-Line -Lines $lines -Line '[tui.model_availability_nux]'
}
Add-Line -Lines $lines

$primaryRuntime = Join-Path $resolvedCodexHome "plugins\cache\openai-primary-runtime"
if (Test-Path -LiteralPath $primaryRuntime) {
    foreach ($plugin in @("documents", "spreadsheets", "presentations")) {
        Add-Line -Lines $lines -Line ('[plugins."{0}@openai-primary-runtime"]' -f $plugin)
        Add-Line -Lines $lines -Line 'enabled = true'
        Add-Line -Lines $lines
    }
}

foreach ($line in @(
        '[mcp_servers.openaiDeveloperDocs]',
        'url = "https://developers.openai.com/mcp"',
        '',
        '[mcp_servers.plan]',
        'url = "https://plan.agent-native.com/_agent-native/mcp"',
        ''
    )) {
    Add-Line -Lines $lines -Line $line
}

foreach ($header in @(
        '[mcp_servers.node_repl]',
        '[mcp_servers.node_repl.env]',
        '[windows]',
        '[desktop]',
        '[marketplaces.openai-bundled]',
        '[plugins."browser@openai-bundled"]',
        '[plugins."chrome@openai-bundled"]',
        '[plugins."computer-use@openai-bundled"]'
    )) {
    $block = Get-SectionBlock -Content $existing -Header $header
    if ($block.Count -gt 0) {
        foreach ($line in $block) {
            Add-Line -Lines $lines -Line $line
        }
        Add-Line -Lines $lines
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, (($lines -join [Environment]::NewLine) + [Environment]::NewLine), $utf8NoBom)

$summary = [pscustomobject]@{
    status = "synced"
    codexHome = $resolvedCodexHome
    configPath = $configPath
    backupPath = $backupPath
    lineCount = (Get-Content -LiteralPath $configPath).Count
    planMcp = (Select-String -LiteralPath $configPath -Pattern '^\[mcp_servers\.plan\]' -Quiet)
    documentsPlugin = (Select-String -LiteralPath $configPath -Pattern '^\[plugins\."documents@openai-primary-runtime"\]' -Quiet)
    spreadsheetsPlugin = (Select-String -LiteralPath $configPath -Pattern '^\[plugins\."spreadsheets@openai-primary-runtime"\]' -Quiet)
    presentationsPlugin = (Select-String -LiteralPath $configPath -Pattern '^\[plugins\."presentations@openai-primary-runtime"\]' -Quiet)
    preservedNodeRepl = (Select-String -LiteralPath $configPath -Pattern "^command = 'C:\\Users\\Alejg\\AppData\\Local\\OpenAI\\Codex\\runtimes\\cua_node" -Quiet)
    preservedWindowsSandbox = (Select-String -LiteralPath $configPath -Pattern '^\[windows\]' -Quiet)
    cavemanPluginInstalled = (Test-Path -LiteralPath (Join-Path $resolvedCodexHome "plugins\cache\caveman-repo"))
}

Write-Step ($summary | ConvertTo-Json -Compress)
