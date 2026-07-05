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
  "validateOnly": false,
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
- `discoverTargets`: optional. Defaults `false`. Enumerates binding-backed
  targets and named `TARGET_*` shapes.
- `bindNamedTargets`: optional. Defaults `false`. Repairs matching
  `TARGET_<TARGET_ID>` shapes into binding/tag targets before inspect/apply.
- `validateOnly`: optional. Defaults `false`. When `true`, add-in inspects stable target ids only and does not mutate slides.
- `requestedBy`: caller label.
- `createdAt`: caller timestamp.
- `operations`: one or more `replaceText`, `replaceImage`, `readTable`, `replaceTableCell`, or `replaceTableRange` operations.

`jobId` and artifact ids must use lowercase ASCII letters, digits, `_`, `-`, or interior dots. They cannot start or end with `.`, and cannot use Windows device names like `con` or `lpt1`.

`targetId` values are semantic context cues. Use uppercase ASCII snake case:

```text
<CONTEXT>_<ROLE>[_<KIND>][_<NN>]
```

Examples: `TITLE_MAIN`, `HERO_IMAGE`, `DATA_TABLE`,
`KPI_TONNES_VALUE`, `SUMMARY_TABLE_01`.

Target ids must be deck-global, stable, and independent of visible text,
coordinates, z-order, or default PowerPoint names. Generated shape names should
be `TARGET_<TARGET_ID>`. Binding id and `TARGET_ID` tag should both equal the
target id; `TARGET_KIND` should be `text`, `image`, or `table`.

Named-template mode:

- Author a shape name as `TARGET_<TARGET_ID>`.
- Send operations with `targetId: "<TARGET_ID>"`.
- Set `bindNamedTargets: true` to create missing binding and tags before apply.
- Duplicate target names fail with `TARGET_AMBIGUOUS`.
- Existing bindings are never overwritten; a binding/name conflict fails.
- Operation target results repaired from names report `source: "repairedName"`
  plus `shapeName`, `bound: true`, and `tagged: true`.

`replaceText`

- `kind`: `replaceText`.
- `targetId`: Office binding/tag target id.
- `text`: replacement text. Required for executable jobs. Optional when `validateOnly=true`.
- `mode`: currently `plain`.
- `allowEmpty`: optional. Whitespace-only `text` is rejected unless `allowEmpty` is `true`.

`replaceImage`

- `kind`: `replaceImage`.
- `targetId`: Office binding/tag target id.
- `artifact`: image artifact ref. Required for executable jobs. Optional when `validateOnly=true`.
- `altText`: optional.
- `fit`: optional `cover` or `contain`.

`readTable`

- `kind`: `readTable`.
- `targetId`: Office binding/tag target id for a table shape.
- Non-mutating. Returns `targets[].table` with `rowCount`, `columnCount`, and `values`.

`replaceTableCell`

- `kind`: `replaceTableCell`.
- `targetId`: Office binding/tag target id for a table shape.
- `rowIndex`: zero-based row index. Required for executable jobs. Optional when `validateOnly=true`.
- `columnIndex`: zero-based column index. Required for executable jobs. Optional when `validateOnly=true`.
- `text`: replacement cell text. Required for executable jobs. Optional when `validateOnly=true`.
- `allowEmpty`: optional. Whitespace-only `text` is rejected unless `allowEmpty` is `true`.

`replaceTableRange`

- `kind`: `replaceTableRange`.
- `targetId`: Office binding/tag target id for a table shape.
- `startRowIndex`: optional zero-based row index. Defaults to `0`.
- `startColumnIndex`: optional zero-based column index. Defaults to `0`.
- `values`: rectangular string matrix. Required for executable jobs. Optional when `validateOnly=true`.
- `allowEmpty`: optional. Empty or whitespace-only values are rejected unless `allowEmpty` is `true`.

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
- `found`: optional inspection result.
- `editable`: optional inspection result.
- `type`: optional inspected target type.
- `message`: optional inspection detail.
- `shapeName`: optional PowerPoint shape name.
- `source`: optional `binding`, `name`, or `repairedName`.
- `bound`: optional. `true` when a binding existed or was required for apply.
- `tagged`: optional. `true` when `TARGET_ID` and `TARGET_KIND` match.
- `table`: optional table snapshot for `readTable`, with `rowCount`, `columnCount`, and `values`.

## Add-in Behavior

- Use `PowerPoint.run`.
- Load only needed target properties.
- Batch reads/writes.
- Avoid `context.sync()` in tight loops.
- Gate runtime APIs with Office requirement sets.
- For `validateOnly=true`, inspect targets, return `skipped` for editable targets, `failed` for missing/not-editable targets, and skip artifact resolution and mutation.
- Treat Office.js applied/synced result as apply confirmation only, not durable save/version proof.

## Probe Diagnostics

`POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` checks local add-in package health before task pane activation conclusions. Result includes normalized `taskPaneUrl`, `manifestUrl`, reachability flags for both, plus parsed manifest `id`, `version`, `displayName`, and task pane `sourceLocation`.

`hostReachable=true` means both `taskpane.html` marker validation and `manifest.xml` parsing passed. It does not mean the Office task pane is open. Tenant/user installation or sideload state is represented separately by `taskPaneVisible`, `commandVisible`, and `status=blockedActivation`.
