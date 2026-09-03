# Comprehensive v1 Contract Roadmap

Date: 2026-07-22

Status: recoverable source baseline established; current-runtime publication
authorized but Legion unavailable; live acceptance blocked at 44/67

Target: Windows Operator contract `1.0.0` on the existing `/v1` route family

## Outcome

Publish a comprehensive, stable application contract that another project can
adopt through REST/OpenAPI or the generated Go client without depending on
Windows Operator scripts, repository paths, SSH, Just recipes, staged
PowerShell, COM, UIA, Office.js, or machine-local state.

The v1 promise covers:

- an intentionally frozen stable route and workflow set;
- machine-accurate schemas and uniform runtime semantics;
- capability and version discovery;
- branchable errors, long-running work, and opaque artifacts;
- local loopback use and an authenticated remote relay profile;
- generated-client usability and language-neutral HTTP examples;
- compatibility, deprecation, drift, and release governance;
- live conformance proof from a fresh external consumer;
- operation-level functional proof for every published operation, including
  diagnostic and development surfaces.

`/v1` in a URL does not itself establish this promise. The promise begins with
the `1.0.0` contract release and its frozen baseline.

## Non-Goals

- Do not expose arbitrary Windows internals as an application contract.
- Do not make CLI, Just, SSH runners, or staged PowerShell consumer APIs.
- Do not move authentication or public binding into the loopback Host.
- Do not promise every diagnostic, development, or low-level automation route
  as stable. Those routes must still work as documented while published.
- Do not add SDK languages without a named consumer requirement. OpenAPI stays
  language-neutral; Go remains the reference generated client for v1.
- Do not cut or push a release tag, deploy a relay, provision credentials, or
  run consequential app mutations without the stated operator gate.

## Governing Sources

Ranked authority:

1. Governing intent:
   - `docs/external-consumer-integration.md`
   - `docs/operator-harness-architecture.md`
2. Contract implementation:
   - `src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs`
   - `src/WindowsOperator.Core/Contracts/OperatorJsonSchema.cs`
   - Core request, result, error, artifact, capability, and job contracts
   - Host endpoint behavior
3. Generated projections:
   - `openapi/windows-operator.openapi.json`
   - `clients/go`
4. Supporting policy:
   - `docs/operator-error-codes.md`
   - `docs/external-consumer-relay.md`
   - `docs/external-consumer-release.md`
5. Historical evidence:
   - `.work/external-consumer-integration-roadmap.md`

Generated projections never override source behavior. Historical completion
claims require current code, test, or live-runtime evidence.

## Reconciled Baseline

Audit snapshot, 2026-07-22:

- Contract version: `0.1.0`; no local or remote release tag.
- OpenAPI: 66 operations, 109 schemas; 54 stable, 10 diagnostic, 2
  development.
- Every operation has an ID, summary, tag, surface class, and typed error
  response.
- Live Host health and capabilities pass; live OpenAPI is semantically equal to
  the committed document.
- Schema fidelity is incomplete: request requiredness is suppressed and core
  error members appear optional.
- Public schemas have no descriptions, defaults, examples, or patterns.
- Generated Go tests do not compile because one helper/test uses an undefined
  `Succeeded` member.
- README route parity fails on the Power Automate Edge cleanup route.
- Error documentation omits two source error codes.
- Current live smoke proves health, version/capabilities, and a typed negative
  error. Current artifact success proof lacks a run ID.
- Host is correctly loopback-only. Relay policy exists; a reusable authenticated
  relay and live authorization proof do not.
- Compatibility/deprecation enforcement and fresh tagged-consumer proof do not
  exist.

Verdict: architecture supports a strong contract. Current state is a capable
pre-v1 surface, not a comprehensive `1.0.0` commitment.

## Execution Checkpoint

Campaign started: 2026-07-23

Goal: complete this roadmap through the strongest locally and live-verifiable
state, preserving explicit gates for release publication, relay deployment,
credential provisioning, and consequential application mutations.

Implemented:

- source and live OpenAPI contain 67 operations and 122 schemas;
- surface policy freezes 55 stable, 10 diagnostic, and 2 development
  operations;
- every operation has owned schemas, lifecycle semantics, exposure policy,
  fixture, cleanup, gates, and proof state;
- Host and Agent return typed `OperatorError` responses for binding,
  validation, unsupported media, unavailable authentication, routing, Windows
  desktop state, and unexpected failures;
- public REST/MCP serialization removes machine-local paths;
- capabilities report contract version, executable build identity, Host state,
  and feature availability;
- schema direction, requiredness, nullability, defaults, constraints,
  descriptions, and generated-client projection are mechanically checked;
- stable-only Go generation, polling/error/artifact/version helpers, error-code
  parity, route parity, lint, and breaking-check hooks run from release checks;
- authenticated relay template, allowlists, redaction, rate limiting, artifact
  privacy, and base-URL rewriting are implemented and tested.

Live campaign evidence, 2026-07-23:

- live endpoints: Legion `http://127.0.0.1:43127`; Windows Server verification
  target `http://127.0.0.1:43117`;
- full Windows tests passed: Core 44, Agent 143, MCP 15, Host 120, Relay 6;
  evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-full-tests-20260723c`;
- post-hardening Windows tests passed: Core 34, Agent 135 after one clean rerun,
  MCP 15, Host 104, Relay 6; focused Legion Agent/Host authentication
  not-found mapping tests also passed; evidence run prefixes
  `v1-contract-tests-*-20260723d/e`,
  `v1-contract-legion-tests-agent-map-20260723b`, and
  `v1-contract-legion-tests-host-map-20260723b`;
- current Legion registration now uses the physical-machine exchange root
  `C:\ProgramData\WindowsOperator\exchange`; this removed the invalid VM-only
  `Z:\operator-exchange` dependency from both scheduled runtimes;
- exact semantic negative handling now distinguishes absent browser, workbench,
  and PowerPoint Online sessions with typed `404` codes instead of accepting
  routing or malformed-request substitutes;
- conformance runner verified 39 safe operations on the presentable Windows
  Server desktop with live success, schema assertion, exact typed negative, and
  cleanup evidence; report:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-conformance-20260723m/v1-contract-conformance.json`;
- gate-safe preflight exercised all 23 externally blocked operations without
  invoking a credentialed or mutating success path: 12 malformed requests, 9
  absent PowerPoint sessions, disabled development automation, and one absent
  mail run all returned their exact typed contract errors; reports:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-gated-preflight-20260723a/v1-contract-conformance.json`
  and
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-vm-gated-preflight-20260723a/v1-contract-conformance.json`;
- reversible raw-JavaScript proof verified the 40th operation, omitted its local
  evidence path from the public result, restored exact launcher bytes, and
  confirmed `422 dev_automation_disabled`; evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-raw-js-proof-20260723i`;
- four cached Microsoft authentication status reads now pass live success,
  typed `404 auth_run_not_found` negatives, and cleanup checks; evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-auth-status-20260723f/v1-contract-conformance.json`;
- post-proof contract hardening removed Power Automate diagnostics and Outlook
  worker errors containing machine-local paths from public schemas; regenerated
  committed/live OpenAPI documents are structurally equivalent;
- proof ledger state: 44 verified, 23 blocked;
- the 2026-07-23 full Windows solution test run passed 339 tests: Core 44, Agent 150,
  MCP 15, Host 124, Relay 6; evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/v1-contract-vm-full-tests-20260723a`;
- safe desktop proof includes activation, UI typing/clicking, screen clicking,
  capture, session/desktop screenshot, hotkey, and typed
  `minimized_rdp`/`locked_desktop` negatives;
- safe job proof includes PowerPoint job enqueue, claim, complete, fail,
  artifact association, reads, and cleanup;
- standalone Go consumer called health/capabilities, decoded a typed mail
  negative, listed artifacts, and downloaded a 990-byte artifact without
  harness APIs;
- local authenticated Relay returned `401` for missing/wrong credentials,
  `403 relay_route_forbidden` for a disallowed route, `200` for authenticated
  health/artifact calls, rewrote artifact `href`, and downloaded the same
  990-byte private artifact.

Current source reconciliation, 2026-07-31:

- checkpoint `cp-20260731-01` fingerprints the complete non-`.work` source at
  HEAD `9ff01e88020dea08f8124cfee04759ddf90e6674` plus tracked and untracked
  worktree content;
- OpenAPI lint/generation/contract checks pass for 67 operations and 122
  schemas; policy checks pass at 55 stable, 10 diagnostic, 2 development, 44
  verified, and 23 blocked;
- README route parity passes for all 67 method/path entries; all 30 source error
  codes are documented; Go client tests pass;
- Linux portable build/tests pass; PowerPoint add-in 36 tests and production
  build pass;
- the post-sync source mirror at `C:\src\windows-operator` passed the full
  Windows solution: Core 44, Agent 150, MCP 15, Host 124, Relay 6, total 339;
  the sync archive and test run identify the transfer and source path but do not
  cryptographically bind Windows files to the worktree fingerprint; evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/run-20260731T072251Z-2092480`;
- the repo-owned operator-safe provisioning profile passed isolated audit,
  apply, idempotence, and rollback verification; evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/run-20260731T072445Z-2112938`;
- live `GET http://127.0.0.1:43117/v1/health` returned `status: ok`, but live
  capabilities report source revision
  `3b97b07894dd92074fb82cc8746eb82761cd65af`. The live runtime therefore does
  not prove the current dirty checkpoint. Historical operation proof remains
  valid only as historical evidence; no blocked row was promoted.

Remaining gates:

1. authorize and supply prerequisites for 23 operations: 1 Edge reset, 4 Power
   Automate, 12 PowerPoint Online, 2 interactive Microsoft-auth starts, and 4
   Outlook mail operations;
2. reach 67/67 verified with zero pending/blocked rows;
3. approve RC tag, then run a clean consumer pinned to `v1.0.0-rc.1`;
4. approve final `v1.0.0` tag/push and any relay deployment.

## Critical Proof Dependencies

Availability records are non-secret planning state. They do not authorize use.

| Dependency | Availability | Earliest blocked slice | Planning evidence |
| --- | --- | --- | --- |
| Legion Windows host and desktop session | unavailable | current-runtime publication | SSH/Tailscale/LAN probes timed out on 2026-07-31; targeted WoL did not restore reachability. |
| Recoverable checkpoint baseline | available | none | Checkpoint source is local commit `f8a3c4225de9cf7e77479ae2309e5a4be5663a63`. |
| Current-source runtime publication | unavailable | current-runtime conformance | User authorized publication and task restart on 2026-07-31, but Legion has no reachable control plane. Prior live Host reports older source revision `3b97b07894dd92074fb82cc8746eb82761cd65af`. |
| Disposable Edge state for reset proof | unconfirmed | Edge gated conformance | Named disposable browser fixture and mutation approval are not recorded. |
| Authenticated Power Automate tenant and disposable flows | unconfirmed | Power Automate gated conformance | Tenant/session availability and consequential-flow approval are not recorded. |
| Authenticated PowerPoint Online session and disposable deck | unconfirmed | PowerPoint Online gated conformance | Session/deck availability and mutation approval are not recorded. |
| Interactive Microsoft-auth test account/session | unconfirmed | Microsoft-auth start conformance | Credential/session availability is not recorded. |
| Outlook mailbox and disposable messages/attachments | unconfirmed | Outlook gated conformance | Mailbox fixture and mutation approval are not recorded. |
| RC/final tag, push, and release authority | unavailable | `V1-RC-PROOF` | Explicit authority has not been granted. |
| Relay deployment authority | unavailable | remote release proof, if claimed | Explicit deployment authority has not been granted. |

No implementation slice is currently admitted. `LEGION-CONNECTIVITY` is the
next gate; current-runtime publication authority remains available. After
publication, each gated conformance batch remains blocked until its dependency
changes to `available` and its required effect is authorized.

## Release Principles

- Freeze promises, not accidents. Audit current routes before assigning v1
  stability.
- Prefer additive changes before `1.0.0`; reject unreviewed breaking changes
  after the release-candidate baseline.
- Keep policy at Host/relay boundaries and platform mechanics behind contracts.
- Make invalid requests mechanically difficult and failures mechanically
  branchable.
- Require live Windows proof for behavior dependent on Windows, desktop apps,
  browser state, Office, COM, UIA, credentials, or Task Scheduler.
- Treat dry-run as serialization/routing evidence only.
- Treat surface class as compatibility policy, not a verification exemption.
- Fix or remove any published operation that cannot pass its functional proof.
  No skipped operation can ship in the `1.0.0` OpenAPI document.

## Ordered Roadmap

| Handle | State | Priority | Outcome | Completion evidence |
| --- | --- | --- | --- | --- |
| `V1-SURFACE-FREEZE` | implemented | P0 | Define consumer jobs and classify every operation as stable, diagnostic, development, or excluded from v1. Resolve whether low-level `input`, `uia`, authentication, and currently diagnostic Power Automate routes are durable application APIs. | `openapi/windows-operator.operation-policy.json` owns all 67 rows: 55 stable, 10 diagnostic, 2 development. |
| `V1-SCHEMA-FIDELITY` | implemented | P0 | Make OpenAPI requiredness, nullability, formats, constraints, defaults, descriptions, examples, and enums match Core/runtime invariants. | Generator tests, 122 generated schemas, regenerated OpenAPI/Go projection, valid fixtures, and invalid-request tests pass. |
| `V1-SEMANTICS` | implemented | P0 | Standardize status codes, error shape, correlation, retryability, timeouts, idempotency, concurrency, cancellation, polling, terminal states, pagination where needed, and artifact lifetime/integrity. | Normative semantics and policy rows are checked; live safe negatives return documented typed errors. |
| `V1-DISCOVERY` | implemented | P0 | Make version, feature availability, surface maturity, and unsupported-feature behavior discoverable before workflow execution. | Live health, capabilities, root OpenAPI, and namespace documents passed; Go version checks pass. |
| `V1-ALL-OPERATIONS-CONFORMANCE` | blocked at 44/67 | P0 | Prove every published operation works end to end for its documented purpose, regardless of stable, diagnostic, or development classification. | 44 verified; all 23 gated rows pass exact negative preflight but remain blocked on credentialed/content-mutating success proof. Evidence: `v1-contract-conformance-20260723m`, `v1-contract-raw-js-proof-20260723i`, `v1-contract-auth-status-20260723f`, and `v1-contract-vm-gated-preflight-20260723a`. |
| `V1-CLIENT-USABILITY` | implemented; pre-RC proof passed | P0 | Repair and harden the Go reference client; document equivalent raw HTTP flows. Hide polling, typed-error, and artifact-download complexity behind useful helpers without hiding policy. | Clean generation/tests and standalone live consumer proof passed. RC-tag pin remains downstream. |
| `V1-DOC-PARITY` | implemented | P0 | Keep route inventory, error-code table, examples, and generated contract synchronized. | Route/error/policy checks pass; governing semantics and release docs reconciled on 2026-07-23. |
| `V1-REMOTE-PROFILE` | implemented; local proof passed; deployment gated | P1 release requirement | Supply a reusable authenticated relay profile while Host remains loopback-only. Define allowlists, caller/workflow authorization, redaction, rate limits, artifact privacy, base-URL rewriting, and audit fields. | Relay tests and live loopback auth/allowlist/rewrite/private-artifact proof passed. Public deployment remains unauthorized. |
| `V1-COMPAT-GOVERNANCE` | implemented before RC; baseline gate unexercised | P0 | Establish SemVer, deprecation metadata, compatibility window, breaking-change review, and frozen-baseline drift checks. | Default lint/projection checks pass before the first tag. A committed baseline invokes the breaking checker; release preparation must supply either that baseline or an explicit checker command once a previous tag exists. No tag or RC baseline exists yet. |
| `V1-RC-PROOF` | blocked on 67/67 and tag authority | P0 | Freeze `1.0.0-rc.1` and prove adoption from outside this repository against the live Windows runtime. | Pre-RC standalone consumer passed. Frozen RC ledger and tag-pinned install cannot run before gates. |
| `V1-RELEASE` | blocked on RC evidence and operator authority | P0 gate | Publish `v1.0.0` and its generated client with migration, support, and rollback guidance. | All release checks green from clean state; operator approves tag/push; fresh external install from `v1.0.0`; repeat live smoke; release notes identify stable surface and deferred capabilities. |
| `V1-POSTRELEASE` | deferred until release | P1 | Preserve the v1 baseline and operate compatibility maintenance. | Tagged baseline fixture; scheduled drift review; additive `1.x` policy; documented security/compatibility response path. |

## Dependency Flow

```text
V1-SURFACE-FREEZE
  -> V1-SCHEMA-FIDELITY
      -> V1-SEMANTICS
      -> V1-DISCOVERY
      -> V1-CLIENT-USABILITY
      -> V1-DOC-PARITY
  -> V1-ALL-OPERATIONS-CONFORMANCE
  -> V1-COMPAT-GOVERNANCE
  -> V1-RC-PROOF
  -> V1-RELEASE
  -> V1-POSTRELEASE

V1-REMOTE-PROFILE -> V1-RC-PROOF when remote application use is claimed.
```

`V1-CLIENT-USABILITY` and `V1-DOC-PARITY` can proceed together after schema
shape stabilizes. Operation proof waits for schema and semantics to avoid
validating accidental behavior.

## v1 Surface Freeze Deliverable

Create one matrix row per OpenAPI operation with:

- method, path, operation ID, namespace, and current surface class;
- consumer job and expected caller;
- local and remote exposure profile;
- request/result/error schema ownership;
- synchronous, polling, or artifact lifecycle;
- idempotency, retry, timeout, cancellation, and concurrency semantics;
- sensitive-input/output classification;
- representative success and negative conformance cases;
- controlled fixture, expected observable effect/result, and cleanup strategy;
- proof environment, prerequisites, external-effect gate, and evidence location;
- proposed v1 disposition: stable, diagnostic, development, or excluded;
- unresolved product decision and owner.

Current namespace totals establish the audit boundary:

| Namespace | Operations | Stable | Diagnostic | Development |
| --- | ---: | ---: | ---: | ---: |
| `artifacts` | 2 | 2 | 0 | 0 |
| `auth.microsoft` | 7 | 5 | 2 | 0 |
| `browser.edge` | 11 | 10 | 0 | 1 |
| `desktop` | 5 | 5 | 0 | 0 |
| `input` | 2 | 2 | 0 | 0 |
| `mail.outlook` | 5 | 5 | 0 | 0 |
| `power-automate.mcp` | 6 | 0 | 6 | 0 |
| `powerpoint.jobs` | 6 | 6 | 0 | 0 |
| `powerpoint.online` | 12 | 9 | 2 | 1 |
| `sessions` | 3 | 3 | 0 | 0 |
| `system` | 5 | 5 | 0 | 0 |
| `uia` | 3 | 3 | 0 | 0 |

Classification changes require contract rationale. A route is not promoted
because it exists or demoted because implementation is difficult.

## Every-Operation Proof Standard

Freeze the verification denominator from the RC OpenAPI document. The current
baseline is 67 operations; the final denominator changes when operations are
added or removed before RC.

Create one proof-ledger row per operation. A row reaches `verified` only when:

1. A valid, reproducible fixture exercises the real live Windows runtime.
2. The endpoint returns its documented success status and schema-valid result.
3. The intended user-visible effect or read result is observed at the owning
   Windows application/runtime boundary.
4. The generated Go client can serialize and decode the operation when the
   operation belongs to the stable external surface.
5. At least one meaningful invalid, unavailable, or not-found case returns the
   documented HTTP status and typed `OperatorError`.
6. Retry, timeout, idempotency, polling, cancellation, concurrency, and artifact
   behavior pass where the operation exposes those semantics.
7. Created sessions, jobs, files, windows, browser state, or other owned
   fixtures are cleaned up and the cleanup result is recorded.
8. Evidence records exact method/path, operation ID, request fixture, observed
   status/result, runtime/build identity, timestamp, and log/artifact location.

Proof exclusions:

- Compile, schema generation, mock, serialization, and dry-run evidence support
  a row but cannot replace live success.
- Namespace-level sampling cannot close untested operation rows.
- A safe negative test does not substitute for live success. When credentials,
  content, approval, or consequential mutation blocks success, the row stays
  `blocked` and blocks `V1-RC-PROOF`.
- Diagnostic and development labels do not waive functional proof.
- If an operation should not be supported, remove it from the published RC
  contract and document the disposition before freezing the denominator.

## Comprehensive v1 Acceptance Gate

All conditions must hold before `V1-RELEASE`:

1. Stable scope
   - Every v1 operation belongs to an approved consumer job.
   - Diagnostic/development operations are visibly excluded from compatibility
     promises and ordinary relays.
   - Every operation remaining in the published RC contract has an owned proof
     row, even when excluded from the stable compatibility promise.
2. Contract fidelity
   - Requiredness, nullability, validation, defaults, and enums match runtime.
   - Every stable operation has normative request, success, and error semantics.
3. Reliability
   - Retry, idempotency, timeout, cancellation, polling, and artifact rules are
     explicit wherever applicable.
4. Security
   - Host remains loopback-only.
   - Remote profile rejects unauthorized and non-allowlisted calls, redacts
     secrets, and protects artifacts.
5. Consumer usability
   - OpenAPI lint, generation, Go tests, route parity, error parity, and breaking
     checks pass by default.
   - A fresh external module needs only the contract/client, base URL, and
     credentials appropriate to its profile.
6. Live proof
   - The frozen operation ledger is complete: target 67/67; final numerator
     equals the RC OpenAPI denominator. Current state is 44/67.
   - Exact live endpoints, fixtures, observed outcomes/effects, and cleanup
     results are recorded.
   - At least one artifact-bearing success and typed negative path pass.
   - No row is skipped, waived, inferred from another operation, or closed by
     dry-run/mock evidence.
7. Release governance
   - RC baseline has no unapproved breaking diff.
   - Migration/release/support docs identify the v1 promise.
   - Tag/push and any live external effects have operator approval.

## Operator Decisions

Roadmap work should produce recommendations before requesting decisions:

1. Restore `LEGION-CONNECTIVITY`: physically power on/wake
   `DESKTOP-6BT2OFE`; if already on, reboot or restore networking until SSH port
   22 responds. Log in/unlock as `alejg` for Desktop Agent verification.
2. Approve the proposed stable v1 operation set, especially low-level
   `input`/`uia`, Microsoft auth, and Power Automate MCP classifications.
3. Confirm whether remote consumability is a `1.0.0` release claim. Default:
   yes; include the reusable relay profile while keeping Host loopback-only.
4. Approve any live proof requiring real credentials, mailbox/deck contents, or
   consequential mutations.
5. Approve RC and final tag/push only after acceptance evidence is complete.

## Highest-Value Next Operator Action

Restore `LEGION-CONNECTIVITY`; publication is already authorized.

Power on/wake Legion (`DESKTOP-6BT2OFE`). If it is already on, reboot or restore
networking until SSH port 22 responds. Log in/unlock as `alejg` for Desktop
Agent/live desktop verification. Then resume the authorized publication of
commit `f8a3c4225de9cf7e77479ae2309e5a4be5663a63`, restart
`WindowsOperator.Host` and `WindowsOperator.Agent`, and require
health/capabilities to report the checkpoint revision before any operation row
is promoted. Publication authority includes no tenant, mailbox, deck, flow, or
browser-state mutation.

After current build identity is proved, admit only dependency-ready
`V1-ALL-OPERATIONS-CONFORMANCE` batches. The smallest next proof candidate is
the single Edge reset row, but it still requires a named disposable browser
fixture and explicit mutation approval.

Next authority packet:

1. Restore Legion power/network/SSH and log in/unlock for Agent verification.
2. Provide/authorize credentialed Microsoft auth, Outlook mailbox, Power
   Automate tenant, and PowerPoint Online fixtures.
3. Approve consequential writes only against named disposable resources.

Exit evidence:

- all 67 operations contain success, negative, and cleanup evidence;
- no proof row remains pending or blocked;
- credentials and tenant content are absent from logs/evidence;
- owned browser/deck/mail/flow fixtures are cleaned up;
- Desktop Agent raw JavaScript returns to disabled-by-default;
- RC decision packet contains exact runtime/build and evidence locations.

## Execution Handoff

After acceptance of this roadmap, invoke `$autonomous-work` with:

```text
Governing roadmap:
.work/comprehensive-v1-contract-roadmap.md

Governing intent:
docs/external-consumer-integration.md
docs/operator-harness-architecture.md

Admission gate:
LEGION-CONNECTIVITY

First post-admission slice:
resume the authorized checkpoint publication to Legion, prove
health/capabilities build identity, then run V1-ALL-OPERATIONS-CONFORMANCE
batches only where dependencies are available and effects are authorized

Required exit evidence:
the "Exit evidence" under "Highest-Value Next Decision"
```

Do not begin `V1-RELEASE`, deploy a relay, provision credentials, or run
consequential external mutations without its operator gate.

## Current Handoff

```text
Last-Validated-Checkpoint:
  Checkpoint-ID: cp-20260731-01
  Evidence-Base:
    Commit: f8a3c4225de9cf7e77479ae2309e5a4be5663a63
    Scope: complete repository source, tests, contracts, projections, scripts,
      configuration, and governing docs; excludes .work bookkeeping and ignored
      or generated runtime artifacts
    Fingerprint: git:f8a3c4225de9cf7e77479ae2309e5a4be5663a63
      mapped without source changes from reviewed worktree-v1
      sha256:e9f1d7a63260497d350799ba88e9cb26c9c0aaec53e90cee642d4023d2d1ee5d
  Validation:
    Coverage: complete
    Result: pass
    Evidence: contract/policy/route/error checks; Go tests; Linux portable
      build/tests; PowerPoint add-in tests/build; git diff --check; Windows
      source sync sync-20260731T072242Z-2092062; post-sync full-solution run
      run-20260731T072251Z-2092480; isolated provisioning run
      run-20260731T072445Z-2112938
  Acceptance-Evidence: current source and projection validation only; the
    retained operation-policy ledger remains 44/67 from prior live evidence
    because the published runtime reports source revision
    3b97b07894dd92074fb82cc8746eb82761cd65af
  Review:
    Required: yes
    Result: pass
    Evidence-Base: git:f8a3c4225de9cf7e77479ae2309e5a4be5663a63,
      mapped from reviewed worktree-v1
      sha256:e9f1d7a63260497d350799ba88e9cb26c9c0aaec53e90cee642d4023d2d1ee5d
    Evidence: independent read-only reconciliation review,
      /root/reconciliation_review, 2026-07-31; fingerprint reproduced; planning
      checkpoint passed; live acceptance/release failed as expected
Current-Relation: current
In-Flight: none; authorized publication made no target changes because Legion
  was unreachable
Inherited-Dirty: none in checkpoint scope; planning-only .work records remain
  outside the source baseline
Next-Safe-Slice: after Legion SSH returns, resume authorized publication and
  prove checkpoint build identity
Active-Gates: LEGION-CONNECTIVITY, CURRENT-RUNTIME-PUBLICATION,
  V1-ALL-OPERATIONS-CONFORMANCE, V1-RC-PROOF, V1-RELEASE
```

Authorization record:

- Authority source: explicit user request, `$autonomous-work reconcile`,
  2026-07-31.
- Target/environment: local repository planning state, Legion machine-local
  source mirror `C:\src\windows-operator`, and isolated Windows test state.
- Authorized/performed effects: inspect, reconcile `.work`, sync the disposable
  source mirror, and run non-consequential validation. The isolated provisioning
  test restored its registry fixture. User response `go`, 2026-07-31,
  authorized and consumed one local atomic checkpoint commit:
  `f8a3c4225de9cf7e77479ae2309e5a4be5663a63`. User response `do it`,
  2026-07-31, authorizes publishing that commit to Legion machine-local Host and
  Agent runtimes plus restarting `WindowsOperator.Host` and
  `WindowsOperator.Agent`; authority remains available because no target change
  occurred.
- Not authorized: credentials or tenant/mail/deck access, consequential
  application mutation, relay deployment, release tag, push, or release
  publication.
- Invalidation: target, environment, effect, or material-risk change.

Portfolio transfer:

```text
Project: windows-operator
Roadmap: .work/comprehensive-v1-contract-roadmap.md
Last validated checkpoint: cp-20260731-01
Highest-value safe slice: restore LEGION-CONNECTIVITY; then resume the already
  authorized publication and prove checkpoint build identity before admitting
  dependency-ready gated conformance batches
Required acceptance evidence: current-runtime proof for all 67 published
  operations with success, typed negative, cleanup, build identity, and exact
  evidence locations
Proof dependencies: see Critical Proof Dependencies; recoverable baseline
  available; Legion and current-runtime publication unavailable on connectivity;
  all credentialed or disposable fixtures unconfirmed; RC/tag/push/release and
  relay deployment unavailable
Operator gates: LEGION-CONNECTIVITY, CURRENT-RUNTIME-PUBLICATION,
  V1-ALL-OPERATIONS-CONFORMANCE, V1-RC-PROOF, V1-RELEASE
Authorized effects: planning reconciliation, non-consequential validation, one
  consumed local checkpoint commit, and pending Legion Host/Agent publication
  plus task restart under the explicit 2026-07-31 requests
```
