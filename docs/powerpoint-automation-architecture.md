# PowerPoint Automation Target Architecture

Goal: let external services request precise PowerPoint slide edits while Windows Operator hides queueing, artifact staging, Office.js mechanics, and local runtime details.

## Decision

Use Windows Operator Host plus Office.js task pane for PowerPoint mutation.

No Microsoft Graph access. No desktop PowerPoint COM edit path. No browser DOM slide mutation.

PowerPoint itself remains the mutation runtime, but the mutation surface is Office.js inside the add-in:

```text
external service
  -> Windows Operator Host REST 127.0.0.1:43117
  -> local durable PowerPoint job queue
  -> Office.js add-in hosted at https://localhost:3003
  -> PowerPoint.run against active presentation
  -> job record/result in Host queue
```

## Runtime Topology

Host owns:

- Static HTTPS add-in hosting.
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
```

Do not add MCP tools unless AI runtimes need direct PowerPoint mutation. External services should call REST.

## Job Format

`POST /v1/powerpoint/jobs` accepts desired state, not Office.js steps.

```json
{
  "jobId": "job-123",
  "expectedDocumentUrl": "https://tenant.sharepoint.com/sites/team/report.pptx",
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

Document matching uses normalized `expectedDocumentUrl` when both expected and actual URLs are present. The add-in also checks active document before applying.

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

## Targeting Model

Use stable target ids created by the add-in template setup or by authored bindings/tags.

Preferred targets:

1. Explicit binding/tag id, for example `TITLE_MAIN`.
2. Stable shape alt text/tag mapping.
3. Generated test deck targets only in fixtures.

Do not depend on coordinates, z-order, or default names like `Rectangle 3`.

## Result Semantics

`PowerPointUpdateResult` means Office.js applied the operation and completed its sync path in the open presentation.

It does not prove:

- OneDrive/SharePoint durable save.
- Cloud version increment.
- Conflict-free remote persistence.

Without Graph access, callers needing durable save proof need a separate Windows/PowerPoint-visible validation path.

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
    "baseUrl": "https://localhost:3003",
    "staticRoot": "",
    "stateRoot": "",
    "maxArtifactBytes": 15728640
  }
}
```

Defaults:

- `baseUrl`: `https://localhost:3003`
- `staticRoot`: sibling `src/WindowsOperator.PowerPointAddIn/dist`
- `stateRoot`: `%LOCALAPPDATA%\WindowsOperator\run\powerpoint-officejs`

## Validation

Local:

- `dotnet build WindowsOperator.sln`
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
- `npm run typecheck`
- `npm test`
- `npm run build`
- `npm run manifest:validate`

Live:

- Start Host.
- Sideload add-in manifest.
- Open target presentation in PowerPoint.
- Enqueue job with matching `expectedDocumentUrl`.
- Claim/apply from task pane.
- Inspect slide visually.
