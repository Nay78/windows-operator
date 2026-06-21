# Spec

## Host REST

Base path: `/v1/powerpoint`.

### Enqueue Job

`POST /jobs`

Request: `PowerPointUpdateJob`

```json
{
  "jobId": "job-123",
  "expectedDocumentUrl": "https://tenant.sharepoint.com/sites/team/doc.pptx",
  "requestedBy": "orchestrator",
  "createdAt": "2026-06-17T12:00:00Z",
  "operations": [
    {
      "kind": "replaceText",
      "targetId": "TITLE_MAIN",
      "text": "Updated title",
      "mode": "plain"
    }
  ]
}
```

Response: `PowerPointJobRecord`.

Host rejects malformed job payloads with `422 powerpoint_validation_failed` before queueing or staging artifacts.

### Claim Job

`POST /jobs/claim`

Request:

```json
{
  "workerId": "officejs-taskpane",
  "documentUrl": "https://tenant.sharepoint.com/sites/team/doc.pptx"
}
```

Response:

- `200` with `PowerPointUpdateJob` when queued work matches.
- `204` when no queued work matches.

Matching is by normalized `expectedDocumentUrl` when both expected and active URLs are present.

### Complete Job

`POST /jobs/{jobId}/complete`

Request: `PowerPointUpdateResult`.

Route `jobId` must match result `jobId`. `status: "succeeded"` stores job status as `succeeded`; `failed` or `partial` stores job status as `failed`.

### Fail Job

`POST /jobs/{jobId}/fail`

Request: `PowerPointUpdateError`.

Requires non-empty `code` and `operatorMessage`. Stores job status as `failed`.

### Read Job

`GET /jobs/{jobId}`

Response: `PowerPointJobRecord`.

### Read Artifact

`GET /jobs/{jobId}/artifacts/{artifactId}`

Response: staged `image/png` or `image/jpeg` bytes.

## Job Contract

`PowerPointUpdateJob`

- `jobId`: stable caller-provided id.
- `expectedDocumentUrl`: optional active presentation guard.
- `requestedBy`: caller label.
- `createdAt`: caller timestamp.
- `operations`: one or more `replaceText` or `replaceImage` operations.

`jobId` and artifact ids must use lowercase ASCII letters, digits, `_`, `-`, or interior dots. They cannot start or end with `.`, and cannot use Windows device names like `con` or `lpt1`.

`replaceText`

- `kind`: `replaceText`.
- `targetId`: Office binding/tag target id.
- `text`: replacement text.
- `mode`: currently `plain`.
- `allowEmpty`: optional. Whitespace-only `text` is rejected unless `allowEmpty` is `true`.

`replaceImage`

- `kind`: `replaceImage`.
- `targetId`: Office binding/tag target id.
- `artifact`: image artifact ref.
- `altText`: optional.
- `fit`: optional `cover` or `contain`.

Artifact refs accepted by Host:

- `artifactId`.
- `url`: remote HTTPS URL or `data:` base64 URL.
- `mediaType`: `image/png` or `image/jpeg`.
- `sha256`: optional expected digest.
- `expiresAt`: optional expiry.

Host stages artifacts and rewrites artifact URLs to `/v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}` before add-in claim.

## Result Contract

`PowerPointUpdateResult`

- `jobId`.
- `status`: `succeeded`, `failed`, `partial`.
- `startedAt`.
- `finishedAt`.
- `targets`: per-target result list.

Target result:

- `targetId`.
- `operationKind`.
- `status`: `succeeded`, `failed`, or `skipped`.
- `error`: optional `PowerPointUpdateError`.

## Add-in Behavior

- Use `PowerPoint.run`.
- Load only needed target properties.
- Batch reads/writes.
- Avoid `context.sync()` in tight loops.
- Gate runtime APIs with Office requirement sets.
- Treat Office.js applied/synced result as apply confirmation only, not durable save/version proof.
