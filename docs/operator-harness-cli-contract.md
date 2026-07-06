# Operator Harness CLI Contract

## Purpose

This document defines the shared contract for agent-facing CLI harness commands.
It keeps workflow UX uniform without expanding the REST contract.

Layer split stays explicit:

- REST is the stable runtime API.
- CLI is the workflow harness.
- `Justfile` is the shortcut menu.

CLI commands may compose one or more REST calls, but they do not redefine REST
routes, payloads, or error semantics.

## Scope

Use this contract for shared agent/operator commands such as:

```text
scripts/linux/wo health
scripts/linux/wo windows list
scripts/linux/wo ppt profile
scripts/linux/wo ppt warm
scripts/linux/wo ppt hot run
scripts/linux/wo mail search
scripts/linux/wo auth microsoft cleanup
scripts/linux/wo smoke
```

Provisioning scripts, repair scripts, and break-glass scripts may stay outside
this contract until promoted into the harness surface.
Lower-level delegate scripts may keep specialized options, but agent-facing
examples should prefer `scripts/linux/wo`.

## Required Flags

Shared harness commands should accept these flags when they need the value:

- `--base-url`
  - Base Host REST URL.
  - Default should target Host REST on Linux tunnel:
    `http://127.0.0.1:43117`.
  - CLI may reject malformed URLs as local gate failures.
- `--exchange-root`
  - Linux-visible exchange root for summaries and evidence.
  - Default should resolve from environment/config, then fall back to
    `/var/lib/windows-server/shared/operator-exchange`.
- `--run-id`
  - Caller-supplied run identifier for artifact grouping and evidence lookup.
  - If omitted, CLI may generate one.
- `--json`
  - Print machine-readable stdout instead of human text.
  - Does not remove summary writing unless command is pure usage/help output.

Commands may add domain-specific flags, but these four names and meanings stay
stable across harness surfaces.

## Separation Rules

REST stability stays separate from CLI workflow policy:

- REST owns capability routes, typed request/result bodies, and durable domain
  contracts.
- CLI owns run naming, safe defaults, artifact paths, summaries, retries,
  cleanup, and operator proof policy.
- `Justfile` owns discoverable aliases and common shortcuts only.

Do not move CLI-only concerns into REST to satisfy shell UX. Do not put CLI
state machines into `Justfile`.

## Exit Codes

Harness commands must use these exit codes:

- `0`
  - Flow succeeded.
  - Required summary written when command performs work.
- `1`
  - Flow ran and reached a runtime or workflow failure.
  - Examples: REST returned failure, gate failed after live observation, cleanup
    failed, target app behavior wrong.
  - Summary must exist and describe failure.
- `2`
  - Local gate or usage failure before runnable workflow proof.
  - Examples: bad arguments, missing local file, malformed URL, missing exchange
    root, unsupported flag combination.
  - Summary must exist when the command already has a resolved run context.
  - Pure parser/help failures may exit `2` without summary.

Exit code `2` is not proof of runtime behavior. It marks caller/setup issues.

## Stdout Contract

Commands that produce a run summary must print one stdout line containing the
summary path.

Convention:

```text
<absolute-summary-path>
```

Rules:

- Path is Linux-visible.
- Path points to the final summary JSON for that run.
- Human text should go to stderr when summary-path stdout matters.
- With `--json`, stdout may be a JSON object, but it must still include the
  summary path as `summaryPath`.

Recommended plain stdout example:

```text
/var/lib/windows-server/shared/operator-exchange/ppt/runs/20260705T120102Z-hot-run/summary.json
```

Recommended `--json` stdout example:

```json
{"summaryPath":"/var/lib/windows-server/shared/operator-exchange/ppt/runs/20260705T120102Z-hot-run/summary.json"}
```

## Summary Path Convention

Default location pattern:

```text
<exchange-root>/runs/<run-id>/summary.json
```

Requirements:

- `run-id` identifies the artifact directory.
- Summary lives with evidence for same run.
- Existing harnesses use the shared `runs` subtree. Keep that layout unless a
  domain already owns a deeper artifact tree.
- Commands with a domain subtree may keep it if stdout points to one final
  `summary.json`.

Keep summary path deterministic from `exchange-root` and `run-id`.

## Summary Schema

Top-level summary fields:

- `success`
  - Boolean. True only when exit code is `0`.
- `status`
  - Short stable string such as `ok`, `failed`, `gate_failed`,
    `usage_error`, `cleanup_failed`.
- `command`
  - Invoked logical command name such as `wo ppt hot run`.
- `runId`
  - Effective run ID.
- `baseUrl`
  - Effective REST base URL when command uses Host REST.
- `exchangeRoot`
  - Effective exchange root path.
- `summaryPath`
  - Absolute path to this summary file.
- `startedAtUtc`
  - RFC 3339 UTC timestamp.
- `observedAtUtc`
  - RFC 3339 UTC timestamp for final observed result.
- `elapsedSeconds`
  - Numeric elapsed wall time.
- `inputs`
  - Normalized caller inputs or resolved workflow options.
- `artifacts`
  - Paths or refs for request, response, screenshots, logs, lease files, or
    other run outputs.
- `gates`
  - Array of gate results used to decide success/failure.
- `error`
  - Null on success. Structured object on failure.
- `cleanup`
  - Cleanup result object when cleanup relevant.

Commands may add domain fields, but these top-level fields should remain common
so callers can reason about any harness result.

## Gate Shape

`gates` records proof checkpoints. Each item should use this shape:

```json
{
  "name": "session-ready",
  "status": "passed",
  "required": true,
  "observedAtUtc": "2026-07-05T12:01:07Z",
  "detail": "REST session reported ready",
  "evidencePath": "/var/lib/windows-server/shared/operator-exchange/ppt/runs/example/session-status.json"
}
```

Field rules:

- `name`: stable gate identifier.
- `status`: `passed`, `failed`, `skipped`.
- `required`: boolean.
- `observedAtUtc`: timestamp for that gate observation.
- `detail`: short human-readable reason.
- `evidencePath`: optional path for direct proof artifact.

Required gates should make success/failure explainable without reading logs.

## Error Shape

`error` should be null or object:

```json
{
  "code": "rest_request_failed",
  "message": "POST /v1/powerpoint/online/updates returned 502",
  "retryable": true,
  "stage": "update-run",
  "details": {
    "httpStatus": 502
  }
}
```

Field rules:

- `code`: stable machine-readable identifier.
- `message`: short human-readable explanation.
- `retryable`: boolean when known.
- `stage`: logical workflow stage.
- `details`: optional structured payload.

`error` explains why command exited `1` or `2`. Gate failures may appear in both
`gates` and `error`.

## Live Proof Requirements

Harness success claims need live proof against real runtime boundaries when the
workflow depends on Windows behavior, browser state, COM, Office.js, external
services, or task/session state.

Minimum rule:

- success requires at least one live observation tied to the command goal
- summary must name direct evidence path or response proving observed result
- final status must reflect observed behavior, not request submission only

Examples:

- `wo health`: live GET to `/v1/health`
- `wo windows list`: live REST result from Host
- `wo ppt hot run`: live PowerPoint session/update observation plus cleanup or
  retained lease evidence
- `wo smoke`: live probe result for each claimed subsystem

If real success path needs credentials, MFA, mailbox contents, or third-party
approval, run a safe negative live test and record the expected real failure
mode in `gates` and `error`.

## Dry-Run Limits

Dry-run proves only local construction work:

- argument parsing
- request shaping
- path resolution
- serialization
- command routing

Dry-run does not prove:

- target browser/app behavior
- COM success
- Outlook or PowerPoint automation success
- Office.js add-in readiness
- external authentication success
- cleanup effectiveness on live runtime state

Dry-run results must not set `success=true` for workflows whose goal is live
runtime behavior. Use statuses such as `dry_run_only` or `usage_ok` instead.

## Compatibility

This contract stabilizes harness UX without freezing every command-specific
field. Additive domain fields are fine. Breaking changes to required flags, exit
codes, stdout summary-path behavior, or top-level summary keys need doc updates
and caller review.
