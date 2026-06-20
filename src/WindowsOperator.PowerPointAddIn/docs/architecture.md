# Architecture

`WindowsOperator.PowerPointAddIn` turns queued desired-state jobs into Office.js mutations on the active presentation.

## Owners

Windows Operator Host owns:

- HTTPS static hosting for the add-in at `https://localhost:3003`.
- Durable local queue under `PowerPointAddIn:StateRoot`.
- Public REST queue contract under `/v1/powerpoint/jobs`.
- Image artifact fetch, validation, checksum, size limits, staging, and artifact read URLs.
- Job status transitions: `queued`, `running`, `succeeded`, `failed`.
- Stable `OperatorError` translation.

PowerPoint add-in owns:

- Claiming jobs from same-origin `/v1/powerpoint/jobs/claim`.
- Active document URL guard via `Office.context.document.url`.
- Office.js requirement checks.
- Target inspection and update application through `PowerPoint.run`.
- Reporting Office.js apply/sync result back to Host.

External caller owns:

- Enqueueing a typed update job.
- Choosing `expectedDocumentUrl` when document identity matters.
- Polling job record if status is needed.

## Non-Goals

- No Graph identity, metadata, download, upload, or version checks.
- No desktop PowerPoint COM edit path.
- No browser DOM slide mutation.
- No claim of durable OneDrive/SharePoint save confirmation.

## Flow

1. Caller posts `PowerPointUpdateJob` to `POST /v1/powerpoint/jobs`.
2. Host validates job, stages image artifacts, writes `job.json`.
3. User opens the add-in in PowerPoint.
4. Add-in posts `PowerPointClaimJobRequest` with active document URL.
5. Host returns first matching queued job or `204`.
6. Add-in resolves staged artifacts and prevalidates targets.
7. Add-in applies operations through `PowerPoint.run` and batches syncs.
8. Add-in posts result to `/complete` or `/fail`.
9. Caller reads `GET /v1/powerpoint/jobs/{jobId}`.

## Deep Module Boundary

The public boundary stays small:

- Input: `PowerPointUpdateJob`
- Output/status: `PowerPointJobRecord`

Complexity hidden behind that boundary:

- Local queue layout.
- Artifact URL retrieval and staging.
- Artifact media/checksum validation.
- Active-document claim filtering.
- Office.js target lookup and apply mechanics.
- Implementation-specific exceptions.

## Runtime Notes

Host binds both:

- `Operator:RestBaseUrl`, usually `http://127.0.0.1:43117`.
- `PowerPointAddIn:BaseUrl`, default `https://localhost:3003`.

The add-in build output lives in `dist`. Host serves it when `dist` exists or when `PowerPointAddIn:StaticRoot` points to another built directory.
