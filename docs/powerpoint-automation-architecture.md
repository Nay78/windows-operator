# PowerPoint Automation Target Architecture

Goal: let external services request precise PowerPoint slide edits while Windows Operator hides queueing, artifact staging, Office.js mechanics, and local runtime details.

## Decision

Use Windows Operator Host plus the Office.js task pane for PowerPoint mutation.
PowerPoint Online shell automation opens decks, selects slides, activates the
add-in, observes save state, captures evidence, and closes Edge. Slide mutation
belongs to Office.js inside the add-in.

No Microsoft Graph access. No desktop PowerPoint COM edit path. No browser DOM
slide mutation.

```text
external service
  -> Windows Operator Host REST 127.0.0.1:43117
  -> local durable PowerPoint job queue
  -> Office.js add-in hosted at https://localhost:3003
  -> PowerPoint.run against active presentation
  -> job record/result in Host queue
```

## Current Proven State

The V1 harness is live-proven against the SEM27 SharePoint deck.

- Final proof route: `POST /v1/powerpoint/online/updates`.
- Proof shape: `prepareTemplate=true`, `replaceText` on `TITLE_MAIN`,
  `verifyReopen=true`, `cleanupTemplate=true`, `cleanupSession=true`,
  `allowDeckMutation=true`.
- Result: HTTP `200`, `success=true`, `status=succeeded`,
  `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`,
  `titleMainTargetSucceeded=true`, `titleMainDiscovered=true`.
- Evidence: three Linux-visible distinct PNG screenshots: initial edit,
  reopened persistence, and post-cleanup.
- Cleanup: template cleanup `ready`, session cleanup `closed`, final
  Edge/Chrome-like window count `0`.
- Evidence paths:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`.

The proof runner default Host HTTP timeout is `420s`; the successful live proof
took `343.19s`.

Table editing is also live-proven against SEM27 through the same high-level
route.

- Proof shape: `prepareTemplate=true`, `readTable`, `replaceTableCell`, and
  `replaceTableRange` on `DATA_TABLE`, `verifyReopen=true`,
  `cleanupTemplate=true`, `cleanupSession=true`, `allowDeckMutation=true`.
- Result: HTTP `200`, `success=true`, `status=succeeded`,
  `saveProofTier=tier3ReopenVisual`, `jobRecord.status=succeeded`.
- Readback: `readTable` returned the initial 3x3 table; visual evidence showed
  `67 kt`, `101%`, and `103%` after write and after reopen.
- Cleanup: template cleanup `ready`, session cleanup `closed`, final
  Edge/Chrome-like window count `0`.
- Evidence paths:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`.

Named-template target repair is live-proven against SEM27.

- Proof shape: typed `Prepare Named Targets`, `bindNamedTargets=true`,
  `replaceText` on `TITLE_MAIN`, `readTable`, `replaceTableCell`, and
  `replaceTableRange` on `DATA_TABLE`, `verifyReopen=true`, explicit
  `Cleanup Named Targets`, and session cleanup.
- Result: HTTP `200`, `success=true`, `status=succeeded`,
  `saveProofTier=tier3ReopenVisual`, four operation results with
  `source=repairedName`, `shapeName`, `bound=true`, and `tagged=true`.
- Cleanup: named cleanup `ready`, cleanup save state `saved`, session cleanup
  `closed`, final Edge/Chrome-like window delta `0`.
- Evidence paths:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/summary.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/06-update-response.json`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/screenshots/powerpoint-online-update.png`,
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z-verification/screenshots/powerpoint-online-update.png`.

## Documentation Map

- Durable architecture and public contract: this document.
- Working notes and historical evidence trail: `.work/powerpoint-online-*`.
- Office.js/PowerPoint Online field notes:
  `.codex/skills/office-js-powerpoint-debug/references/office-js-powerpoint.md`.
- Add-in module docs: `src/WindowsOperator.PowerPointAddIn/docs/`.

## Operating Rules

- Future production-deck mutation still needs explicit operator intent because
  SharePoint version history can retain prepare/update/cleanup writes.
- `allowDeckMutation=false` must reject executable jobs and template
  prepare/cleanup before Edge opens or jobs queue.
- `verifyReopen=true` is required for tier-3 visual proof. Tier 4
  SharePoint/Graph version proof is not implemented.
- Browser/DOM/CDP automation may control the PowerPoint Online shell and gather
  evidence, but it is not the slide-editing contract.

## Agent Profiling Modes

Default agent proof/profile runs use a fresh owned PowerPoint Online session and
`cleanupSession=true`. This remains the encouraged path for final proof,
unattended checks, CI-like verification, and any result that should prove no
session state leaked between runs.

When a user explicitly needs repeated profiling speed, use a warm or hot agent
session as an opt-in development path. These sessions are encouraged only for
user-supervised loops where avoiding the initial PowerPoint Online open cost is
worth keeping one browser session alive between iterations. They are not final
proof, and they must end with explicit cleanup.

Warm-session target spec:

1. Start one named session with
   `POST /v1/powerpoint/online/sessions`, passing `deckUrl`, stable
   `sessionId`, `capture=false`, and the normal open wait budget.
2. Run repeated `POST /v1/powerpoint/online/updates` calls with `sessionId` and
   no `deckUrl`. The Host loads the existing session instead of opening a new
   one. For profiling, use `allowDeckMutation=false`, `validateOnly=true`,
   `verifyReopen=false`, and `cleanupSession=false`.
3. Capture evidence only when needed for the current iteration. Use
   `phaseTimings` to compare `jobMs`, `saveMs`, and `evidenceMs`; ignore
   `openSessionMs` for warm-loop steady-state comparisons because the open cost
   was paid once.
4. Always close the session with
   `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup` in a final cleanup
   step. Verify final Edge/Chrome-like window count returns to the pre-loop
   baseline.

Implemented one-shot warm command:

```text
just ppt-profile-warm
```

The command is a thin harness around the existing session/update APIs. It
creates a unique warm `sessionId`, writes artifacts under one run root, attempts
final cleanup, and labels output as warm-profile evidence rather than final
proof.

Implemented persistent hot lease commands:

```text
just ppt-hot-start
just ppt-hot-status
just ppt-hot-run
just ppt-hot-cleanup
```

`ppt-hot-start` creates or reuses a named SEM27 lease and writes lease state to
the exchange root. `ppt-hot-run` refuses missing, expired, or non-ready leases;
it runs one safe validate-only update with `sessionId`, no `deckUrl`,
`verifyReopen=false`, and `cleanupSession=false`, then refreshes the lease TTL.
`ppt-hot-cleanup` closes the leased session, removes the lease file on success,
and verifies the final Edge/Chrome-like window count is no higher than the
pre-cleanup baseline.

Boundary: warm-session ownership belongs in the PowerPoint Online harness and
its profile runner, not in scattered caller scripts. The harness hides session
start/reuse, update request shaping, cleanup, window-count checks, and timing
summary. Callers choose only cold, fast, one-shot warm, or persistent hot lease
profile mode.

Live warm proof on 2026-07-05 succeeded against SEM27 without deck mutation:
one named session opened, two update iterations reused `sessionId` with no
`deckUrl`, both jobs completed as `officejs-taskpane`, cleanup returned
`closed`, and final Edge/Chrome-like window count returned to baseline `0`.
Evidence:
`/var/lib/windows-server/shared/operator-exchange/runs/ppt-surface-profile-warm-20260705t211739z/summary.json`.

## Runtime Topology

Host owns:

- Optional static HTTPS add-in hosting.
- REST queue endpoints.
- Durable local queue state.
- Artifact fetch, size/checksum/media validation, staging, and artifact serving.
- Structured `OperatorError` translation.

Add-in owns:

- Job claim from active task pane.
- Active presentation URL guard.
- Office.js requirement checks.
- Target inspection.
- `PowerPoint.run` read/write batching.
- Complete/fail callback.

Caller owns:

- Enqueueing desired state.
- Providing `expectedDocumentUrl` when active presentation identity matters.
- Polling job status.

## REST Namespace

Use the `powerpoint` domain namespace:

```text
POST /v1/powerpoint/jobs
POST /v1/powerpoint/jobs/claim
POST /v1/powerpoint/jobs/{jobId}/complete
POST /v1/powerpoint/jobs/{jobId}/fail
GET  /v1/powerpoint/jobs/{jobId}
GET  /v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}
POST /v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job
```

Do not add MCP tools unless AI runtimes need direct PowerPoint mutation. External services should call REST.

## Job Format

`POST /v1/powerpoint/jobs` accepts desired state, not Office.js steps.

```json
{
  "jobId": "job-123",
  "expectedDocumentUrl": "https://tenant.sharepoint.com/sites/team/report.pptx",
  "discoverTargets": false,
  "bindNamedTargets": false,
  "validateOnly": false,
  "requestedBy": "orchestrator",
  "createdAt": "2026-06-17T12:00:00Z",
  "operations": [
    {
      "kind": "replaceText",
      "targetId": "TITLE_MAIN",
      "text": "Updated title",
      "mode": "plain"
    },
    {
      "kind": "replaceImage",
      "targetId": "HERO_IMAGE",
      "artifact": {
        "artifactId": "hero",
        "url": "https://artifact-service.local/hero.png",
        "mediaType": "image/png",
        "sha256": "..."
      },
      "fit": "contain"
    }
  ]
}
```

Host writes `PowerPointJobRecord` with status:

```text
queued
running
succeeded
failed
```

Host validates the REST boundary before queueing or storing results:

- `jobId` and `artifactId` must use lowercase ASCII letters, digits, `_`, `-`, or interior dots, and must avoid Windows device names.
- Jobs require `requestedBy` and at least one operation unless `discoverTargets: true`.
- Operation kinds are `replaceText`, `replaceImage`, `readTable`, `replaceTableCell`, and `replaceTableRange`.
- `discoverTargets: true` asks add-in to enumerate supported binding-backed
  targets and named `TARGET_*` shapes without mutation, returning metadata in
  `discoveredTargets`.
- `bindNamedTargets: true` asks the add-in to repair matching
  `TARGET_<TARGET_ID>` shapes into durable bindings/tags before inspection or
  apply. The high-level online route treats this as deck mutation.
- `replaceText` requires `text`; whitespace-only text requires `allowEmpty: true`.
- `replaceImage` requires a valid staged artifact with `image/png` or `image/jpeg`.
- `readTable` is non-mutating and returns `targets[].table` with `rowCount`, `columnCount`, and `values`.
- `replaceTableCell` requires zero-based `rowIndex`, `columnIndex`, and `text`; whitespace-only text requires `allowEmpty: true`.
- `replaceTableRange` writes a rectangular `values` matrix at optional zero-based `startRowIndex` and `startColumnIndex`, defaulting to `0`.
- `validateOnly: true` keeps job shape and target ids required, but allows
  execution payloads to be omitted. If an image artifact is present, Host still
  validates and stages it. If table range values are present, Host still
  validates rectangular shape and empties.
- `/v1/powerpoint/online/updates` requires `allowDeckMutation: true` before
  queueing mutating jobs or clicking high-level template prepare/cleanup
  controls, or before repairing named targets with `bindNamedTargets: true`.
  `readTable` can execute with `allowDeckMutation: false` when named-target
  repair is not requested.
- The high-level update route uses the typed
  `/v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job` route to
  click the add-in `Run Pending Job` control. If that click fails after a job is
  queued, Host fails the queued job with `ADDIN_RUN_COMMAND_FAILED` so a later
  session cannot claim stale work.
- Direct `/template/prepare` and `/template/cleanup` requests require
  `allowDeckMutation: true` before session lookup, UIA query, or click dispatch.
- Completion `jobId` in the route and payload must match.
- Result statuses are `succeeded`, `failed`, or `partial`; `partial` stores the record as `failed`.
- Malformed PowerPoint payloads return `422 powerpoint_validation_failed`.

## Claim Flow

The add-in posts:

```json
{
  "workerId": "officejs-taskpane",
  "documentUrl": "https://tenant.sharepoint.com/sites/team/report.pptx"
}
```

Host returns:

- `200 PowerPointUpdateJob` when a queued job matches.
- `204` when no queued job matches.

Document matching uses normalized `expectedDocumentUrl` when both expected and
actual URLs are present. SharePoint canonical `_layouts/Doc.aspx` URLs are
matched against Office document paths by exact normalized URL first, then
`sourcedoc`, then same host + filename. The add-in uses the same
active-document guard before applying.

## Artifact Handling

Host accepts only:

- `image/png`
- `image/jpeg`

Host validates:

- non-empty bytes
- max `PowerPointAddIn:MaxArtifactBytes`
- media type
- optional SHA-256
- optional expiry

Host rewrites artifact URLs to:

```text
/v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}
```

The add-in fetches staged artifacts same-origin and converts bytes to base64 for Office.js image APIs.

For `validateOnly=true`, the add-in does not fetch artifacts.

## Targeting Model

Use stable target ids created by the add-in template setup or by authored bindings/tags.

Target ids are semantic context cues, not visible copy, coordinates, or object
names. Use uppercase ASCII snake case:

```text
<CONTEXT>_<ROLE>[_<KIND>][_<NN>]
```

- `CONTEXT`: slide or business context, for example `TITLE`, `HERO`, `SUMMARY`,
  `KPI`, `DATA`, `SOURCE`, `FOOTER`, `COVER`, or `EXEC`.
- `ROLE`: target purpose inside that context, for example `MAIN`, `SUB`,
  `IMAGE`, `TABLE`, `VALUE`, `LABEL`, `DATE`, or `STATUS`.
- `KIND`: optional when the role does not already imply the object type.
- `NN`: optional two-digit ordinal only for repeated sibling targets.

Examples: `TITLE_MAIN`, `HERO_IMAGE`, `DATA_TABLE`,
`KPI_TONNES_VALUE`, `KPI_TONNES_LABEL`, `SUMMARY_TABLE_01`.

Keep ids deck-global and stable. Prefix with a semantic scope such as `COVER`,
`EXEC`, or `APPENDIX` when the same role appears in multiple templates. Avoid
slide numbers unless the target belongs to a generated fixture where slide order
is fixed.

For generated or authored targets, set:

- Binding id: exactly the target id.
- `TARGET_ID` tag: exactly the target id.
- `TARGET_KIND` tag: `text`, `image`, or `table`.
- Shape name: `TARGET_<TARGET_ID>` for debugging only.

Named-template mode:

- Authors may name an existing PowerPoint shape `TARGET_<TARGET_ID>`.
- Jobs keep using `targetId: "<TARGET_ID>"`.
- With `bindNamedTargets: true`, the add-in scans named shapes needed by the
  operations, fails duplicate names with `TARGET_AMBIGUOUS`, validates shape
  compatibility, then creates missing binding and `TARGET_ID`/`TARGET_KIND`
  tags.
- Existing valid bindings win. If a binding and same target name point to
  different shapes, the add-in fails instead of overwriting either target.
- Result and discovery metadata may include `shapeName`, `source` (`binding`,
  `name`, or `repairedName`), `bound`, and `tagged`.

Preferred targets:

1. Explicit binding/tag id, for example `TITLE_MAIN`.
2. Authored `TARGET_<TARGET_ID>` shape names repaired with `bindNamedTargets`.
3. Stable shape alt text/tag mapping.
4. Generated test deck targets only in fixtures.

Generated template targets must be identifiable by both binding id and `TARGET_ID`
tag before cleanup deletes them. Cleanup must skip mismatched shapes.

Do not depend on coordinates, z-order, or default names like `Rectangle 3`.

Validation-only path:

- Queue `validateOnly: true` when caller needs stable-id inspection only.
- Add-in inspects each operation target id.
- Found/editable targets return `status: "skipped"` plus optional inspection fields.
- Missing or blocked targets return `status: "failed"` plus `TARGET_NOT_FOUND` or `TARGET_NOT_EDITABLE`.
- Validation-only runs do not mutate slides.

Discovery path:

- Queue `discoverTargets: true` to enumerate current binding-backed targets and
  authored `TARGET_*` names.
- Add-in returns `discoveredTargets` with `targetId`, `editable`, `type`,
  optional `message`, and optional target metadata.
- Discovery can run with zero operations. If operations are also supplied, add-in still validates them through normal inspection/mutation flow.

## Result Semantics

`PowerPointUpdateResult` means Office.js applied the operation and completed its sync path in the open presentation.

It does not prove:

- OneDrive/SharePoint durable save.
- Cloud version increment.
- Conflict-free remote persistence.

Without Graph access, callers needing durable save proof need a separate Windows/PowerPoint-visible validation path.

High-level `PowerPointOnlineUpdateResult` now carries a `saveProofTier` field so
callers can distinguish Office.js sync-only proof from saved-indicator and
reopen proof. `tier1` means Office.js job succeeded in the open deck, `tier2`
means Host observed PowerPoint Online return to `saved`, and `tier3` means the
deck was reopened and screenshot evidence was captured again. `tier4` remains
reserved for future SharePoint/Graph version proof.

## Deep Module Boundary

Public contract stays small:

- Input: `PowerPointUpdateJob`.
- Output/status: `PowerPointJobRecord`.

Hidden complexity:

- Queue file layout.
- Artifact staging paths.
- Vendor API checks.
- Office.js sync batching.
- Active-presentation guard.
- Implementation-specific exceptions.

## Configuration

Host:

```json
{
  "PowerPointAddIn": {
    "enabled": false,
    "baseUrl": "https://localhost:3003",
    "staticRoot": "",
    "stateRoot": "",
    "maxArtifactBytes": 15728640
  }
}
```

Defaults:

- `enabled`: `false`
- `baseUrl`: `https://localhost:3003`
- `staticRoot`: sibling `src/WindowsOperator.PowerPointAddIn/dist`
- `stateRoot`: `%LOCALAPPDATA%\WindowsOperator\run\powerpoint-officejs`

Host REST on `127.0.0.1:43117` is independent from add-in HTTPS.
`PowerPointAddIn:enabled` gates only the `https://localhost:3003` listener and
static file middleware.

`scripts/windows/register-host-autostart.ps1` is the production Host path. It
publishes Host to `%ProgramData%\WindowsOperator\host`, writes
`%ProgramData%\WindowsOperator\run\host.appsettings.Local.json`, and launches
Host through a generated wrapper that sets `WINDOWS_OPERATOR_HOST_STATE_ROOT`.
When `dist/taskpane.html` exists, it copies the add-in build to
`%ProgramData%\WindowsOperator\host\powerpoint-addin`, provisions a trusted
LocalMachine `localhost` certificate, and enables the add-in HTTPS listener.
When `dist` is missing or `-DisablePowerPointAddIn` is passed, REST still starts
with `PowerPointAddIn:enabled=false`.

## Validation

Local:

- `dotnet build WindowsOperator.sln`
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
- `npm run typecheck`
- `npm test`
- `npm run build`
- `npm run manifest:validate`

Live:

- Build add-in assets with `npm run build --prefix src/WindowsOperator.PowerPointAddIn`.
- Register and start Host with `scripts/windows/register-host-autostart.ps1`.
- Confirm `GET http://127.0.0.1:43117/v1/health`.
- From Windows, confirm `Invoke-WebRequest https://localhost:3003/taskpane.html` returns `200`.
- Add-in probe route
  `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` now separates
  local package health from tenant activation state. It probes both
  `taskpane.html` and `manifest.xml`, records task pane URL/reachability,
  manifest URL/reachability, manifest id/version/display name/source location,
  and keeps `hostReachable=false` unless both task pane content and manifest
  parse succeed.
- Historical SEM27 proof showed package health was good while task pane
  activation was blocked: `hostReachable=true`, `taskPaneReachable=true`,
  `manifestReachable=true`, `taskPaneVisible=false`,
  `status=blockedActivation`. Evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`.
- Target discovery is implemented as a binding-backed Office.js contract:
  `discoverTargets=true` can run with zero operations and returns
  `discoveredTargets` when the add-in task pane can claim the job. A 2026-07-04
  retry still blocked because the installed `Run Update` command was not visible
  in the current Edge profile; later profile/add-in activation recovered and the
  final proof discovered `TITLE_MAIN` and `HERO_IMAGE`.
- Slide selection is verified after every DOM or thumbnail click. If UIA
  observes a nearby wrong slide, the Agent sends bounded `pageup`/`pagedown`
  corrections and verifies again; mismatches return structured failure. Live
  SEM27 proof on 2026-07-04 selected slide 4 with DOM unavailable, thumbnail
  fallback, `slide_select_verified:4`, screenshot evidence, cleanup success, and
  final Edge/Chrome widget count `0`. Evidence:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z/screenshots/slide-nav-4.png`.
- Reopen visual proof is live for non-mutating Office.js discovery, mutating
  text/image edits, and table reads/writes on binding/tag-backed `DATA_TABLE`.
  Final text/image and table evidence paths are listed in Current Proven State.
- Named-template targets are implemented as first-class metadata repair:
  `TARGET_<TARGET_ID>` shape names can be repaired into bindings/tags with
  `bindNamedTargets: true`. SEM27 live proof on 2026-07-05 edited text and
  table targets, returned `source=repairedName`, reopened with visual proof,
  cleaned named targets, saved cleanup, and restored the Edge/Chrome-like window
  count to baseline.
- After tier-3 reopen, a new verification session does not necessarily have the
  add-in task pane active. Run the typed add-in probe with
  `activateIfNeeded=true` before pressing `Cleanup Named Targets` on that
  verification session.
- In live SEM27 runs, the task pane `Run Pending Job` button was visibly present
  while UIA sometimes missed the button node. The Agent keeps the public route
  typed and uses a narrow sibling-button geometry fallback from visible
  `Prepare Template` or `Cleanup Template` controls.
- From Linux, run the Host-staged add-in smoke from [Development](development.md#live-smoke).
- Sideload add-in manifest.
- Open target presentation in PowerPoint.
- Enqueue job with matching `expectedDocumentUrl`.
- Claim/apply from task pane.
- Inspect slide visually.
