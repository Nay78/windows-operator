# PowerPoint Online Surface Profile Improvements

Date: 2026-07-05

## Live Evidence

Manual surface profile:

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-20260705t080715z/summary.json`
- Result: session opened, slide selected, screenshot captured, save observed, add-in activated, job APIs exercised, Office.js manual validate-only job completed, session cleanup closed.
- Final Edge/PowerPoint windows: `0`

One-call readiness profile:

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-onecall-20260705t080929z/summary.json`
- Command: `scripts/linux/powerpoint-online-final-proof.py --verify-readiness`
- Endpoint: `POST /v1/powerpoint/online/updates`
- Result: HTTP `200`, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`, session cleanup `closed`.
- Elapsed: `164.603s`
- Final Edge/PowerPoint windows: `0`

Slowest manual steps:

| Surface | Elapsed | Result |
| --- | ---: | --- |
| `POST /v1/powerpoint/online/sessions` | `34.218s` | HTTP `200`, `ready` |
| `POST /v1/powerpoint/online/sessions/{id}/slides/select` | `31.752s` | HTTP `200`, slide 4 verified |
| `POST /v1/powerpoint/online/sessions/{id}/addin/probe` | `12.351s` | HTTP `200`, add-in ready |
| `POST /v1/powerpoint/online/sessions/{id}/addin/run-pending-job` | `6.251s` | HTTP `200`, command clicked |
| `POST /v1/powerpoint/online/sessions/{id}/screenshot` | `4.642s` | HTTP `200`, PNG evidence |
| `POST /v1/powerpoint/online/sessions/{id}/save/wait` | `2.498s` | HTTP `200`, `saved` |
| `GET /v1/powerpoint/online/sessions/{id}` | `2.326s` | HTTP `200`, ready |
| Office.js job terminal wait | `2.011s` | `succeeded` |

Fast surfaces:

- Job API enqueue/claim/complete/get: `<0.04s`
- Template prepare/cleanup mutation gates with `allowDeckMutation=false`: HTTP `422` in `<0.01s`
- Dev script gate: HTTP `422 dev_automation_disabled` in `0.009s`

## Improvement 1: Slide Selection

Boundary: `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`

Evidence:

- `SelectOnlineSlideAsync` tries DOM click paths before thumbnail fallback.
- `SlideClickRequests` defines four selectors, each with `TimeoutSeconds = 2`.
- Live action trace: `devtools_snapshot_unavailable`, `slide_select_dom_unavailable:4`, then thumbnail click and UIA verification.
- Live elapsed: `31.752s`.

Interface:

- Prefer no public REST change.
- Add internal session capability state, e.g. `DevToolsUsable=false`, owned by Edge session state.
- `SelectOnlineSlideAsync` chooses strategy from capability:
  - DevTools usable: DOM first.
  - DevTools unavailable: thumbnail/UIA first.

Hidden depth:

- DevTools target probing.
- Stale remote debugging port handling.
- Retry TTL before re-probing DevTools.
- Fallback click geometry.
- UIA verification and keyboard correction.

Caller impact:

- Callers still request slide number only.
- No caller needs to know DOM selectors, DevTools availability, or thumbnail math.

Validation:

- Live SEM27 slide 4 selection should drop from ~32s to single-digit seconds.
- Test DevTools-unavailable path skips DOM attempts and still verifies `currentSlide=4`.
- Test DevTools-available path still tries DOM first.

## Improvement 2: DevTools State

Boundary: `src/WindowsOperator.Agent/Services/EdgeMicrosoftAuthService.Browser.cs`

Evidence:

- `RunDomAction` loops until timeout even when no DevTools target is available.
- `ReadAndPersistBrowserState` records `devtools_snapshot_unavailable`, but callers keep retrying as if DevTools might be usable.

Interface:

- Extend internal browser session state with a stable capability, e.g.:

```text
DevToolsStatus = Ready | TargetUnavailable | PortClosed | Unknown
```

- DOM actions fail fast when status is `TargetUnavailable` or `PortClosed`, unless TTL says to re-probe.

Hidden depth:

- `/json/list` read failures.
- Target selection.
- Port reuse/staleness.
- Probe backoff.
- Error translation to stable action/warning names.

Caller impact:

- PowerPoint code stops rediscovering the same DevTools failure.
- Dev scripts and browser DOM helpers get clearer blocked state.

Validation:

- Unit test `RunDomAction` target-unavailable fast path.
- Live profile should show fewer `devtools_snapshot_unavailable` repeats and faster slide selection.

## Improvement 3: Observation Budget

Boundary: `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`

Evidence:

- `BuildSessionResultAsync` can perform screenshot capture and UIA query for many operations.
- `GET session`, `save/wait`, and `screenshot` all pay observation cost.
- Manual timings: `session.get=2.326s`, `save.wait=2.498s`, `screenshot=4.642s`.

Interface:

- Add internal observation modes, not necessarily public REST fields:

```text
StatusOnly
SlideState
FullEvidence
```

- Or use an internal request object:

```text
PowerPointObservationRequest { NeedSlideState, NeedSaveState, NeedScreenshot, Label }
```

Hidden depth:

- Metadata reuse.
- When UIA is required for correctness.
- When stale slide/save state is acceptable.
- Screenshot artifact write.
- Error/warning merge.

Caller impact:

- High-level orchestration stops doing full observations between every step.
- Public contracts can remain stable.

Validation:

- Save polling should avoid unnecessary full UIA scans.
- One-call readiness should still produce same tier3 evidence.
- Unit tests cover mode selection and metadata fallback.

## Improvement 4: Add-In Command Path

Boundary: PowerPoint add-in command contract plus `RunOnlinePendingJobAsync`.

Evidence:

- `run-pending-job` endpoint took `6.251s`.
- Current path depends on visible taskpane button lookup and screen click.
- Office.js job itself completed quickly after click: terminal status observed after ~2s polling.

Interface:

- Keep Host API shape stable.
- Change implementation so taskpane can run queued work without UI button hunting:
  - auto-claim on ready taskpane, or
  - lightweight taskpane command channel, or
  - URL/hash/local storage trigger consumed by add-in.

Hidden depth:

- Taskpane readiness.
- Duplicate command prevention.
- Current document binding.
- Job claim idempotency.
- Office.js error reporting.

Caller impact:

- Host still says "run pending job".
- No caller handles UIA button names or taskpane geometry.

Validation:

- Live validate-only Office.js job completes without screen click.
- Existing button path remains fallback.
- Job API state remains `queued -> running -> succeeded/failed`.

## Improvement 5: Tier3 Proof Cost

Boundary: `src/WindowsOperator.Host/Services/PowerPointOnlineUpdateService.cs`

Evidence:

- One-call readiness elapsed `164.603s`.
- Tier3 path closes, reopens, selects slide, and captures evidence again.
- Reopen proof is valuable, but too expensive for every stress/profile loop.

Interface:

- Existing `verifyReopen` already controls tier3.
- Consider explicit proof policy if call sites keep confusing readiness/profiling/final proof:

```text
ProofPolicy = None | InSessionEvidence | ReopenVisual
```

Hidden depth:

- Session cleanup.
- Reopen URL choice.
- Evidence slide selection.
- Screenshot validation.
- Final cleanup.

Caller impact:

- Fast loops use in-session evidence.
- Final production proof uses reopen visual.

Validation:

- `verifyReopen=false` stress loop returns materially faster.
- `verifyReopen=true` remains tier3 and still cleans all windows.

## Do Not Optimize First

- Host PowerPoint job API: already sub-40ms in synthetic path.
- Template mutation gates: fast reject path.
- Health/windows endpoints: negligible.
- Mutating template prepare/cleanup on production decks: still require explicit operator intent because SharePoint version history changes.

## Implementation Ledger

Ledger owner: orchestrator.

Implementation owner: worker slice only. Orchestrator does not edit code.

Goal:

- Active Codex goal created 2026-07-05: complete PowerPoint Online surface performance implementation through S1/S2, live re-profiles, and evidence-backed S3/S4 decision gates.

Source truth:

1. `docs/powerpoint-automation-architecture.md`
2. `AGENTS.md`
3. Live profiler artifacts listed above
4. Current code/tests under listed write scopes

Local planning rules:

- No `.work/README.md` or `.work/AGENTS.md` exists in this repo.
- This file is the active ledger for this optimization track.
- Do not mark a slice done until unit tests and live Windows evidence are recorded here.
- Do not mutate SEM27 or any production deck unless the slice explicitly requires it and operator approval is explicit. Current planned slices do not require deck mutation.

Validation commands:

- Unit/focused tests on Windows where affected code runs:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-dotnet-test.ps1 -RepoRoot Z:\windows-operator -Project tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj
```

- Linux runner form, if invoking from this repo:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'C:\src\windows-operator' -Project tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj
```

- Live non-mutating readiness/profile proof:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
  --run-id "ppt-surface-profile-readiness-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
  --verify-readiness \
  --http-timeout-seconds 420
```

Live profile close condition:

- HTTP `200`, `success=true`, `status=succeeded`.
- `saveProofTier=tier3ReopenVisual` for readiness proof.
- Evidence screenshots exist and are non-empty PNGs.
- Final Edge/PowerPoint window count `0`.
- New timings recorded in this ledger.

## Slice Status

| Slice | Status | Priority | Kind | Gate |
| --- | --- | ---: | --- | --- |
| S1 Edge DevTools capability + slide fast fallback | done | P0 | medium refactor | no public API/spec change |
| S1b Re-profile after S1 | done-via-S2b | P0 | live validation | no mutation |
| S2 Observation budget | done | P1 | internal cleanup | no public API/spec change |
| S2b Re-profile after S2 | done | P1 | live validation | no mutation |
| S3 Add-in command protocol | implemented-with-fallback | P2 | behavior change | stable public API; live signal blocked when DevTools port is closed |
| S4 Proof policy naming | deferred-after-review | P3 | API/policy cleanup | not warranted without caller confusion |
| S5 Phase timings | done | P1 | instrumentation | public result schema regenerated |
| S6 Fast profile command | done | P1 | agent UX/profile speed | no deck mutation; tier2 evidence only |
| S7 Warm agent profile target spec | documented | P2 | profile workflow | opt-in only; cleanup guard required |
| S8 Warm agent profile command | live-proven | P1 | profile workflow | non-mutating SEM27 warm loop with cleanup |
| S9 Persistent hot lease command | live-proven | P0 | profile workflow | non-mutating SEM27 hot lease with explicit cleanup |

## Implementation Evidence

S1 implemented:

- Changed paths:
  - `src/WindowsOperator.Agent/Services/EdgeMicrosoftAuthService.Browser.cs`
  - `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
  - `tests/WindowsOperator.Agent.Tests/EdgeMicrosoftAuthServiceBrowserTargetTests.cs`
  - `tests/WindowsOperator.Agent.Tests/PowerPointOnlineServiceTests.cs`
- Behavior:
  - Edge session metadata records bounded DevTools status: `unknown`, `ready`, `target_unavailable`, `port_closed`.
  - DOM actions fail fast on fresh `target_unavailable` or `port_closed`.
  - PowerPoint slide selection skips DOM attempts when latest state has `devtools_status:target_unavailable` or `devtools_status:port_closed`.
  - Latest `devtools_status:*` marker wins, so stale unavailable markers do not mask later ready markers.
- Tests:
  - Linux build: `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj` passed, 0 warnings/errors.
  - Linux test execution blocked by missing `Microsoft.WindowsDesktop.App 8.0.0`.
  - Windows VM focused test:
    `/var/lib/windows-server/shared/operator-exchange/runs/s1s2-agent-tests-vm-20260705c/result.json`;
    42 passed, 0 failed.

S1b live attempt:

- Evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-s1b-readiness-20260705t1722z/summary.json`.
- Command:
  `scripts/linux/powerpoint-online-final-proof.py --verify-readiness`.
- Result:
  HTTP 200, `success=false`, `status=blockedAddIn`, `elapsedSeconds=51.679`, `sessionCleanupStatus=closed`, `edgeLikeWindowCount=0`.
- Useful proof from failed run:
  live slide fallback worked with `devtools_status:port_closed`, `slide_select_dom_skipped:4`, `slide_select_verified:4`.
- Reason not accepted:
  UIA failed to find `Run Pending Job` while screenshot showed add-in pane and button visible.
  This activated S2.

S2 implemented:

- Changed paths:
  - `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
  - `tests/WindowsOperator.Agent.Tests/PowerPointOnlineServiceTests.cs`
- Behavior:
  - `Run Pending Job` button lookup uses a bounded 2s observation loop before `button_not_found`.
  - Button lookup still starts with broad UIA query so sibling fallback remains available.
  - If broad query misses the button, a targeted UIA query uses exact `Name="Run Pending Job"` and `ControlType="Button"` before retry/timeout.
  - Failure path preserves retry/timeout actions before `button_not_found`.
- Tests:
  - Windows VM focused test:
    `/var/lib/windows-server/shared/operator-exchange/runs/s1s2-agent-tests-vm-20260705c/result.json`;
    42 passed, 0 failed.

S2b live proof:

- Evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-s2b-readiness-20260705t1734z/summary.json`.
- Command:
  `scripts/linux/powerpoint-online-final-proof.py --verify-readiness`.
- Result:
  HTTP 200, `success=true`, `status=succeeded`, `jobStatus=succeeded`,
  `claimedBy=officejs-taskpane`, `saveProofTier=tier3ReopenVisual`,
  `evidenceCount=2`, `verifiedEvidenceCount=2`, `distinctVerifiedEvidenceCount=2`,
  `sessionCleanupStatus=closed`, `edgeLikeWindowCount=0`, `elapsedSeconds=94.952`.
- Baseline comparison:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-onecall-20260705t080929z/summary.json`
  was `success=true`, `elapsedSeconds=164.603`, `sessionCleanupStatus=closed`, `edgeLikeWindowCount=0`.
  New run is 69.651s faster while preserving tier3 evidence and cleanup.

Agent profile command:

- Added `just ppt-profile` as the canonical agent-facing PowerPoint Online surface profile command.
- Added `just easy-profile` as a low-friction alias for agents that search for the generic profile entrypoint.
- Both run the safe SEM27 `--verify-readiness` path without deck mutation; profile evidence lands under
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-*/summary.json`.

Live `easy-profile` run:

- First attempt:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-20260705t185906z/summary.json`.
  Result: HTTP 200, `success=false`, `status=blockedAddIn`, `jobStatus=failed`,
  `saveProofTier=tier0VisualOpen`, `elapsedSeconds=50.691`,
  `sessionCleanupStatus=closed`, `edgeLikeWindowCount=0`.
  Screenshot showed visible `Run Pending Job`; UIA missed it, so this remains a flake signal for the add-in button observation path.
- Retry:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-20260705t190210z/summary.json`.
  Result: HTTP 200, `success=true`, `status=succeeded`, `jobStatus=succeeded`,
  `claimedBy=officejs-taskpane`, `saveProofTier=tier3ReopenVisual`,
  `verifiedEvidenceCount=2`, `distinctVerifiedEvidenceCount=2`,
  `verificationStatus=ready`, `elapsedSeconds=92.652`,
  `sessionCleanupStatus=closed`, `edgeLikeWindowCount=0`.
- Final Host REST `GET /v1/windows` check after retry reported `edgeLikeWindowCount=0`.

Speedup opportunities from `ppt-surface-profile-20260705t190210z`:

- Current dominant costs:
  initial PowerPoint Online open/load was about 34s (`19:02:10` request to first ready event at `19:02:44`);
  tier3 verification reopen was about 37s (`19:03:05` cleanup closed to `19:03:41` verification evidence);
  add-in activation/enqueue was about 9s (`19:02:44` ready to `19:02:53` enqueue);
  actual Office.js job execution was about 3.2s (`19:02:54.537` claimed to `19:02:57.756` completed).
- Best safe profile-only speedup:
  add `ppt-profile-fast` that disables reopen verification and keeps cleanup, producing a lower-tier readiness/profile signal.
  Expected savings: about 35-40s per run. Not acceptable as final proof because it drops tier3 reopen evidence.
- Best repeated-agent speedup:
  add a warm-session profile mode that opens SEM27 once, runs update/readiness against existing `sessionId`, and cleans up explicitly at the end.
  Expected savings: up to about 30-35s after first run. Needs clear cleanup guard to avoid orphan Edge sessions.
- Best reliability/speed fix:
  replace or supplement UIA `Run Pending Job` click with an add-in command protocol after activation.
  Expected savings: about 5-10s and removes the visible-button/UIA-miss flake seen in
  `ppt-surface-profile-20260705t185906z`. Larger behavior change than Just aliases, but not a full surface refactor.
- Low-risk instrumentation:
  add phase timings to `PowerPointOnlineUpdateResult`/summary so future `easy-profile` runs report open, activation, job, save, evidence,
  reopen, and cleanup durations directly instead of reconstructing them from events.

Completion decision from S1/S2 review:

- S3 add-in command protocol is not activated now. `Run Pending Job` no longer fails after targeted/bounded UIA lookup, and full readiness elapsed dropped below the earlier 164.603s baseline without add-in protocol changes.
- S4 proof policy naming is not activated now. Current `--verify-readiness` still returns tier3 evidence correctly; no public contract confusion is blocking implementation.
- Remaining micro-optimization: save-wait still does UIA observation twice in the successful S2b action log. It is small relative to tier3 reopen/Office load, so defer unless future profiling shows it has become material.

Superseded implementation update:

- User requested implementation of remaining speedups after this review.
- S3 was implemented behind fallback, but live command-signal dispatch depends on an active Edge DevTools port.
- S4 public proof-policy naming remains deferred; agent-facing fast profile commands cover profiling need without changing public API.

S3/S5/S6 implemented 2026-07-05:

- Changed paths:
  - `Justfile`
  - `scripts/linux/powerpoint-online-final-proof.py`
  - `scripts/linux/powerpoint-online-final-proof-tests.sh`
  - `src/WindowsOperator.Core/Contracts/PowerPointOnlineUpdateResult.cs`
  - `src/WindowsOperator.Core/Contracts/PowerPointOnlineUpdatePhaseTimings.cs`
  - `src/WindowsOperator.Host/Services/PowerPointOnlineUpdateService.cs`
  - `openapi/windows-operator.openapi.json`
  - `clients/go/windowsoperator.gen.go`
  - `src/WindowsOperator.PowerPointAddIn/src/addInCommandProtocol.ts`
  - `src/WindowsOperator.PowerPointAddIn/src/app.ts`
  - `src/WindowsOperator.PowerPointAddIn/tests/addInCommandProtocol.test.ts`
  - `src/WindowsOperator.Agent/Hosting/OperatorApp.cs`
  - `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
  - `tests/WindowsOperator.Agent.Tests/PowerPointOnlineServiceTests.cs`
- Behavior:
  - Added `just ppt-profile-fast` and `just easy-profile-fast`.
  - Fast profile uses `--verify-readiness-fast`, keeps non-mutating SEM27 readiness path, disables reopen verification, requires cleanup closed and final Edge-like window count `0`.
  - `PowerPointOnlineUpdateResult` now reports `phaseTimings` for total/open/add-in probe/template/job/save/evidence/reopen/template cleanup/session cleanup.
  - PowerPoint add-in now accepts `postMessage` command channel `windows-operator.powerpoint-addin` command `runPendingJob`.
  - Agent tries command-signal dispatch before UIA button click, then falls back to existing UIA path if DevTools/channel unavailable.
  - Parser is case-insensitive for JSON ack payloads.

Validation:

- `just --dry-run ppt-profile-fast`: passed; command includes `--verify-readiness-fast`.
- `just --dry-run easy-profile-fast`: passed; command includes `--verify-readiness-fast`.
- `scripts/linux/powerpoint-online-final-proof-tests.sh`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --filter "PowerPointOnlineUpdateServiceTests|HostOperatorEndpointsTests"`: 33 passed, 0 failed.
- `dotnet build src/WindowsOperator.Host/WindowsOperator.Host.csproj --no-restore`: passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`: passed.
- `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-build --filter ContractSerializationTests`: blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`.
- `cd clients/go && go test ./...`: passed.
- `cd src/WindowsOperator.PowerPointAddIn && npm test -- addInCommandProtocol`: 2 passed.
- `cd src/WindowsOperator.PowerPointAddIn && npm test`: 31 passed, 0 failed.
- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj -clp:ErrorsOnly`: passed.
- `dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-build --filter PowerPointOnlineServiceTests`: blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`.
- Windows Host focused tests:
  `/var/lib/windows-server/shared/operator-exchange/runs/speed-host-tests-20260705t1937z/result.json`;
  33 passed, 0 failed.
- Windows Agent focused tests:
  `/var/lib/windows-server/shared/operator-exchange/runs/speed-agent-tests-20260705t1939z/result.json`;
  38 passed, 0 failed.
- Runtime deploy:
  `/var/lib/windows-server/shared/operator-exchange/runs/speed-register-host-20260705t1940z/result.json`;
  Host registered/restarted as SYSTEM and add-in static files published.
- Agent restart:
  `/var/lib/windows-server/shared/operator-exchange/runs/speed-restart-agent-20260705t1940z/result.json`;
  beforeState `Running`, afterState `Running`.
- Live Host health after deploy:
  `GET http://127.0.0.1:43117/v1/health` returned `ok`.
- Live OpenAPI after deploy exposes `phaseTimings` and `PowerPointOnlineUpdatePhaseTimings`.

Fast live profile attempts:

- First fast attempt:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-fast-20260705t193923z/summary.json`.
  Result: HTTP 200, `success=false`, `status=blockedAddIn`, `jobStatus=notQueued`,
  `saveProofTier=tier0VisualOpen`, `elapsedSeconds=39.499`, `sessionCleanupStatus=closed`,
  `edgeLikeWindowCount=0`, `verifiedEvidenceCount=1`.
  `phaseTimings.openSessionMs=34682`, `phaseTimings.totalMs=39381`.
  Failure was add-in activation command not clickable before job queue; screenshot showed deck open and no add-in pane.
- Retry fast attempt:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-fast-20260705t194124z/summary.json`.
  Result: HTTP 200, `success=true`, `status=succeeded`, `jobStatus=succeeded`,
  `saveProofTier=tier2SavedIndicator`, `elapsedSeconds=58.039`, `sessionCleanupStatus=closed`,
  `edgeLikeWindowCount=0`, `verifiedEvidenceCount=1`, `distinctVerifiedEvidenceCount=1`.
  `phaseTimings.totalMs=58000`, `openSessionMs=34165`, `addInProbeMs=8582`,
  `jobMs=7803`, `saveMs=2322`, `evidenceMs=4760`, `sessionCleanupMs=365`.
- Retry response actions:
  `addin_run_pending_job_command_requested`, then `devtools_status:port_closed`,
  `devtools_snapshot_unavailable`, `addin_run_pending_job_command_signal_unavailable`,
  `addin_run_pending_job_click_dispatched`.
  Command protocol is implemented and tested, but live path fell back to UIA because the work-profile Edge session had no reachable DevTools port.

Current speed profile:

- Full tier3 `easy-profile` successful retry: `92.652s`.
- Fast tier2 `ppt-profile-fast` successful retry: `58.039s`.
- Observed savings: `34.613s`, matching expected removal of tier3 reopen proof.
- Slowest remaining phase in fast run: `openSessionMs=34165`.
- Next material speedup would require warm-session/session-owner work or ensuring Edge work-profile sessions launch with usable DevTools. That is a larger session/profile ownership change, not a local PowerPoint update refactor.

S7 documented 2026-07-05:

- Durable target spec:
  `docs/powerpoint-automation-architecture.md`, section `Agent Profiling Modes`.
- Decision:
  warm agent sessions are encouraged only when the user explicitly needs repeated profiling speed.
  Default/final proof remains fresh owned session with `cleanupSession=true`.
- Target interface:
  - Start one named session through `POST /v1/powerpoint/online/sessions`.
  - Reuse it through `POST /v1/powerpoint/online/updates` with `sessionId` and no `deckUrl`.
  - For profiling, keep `allowDeckMutation=false`, `validateOnly=true`, `verifyReopen=false`, and `cleanupSession=false` during the loop.
  - End with `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup` and verify final Edge/Chrome-like window count returns to baseline.
- Target command:
  `just ppt-profile-warm`, implemented later as a thin harness around existing session/update APIs.
- Boundary rationale:
  warm-session ownership belongs in the PowerPoint Online harness/profile runner, not scattered caller scripts, because the harness can own session naming, cleanup trap, window-count checks, request shaping, and timing summary.

## Slice S8 Handoff Packet: Warm Agent Profile Command

Source truth:

- `docs/powerpoint-automation-architecture.md`, section `Agent Profiling Modes`.
- This ledger S7 target spec.
- Existing Host update route behavior: `PowerPointOnlineUpdateService` starts a session when `deckUrl` is present and loads an existing session when `deckUrl` is omitted.
- Existing `scripts/linux/powerpoint-online-final-proof.py` owns safe profile request shaping, summary JSON, image evidence checks, and window-count checks.
- Existing `Justfile` owns agent-facing profile entrypoints.
- `AGENTS.md` live Windows verification rule applies before claiming done.

Progress status:

- Warm-session target spec is documented.
- No `ppt-profile-warm` command exists yet.
- No script mode starts one session, reuses it for multiple update iterations, and cleans it in a final guard.
- Current production-ready status: planned, not implemented.

Objective:

Add a production-ready warm profile command for supervised agent profiling loops. It should open one SEM27 PowerPoint Online session, run at least two non-mutating validate-only update iterations against that existing session, emit per-iteration timing evidence, and always attempt cleanup.

Owning boundary:

- `scripts/linux/powerpoint-online-final-proof.py` is the harness boundary because it already owns profile request construction, safety gates, summary artifacts, and window leak checks.
- `Justfile` exposes the agent-facing command.
- Do not push warm-session orchestration into ad hoc caller scripts.

Write scope:

- `scripts/linux/powerpoint-online-final-proof.py`
- `scripts/linux/powerpoint-online-final-proof-tests.sh`
- `Justfile`

Non-scope:

- No Host service/API changes unless the existing session reuse route is proven insufficient.
- No OpenAPI/client regeneration expected.
- No SharePoint/production deck mutation.
- No tier3/final-proof behavior changes.
- No DevTools/session-owner architecture changes.
- No docs/spec changes in this worker slice; orchestrator will update ledger/docs after verified implementation.

Acceptance criteria:

- `just ppt-profile-warm` exists and runs a safe SEM27 non-mutating warm profile mode.
- Script mode starts one named session with `deckUrl`, `capture=false`, and normal open wait.
- Script mode runs at least two update iterations with `sessionId`, no `deckUrl`, `allowDeckMutation=false`, `validateOnly=true`, `verifyReopen=false`, and `cleanupSession=false`.
- Each iteration gets a unique `jobId` and writes request/response/summary artifacts under one run root.
- Top-level summary contains `iterations`, per-iteration `phaseTimings`, `openSessionMs` or equivalent session-start timing, cleanup status, window counts before/after, and `success`.
- Script always attempts final `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup` after session start, including failure paths.
- Success requires at least two successful iterations, each with job succeeded and claimed by `officejs-taskpane`, final cleanup closed, and final Edge-like window count at or below baseline.
- Failure summaries must retain enough evidence to debug start/update/cleanup/window problems.

Validation:

- Extend `scripts/linux/powerpoint-online-final-proof-tests.sh` with fake HTTP tests proving:
  - warm mode sends one session start, two update calls without `deckUrl`, one cleanup, and window checks;
  - update requests are validate-only, non-mutating, no reopen, no cleanup;
  - cleanup still runs if the second iteration fails after session start;
  - mutual exclusion rejects warm mode combined with execute/readiness/gate modes.
- Run `scripts/linux/powerpoint-online-final-proof-tests.sh`.
- Run `just --dry-run ppt-profile-warm`.
- After worker implementation and review, orchestrator must run live Windows SEM27 warm profile and record artifacts here.

Close conditions:

- Worker returns changed paths, behavior implemented, tests/checks run, and residual risk.
- Orchestrator verifies the diff against this packet.
- Live Windows warm profile evidence is recorded here.
- Final Edge/Chrome-like window count returns to baseline.

Risks:

- Existing Host update route may spend time in `GetOnlineSessionAsync`; that is acceptable for first slice if no new browser opens.
- Add-in activation may still be flaky; summary must show whether failures happen before or during warm iterations.
- A failed cleanup is production-blocking for this slice.
- Dirty worktree contains existing uncommitted profile runner and Justfile changes; worker must preserve them.

Approval needed: no.

Worker prompt:

```text
The orchestrator owns planning and specs. You own only this bounded implementation slice.
Edit files directly inside this write scope:
- scripts/linux/powerpoint-online-final-proof.py
- scripts/linux/powerpoint-online-final-proof-tests.sh
- Justfile
Do not edit .work planning state. Do not edit Host service/API/OpenAPI/clients unless you prove the existing session reuse route cannot support the target; if so, stop and report why.
Do not mutate any SharePoint deck.
Implement `just ppt-profile-warm` and the backing warm profile mode per S8 acceptance criteria in .work/powerpoint-online-surface-profile-improvements.md.
Return changed paths, behavior implemented, tests/checks run, and residual risk.
```

S8 implemented 2026-07-05:

- Changed paths:
  - `Justfile`
  - `scripts/linux/powerpoint-online-final-proof.py`
  - `scripts/linux/powerpoint-online-final-proof-tests.sh`
  - `docs/powerpoint-automation-architecture.md`
- Behavior:
  - Added `just ppt-profile-warm`.
  - Added `--profile-warm` with default `--warm-iterations=2`.
  - Warm mode starts one named session through `POST /v1/powerpoint/online/sessions`.
  - Warm iterations call `POST /v1/powerpoint/online/updates` with `sessionId`, no `deckUrl`, `allowDeckMutation=false`, `validateOnly=true`, `verifyReopen=false`, `cleanupSession=false`, `capture=false`, and unique job ids.
  - Summary records session-start timing as `openSessionMs` and `sessionStart.elapsedMs`, per-iteration `phaseTimings`, cleanup result, window counts, and success state.
  - Cleanup is attempted after the warm start POST returns, including non-ready start responses.
- Local validation:
  - `scripts/linux/powerpoint-online-final-proof-tests.sh`: passed.
  - `just --dry-run ppt-profile-warm`: passed and prints safe SEM27 warm command.
- Live Windows proof:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-warm-20260705t211739z/summary.json`.
- Live result:
  HTTP/Host health was `ok` before run.
  `success=true`, `status=warmProfileSucceeded`, `profileWarm=true`,
  `requestedIterations=2`, `successfulIterations=2`, `openSessionMs=34193.532`,
  `elapsedSeconds=73.992`, `edgeLikeWindowCountBefore=0`, `edgeLikeWindowCount=0`,
  cleanup `attempted=true`, cleanup HTTP `200`, cleanup status `closed`.
- Iteration timings:
  - iteration 1: `totalMs=22523`, `openSessionMs=104`, `addInProbeMs=8345`, `jobMs=7666`, `saveMs=2325`, `evidenceMs=4081`.
  - iteration 2: `totalMs=16980`, `openSessionMs=510`, `addInProbeMs=1144`, `jobMs=8689`, `saveMs=2541`, `evidenceMs=4094`.
- Request-shape proof:
  - start request has `deckUrl`, `sessionId`, `runId`, `capture=false`, `waitSeconds=40`, and no `job`.
  - both iteration requests have no `deckUrl`, preserve the warm `sessionId`, `allowDeckMutation=false`, `verifyReopen=false`, `cleanupSession=false`, `capture=false`, `validateOnly=true`, `discoverTargets=true`, and empty operations.
- Close status:
  production-ready warm profile path is live-proven for supervised non-mutating agent profiling.
  It is not final proof and does not replace cold owned session tier3 evidence.
- Session boundary:
  current path is hot-session capable inside one command: start once, reuse `sessionId` for multiple update iterations, then cleanup.
  It is not yet a persistent hot lease across agent turns or separate shell commands, because success currently requires explicit final cleanup and window-count return to baseline.

Warm profile rerun 2026-07-05:

- Command:
  `just ppt-profile-warm`.
- Evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-warm-20260705t215316z/summary.json`.
- Result:
  `success=true`, `status=warmProfileSucceeded`, `profileWarm=true`,
  `requestedIterations=2`, `successfulIterations=2`, `openSessionMs=34173.044`,
  `elapsedSeconds=73.302`, `edgeLikeWindowCountBefore=0`, `edgeLikeWindowCount=0`,
  cleanup HTTP `200`, cleanup status `closed`.
- Iteration timings:
  - iteration 1: `totalMs=21847`, `openSessionMs=103`, `addInProbeMs=8562`, `jobMs=6733`, `saveMs=2361`, `evidenceMs=4086`.
  - iteration 2: `totalMs=16984`, `openSessionMs=528`, `addInProbeMs=1131`, `jobMs=8669`, `saveMs=2546`, `evidenceMs=4109`.
- Current bottleneck:
  first session open still dominates at `34173.044ms`; steady warm iteration bottleneck is `jobMs=8669` plus fixed evidence around `4109ms`.

Improvement candidates from warm rerun:

- P0 persistent hot lease commands:
  keep a named PowerPoint session alive across agent turns instead of only within one `ppt-profile-warm` process.
  Expected saving: avoid the `~34s` open cost for every subsequent agent profile command.
  Scope: harness/Justfile/docs/tests around existing session/update/cleanup endpoints; no Host API change expected.
  Required guard: lease file, TTL/status, explicit cleanup command, final window-count check.
- P1 split discovery stress from steady hot loop:
  current warm iterations run `discoverTargets=true`; steady `jobMs=8669ms` likely includes repeated target discovery.
  Expected saving: several seconds per steady iteration if discovery is cached or skipped after the first pass.
  Scope: profile harness first; service/add-in changes only if existing request flags cannot express the mode.
- P1 repair live command-signal path:
  add-in command protocol exists, but live run still falls back because Edge DevTools reports `port_closed`.
  Expected gain: fewer UIA flakes and lower add-in/job dispatch latency; speed gain probably smaller than open/discovery.
  Scope: session/profile ownership and Edge launch/debug-port behavior; larger than local PowerPoint update cleanup.
- P2 evidence interval for warm stress loops:
  current evidence costs about `4100ms` every iteration.
  Expected saving: about `4s` per skipped evidence pass when running many iterations.
  Scope: harness flag plus service option/policy if current update route always captures slide evidence.
- P2 validate-only save wait reduction:
  current validate-only empty-operation loop still spends `~2500ms` in save wait.
  Expected saving: `1-2.5s` per iteration if non-mutating validation can skip or single-sample save proof.
  Scope: service proof policy; not for final proof.
- P3 UIA/session observation batching:
  repeated `session_state_observed` and `powerpoint_online_uia_observed` calls remain.
  Expected saving: sub-second to low-single-second; implement only after P0-P2.

Refactor decision:

- Major refactor not warranted for P0, P1 discovery split, P2 evidence interval, or P2 save-wait reduction.
- Major/session-owner refactor is only warranted if we choose to make DevTools command-signal reliable for production hot sessions.

S9 implemented 2026-07-05:

- Changed paths:
  - `Justfile`
  - `scripts/linux/powerpoint-online-final-proof.py`
  - `scripts/linux/powerpoint-online-final-proof-tests.sh`
  - `docs/powerpoint-automation-architecture.md`
  - `.work/powerpoint-online-surface-profile-improvements.md`
- Behavior:
  - Added `just ppt-hot-start`, `just ppt-hot-status`, `just ppt-hot-run`, and `just ppt-hot-cleanup`.
  - Lease state lives at `/var/lib/windows-server/shared/operator-exchange/state/ppt-hot-lease.json` by default.
  - `ppt-hot-start` starts or reuses named session `ppt-hot-sem27`; expired, non-ready, or deck-mismatched leases are cleaned before a new start.
  - `ppt-hot-run` refuses missing, expired, or non-ready leases; successful runs refresh TTL.
  - `ppt-hot-run` uses existing `sessionId`, no `deckUrl`, `allowDeckMutation=false`, `validateOnly=true`, `verifyReopen=false`, `cleanupSession=false`, and `capture=false`.
  - `ppt-hot-cleanup` closes the leased session, removes the lease file, and verifies final Edge-like window count is no higher than baseline.
- Local validation:
  - `scripts/linux/powerpoint-online-final-proof-tests.sh`: passed.
  - `python3 -m py_compile scripts/linux/powerpoint-online-final-proof.py`: passed.
  - `just --dry-run ppt-hot-start`, `ppt-hot-run`, `ppt-hot-status`, and `ppt-hot-cleanup`: passed.
- Live Host health before proof:
  `GET http://127.0.0.1:43117/v1/health` returned `ok`.
- Live hot start:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-start-20260705t232236z/summary.json`.
  Result: `success=true`, `status=hotLeaseStarted`, `openSessionMs=34187.323`,
  `elapsedSeconds=34.214`, Edge-like windows `0 -> 1`, lease session `ppt-hot-sem27`.
- Live hot status:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-status-20260705t232322z/summary.json`.
  Result: `success=true`, `status=hotLeaseReady`, HTTP `200`, lease not expired.
- Live hot run:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-run-20260705t232322z/summary.json`.
  Result: `success=true`, `status=hotRunSucceeded`, `jobStatus=succeeded`, `claimedBy=officejs-taskpane`,
  `elapsedSeconds=23.788`, Edge-like windows `1 -> 1`.
  Timings: `totalMs=23433`, `openSessionMs=323`, `addInProbeMs=8958`, `jobMs=9736`, `saveMs=329`, `evidenceMs=4085`.
  Request shape: no `deckUrl`, `sessionId=ppt-hot-sem27`, `allowDeckMutation=false`, `verifyReopen=false`, `cleanupSession=false`, `capture=false`, `validateOnly=true`, `discoverTargets=true`, empty operations.
- Live hot cleanup:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-hot-cleanup-20260705t232358z/summary.json`.
  Result: `success=true`, `status=hotLeaseClosed`, cleanup HTTP `200`, cleanup status `closed`,
  Edge-like windows `1 -> 0`, lease file removed, final live Edge-like window count `0`.
- Close status:
  persistent hot lease path is production-ready for supervised non-mutating agent profiling loops.
  It still does not replace cold owned session tier3 evidence for final proof.

Harness layering decision 2026-07-05:

- REST is the stable composition layer: typed primitives/domain workflows, OpenAPI, generated clients, durable contracts.
- CLI scripts own agent/operator flows: safe defaults, run ids, lease files, TTL, cleanup traps, summaries, artifact paths, and evidence aggregation.
- `Justfile` owns unstable developer and agent shortcuts only: discoverable names and common defaults that call CLI scripts.
- Do not move session ownership, proof policy, retries, or JSON state machines into `Justfile`.
- Target architecture documented at `docs/operator-harness-architecture.md`.

## Slice S1 Handoff Packet

Source truth:

- `docs/powerpoint-automation-architecture.md`: browser automation may control shell and gather evidence; slide editing remains Office.js.
- `AGENTS.md`: live Windows verification required for desktop/browser runtime behavior.
- Profiler evidence in this file: slide selection `31.752s`, repeated `devtools_snapshot_unavailable`, fallback thumbnail click succeeds.

Progress status: planned.

Objective:

Make PowerPoint slide selection skip doomed DOM-click attempts when the Edge session has recently proven DevTools target unavailable, while preserving DOM-first behavior when DevTools is available.

Write scope:

- `src/WindowsOperator.Agent/Services/EdgeMicrosoftAuthService.Browser.cs`
- `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
- browser/session state model files if needed under `src/WindowsOperator.Core/Contracts` or `src/WindowsOperator.Agent/Services`
- focused tests under `tests/WindowsOperator.Agent.Tests`

Non-scope:

- No public REST contract change.
- No OpenAPI/client regeneration unless worker proves contract changed, which should be avoided.
- No add-in changes.
- No Host job API changes.
- No production deck mutation.
- No broad browser service rewrite.

Acceptance criteria:

- Edge session records a usable internal DevTools capability/status after target probing.
- DOM actions fail fast when target is known unavailable or port is closed.
- PowerPoint slide selection skips DOM selector attempts when DevTools is known unavailable and goes directly to thumbnail/UIA fallback.
- PowerPoint slide selection still uses DOM-first path when DevTools is available or capability is unknown/stale.
- Capability has bounded staleness/reprobe behavior so a temporary DevTools failure does not permanently disable DOM actions.
- Existing action/warning strings remain stable enough for current tests unless explicitly updated with reason.

Validation:

- Add/update focused unit tests for:
  - DevTools-unavailable fast failure in DOM action path.
  - PowerPoint slide select skips four DOM selector attempts when capability says unavailable.
  - PowerPoint slide select preserves DOM-first behavior when capability says ready/unknown.
- Run Windows Agent tests.
- Run live non-mutating SEM27 slide selection/profile proof.

Close conditions:

- Test output path or command output recorded here.
- Live evidence path recorded here.
- New slide selection elapsed recorded here.
- Final Edge/PowerPoint window count `0`.

Risks:

- Current browser session metadata may not have a natural place for capability state.
- Tests may need small fake service extension to expose capability.
- DevTools target can become available after first failure; stale status must be bounded.

Approval needed: no.

Worker prompt:

```text
You are not alone in this repo. The orchestrator owns planning and specs. You own only this bounded implementation slice.
Edit files directly inside this write scope:
- src/WindowsOperator.Agent/Services/EdgeMicrosoftAuthService.Browser.cs
- src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs
- browser/session state model files if needed under src/WindowsOperator.Core/Contracts or src/WindowsOperator.Agent/Services
- focused tests under tests/WindowsOperator.Agent.Tests
Do not edit .work planning state. Do not edit public REST contracts/OpenAPI/clients unless strictly necessary; if necessary, stop and report why.
Do not mutate any SharePoint deck.
Acceptance: internal DevTools capability/status, fast DOM failure on known unavailable target, PowerPoint slide selection skips doomed DOM attempts, DOM-first preserved when available/unknown, bounded reprobe/staleness, focused tests.
Validation: run focused Windows Agent tests or report exact blocker. Return changed paths, behavior implemented, tests/checks run, residual risk, and any live verification recommendation.
```

## Slice S1b Re-profile Packet

Objective:

Measure S1 user-visible effect against live PowerPoint Online without deck mutation.

Write scope:

- This ledger only.

Validation:

- Run a manual profile equivalent to `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-20260705t080715z/summary.json`, or run the readiness proof command above.
- Capture:
  - session start elapsed
  - slide select elapsed
  - add-in probe elapsed
  - one-call readiness elapsed if run
  - final Edge/PowerPoint window count

Close condition:

- Update `S1b` status and add new evidence paths/timings here.

Approval needed: no.

## Slice S2 Handoff Packet

Source truth:

- `docs/powerpoint-automation-architecture.md`: shell automation opens/selects/observes/captures evidence.
- Profiler evidence: `session.get=2.326s`, `save.wait=2.498s`, `screenshot=4.642s`.
- S1b attempt evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-s1b-readiness-20260705t1722z/summary.json`.
  The run was non-mutating, returned HTTP 200 with final cleanup closed and final Edge-like window count `0`,
  but failed `blockedAddIn` because UIA did not find `Run Pending Job` even though the final screenshot showed
  the add-in pane and button visible. The same run proved S1 slide fallback behavior in live runtime:
  `devtools_status:port_closed`, `slide_select_dom_skipped:4`, `slide_select_verified:4`.

Progress status: active after S1b exposed add-in UIA timing.

Objective:

Add bounded add-in button observation wait/retry and targeted UIA lookup in PowerPoint session operations, without changing public REST contracts.

Write scope:

- `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
- focused tests under `tests/WindowsOperator.Agent.Tests`

Non-scope:

- No public REST field or OpenAPI/client regeneration.
- No change to evidence semantics for final screenshots.
- No change to save-state correctness.
- No add-in or Host queue changes.

Acceptance criteria:

- Triggering add-in buttons uses a bounded observation loop before declaring the button missing.
- The loop can wait for `Run Pending Job` after add-in activation when the pane is visible but UIA has not surfaced the button yet.
- Button-not-found failures include enough action evidence to distinguish timeout from no-window/session-not-ready.
- Targeted `Run Pending Job` lookup can find the visible button even when broad UIA enumeration misses it.
- Final evidence capture still performs required slide verification and screenshot.
- Metadata fallback remains deterministic.

Validation:

- Focused tests for mode selection and metadata fallback.
- Existing PowerPoint service tests pass.
- Live readiness proof still returns tier3 evidence.

Close conditions:

- Test output recorded here.
- Live evidence path recorded here.
- One-call readiness timing compared with baseline.
- Residual save/UIA observation budget recorded as deferred if no longer material.

Risks:

- UIA is current source for slide/save state. Reducing observation too aggressively can hide failures.
- Keep this slice after S1 so profiler noise is lower.

Deferred from original S2 scope:

- Internal status-only/save-state/full-evidence observation modes.
- Save polling UIA reduction.
- Reason: S2b already restored live success and reduced one-call readiness from `164.603s` to `94.952s`; remaining save-wait UIA observations are not the leading bottleneck and are safer to defer than to weaken save-state correctness.

Approval needed: no.

## Slice S3 Deferred Packet: Add-In Command Protocol

Status: deferred until S1/S2 profiles prove `run-pending-job` remains material.

Current evidence:

- `run-pending-job` click path `6.251s`.
- Office.js job terminal wait ~`2s`.

Potential objective:

Remove UI button hunting from queued job execution by letting taskpane auto-claim or accept a lightweight command trigger while preserving the existing button path as fallback.

Likely write scope:

- `src/WindowsOperator.PowerPointAddIn/src`
- `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
- `src/WindowsOperator.Host/Services/PowerPointOnlineUpdateService.cs`
- add-in and Agent/Host focused tests

Decision gate:

- Re-profile after S1/S2.
- If `run-pending-job` remains >5s and contributes materially to one-call runtime, convert this packet into an active plan.

Approval needed:

- No if public REST contract stays stable.
- Yes if public update/job semantics change.

## Slice S4 Deferred Packet: Proof Policy Naming

Status: deferred.

Current evidence:

- Existing `verifyReopen` already controls tier3 cost.
- One-call readiness `164.603s` because tier3 intentionally reopens and captures evidence.

Potential objective:

Rename or wrap proof selection as `ProofPolicy = None | InSessionEvidence | ReopenVisual` only if call sites continue to confuse profiling/readiness/final proof intent.

Decision gate:

- Do not implement unless repeated caller confusion appears or public API cleanup is explicitly requested.

Approval needed:

- Yes if public REST contract changes.

## 2026-07-06 Harness Command Alignment Note

Preferred agent-facing PowerPoint profile/hot commands now live under
`scripts/linux/wo`:

```bash
scripts/linux/wo ppt profile
scripts/linux/wo ppt profile-fast
scripts/linux/wo ppt warm
scripts/linux/wo ppt hot start
scripts/linux/wo ppt hot status
scripts/linux/wo ppt hot run
scripts/linux/wo ppt hot cleanup
```

Existing Just recipes remain as shortcuts:

```bash
just ppt-profile
just ppt-profile-fast
just ppt-profile-warm
just ppt-hot-start
just ppt-hot-status
just ppt-hot-run
just ppt-hot-cleanup
```

Live harness-v2 proof:

- Hot start/status/run/cleanup passed with uppercase run ids:
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Start-Live-20260706T003926Z/summary.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Status-Live-20260706T003926Z/summary.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Run-Live-20260706T003926Z/summary.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/PPT-Hot-Cleanup-Live-20260706T003926Z/summary.json`.
- Run was claimed by `officejs-taskpane`, used sanitized REST job id
  `ppt-hot-run-live-20260706t003926z-hot`, preserved original run id, and
  cleanup removed the lease with Edge-like window count returning to `0`.
