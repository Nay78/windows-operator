# Operator Harness Target Architecture

## Purpose

Windows Operator is a runtime plus an agent-facing harness.

The runtime owns stable capabilities for controlling Windows desktop apps. The
harness owns repeatable operator workflows, evidence, summaries, and cleanup.
These two concerns should stay separate so APIs remain composable while agents
get safe, low-friction commands.

## Layer Model

```text
agent or operator
  -> Justfile shortcut
  -> CLI harness command
  -> Host REST API
  -> Desktop Agent
  -> Windows app, browser, COM, UIA, Office.js
```

## REST Layer

REST is the stable composition layer.

Use REST for durable primitives and domain workflows that other tools,
services, scripts, and generated clients should compose directly.

REST owns:

- typed request/result contracts
- OpenAPI and generated clients
- domain namespaces under `/v1/*`
- stable error model
- Host-to-Agent proxy shape
- runtime capability boundaries

REST should hide:

- Windows paths and local state layout
- COM object shape
- UIA quirks
- browser profile and DevTools details
- scheduled task mechanics
- retry and observation implementation details

REST should not own:

- agent-specific run naming
- local lease files
- profiling TTLs
- shell UX
- "print summary path" conventions
- ad hoc development shortcuts

Good REST examples:

```text
GET  /v1/health
GET  /v1/windows
POST /v1/powerpoint/online/sessions
GET  /v1/powerpoint/online/sessions/{sessionId}
POST /v1/powerpoint/online/updates
POST /v1/powerpoint/online/sessions/{sessionId}/cleanup
```

REST contracts are boring and hard to misuse. Add a new REST field or route only
when the capability should be stable for more than one caller.

## CLI Harness Layer

CLI harness commands are the agent/operator workflow layer.

Use CLI scripts for repeatable flows that compose REST calls and own local
operator state. The CLI is allowed to be more specific than REST because it is a
workflow surface, not the core runtime contract.

CLI owns:

- safe defaults
- deck/profile selection
- run IDs
- artifact roots
- summary JSON
- lease files and TTLs
- cleanup traps
- retries and status gates
- evidence aggregation
- exit codes

CLI output contract:

- exit `0` for success
- exit `1` for completed flow failure
- exit `2` for local gate/usage failure
- print the summary path on stdout
- write `summary.json` with `success`, `status`, inputs, evidence paths,
  cleanup result, and next debugging hints where useful

Current CLI harness example:

```text
scripts/linux/powerpoint-online-final-proof.py --hot-start
scripts/linux/powerpoint-online-final-proof.py --hot-run
scripts/linux/powerpoint-online-final-proof.py --hot-status
scripts/linux/powerpoint-online-final-proof.py --hot-cleanup
```

Target CLI shape as harnesses grow:

```text
scripts/linux/wo health
scripts/linux/wo windows list
scripts/linux/wo ppt profile
scripts/linux/wo ppt hot start
scripts/linux/wo ppt hot run
scripts/linux/wo ppt hot status
scripts/linux/wo ppt hot cleanup
scripts/linux/wo mail search
scripts/linux/wo mail download-attachments
```

The `wo` command is a consolidation target, not a prerequisite for adding a
single focused harness script. Promote repeated patterns into `wo` when command
discovery, shared flags, or common summary behavior would remove real caller
complexity.

## Justfile Layer

`Justfile` is the unstable developer and agent command menu.

Use Just recipes for discoverable shortcuts with common defaults. Recipes should
call CLI harness commands or simple developer tools. They should not contain
state machines.

Just owns:

- easy command names
- common development defaults
- repo-local command discovery through `just --list`
- aliases such as `easy-profile`

Just should not own:

- JSON parsing
- lease persistence
- TTL policy
- cleanup rules
- retry loops
- proof policy
- REST request construction beyond simple CLI invocation

Good Just examples:

```text
just ppt-hot-start
just ppt-hot-run
just ppt-hot-status
just ppt-hot-cleanup
just ppt-profile
just ppt-profile-fast
```

Bad Just examples:

```text
# Avoid this pattern.
just recipe with inline curl, jq state mutation, retry loops, and cleanup traps
```

## Promotion Rules

Use this decision table when adding a feature:

| Need | Layer |
| --- | --- |
| Stable capability for many callers | REST |
| External service or generated client needs it | REST |
| Domain workflow should hide Windows quirks | REST |
| Agent/operator flow with run artifacts | CLI |
| Flow needs local lease, TTL, cleanup trap, or summary | CLI |
| Common safe default for an existing CLI flow | Justfile |
| Temporary development shortcut | Justfile |
| One-off manual investigation | Shell command or `.work` note |

When in doubt, put capabilities lower and ergonomics higher:

```text
capability -> REST
workflow -> CLI
shortcut -> Justfile
evidence/planning -> .work
```

## Boundary Examples

PowerPoint hot profiling:

- REST owns PowerPoint Online session start/status/update/cleanup.
- CLI owns the persistent hot lease file, TTL, run IDs, summaries, and cleanup
  verification.
- Just owns `ppt-hot-start`, `ppt-hot-run`, `ppt-hot-status`, and
  `ppt-hot-cleanup`.

Outlook attachment download:

- REST should own typed mail search/download operations and Outlook COM worker
  policy.
- CLI should own batch run IDs, output folders, summary JSON, and operator
  defaults.
- Just should expose common local smoke or development commands only.

## State Placement

Runtime state that only Windows Operator needs belongs under
`%LOCALAPPDATA%\WindowsOperator`.

Artifacts and summaries Linux tools need belong under the exchange root:

```text
Linux:   /var/lib/windows-server/shared/operator-exchange
Windows: Z:\operator-exchange
```

CLI lease files that agents inspect may live under the exchange root when they
are part of the operator workflow contract. Keep credentials, browser profiles,
tokens, and private machine state out of exchange.

## Compatibility

REST breaking changes require contract review, OpenAPI regeneration, generated
client updates, tests, docs, and live Windows proof when runtime behavior is
affected.

CLI command shape can evolve faster, but should keep existing agent-facing
commands working until the replacement is documented and live-proven.

Just recipes may change whenever developer workflow changes, but stable names
used by agents should stay as thin aliases over the current CLI path.

## Target Invariant

Callers should never need to know Edge windows, UIA selectors, Office.js task
pane timing, COM worker lifetimes, scheduled task names, or exchange path
internals unless they are explicitly debugging those layers.

Deep modules own platform complexity. Agents get small commands and structured
evidence.
