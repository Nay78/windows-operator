# Operator Harness Alignment Roadmap

Date: 2026-07-05

## Objective

Align Windows Operator to the target harness architecture:

```text
REST = stable composable runtime API
CLI = agent/operator workflow harness
Justfile = unstable developer and agent shortcut menu
.work = planning/evidence ledger
```

Target spec:

- `docs/operator-harness-architecture.md`
- `docs/development.md`, section `Harness layering`
- `docs/feature-namespaces.md`

## Execution Ledger

Goal:
`019f314e-15c2-72e1-b26c-0a38a440a4e2`

Branch:
`harness-v2`

Orchestrator rules:

- Main keeps this roadmap, target spec map, and evidence ledger current.
- Implementation code, tests, migrations, and generated edits go to workers.
- REST contract changes need explicit operator approval before mutation.
- Progress claims require `.work` evidence updates and validation commands.

Current execution state:

| Slice | State | Owner | Evidence |
| --- | --- | --- | --- |
| S0 Baseline Inventory | complete | main | Inventory seeded below. |
| S1 Shared CLI Harness Contract | complete | worker + main review | `docs/operator-harness-cli-contract.md`; final validation passed 2026-07-06. |
| S2 Introduce `scripts/linux/wo` | complete | worker + main review | `wo` dispatcher added; direct health/windows summaries align with contract. |
| S3 Point Justfile At `wo` | complete | worker + main review | Just PowerPoint shortcuts route through `wo`; dry-runs passed. |
| S5 Command Manifest | complete | worker + main review | `harness/windows-operator.commands.json` parses and includes mail/auth/smoke updates. |
| S4 Shared Harness Helpers | complete | worker + main review | `scripts/linux/windows_operator_harness.py`; `wo` direct REST summaries use it. |
| S6 Mail/Auth CLI Wrappers | complete | worker + main review | `wo mail ...` and `wo auth microsoft ...`; live safe commands passed. |
| S7 Live Smoke Unification | complete | worker + main review | `wo smoke` writes contract summary and live-smoke artifacts; live smoke passed. |
| S8 REST/API Alignment Audit | complete | main | Committed and live OpenAPI path counts both `55`; no REST mutation needed. |
| S9 Documentation Alignment | complete | worker + main review | README and architecture/domain docs point agents to `scripts/linux/wo`. |
| S10 Completion Proof | complete | main + workers | Hot start/status/run/cleanup passed live; lease removed and Edge-like windows returned to `0`. |
| S11 External Project Boundary | complete | main | AGENTS, README, development notes, and architecture docs state REST/OpenAPI/generated clients are the no-drift external integration path; CLI/Just/scripts are operator tooling. |
| S12 External Consumer Spec | complete | main | `docs/external-consumer-integration.md` defines target contract, release gates, artifact/error/status/capability/relay/SDK rules, and implementation roadmap. |
| S13 External Consumer Roadmap | complete | main | `.work/external-consumer-integration-roadmap.md` breaks the spec into implementation slices E1-E9 with validation and live-proof requirements. |

### Handoff S1: Shared CLI Harness Contract

Worker scope:

- Edit docs only.
- Preferred target: `docs/operator-harness-cli-contract.md`.
- Add links from `docs/operator-harness-architecture.md` or
  `docs/development.md` only if needed for discoverability.

Acceptance:

- Defines common flags: `--base-url`, `--exchange-root`, `--run-id`, `--json`.
- Defines exit codes `0`, `1`, `2`.
- Defines stdout summary-path convention.
- Defines summary top-level fields and error/gate shape.
- Defines live-proof requirements and dry-run limits.
- No REST or implementation change.

Validation:

- `git diff --check`

### Handoff S2: `scripts/linux/wo` Dispatcher

Worker scope:

- Add `scripts/linux/wo`.
- Add focused CLI tests if needed, preferably under `scripts/linux/`.
- Do not move PowerPoint workflow logic.
- Do not alter REST/OpenAPI contracts.

Required first-pass commands:

```text
scripts/linux/wo --help
scripts/linux/wo health
scripts/linux/wo windows list
scripts/linux/wo ppt profile
scripts/linux/wo ppt profile-fast
scripts/linux/wo ppt warm
scripts/linux/wo ppt hot start
scripts/linux/wo ppt hot run
scripts/linux/wo ppt hot status
scripts/linux/wo ppt hot cleanup
scripts/linux/wo smoke
```

Implementation guidance:

- Prefer Python dispatcher with boring subprocess delegation.
- Delegate existing PowerPoint behavior to
  `scripts/linux/powerpoint-online-final-proof.py`.
- Preserve existing safe SEM27 defaults.
- `wo health` may call Host REST directly and print a summary path.
- `wo smoke` may delegate to `scripts/linux/live-smoke.py` if safe.

Acceptance:

- `wo --help` lists domains.
- `wo ppt hot run --help` or equivalent is discoverable.
- Existing PowerPoint script tests still pass.
- No REST contract changes.

Validation:

- `scripts/linux/wo --help`
- `scripts/linux/wo health`
- `scripts/linux/wo ppt hot status`
- `scripts/linux/powerpoint-online-final-proof-tests.sh`

### Handoff S5: Command Manifest

Worker scope:

- Add `harness/windows-operator.commands.json`.
- Optional: add a small validation script if useful.
- Do not edit `scripts/linux/wo`, `Justfile`, REST/OpenAPI, generated clients,
  docs, or `.work`.

Manifest must cover:

- Existing Just recipes.
- Planned `wo` commands from S2.
- Direct low-level scripts that should remain visible to agents.

Schema fields:

- `name`
- `summary`
- `layer`
- `command`
- `safeDefault`
- `mutatesExternalState`
- `requiresDesktop`
- `requiresCleanup`
- `summaryPath`
- `examples`
- `preferredFor`
- `replacedBy`

Acceptance:

- Distinguishes final proof, fast profile, warm one-shot, and persistent hot
  lease.
- Marks SEM27 profile/hot flows non-mutating unless explicitly final mutation
  proof.
- Marks break-glass/recovery scripts as not preferred default commands.
- JSON parses cleanly.

Validation:

- `python3 -m json.tool harness/windows-operator.commands.json >/dev/null`

### Handoff S3: Point Justfile At `wo`

Worker scope:

- Edit `Justfile` only.
- Do not edit `scripts/linux/wo`, docs, `.work`, REST/OpenAPI, generated
  clients, or workflow scripts.

Expected change:

- Route PowerPoint profile/hot shortcuts through `scripts/linux/wo`:
  - `ppt-profile`
  - `easy-profile`
  - `ppt-profile-fast`
  - `easy-profile-fast`
  - `ppt-profile-warm`
  - `ppt-hot-start`
  - `ppt-hot-run`
  - `ppt-hot-status`
  - `ppt-hot-cleanup`
- Keep final-proof prepare/host-gate/readiness recipes direct unless routing
  through `wo` is already exact and low risk.
- Keep `ppt-final-proof-test` direct.

Acceptance:

- Justfile remains a shortcut menu only.
- Existing recipe names still exist.
- No JSON parsing, REST calls, or state logic in Justfile.

Validation:

- `just --list`
- `just --dry-run ppt-profile`
- `just --dry-run ppt-profile-fast`
- `just --dry-run ppt-profile-warm`
- `just --dry-run ppt-hot-start`
- `just --dry-run ppt-hot-run`
- `just --dry-run ppt-hot-status`
- `just --dry-run ppt-hot-cleanup`

### Handoff S4: Shared Harness Helpers

Worker scope:

- Add `scripts/linux/windows_operator_harness.py`.
- Update `scripts/linux/wo` to use the helper.
- Optionally update `scripts/linux/powerpoint-online-final-proof.py` only for
  low-risk helper imports that preserve exact behavior.
- Update focused tests only as needed.
- Do not edit Justfile, docs, `.work`, REST/OpenAPI, generated clients, or
  command manifest.

Helper should own:

- UTC timestamp helpers.
- Exchange root default.
- JSON read/write.
- Host REST request helper for GET/POST JSON.
- Run root and summary path creation.
- Contract summary emitter for direct `wo` commands.

Acceptance:

- Direct `wo health` and `wo windows list` behavior stays unchanged.
- PowerPoint harness summaries and fake HTTP tests stay unchanged if the
  PowerPoint script is touched.
- No REST contract changes.

Validation:

- `scripts/linux/wo-tests.sh`
- `scripts/linux/powerpoint-online-final-proof-tests.sh`
- `python3 -m py_compile scripts/linux/wo scripts/linux/windows_operator_harness.py scripts/linux/powerpoint-online-final-proof.py`

### Handoff S6: Mail/Auth CLI Wrappers

Worker scope:

- Edit `scripts/linux/wo`.
- Edit `scripts/linux/wo-tests.sh`.
- Optionally extend `scripts/linux/windows_operator_harness.py` only for generic
  JSON POST/summary needs.
- Do not edit Justfile, docs, `.work`, REST/OpenAPI, generated clients,
  command manifest, or existing mail/auth scripts.

Commands:

```text
scripts/linux/wo mail status
scripts/linux/wo mail folders
scripts/linux/wo mail search --subject ...
scripts/linux/wo mail download --subject ... --folder ...
scripts/linux/wo auth microsoft cleanup --dry-run
scripts/linux/wo auth microsoft device-login --device-code ...
scripts/linux/wo auth microsoft authorize-probe --authorize-url ...
```

Safe defaults:

- `mail status`: GET `/v1/mail/status`.
- `mail folders`: POST `/v1/mail/folders` with freshness option.
- `mail search`: POST `/v1/mail/messages/search`; default synthetic no-match
  subject if no subject is supplied.
- `mail download`: POST `/v1/mail/attachments/download`; require explicit
  `--subject` or `--message-id` to avoid broad downloads.
- `auth microsoft cleanup`: POST `/v1/auth/microsoft/cleanup`; default
  `--dry-run`.
- `auth microsoft device-login`: require `--device-code`; allow `--dry-run`.
- `auth microsoft authorize-probe`: require `--authorize-url`; allow
  `--dry-run`.

Acceptance:

- Each command writes contract summary and prints one summary path.
- `--json` emits `{"summaryPath": ...}`.
- Unsafe/broad input exits `2` with a summary when run context is resolved.
- No REST contract changes.

Validation:

- `scripts/linux/wo-tests.sh`
- `python3 -m py_compile scripts/linux/wo scripts/linux/windows_operator_harness.py`

### Handoff S7: Live Smoke Unification

Worker scope:

- Edit `scripts/linux/wo`.
- Edit `scripts/linux/wo-tests.sh`.
- Optionally extend `scripts/linux/windows_operator_harness.py` only for generic
  summary helpers.
- Do not edit `scripts/linux/live-smoke.py` unless unavoidable.
- Do not edit Justfile, docs, `.work`, REST/OpenAPI, generated clients, or
  command manifest.

Objective:

- `scripts/linux/wo smoke` must print one contract summary path instead of
  passing through `live-smoke.py` mixed PASS/REPORT output.

Implementation guidance:

- Run `scripts/linux/live-smoke.py` as subprocess with `--output` pointing to
  `<exchange-root>/runs/<run-id>/live-smoke-report.json`.
- Capture stdout/stderr into artifacts.
- Write contract `summary.json` with `command=wo smoke`, `status=ok` when
  subprocess exits `0` and report JSON has `ok=true`.
- Plain stdout: one summary path.
- `--json`: `{"summaryPath": ...}`.
- Delegate args after `--` still pass through to `live-smoke.py`.

Acceptance:

- `wo smoke -- --help` remains useful.
- `wo smoke` summary links report/stdout/stderr artifacts.
- Existing live-smoke report contents remain available.

Validation:

- `scripts/linux/wo-tests.sh`
- `python3 -m py_compile scripts/linux/wo scripts/linux/windows_operator_harness.py`

### Handoff S5B: Command Manifest Update

Worker scope:

- Edit `harness/windows-operator.commands.json` only.
- Do not edit scripts, docs, `.work`, Justfile, REST/OpenAPI, or generated
  clients.

Required updates:

- Add `wo mail status`, `wo mail folders`, `wo mail search`, `wo mail download`.
- Add `wo auth microsoft cleanup`, `wo auth microsoft device-login`,
  `wo auth microsoft authorize-probe`.
- Update `wo-smoke` summary path to contract `summary.json`; report path should
  be an artifact note, not the command summary path.
- Keep direct lower-level script entries.

Validation:

- `python3 -m json.tool harness/windows-operator.commands.json >/dev/null`

### Handoff S9: Documentation Alignment

Worker scope:

- Edit docs only:
  - `README.md`
  - `docs/development.md`
  - `docs/operator-harness-architecture.md`
  - `docs/powerpoint-automation-architecture.md`
  - `docs/outlook-mail-automation-architecture.md`
  - `docs/email-attachment-automation.md`
- Do not edit scripts, `.work`, Justfile, command manifest, REST/OpenAPI, or
  generated clients.

Required updates:

- Point agent-facing harness guidance to `scripts/linux/wo`.
- Preserve direct REST examples for API users.
- Update smoke guidance: `scripts/linux/wo smoke` produces `summary.json` and
  stores `live-smoke-report.json` as an artifact.
- Update PowerPoint hot/profile docs to show `wo ppt ...` as preferred path and
  Just recipes as shortcuts.
- Update mail/auth docs to show `wo mail ...` and `wo auth microsoft ...`
  wrappers, with REST examples preserved as lower-level API examples.
- Fix stale `wo mail download-attachments` text to `wo mail download`.

Validation:

- `git diff --check`
- `rg -n "wo mail download-attachments" README.md docs` returns no matches.

## Current State

Already aligned:

- Host REST owns stable runtime surface on `127.0.0.1:43117`.
- Agent REST owns desktop/UI automation on Windows loopback `127.0.0.1:43119`.
- OpenAPI and generated Go client exist under `openapi/` and `clients/go/`.
- Domain REST namespaces already exist for `windows`, `desktop`, `sessions`,
  `browser/edge`, `auth/microsoft`, `powerpoint`, `powerpoint/online`, and `mail`.
- PowerPoint Online has deep REST workflows:
  - session start/status/action/cleanup
  - update orchestration
  - job queue
  - add-in job claim/complete/fail
- PowerPoint agent workflow harness exists:
  `scripts/linux/powerpoint-online-final-proof.py`.
- PowerPoint harness already owns:
  - run IDs
  - SEM27 safe defaults
  - request shaping
  - lease file and TTL for hot profiling
  - summary JSON
  - cleanup proof
  - live evidence paths
- `Justfile` exposes thin agent shortcuts for PowerPoint profiling and sync.

Partially aligned:

- CLI harness is PowerPoint-specific, not a consolidated operator CLI.
- Just recipes still call individual scripts directly. This is acceptable now,
  but target shape is recipes calling a stable `scripts/linux/wo` entrypoint.
- Sync/bootstrap helpers are scripts first, with Just shortcuts; no shared CLI
  summary contract.
- Mail/auth workflows have REST and docs, but not uniform CLI harness wrappers
  with summary-path output.
- Live smoke exists, but command output shape differs from PowerPoint harness.

Not aligned yet:

- No machine-readable command manifest for agent command discovery.
- No consolidated CLI taxonomy such as `wo ppt hot run`.
- No shared harness helper module for:
  - Host REST calls
  - summary writing
  - exchange path defaults
  - run ID generation
  - exit code convention
  - JSON gate errors
- Justfile is growing as a command index; without consolidation it can become
  noisy even if it stays thin.

## Initial Command Surface Inventory

Just recipes:

| Command | Current layer | Target classification | Notes |
| --- | --- | --- | --- |
| `ppt-profile` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt profile`. |
| `easy-profile` | Just alias | stable shortcut | Keep as low-friction alias. |
| `ppt-profile-fast` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt profile-fast`. |
| `easy-profile-fast` | Just alias | stable shortcut | Keep as low-friction alias. |
| `ppt-profile-warm` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt warm`. |
| `ppt-hot-start` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt hot start`. |
| `ppt-hot-run` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt hot run`. |
| `ppt-hot-status` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt hot status`. |
| `ppt-hot-cleanup` | Just -> PowerPoint CLI | stable shortcut | Keep; later delegate to `wo ppt hot cleanup`. |
| `ppt-final-proof-*` | Just -> PowerPoint CLI | stable shortcut | Keep for mutation/readiness proof; not hot profiling. |
| `ppt-final-proof-test` | Just -> test script | stable dev check | Keep. |
| `sync*` | Just -> sync scripts | stable dev tooling | Keep; later optional `wo sync ...`. |

Linux scripts:

| Script | Current layer | Target classification | Notes |
| --- | --- | --- | --- |
| `powerpoint-online-final-proof.py` | CLI harness | keep, maybe delegate under `wo` | Owns mature PPT workflow logic. Do not inline into Just. |
| `powerpoint-online-final-proof-tests.sh` | CLI test harness | keep | Extend as PPT CLI contract tests. |
| `live-smoke.py` | CLI harness | candidate `wo smoke` wrapper | Align summary contract later. |
| `windows-run-ps.sh` | operational transport helper | keep direct and optional `wo windows run-ps` | Contract-sensitive transport defaults. |
| `windows-sync-available.sh` | operational helper | keep direct and Just shortcut | Sync has separate target logic. |
| `windows-sync-repo.sh` | operational helper | keep direct | Lower-level sync primitive. |
| `windows-sync-codex-profile.sh` | operational helper | keep direct and Just shortcut | Codex-specific. |
| `cleanup-microsoft-auth-edge.sh` | break-glass helper | candidate `wo auth microsoft cleanup` | REST cleanup exists; prefer REST/CLI wrapper. |
| `test-microsoft-graph-mail-read.sh` | one-off/dev | keep direct or retire after mail CLI | Graph viability probe, not stable harness. |
| `audit_entra_apps.py` | one-off/dev | keep direct | External app audit, not runtime harness. |

Windows scripts:

| Script family | Current layer | Target classification | Notes |
| --- | --- | --- | --- |
| `bootstrap*.ps1`, `register*.ps1`, `run-*.ps1` | provisioning/runtime | keep direct | Machine setup and task registration stay script-first. |
| `restart-scheduled-task.ps1`, `run-dotnet-test.ps1` | operational helper | keep direct, optional CLI wrappers | Useful from Linux runner. |
| `login-microsoft-device-code.ps1` | legacy/fallback auth helper | break-glass | REST auth path preferred. |
| `recover-outlook-mail.ps1`, `restart-outlook.ps1` | break-glass mail recovery | keep direct | Do not hide destructive/recovery semantics behind generic shortcuts. |
| `sync-codex-*.ps1`, `verify-codex-profile.ps1` | Codex profile ops | keep direct behind Linux sync scripts | Machine/profile state needs explicit scripts. |

## Target End State

Agent-facing entrypoints:

```text
just --list
just ppt-hot-run
scripts/linux/wo --help
scripts/linux/wo ppt hot run
scripts/linux/wo health
scripts/linux/wo mail status
```

Shared CLI contract:

- exit `0`: success
- exit `1`: runtime/workflow failure with written summary
- exit `2`: local gate or usage failure with written summary
- stdout: one summary path for commands that produce artifacts
- summary JSON includes:
  - `success`
  - `status`
  - `command`
  - `runId`
  - `requestPath` or request summary
  - `responsePath` where applicable
  - `evidence` or evidence paths where applicable
  - `cleanup`
  - `elapsedSeconds`
  - `observedAtUtc`

Layer ownership:

- REST owns capability and typed contracts.
- CLI owns workflow state and summaries.
- Just owns discoverable shortcuts only.
- `.work` owns campaign ledgers and live evidence records.

## Roadmap

### S0 Baseline Inventory

Status: seeded

Objective:
Create a lightweight inventory of current command surfaces and classify each as
REST, CLI harness, Just shortcut, or legacy/dev script.

Scope:

- `Justfile`
- `scripts/linux/*`
- `scripts/windows/*`
- `docs/*architecture*.md`
- `docs/development.md`
- `README.md`

Deliverable:

- Maintain `Initial Command Surface Inventory` section in this roadmap.
- Mark each script/recipe as:
  - `stable shortcut`
  - `candidate wo wrapper`
  - `break-glass`
  - `one-off/dev`
  - `keep direct`

Validation:

- `just --list`
- `rg --files scripts/linux scripts/windows`

Risk:

- Read-only except roadmap update.

### S1 Shared CLI Harness Contract

Status: planned

Objective:
Define the exact CLI output and summary schema before moving commands.

Boundary:

- CLI harness layer, not REST.

Deliverable:

- Add `docs/operator-harness-cli-contract.md` or a section in
  `docs/operator-harness-architecture.md`.
- Define:
  - common flags: `--base-url`, `--exchange-root`, `--run-id`, `--json`
  - exit codes
  - summary path stdout convention
  - summary fields
  - error/gate shape
  - live-proof requirements

Validation:

- Markdown lint is not configured; run `git diff --check`.

Risk:

- None. Documentation only.

### S2 Introduce `scripts/linux/wo`

Status: planned

Objective:
Add a single agent-friendly CLI entrypoint without moving existing workflow
logic yet.

Boundary:

- New CLI command dispatcher.
- Existing scripts remain source of behavior.

Interface:

```text
scripts/linux/wo health
scripts/linux/wo windows list
scripts/linux/wo ppt profile
scripts/linux/wo ppt profile-fast
scripts/linux/wo ppt warm
scripts/linux/wo ppt hot start
scripts/linux/wo ppt hot run
scripts/linux/wo ppt hot status
scripts/linux/wo ppt hot cleanup
scripts/linux/wo smoke
```

Implementation shape:

- Python dispatcher or small shell dispatcher.
- Prefer Python if it will own JSON summaries and shared helpers soon.
- First pass may delegate to existing scripts.

Acceptance:

- `wo --help` lists domains.
- `wo ppt hot run --help` or equivalent is discoverable.
- Existing PowerPoint scripts still work.
- No REST contract changes.

Validation:

- `scripts/linux/wo --help`
- `scripts/linux/wo health`
- `scripts/linux/wo ppt hot status` against no lease and active lease cases
- existing `scripts/linux/powerpoint-online-final-proof-tests.sh`

Risk:

- Low if dispatcher delegates and does not rewrite behavior.

### S3 Point Justfile At `wo`

Status: planned after S2

Objective:
Keep Justfile as shortcut menu while moving command taxonomy to `wo`.

Scope:

- `Justfile`

Expected changes:

```text
ppt-hot-start:
    scripts/linux/wo ppt hot start

ppt-hot-run:
    scripts/linux/wo ppt hot run
```

Keep aliases:

- `easy-profile`
- `easy-profile-fast`
- existing `ppt-*` names

Acceptance:

- `just --list` remains concise.
- Existing Just commands still work.
- Just recipes contain no JSON parsing or state logic.

Validation:

- `just --dry-run ppt-hot-start`
- `just --dry-run ppt-hot-run`
- `just --dry-run ppt-profile`
- `scripts/linux/powerpoint-online-final-proof-tests.sh`

Risk:

- Low. Reversible wrapper change.

### S4 Extract Shared Harness Helpers

Status: planned after S2/S3 prove command shape

Objective:
Reduce duplicate harness mechanics before adding more domains.

Boundary:

- CLI harness internals.
- No REST or OpenAPI change.

Candidate module:

```text
scripts/linux/windows_operator_harness.py
```

Owns:

- `utc_stamp`
- `utc_now`
- exchange root default
- Host REST JSON request helper
- `write_json`
- `read_json`
- summary path convention
- exit code helper
- edge-like window count helper
- run root creation

First consumers:

- `powerpoint-online-final-proof.py`
- `live-smoke.py` only if low churn
- future `wo`

Acceptance:

- No behavior changes in existing PowerPoint summaries.
- Existing fake HTTP tests still pass.

Validation:

- `scripts/linux/powerpoint-online-final-proof-tests.sh`
- `python3 -m py_compile scripts/linux/*.py`
- one live `wo health` or direct Host health

Risk:

- Medium. Shared helper can accidentally alter paths or summary semantics.
- Treat exchange paths, base URLs, run IDs, and exit codes as
  contract-sensitive.

### S5 Command Manifest

Status: planned

Objective:
Give agents machine-readable command discovery without scraping `Justfile` or
docs.

Candidate file:

```text
harness/windows-operator.commands.json
```

Schema fields:

- `name`
- `summary`
- `layer`
- `command`
- `safeDefault`
- `mutatesExternalState`
- `requiresDesktop`
- `requiresCleanup`
- `summaryPath`
- `examples`
- `preferredFor`
- `replacedBy`

Acceptance:

- Manifest covers current Just recipes and `wo` commands.
- Manifest distinguishes final proof, fast profile, warm one-shot, and hot lease.
- Manifest marks SEM27 flows non-mutating unless explicitly final mutation proof.

Validation:

- JSON parse check.
- Optional script to verify manifest command names exist in `just --list` or
  `wo --help`.

Risk:

- Low. Avoid false contract by marking manifest as agent discovery, not runtime
  API.

### S6 Mail/Auth CLI Wrappers

Status: planned after `wo` exists

Objective:
Align non-PowerPoint domains with the same CLI contract.

Candidate commands:

```text
scripts/linux/wo mail status
scripts/linux/wo mail folders
scripts/linux/wo mail search --subject ...
scripts/linux/wo mail download --subject ... --folder ...
scripts/linux/wo auth microsoft cleanup
scripts/linux/wo auth microsoft device-login --code ...
```

Boundary:

- CLI wraps existing REST.
- REST remains source of capability.

Acceptance:

- Commands write summary JSON and print summary path.
- Commands use safe negative tests when credentials/mailbox contents are not
  available.
- Just gets only high-value shortcuts, not every mail/auth subcommand.

Validation:

- Host health.
- `wo mail status`
- safe negative mail search with synthetic subject.
- auth cleanup dry/safe run if supported.

Risk:

- Medium. Mail/auth often depend on user session, credentials, mailbox state,
  or MFA. Use negative-path axiom when positive proof needs real user action.

### S7 Live Smoke Unification

Status: planned

Objective:
Make `live-smoke.py` conform to the same CLI summary contract or wrap it through
`wo smoke`.

Scope:

- `scripts/linux/live-smoke.py`
- `scripts/linux/wo`

Acceptance:

- `wo smoke` prints one summary path.
- Summary uses common top-level fields.
- Existing live-smoke report contents remain available.

Validation:

- `scripts/linux/live-smoke.py` if safe for current runtime.
- `scripts/linux/wo smoke --dry-run` if added.

Risk:

- Medium. Smoke touches several runtime surfaces. Keep existing command
  behavior until replacement is live-proven.

### S8 REST/API Alignment Audit

Status: planned

Objective:
Find workflow logic that leaked into REST or stable runtime details that only
exist in CLI/Just/docs.

Read-only checks:

- Verify OpenAPI paths align with README route inventory.
- Search for local path/lease/run-id/profile semantics in Host/Agent services.
- Search for direct REST request construction in Justfile.
- Search for duplicated exchange root/base URL/default run ID logic across
  scripts.

Potential findings:

- Promote capability to REST if multiple CLI scripts duplicate the same
  primitive.
- Pull workflow state down from Just into CLI if recipes grow.
- Keep REST unchanged if workflow is agent-specific.

Validation:

- `curl http://127.0.0.1:43117/openapi.json | jq '.paths | length'`
- `jq '.paths | length' openapi/windows-operator.openapi.json`
- existing route inventory check from `docs/development.md`

Risk:

- Read-only first. Any REST contract change requires separate approval and live
  proof.

### S9 Documentation Alignment

Status: planned

Objective:
Make docs point to the correct layer and stop teaching obsolete command paths.

Scope:

- `README.md`
- `docs/development.md`
- `docs/feature-namespaces.md`
- `docs/operator-harness-architecture.md`
- domain architecture docs:
  - `docs/powerpoint-automation-architecture.md`
  - `docs/outlook-mail-automation-architecture.md`
  - `docs/email-attachment-automation.md`

Acceptance:

- README introduces runtime/API/harness split.
- Development docs point agents to `wo` and Just shortcuts.
- PowerPoint docs show hot lease path through final chosen command surface.
- Docs preserve direct REST examples for stable API users.

Validation:

- `rg` for stale script names after migration.
- `git diff --check`.

Risk:

- Low, but stale docs can cause wrong operations. Treat docs as operational
  surface.

### S10 Completion Proof

Status: final alignment gate

Objective:
Prove the aligned command surface works end to end and leaves Windows clean.

Required proof:

- `wo health` returns Host `ok`.
- `just --list` exposes concise shortcut set.
- `wo ppt hot start` opens one lease.
- `wo ppt hot status` reports ready.
- `wo ppt hot run` reuses session with no `deckUrl` and `cleanupSession=false`.
- `wo ppt hot cleanup` closes lease, removes lease file, and returns windows to
  baseline.
- `wo mail status` or safe negative mail command works or records credential
  blocker.
- OpenAPI path count matches committed spec.

Close evidence:

- Record run paths in this roadmap.
- Update `docs/operator-harness-architecture.md` if target changed.
- Update `.work/powerpoint-online-surface-profile-improvements.md` if PPT
  command names changed.

## Evidence Log

### 2026-07-05: S8 Initial REST/API Alignment Audit

Commands:

```bash
jq '.paths | length' openapi/windows-operator.openapi.json
comm -23 <(jq -r '.paths | keys[]' openapi/windows-operator.openapi.json | sort) <(rg -o '(/v1/[A-Za-z0-9_{}./-]+)' README.md | sort -u)
comm -13 <(jq -r '.paths | keys[]' openapi/windows-operator.openapi.json | sort) <(rg -o '(/v1/[A-Za-z0-9_{}./-]+)' README.md | sort -u)
rg -n "hot-lease|hot lease|SEM27|deckUrl|cleanupSession|verifyReopen|profile-warm|ppt-hot" src/WindowsOperator.Host src/WindowsOperator.Agent src/WindowsOperator.Core || true
rg -n "curl .*127\\.0\\.0\\.1|Invoke-RestMethod|/v1/|jq|python3|powershell|pwsh" Justfile
```

Observed:

- Committed OpenAPI has `55` paths.
- README route inventory covers all committed OpenAPI paths.
- README has one expected extra prose marker: `/v1/dev/...`.
- Justfile has no direct REST calls, JSON parsing, or state machine logic.
- PowerPoint URL/session/cleanup fields in Host/Agent are stable runtime
  capabilities, not CLI lease/profile semantics.

Decision:

- No REST contract change needed for S1-S7.
- Keep hot lease, SEM27 safe defaults, run IDs, and summary conventions in CLI.

### 2026-07-06: S10 Live Proof Failure Before Fix

Command:

```bash
scripts/linux/wo ppt hot start
```

Observed:

- Summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-start-20260706t002258z/summary.json`.
- Exit code: `1`.
- Status: `hotLeaseStartFailed`.
- No hot lease file was created at
  `/var/lib/windows-server/shared/operator-exchange/state/ppt-hot-lease.json`.
- Session-start response:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-start-20260706t002258z/session-start-response.json`.
- Host returned HTTP `200`, but response status was `closed` with actions
  `session_state_observed`, `devtools_status:port_closed`,
  `devtools_snapshot_unavailable`, `session_reused`.
- Returned session id was `ppt-hot-sem27`; no Edge-like windows remained after
  the attempt.

Diagnosis:

- Fixed hot session id can hit stale closed session registry state when no
  lease exists.
- Hot start must cleanup/retry stale returned closed session once before
  failing.

Action:

- Worker handoff issued to fix `scripts/linux/powerpoint-online-final-proof.py`
  and add fake HTTP regression coverage.

Follow-up diagnosis:

- CLI retry exposed the deeper runtime issue: `PowerPointOnlineService`
  reused cached metadata even when the browser state was closed.
- Runtime now recreates stale closed cached sessions before building the
  PowerPoint session result.
- Hot-run also now sanitizes REST PowerPoint job ids derived from CLI run ids
  while preserving the original run id, run folder, summary path, and lease
  `lastRunId`.

### 2026-07-06: Final Roadmap Completion Proof

Local validation:

```bash
scripts/linux/wo-tests.sh
scripts/linux/powerpoint-online-final-proof-tests.sh
python3 -m py_compile scripts/linux/wo scripts/linux/windows_operator_harness.py scripts/linux/powerpoint-online-final-proof.py scripts/linux/live-smoke.py scripts/linux/audit_entra_apps.py
python3 -m json.tool harness/windows-operator.commands.json >/dev/null
git diff --check
just --list
just --dry-run ppt-profile
just --dry-run ppt-profile-fast
just --dry-run ppt-profile-warm
just --dry-run ppt-hot-start
just --dry-run ppt-hot-run
just --dry-run ppt-hot-status
just --dry-run ppt-hot-cleanup
dotnet test WindowsOperator.Portable.slnf
```

Observed:

- All commands passed.
- `dotnet test WindowsOperator.Portable.slnf` passed `112` tests
  (`15` MCP, `97` Host), with pre-existing `NETSDK1188` locale warnings.
- Windows VM Agent service test also passed using local artifact redirection:
  `dotnet test tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj --filter PowerPointOnlineServiceTests`
  passed `39` tests.

Live safe wrappers:

- `scripts/linux/wo health --json`
  - Summary:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-health-after-vm-agent-restart-1-20260706T003454Z/summary.json`.
  - Result: `success=true`, HTTP `200`, runtime `headless-host`.
- `scripts/linux/wo windows list --json`
  - Summary:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-windows-after-agent-task-missing-20260706T003210Z/summary.json`.
  - Result: `success=true`, HTTP `200`, `windowCount=9`.
- `scripts/linux/wo mail status --json`
  - Summary:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-mail-status-20260706t002148z/summary.json`.
  - Result: `success=true`, `workerAvailable=true`.
- `scripts/linux/wo auth microsoft cleanup --json`
  - Summary:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-auth-microsoft-cleanup-20260706t002148z/summary.json`.
  - Result: `success=true`, default dry-run.
- `scripts/linux/wo mail search --json --run-id wo-mail-search-live-synthetic-20260706t002156z`
  - Summary:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-mail-search-live-synthetic-20260706t002156z/summary.json`.
  - Result: `success=true`, HTTP `200`, `messageCount=0`, `recovered=false`.

Live hot-session proof:

```bash
scripts/linux/wo ppt hot start --run-id PPT-Hot-Start-Live-20260706T003926Z
scripts/linux/wo ppt hot status --run-id PPT-Hot-Status-Live-20260706T003926Z
scripts/linux/wo ppt hot run --run-id PPT-Hot-Run-Live-20260706T003926Z
scripts/linux/wo ppt hot cleanup --run-id PPT-Hot-Cleanup-Live-20260706T003926Z
```

Observed:

- Start summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Start-Live-20260706T003926Z/summary.json`.
  - `success=true`, `status=hotLeaseStarted`, session start `status=ready`,
    `edgeLikeWindowCountBefore=0`, `edgeLikeWindowCount=1`.
- Status summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Status-Live-20260706T003926Z/summary.json`.
  - `success=true`, `status=hotLeaseReady`.
- Run summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Run-Live-20260706T003926Z/summary.json`.
  - `success=true`, `status=hotRunSucceeded`,
    `claimedBy=officejs-taskpane`, `jobStatus=succeeded`.
  - Request used sanitized REST job id
    `ppt-hot-run-live-20260706t003926z-hot` while preserving run id
    `PPT-Hot-Run-Live-20260706T003926Z`.
  - Request omitted `deckUrl`, used `sessionId=ppt-hot-sem27`,
    `cleanupSession=false`, `validateOnly=true`.
- Cleanup summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Cleanup-Live-20260706T003926Z/summary.json`.
  - `success=true`, `status=hotLeaseClosed`, cleanup HTTP `200`,
    cleanup response `status=closed`, `edgeLikeWindowCountBefore=1`,
    `edgeLikeWindowCount=0`.
  - Lease file removed from
    `/var/lib/windows-server/shared/operator-exchange/state/ppt-hot-lease.json`.

Live smoke and API parity:

```bash
scripts/linux/wo smoke --json --run-id wo-smoke-harness-v2-20260706T004057Z -- --skip-powerpoint-addin
jq '.paths | length' openapi/windows-operator.openapi.json
curl -sS http://127.0.0.1:43117/openapi.json | jq '.paths | length'
```

Observed:

- Smoke summary:
  `/var/lib/windows-server/shared/operator-exchange/runs/wo-smoke-harness-v2-20260706T004057Z/summary.json`.
  - `success=true`, `status=ok`, `reportOk=true`.
  - Report artifact:
    `/var/lib/windows-server/shared/operator-exchange/runs/wo-smoke-harness-v2-20260706T004057Z/live-smoke-report.json`.
  - Live-smoke report: `ok=true`, `passed=32`, `failed=0`.
- Committed OpenAPI paths: `55`.
- Live OpenAPI paths: `55`.

## Suggested Implementation Order

1. S0 inventory
2. S1 CLI contract
3. S2 `wo` dispatcher
4. S3 Justfile delegates to `wo`
5. S5 command manifest
6. S4 shared helper extraction
7. S6 mail/auth wrappers
8. S7 smoke unification
9. S8 REST/API alignment audit
10. S9 docs alignment
11. S10 completion proof

Reason for this order:

- Keep visible command UX stable first.
- Add `wo` as a wrapper before extracting helpers.
- Delay shared helper extraction until behavior is pinned by wrapper tests.
- Delay mail/auth expansion until the CLI contract is proven by PowerPoint.

## Validation Commands

Local:

```bash
scripts/linux/powerpoint-online-final-proof-tests.sh
python3 -m py_compile scripts/linux/*.py
just --list
git diff --check
```

Live Windows:

```bash
curl -sS http://127.0.0.1:43117/v1/health
just ppt-hot-start
just ppt-hot-status
just ppt-hot-run
just ppt-hot-cleanup
curl -sS http://127.0.0.1:43117/v1/windows
```

After `wo` exists:

```bash
scripts/linux/wo health
scripts/linux/wo ppt hot start
scripts/linux/wo ppt hot status
scripts/linux/wo ppt hot run
scripts/linux/wo ppt hot cleanup
```

## Major Refactor Decision

No major refactor is warranted now.

Recommended path is incremental alignment:

- Add `wo` as a thin CLI facade.
- Keep existing PowerPoint harness working.
- Move Just recipes to `wo` only after equivalent behavior is proven.
- Extract shared helpers only after the command contract stabilizes.

Major refactor becomes warranted only if:

- multiple domains need shared state machines,
- CLI duplication starts hiding bugs,
- REST and CLI disagree on contract semantics,
- or DevTools/session ownership work requires a new runtime owner.

## Open Questions

- Should `wo` be a Python script or a small compiled .NET global tool?
  - Recommendation: Python first, because Linux-side harness already uses
    Python and agent install cost is lower.
- Should command manifest be generated from `wo` metadata?
  - Recommendation: static JSON first; generation later if drift appears.
- Should hot lease be a REST resource?
  - Recommendation: no. Lease is agent/operator workflow state, not stable
    runtime capability.
- Should Just recipes remain after `wo` lands?
  - Recommendation: yes. Just remains the quickest agent-discovery menu.
