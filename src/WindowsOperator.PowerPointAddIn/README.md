# Windows Operator PowerPoint Add-in

Office.js task pane for applying queued PowerPoint update jobs to the active presentation.

Production shape:

- Windows Operator Host serves this add-in at `https://localhost:3003`.
- External callers enqueue desired state through `POST /v1/powerpoint/jobs`.
- Task pane claims work from `POST /v1/powerpoint/jobs/claim`.
- Office.js mutates slides with `PowerPoint.run`.
- Host stages image artifacts and serves them from job-local URLs.
- Result means Office.js applied and synced changes in the open presentation. It does not prove durable cloud save/version persistence.

No Graph path. No desktop PowerPoint COM edit path. No browser DOM slide mutation.

## Source Shape

- `src/app.ts`: task pane wiring.
- `src/update/updateEngine.ts`: update orchestration boundary.
- `src/office/presentationAdapter.ts`: Office.js mutation adapter.
- `src/office/currentDocument.ts`: active document URL guard.
- `src/artifacts/httpArtifactResolver.ts`: artifact fetch and base64 normalization.
- `src/jobs/httpJobClient.ts`: Windows Operator queue client.
- `src/jobs/mockJobClient.ts`: local mock job for template setup/dev.
- `manifest.xml`: PowerPoint task pane add-in manifest.

## Dev

```bash
npm install
npm run dev
```

Sideload `manifest.xml` in PowerPoint. Dev server uses `https://localhost:3003`.

Mock mode:

```bash
VITE_USE_MOCK_JOB=true npm run dev
```

Windows Operator queue mode:

```bash
VITE_USE_MOCK_JOB=false npm run dev
```

When hosted by Windows Operator, `VITE_JOB_API_BASE_URL` stays empty so the add-in posts to same-origin `/v1/powerpoint/jobs/*`.

## Checks

```bash
npm run typecheck
npm test
npm run build
npm run manifest:validate
```

## Template Setup

In PowerPoint:

1. Open the add-in task pane.
2. Select the slide to use as the mock template.
3. Click `Prepare Template`.
4. Click `Run Mock Job` or `Run Pending Job`.

`Prepare Template` creates and binds:

- `TITLE_MAIN`: text box target.
- `HERO_IMAGE`: image fill target.
