# PowerPoint Online Editing Harness Roadmap

Date: 2026-07-03

## Purpose

Build a reliable harness for editing SharePoint-hosted PowerPoint decks through a Windows VM, with PowerPoint Online as the visible document runtime and Office.js as the preferred mutation runtime.

The harness should let callers say: open this deck, go to this slide, apply these edits, wait until the deck is saved, and return evidence. Callers should not coordinate Edge sessions, click coordinates, task panes, add-in polling, artifact roots, or save-state retries.

## Current Progress

Working pieces:

- Windows Operator Host is reachable at `http://127.0.0.1:43117`.
- Edge work-profile sessions can open SharePoint PowerPoint Online decks.
- Browser/desktop primitives exist:
  - `POST /v1/browser/edge/open-url`
  - `POST /v1/browser/edge/session/start`
  - `GET /v1/browser/edge/session/{sessionId}/state`
  - `POST /v1/browser/edge/session/{sessionId}/navigate`
  - `POST /v1/browser/edge/session/{sessionId}/dom/click`
  - `POST /v1/browser/edge/session/{sessionId}/dom/fill`
  - `POST /v1/browser/edge/session/{sessionId}/screenshot`
  - `POST /v1/input/click`
  - `POST /v1/input/hotkey`
  - `POST /v1/uia/*`
- The VM opened this deck in PowerPoint Online editing mode:
  - `https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`
- Slide 4 was selected and captured through the VM:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide4/screenshots/slide4.png`
- Existing PowerPoint job queue works as a lower-level mutation contract:
  - `POST /v1/powerpoint/jobs`
  - `POST /v1/powerpoint/jobs/claim`
  - `POST /v1/powerpoint/jobs/{jobId}/complete`
  - `POST /v1/powerpoint/jobs/{jobId}/fail`
  - `GET /v1/powerpoint/jobs/{jobId}`
  - `GET /v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}`
- Host PowerPoint queue live probe passed on 2026-07-03: enqueue, artifact fetch, claim, fail, get-final all returned `200`.
- Add-in code supports `replaceText` and `replaceImage` through `PowerPoint.run`.
- Add-in tests, typecheck, build, and manifest validation pass.

Gaps:

- No dedicated PowerPoint Online domain API exists yet.
- No high-level "open deck, select slide, apply job, wait for saved, verify" orchestration exists.
- Current browser controls are generic; callers still need to know PowerPoint Online UI shape.
- No stable slide/object targeting layer exists for arbitrary existing decks.
- No live proof yet that the add-in edits a PowerPoint Online deck and SharePoint persists the result.
- Existing docs correctly warn that browser DOM/click mutation should not be the slide-editing contract.

## Boundary

Owning boundary: `powerpoint/online` domain service.

Reason: PowerPoint Online work is a PowerPoint-domain workflow, not a generic browser workflow. Edge, DevTools, UIA, screenshot backends, task pane loading, save-state polling, and SharePoint URL normalization should be hidden behind a PowerPoint Online harness. Existing browser endpoints remain primitives for diagnostics and unusual manual control.

Keep mutation ownership split:

- `PowerPointOnlineHarness` owns document/session orchestration, slide navigation, add-in activation, save-state observation, evidence capture, and recovery.
- Existing `PowerPointJobService` owns durable job queue, validation, artifact staging, and result records.
- Office.js add-in owns actual slide mutation through `PowerPoint.run`.
- Browser DOM/click automation may operate shell controls and evidence capture, but should not become the public slide mutation mechanism.

## Public Surface

Use domain namespace:

```text
POST /v1/powerpoint/online/sessions
GET  /v1/powerpoint/online/sessions/{sessionId}
POST /v1/powerpoint/online/sessions/{sessionId}/slides/select
POST /v1/powerpoint/online/sessions/{sessionId}/screenshot
POST /v1/powerpoint/online/sessions/{sessionId}/cleanup
POST /v1/powerpoint/online/updates
```

Existing lower-level queue endpoints stay:

```text
POST /v1/powerpoint/jobs
GET  /v1/powerpoint/jobs/{jobId}
```

### `PowerPointOnlineSessionStartRequest`

```csharp
public sealed record PowerPointOnlineSessionStartRequest
{
    public required string DeckUrl { get; init; }
    public string? SessionId { get; init; }
    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;
    public bool Capture { get; init; } = true;
    public int WaitSeconds { get; init; } = 30;
}
```

Contract:

- Opens or reuses an operator-owned Edge session in the Windows desktop session.
- Normalizes SharePoint redirect URLs and records canonical document URL when observable.
- Waits until PowerPoint Online editor is ready or returns a structured blocker.
- Does not require callers to know Edge profile directory, page-load polling, or screenshot paths.

### `PowerPointOnlineSessionResult`

```csharp
public sealed record PowerPointOnlineSessionResult
{
    public required bool Success { get; init; }
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public required string DeckUrl { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? Title { get; init; }
    public int? CurrentSlide { get; init; }
    public int? SlideCount { get; init; }
    public string? EditMode { get; init; }
    public string? SaveState { get; init; }
    public string? BrowserSessionId { get; init; }
    public long? Hwnd { get; init; }
    public WorkbenchRunRef? ArtifactRoot { get; init; }
    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();
}
```

Status vocabulary:

```text
opening
ready
blocked_auth
blocked_permission
blocked_readonly
blocked_office_error
closed
failed
```

### `PowerPointOnlineSlideSelectRequest`

```csharp
public sealed record PowerPointOnlineSlideSelectRequest
{
    public required int SlideNumber { get; init; }
    public bool Capture { get; init; } = true;
    public int WaitSeconds { get; init; } = 15;
}
```

Contract:

- Selects a 1-based slide number in the open PowerPoint Online deck.
- Handles thumbnail rail virtualization, focus, scroll, and retries internally.
- Returns updated `PowerPointOnlineSessionResult` with screenshot evidence when requested.

### `PowerPointOnlineUpdateRequest`

```csharp
public sealed record PowerPointOnlineUpdateRequest
{
    public string? SessionId { get; init; }
    public string? DeckUrl { get; init; }
    public required PowerPointUpdateJob Job { get; init; }
    public PowerPointOnlineVerificationOptions Verification { get; init; } = new();
}
```

Contract:

- Opens or reuses a PowerPoint Online session.
- Ensures the active deck matches `Job.ExpectedDocumentUrl` when provided.
- Ensures add-in/task pane is available.
- Enqueues the existing `PowerPointUpdateJob`.
- Waits for Office.js result via existing `PowerPointJobRecord`.
- Waits for PowerPoint Online save-state evidence.
- Captures requested slide evidence.
- Optionally reopens the deck and re-captures evidence.

### `PowerPointOnlineUpdateResult`

```csharp
public sealed record PowerPointOnlineUpdateResult
{
    public required string Status { get; init; }
    public required PowerPointOnlineSessionResult Session { get; init; }
    public required PowerPointJobRecord JobRecord { get; init; }
    public IReadOnlyList<PowerPointSlideEvidence> Evidence { get; init; } = Array.Empty<PowerPointSlideEvidence>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();
}
```

Update status vocabulary:

```text
succeeded
failed
partial
blocked_auth
blocked_permission
blocked_addin
blocked_targeting
save_unverified
verification_failed
```

### `PowerPointSlideEvidence`

```csharp
public sealed record PowerPointSlideEvidence
{
    public required int SlideNumber { get; init; }
    public required DesktopScreenshotResult Screenshot { get; init; }
    public string? SaveState { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string? Notes { get; init; }
}
```

## Hidden Depth

The harness should absorb:

- Work-profile selection and account/session reuse.
- SharePoint and PowerPoint Online URL normalization:
  - original `pptx?web=1`
  - redirected `/:p:/r/...`
  - `_layouts/15/Doc.aspx?...`
  - `sourcedoc`, `file`, `action=edit`, `mobileredirect`
- Auth blockers, permission blockers, read-only mode, tenant consent pages, and file-not-found pages.
- PowerPoint Online readiness:
  - editor shell loaded
  - ribbon ready
  - slide thumbnails populated
  - canvas rendered
  - edit mode active
- Slide navigation:
  - thumbnail rail virtualization
  - scroll offsets
  - selected thumbnail detection
  - keyboard fallback
  - zoom and DPI differences
- Add-in readiness:
  - HTTPS static host reachable from Windows
  - manifest available
  - task pane open
  - same-origin job API reachable
  - Office.js requirements met
- Job orchestration:
  - enqueue
  - claim
  - wait for complete/fail
  - timeout and cleanup
  - translate add-in errors into stable `OperatorError`
- Save observation:
  - "Saving..."
  - "Saved"
  - "Saved to OneDrive"
  - conflict/error banners
  - transient offline/retry states
- Evidence:
  - full-window screenshot
  - future slide-canvas crop
  - artifact root under `operator-exchange/runs/<run-id>`
  - state snapshots for debugging
- Recovery:
  - browser reload
  - stale hwnd
  - DevTools disconnect
  - closed tab
  - task pane crash
  - stuck save state

## Targeting Model

Primary targeting: Office.js bindings/tags.

Do not make browser canvas coordinates the public target model. Coordinates are acceptable only inside the harness for shell navigation and for temporary manual/debug flows.

Recommended target workflow:

1. Template authoring or bootstrap creates stable target ids in shapes.
2. `PowerPointUpdateJob` references those ids.
3. Add-in inspects and mutates targets through `PowerPoint.run`.
4. Harness captures evidence and save-state verification.

For existing decks without targets:

- Add a discovery/bootstrap phase before broad edits.
- Allow a supervised "prepare targets" operation that creates or binds target ids to selected shapes.
- Store a target manifest per deck only as local operator state unless the deck itself receives bindings/tags.
- Do not infer permanent targets from z-order, default shape names, or click coordinates.

## Save-Proof Tiers

Use explicit tiers so callers know what was proven.

```text
tier0_visual_open       Deck opened and screenshot captured.
tier1_officejs_sync     Office.js update completed successfully in active presentation.
tier2_saved_indicator   PowerPoint Online reported saved/no pending save.
tier3_reopen_visual     Deck was reopened and affected slides were captured again.
tier4_cloud_version     SharePoint/Graph version proof; not available until credentials/API path exists.
```

Default completion target for this harness: `tier3_reopen_visual`.

## Roadmap

### Phase 1: Online Session Harness

Deliver:

- Core contracts:
  - `PowerPointOnlineSessionStartRequest`
  - `PowerPointOnlineSessionResult`
  - `PowerPointOnlineSlideSelectRequest`
  - `PowerPointSlideEvidence`
- Service:
  - `IPowerPointOnlineService`
  - `PowerPointOnlineService`
- Routes:
  - `POST /v1/powerpoint/online/sessions`
  - `GET /v1/powerpoint/online/sessions/{sessionId}`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/screenshot`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`

Validation:

- Unit tests with fake `IEdgeBrowserService` and fake screenshot service.
- Live smoke opens a known SharePoint deck, selects slide 4, captures screenshot, cleans up or leaves session per option.
- Negative live test with synthetic inaccessible URL returns `blocked_auth`, `blocked_permission`, or `failed` with stable evidence.

### Phase 2: Readiness and Save-State Detection

Deliver:

- PowerPoint Online state probe script using DevTools DOM plus screenshot fallback.
- Stable fields: `EditMode`, `SaveState`, `CurrentSlide`, `SlideCount`.
- Error classification for auth, permission, readonly, Office error, and stale session.

Validation:

- Live test against the current SEM27 deck detects `ready`, `editing`, slide count near 71, and current slide after selection.
- Synthetic blocked URL returns classified blocker instead of generic browser failure.

### Phase 3: Add-in Online Activation

Deliver:

- Harness method to prove add-in host and task pane are reachable from the same browser session.
- Fix current host/static add-in mismatch if needed.
- Document install/sideload path for PowerPoint Online in this tenant/session.
- Live task-pane smoke in PowerPoint Online:
  - open deck
  - open add-in
  - claim synthetic job
  - fail safely or complete no-op

Validation:

- `https://localhost:3003/taskpane.html` reachable from the same Windows VM target as Host REST.
- Add-in can call same-origin or configured Host REST.
- Live no-op/add-in heartbeat proves Office.js is executing in active deck.

### Phase 4: Target Bootstrap and Inspection

Deliver:

- Operation to prepare stable targets in a selected slide or template.
- Target inspection endpoint or add-in operation that reports existing target ids and kinds.
- Target manifest artifact for human review.

Validation:

- Create or detect targets in a disposable deck.
- Reopen deck and confirm targets remain addressable.
- Negative test for missing target returns `blocked_targeting` or job failure with stable target error.

### Phase 5: High-Level Update Orchestration

Deliver:

- `POST /v1/powerpoint/online/updates`.
- Orchestration:
  - open/reuse deck session
  - verify active document
  - ensure add-in ready
  - enqueue existing `PowerPointUpdateJob`
  - wait for job result
  - wait for saved state
  - capture evidence
  - optional reopen validation
- Result:
  - `PowerPointOnlineUpdateResult`
  - save-proof tier
  - screenshot artifacts

Validation:

- Live edit on a disposable SharePoint deck:
  - replace text target
  - replace image target
  - wait saved
  - reopen
  - capture evidence
- Queue-only dry run remains separate and is not presented as edit proof.

### Phase 6: Hardening

Deliver:

- Timeouts and retry policy inside `PowerPointOnlineService`.
- Session cleanup policy.
- Run logs/state snapshots under `operator-exchange/runs/<run-id>`.
- OpenAPI and Go client regeneration.
- Development docs and live smoke entry.

Validation:

- Full live smoke route for PowerPoint Online.
- Crash/reload recovery test.
- Permission/read-only negative tests.
- Repeat run proves no stale jobs remain queued and no orphan Edge sessions remain unless explicitly preserved.

## Module Placement

Core contracts:

```text
src/WindowsOperator.Core/Contracts/PowerPointOnline*.cs
src/WindowsOperator.Core/Services/IPowerPointOnlineService.cs
```

Agent implementation:

```text
src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs
```

Host proxy/facade:

```text
src/WindowsOperator.Host/Api/HostOperatorEndpoints.cs
src/WindowsOperator.Host/Services/DesktopAgentClient.cs
src/WindowsOperator.Host/Services/HostOperatorFacade.cs
```

OpenAPI:

```text
src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs
openapi/windows-operator.openapi.json
clients/go/windowsoperator.gen.go
```

Tests:

```text
tests/WindowsOperator.Agent.Tests/PowerPointOnlineServiceTests.cs
tests/WindowsOperator.Agent.Tests/RestAndMcpParityTests.cs
tests/WindowsOperator.Core.Tests/ContractSerializationTests.cs
```

Live smoke:

```text
scripts/linux/live-smoke.py
```

## Caller Impact

Before:

- Caller opens Edge, chooses profile, waits for page, clicks thumbnails, calls screenshot, enqueues PowerPoint job, hopes add-in is ready, polls job status, interprets save state, captures evidence.

After:

- Caller starts a PowerPoint Online session and asks for slide/update/evidence by PowerPoint-domain intent.
- Browser and Office quirks stay inside `PowerPointOnlineService`.
- Existing low-level browser routes remain available for diagnostics.

## Risks and Decisions

Decision: do not build browser DOM/canvas mutation as the primary edit contract.

Reason: PowerPoint Online canvas internals are implementation details. Direct DOM/click editing would be fragile, hard to verify, and likely to push vendor quirks into every caller. Use Office.js for mutation and browser automation for hosting, navigation, and evidence.

Decision: keep existing `/v1/powerpoint/jobs` queue rather than replace it.

Reason: it already owns validation, artifact staging, record persistence, and result semantics. Online harness should compose it.

Decision: make save proof explicit.

Reason: Office.js sync and SharePoint cloud persistence are different facts. Callers need to know whether we proved in-document sync, saved indicator, reopen evidence, or cloud version.

Risk: PowerPoint Online add-in installation may require tenant/admin setup.

Mitigation: Phase 3 must classify this cleanly. If add-in activation is blocked, session/evidence harness still ships, and update orchestration reports `blocked_addin` with concrete evidence.

Risk: arbitrary decks lack stable target ids.

Mitigation: roadmap includes target bootstrap/inspection. Do not promise safe arbitrary edits until target manifest exists.

## Validation Standard

Minimum "done" for visible edit work:

- Live Windows VM run.
- Real SharePoint-hosted PowerPoint deck.
- PowerPoint Online editor visible.
- Add-in applies an edit through Office.js or returns classified blocker.
- Save-state observed.
- Affected slide screenshot captured.
- Reopen validation captured unless caller explicitly requests weaker proof.

Dry-run validates only serialization/routing. It is not edit proof.

## Codex Goal Seed

Objective:

Build the PowerPoint Online editing harness described in `.work/powerpoint-online-editing-harness-roadmap.md`: expose domain-level `/v1/powerpoint/online/*` session and update APIs, compose existing Edge session and PowerPoint job/add-in infrastructure, prove live slide selection/evidence first, then prove Office.js edit/save/reopen verification on a SharePoint-hosted deck.

Architect inheritance:
Before implementation loops, identify the owning boundary for each non-trivial slice. Prefer root-cause fixes in the module/API/data contract that owns behavior over scattered caller patches. Keep public interfaces small and stable; hide retries, parsing, normalization, vendor quirks, state, and compatibility behavior inside the owner. Do not add new abstractions unless they hide real complexity or match existing repo patterns. For architecture forks, broad simplification, spec/source conflicts, or sustained alignment, route to the appropriate architecture/autonomous skill before implementing.
