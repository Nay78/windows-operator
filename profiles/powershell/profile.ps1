# Repo-owned interactive PowerShell profile for Windows Operator targets.
# Windows user profile files dot-source this file through a managed block.

$script:WindowsOperatorRepoRoot = $env:WINDOWS_OPERATOR_REPO_ROOT
if ([string]::IsNullOrWhiteSpace($script:WindowsOperatorRepoRoot)) {
    $script:WindowsOperatorRepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

function wo-root {
    $script:WindowsOperatorRepoRoot
}

function croot {
    Set-Location -LiteralPath $script:WindowsOperatorRepoRoot
}

function codexw {
    Set-Location -LiteralPath $script:WindowsOperatorRepoRoot
    codex @args
}

function ll {
    Get-ChildItem @args
}

function la {
    Get-ChildItem -Force @args
}

function mkcd {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Location -LiteralPath $Path
}

if (Get-Command git -ErrorAction SilentlyContinue) {
    Set-Alias -Name g -Value git -Scope Global -Option AllScope -Force
}

Set-Alias -Name a -Value codex -Scope Global -Option AllScope -Force
Set-Alias -Name y -Value yazi -Scope Global -Option AllScope -Force
Set-Alias -Name which -Value Get-Command -Scope Global -Option AllScope -Force
