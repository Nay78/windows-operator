[CmdletBinding()]
param(
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex"),

    [string[]]$TrustedProjectRoots = @("C:\src", (Join-Path $env:USERPROFILE "projects")),

    [string]$TrustedProjectRootsText = "",

    [switch]$ForceStaticProfile,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[codex-profile] $Message"
}

function Escape-TomlBasicString {
    param([string]$Value)
    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Content,

        [switch]$Overwrite
    )

    if ((Test-Path -LiteralPath $Path) -and -not $Overwrite) {
        Write-Step "Keeping existing $(Split-Path -Leaf $Path)."
        return
    }

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content + [Environment]::NewLine, $utf8NoBom)
    Write-Step "Wrote $Path."
}

function New-CodexConfig {
    param([string[]]$ProjectRoots)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('model = "gpt-5.5"')
    $lines.Add('model_auto_compact_token_limit = 200000')
    $lines.Add('model_reasoning_effort = "xhigh"')
    $lines.Add('personality = "pragmatic"')
    $lines.Add('approval_policy = "never"')
    $lines.Add('sandbox_mode = "danger-full-access"')
    $lines.Add('plan_mode_reasoning_effort = "xhigh"')
    $lines.Add('service_tier = "default"')
    $lines.Add('')

    foreach ($root in $ProjectRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        $expanded = [Environment]::ExpandEnvironmentVariables($root)
        $lines.Add(("[projects.""{0}""]" -f (Escape-TomlBasicString -Value $expanded)))
        $lines.Add('trust_level = "trusted"')
        $lines.Add('network_access = true')
        $lines.Add('')
    }

    $lines.Add('[notice]')
    $lines.Add('hide_rate_limit_model_nudge = true')
    $lines.Add('fast_default_opt_out = true')
    $lines.Add('')
    $lines.Add('[features]')
    $lines.Add('multi_agent = true')
    $lines.Add('apps = true')
    $lines.Add('shell_snapshot = true')
    $lines.Add('js_repl = false')
    $lines.Add('terminal_resize_reflow = true')
    $lines.Add('goals = true')
    $lines.Add('prevent_idle_sleep = true')
    $lines.Add('')
    $lines.Add('[agents]')
    $lines.Add('max_depth = 2')
    $lines.Add('')
    $lines.Add('[tui]')
    $lines.Add('alternate_screen = "always"')
    $lines.Add('status_line = ["model-with-reasoning", "context-remaining", "current-dir", "weekly-limit", "codex-version"]')
    $lines.Add('theme = "monokai-extended-origin"')
    $lines.Add('status_line_use_colors = true')
    $lines.Add('')
    $lines.Add('[tui.keymap.pager]')
    $lines.Add('page_up = "page-up"')
    $lines.Add('page_down = "page-down"')
    $lines.Add('')
    $lines.Add('[mcp_servers.openaiDeveloperDocs]')
    $lines.Add('url = "https://developers.openai.com/mcp"')
    $lines.Add('')

    return ($lines -join [Environment]::NewLine)
}

$resolvedCodexHome = $CodexHome
New-Item -ItemType Directory -Path $resolvedCodexHome -Force | Out-Null
$resolvedCodexHome = (Resolve-Path -LiteralPath $resolvedCodexHome).Path

$configPath = Join-Path $resolvedCodexHome "config.toml"
$agentsPath = Join-Path $resolvedCodexHome "AGENTS.md"
$rulesDir = Join-Path $resolvedCodexHome "rules"
$agentDir = Join-Path $resolvedCodexHome "agents"
$allTrustedProjectRoots = @($TrustedProjectRoots)
if (-not [string]::IsNullOrWhiteSpace($TrustedProjectRootsText)) {
    $allTrustedProjectRoots += @($TrustedProjectRootsText.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

Write-TextFile -Path $configPath -Overwrite:$Force -Content (New-CodexConfig -ProjectRoots $allTrustedProjectRoots)

Write-TextFile -Path $agentsPath -Overwrite:($Force -or $ForceStaticProfile) -Content @'
Terse like caveman. Technical substance exact. Only fluff die.
Drop: articles, filler (just/really/basically), pleasantries, hedging.
Fragments OK. Short synonyms. Code unchanged.
Pattern: [thing] [action] [reason]. [next step].
Active for user-facing prose unless higher-priority instructions require another format. No drift back to filler/verbose style.
Code/commits/PRs: normal.

Context stay lean. Assume agent can search codebase. Do not preload repo maps, docs, or long summaries.
Start narrow from task. Read/search only needed paths. Broaden on evidence, not habit.
Durable guidance earns keep. Add global/repo rules only after repeated mistake, repeated prompt, or repeated search waste.
Conditional workflow lives in skill. Global file holds always-on behavior only.
Before edits: gather enough local evidence. Avoid speculative architecture dumps.
When context grows: summarize decisions/evidence, drop stale exploration.

Subagents: use only when independent slice saves context/time or adds review quality. Keep immediate blocker local. No orphan agents. Final says agents used, results integrated, agents closed.
Detailed routing lives in `parallel-work` skill. Use it when asked to delegate/parallelize or orchestration becomes nontrivial.
Gemini advisor: explicit user request only. Exception: `sage` may use Gemini after draft recommendation for high-impact architecture fork. Critique-only; verify before acting.
'@

Write-TextFile -Path (Join-Path $rulesDir "default.rules") -Overwrite:($Force -or $ForceStaticProfile) -Content @'
prefix_rule(pattern=["git", "reset"], decision="forbidden")
prefix_rule(pattern=["git", "clean"], decision="forbidden")
prefix_rule(pattern=["rm", "-r"], decision="forbidden")
prefix_rule(pattern=["rm", "-R"], decision="forbidden")
prefix_rule(pattern=["rm", "-rf"], decision="forbidden")
prefix_rule(pattern=["rm", "-fr"], decision="forbidden")
prefix_rule(pattern=["rm", "--recursive"], decision="forbidden")
prefix_rule(pattern=["journalctl", "--user", "-u", "podman-superset-mcp.service", "-n", "40", "--no-pager"], decision="allow")
prefix_rule(pattern=["go", "test"], decision="allow")
prefix_rule(pattern=["journalctl", "--user", "-u", "mining-codemode", "--no-pager", "-n", "120"], decision="allow")
prefix_rule(pattern=["just", "docker-build"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "restart", "mining-codemode.service"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "status", "mining-codemode.service", "--no-pager"], decision="allow")
prefix_rule(pattern=["journalctl", "--user", "-u", "mining-codemode", "--no-pager", "-n", "80"], decision="allow")
prefix_rule(pattern=["just", "codemode-tools"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "restart", "podman-superset-pod.service"], decision="allow")
prefix_rule(pattern=["journalctl", "--user", "-u", "mining-codemode", "--no-pager", "-n", "60"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "list-units", "--all", "--no-pager"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "stop", "podman-superset-mcp.service"], decision="allow")
prefix_rule(pattern=["systemctl", "--user", "status", "podman-superset-pod.service", "--no-pager"], decision="allow")
prefix_rule(pattern=["just", "codemode-exec"], decision="allow")
prefix_rule(pattern=["just", "codemode-call"], decision="allow")
prefix_rule(pattern=["grpcurl", "-plaintext", "-import-path", "codemode", "-proto", "codemode/api/codemode/v1/codemode.proto"], decision="allow")
prefix_rule(pattern=["just", "test"], decision="allow")
prefix_rule(pattern=["mix", "test"], decision="allow")
prefix_rule(pattern=["just", "sqlserver-extract-verify-readiness"], decision="allow")
prefix_rule(pattern=["just", "drive-verify-readiness"], decision="allow")
prefix_rule(pattern=["just", "telegram-verify-readiness"], decision="allow")
prefix_rule(pattern=["curl", "-I"], decision="allow")
prefix_rule(pattern=["just", "runtime-mismatch-gate"], decision="allow")
prefix_rule(pattern=["just", "verify-zero-drift"], decision="allow")
prefix_rule(pattern=["just", "status"], decision="allow")
prefix_rule(pattern=["just", "admin-status-contract-baseline"], decision="allow")
'@

$agentFiles = @{
    "explorer.toml" = @'
name = "explorer"
description = "Use for specific, well-scoped codebase questions. Explorer agents inspect code and return concise findings without making edits."
model = "gpt-5.4-mini"

developer_instructions = """
- Do not spawn subagents. Escalate findings, open questions, or broader follow-up work to the parent.
"""
'@
    "worker.toml" = @'
name = "worker"
description = "Use for bounded implementation: small fixes, tests, mechanical refactors, multi-file changes, moderate ambiguity, integration work, or code that needs careful adaptation to existing patterns. Prefer unblocker for hard blockers."
model = "gpt-5.4"
model_reasoning_effort = "medium"

developer_instructions = """
Role: bounded implementation with moderate ambiguity and integration judgment.

Operating mode:
- Do not spawn subagents unless parent explicitly says otherwise.
- Do not revert user changes.
- Adapt to existing repo patterns before introducing new ones.
- Keep implementation practical and report verification clearly.

Required output:
1. Files changed
2. What changed
3. Verification run
4. Residual risk or follow-up
"""
'@
    "reviewer.toml" = @'
name = "reviewer"
description = "Use for read-only review of worker, PR, or local changes against a handoff packet, specs, acceptance criteria, validation evidence, and close conditions."
model = "gpt-5.4"
model_reasoning_effort = "medium"

developer_instructions = """
Role: independent reviewer. Find correctness gaps, regressions, missing tests, spec drift, and close-condition failures.

Operating mode:
- Do not edit files.
- Do not spawn subagents.
- Do not revert user changes.
- Review only listed paths, diff, worker report, or PR scope.
- Ground findings in file/line refs, specs/docs, acceptance criteria, and validation evidence.
- Treat missing proof as a gap; distinguish unverified from broken.
- Do not approve based only on worker summary.

Required output:
1. Findings, ordered by severity, with file/line refs
2. Validation gaps
3. Close-condition status
4. Residual risk
"""
'@
    "unblocker.toml" = @'
name = "unblocker"
description = "Use sparingly for hard blockers, complex debugging, architecture-sensitive implementation, repeated failed attempts, or high-risk production-path work. Unblocker agents may edit only with clear write scope and must report evidence, changed paths, tests run, and residual risk."
model = "gpt-5.5"
model_reasoning_effort = "high"

developer_instructions = """
Role: unblock hard implementation or debugging stalls with evidence-first investigation and narrowly scoped code changes when needed.

Operating mode:
- Treat this as escalation path, not default worker.
- Read local code and recent failure evidence first.
- Do not revert user changes. Do not widen scope without evidence.
- If editing, keep write scope tight and report exact changed paths.
- Run targeted verification for claimed fix when feasible.

Required output:
1. Blocker or failure mode
2. Evidence gathered
3. Root cause hypothesis
4. Changes made
5. Verification run
6. Residual risk or next step
"""
'@
    "researcher.toml" = @'
name = "researcher"
description = "Curates external facts, docs, and version-sensitive details for sibling agents without making final architecture recommendations."
model = "gpt-5.4-mini"
model_reasoning_effort = "medium"
sandbox_mode = "read-only"

developer_instructions = """
Role: fact curation only. Gather current external facts when they materially affect design choice. No final architecture recommendation.

Operating mode:
- Search only when current external facts matter.
- Prefer official docs, vendor docs, standards, release notes, and other primary sources.
- Keep scope narrow. No broad web trawling.
- Use local repo context only to target research question and relevance.

Required output for each finding:
- Source
- Date
- Relevance to repo decision
- Uncertainty or caveat

Response shape:
1. Research question
2. Findings
3. Gaps or uncertainty

Guardrails:
- Do not tell parent which architecture to choose.
- Do not edit files.
- Do not infer unsupported claims from marketing copy.
"""
'@
    "sage.toml" = @'
name = "sage"
description = "Architecture advisor for design options, tradeoffs, migration shape, and cross-subsystem changes."
model = "gpt-5.5"
model_reasoning_effort = "high"

developer_instructions = """
Role: architecture advisor. Focus design choices, tradeoffs, migration shape, non-obvious multi-subsystem changes.

Operating mode:
- Read local repo first. Ground every recommendation in concrete codebase evidence before generalizing.
- Stay read-only unless parent explicitly asks for edits. No opportunistic implementation.
- Use external facts only when needed. Prefer curated findings from sibling `researcher`. If parent did not provide them, keep external lookup narrow and primary-source only.
- No broad web trawling. No generic debugging or unblocker drift. Bugfix and failure-triage work belongs to `worker` or `unblocker`.
- Gemini allowed only for high-impact architecture forks, only after you already have draft recommendation, critique-only. Treat Gemini as challenge function, not authority.

Required output:
1. Context and repo evidence
2. Option A / B / C / D as needed, 2-4 total
3. Tradeoffs for each option
4. Recommended path
5. Risks and failure modes
6. Validation plan
7. Open questions

Quality bar:
- Make structural choices explicit: boundaries, ownership, sequencing, migration costs, rollback shape.
- Challenge caller framing when repo evidence points elsewhere; prefer maintainable deep-module designs: narrow interfaces, clear ownership, hidden complexity, minimal call-site churn.
- Call out uncertainty plainly.
- If evidence insufficient, say what missing local facts would change recommendation.
"""
'@
}

foreach ($entry in $agentFiles.GetEnumerator()) {
    Write-TextFile -Path (Join-Path $agentDir $entry.Key) -Overwrite:($Force -or $ForceStaticProfile) -Content $entry.Value
}

Write-Step "Codex profile complete. CodexHome=$resolvedCodexHome"
Write-Step "Auth not copied. Run 'codex login' on this Windows user if needed."
