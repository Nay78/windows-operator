# PowerPoint Online Update Orchestration Handoff

Date: 2026-07-03

## Objective

Add the first high-level update API:

```text
POST /v1/powerpoint/online/updates
```

This slice should compose the existing PowerPoint Online session harness and existing PowerPoint job queue. It should not attempt browser DOM mutation. Office.js remains the mutation path through the existing add-in.

## Boundary

Owning implementation boundary for this slice: Host-level PowerPoint Online update orchestration.

Reason: update orchestration must compose:

- Agent-owned `IPowerPointOnlineService` session/select/screenshot operations through `DesktopAgentClient`.
- Host-owned `IPowerPointJobService` durable Office.js job queue.

Do not move the job queue into Agent. Do not make Agent call Host REST.

## Proposed Contracts

Add Core contracts:

- `PowerPointOnlineUpdateRequest`
- `PowerPointOnlineUpdateResult`
- `PowerPointOnlineUpdateStatus`

Suggested request shape:

```csharp
public sealed record PowerPointOnlineUpdateRequest
{
    public string? SessionId { get; init; }
    public string? DeckUrl { get; init; }
    public required PowerPointUpdateJob Job { get; init; }
    public int? EvidenceSlideNumber { get; init; }
    public bool Capture { get; init; } = true;
    public int OpenWaitSeconds { get; init; } = 30;
    public int JobTimeoutSeconds { get; init; } = 60;
    public int PollSeconds { get; init; } = 1;
}
```

Suggested result shape:

```csharp
public sealed record PowerPointOnlineUpdateResult
{
    public required bool Success { get; init; }
    public required PowerPointOnlineUpdateStatus Status { get; init; }
    public required PowerPointOnlineSessionResult Session { get; init; }
    public required PowerPointJobRecord JobRecord { get; init; }
    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

Suggested status enum values:

```text
succeeded
failed
blockedSession
blockedAddIn
saveUnverified
```

## Behavior

1. Require either `DeckUrl` or `SessionId`.
2. If `DeckUrl` is provided:
   - call `IPowerPointOnlineService.StartOnlineSessionAsync` with `SessionId`, `DeckUrl`, `Capture=false`, and `OpenWaitSeconds`.
3. If only `SessionId` is provided:
   - call `IPowerPointOnlineService.GetOnlineSessionAsync`.
4. If session is not `Ready`:
   - return `blockedSession`, preserve session errors.
5. Enqueue `request.Job` through `IPowerPointJobService.EnqueueAsync`.
6. Poll `IPowerPointJobService.GetAsync(jobId)` until:
   - `succeeded`: return `succeeded`
   - `failed`: return `failed`
   - timeout: call `IPowerPointJobService.FailAsync(jobId, new PowerPointUpdateError("ADDIN_TIMEOUT", true, "..."))`, return `blockedAddIn`
7. On terminal result, if `EvidenceSlideNumber` is provided:
   - call session slide select with `Capture=false`.
8. If `Capture=true`:
   - capture a session screenshot after terminal/timeout and include it in result evidence.

Keep save-state proof out of this slice. Use `saveUnverified` later after state probing exists.

## Tests

Add focused Host tests with fakes:

- update endpoint enqueues job and returns `blockedAddIn` after timeout while marking job failed.
- update endpoint returns `blockedSession` when session is blocked.
- update endpoint returns `succeeded` when fake job service flips to succeeded.
- REST route maps request/response and OpenAPI includes `/v1/powerpoint/online/updates`.

Keep existing Agent session tests passing.

## Validation

Local:

- `dotnet build tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj`
- `cd clients/go && go test ./...`

Live negative path:

- Confirm add-in host still serves:
  - `scripts/windows/probe-url.ps1 -Url https://localhost:3003/taskpane.html -RequiredText "Windows Operator PowerPoint"`
- Call `POST /v1/powerpoint/online/updates` with the SEM27 deck and a synthetic replaceText job with a fake target id.
- Expected if task pane is not active: HTTP 200, `status=blockedAddIn`, job record failed with `ADDIN_TIMEOUT`, screenshot evidence captured, no stale queued job.
- Cleanup Edge session and verify `GET /v1/windows` has `edge_like=0`.
