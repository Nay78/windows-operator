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

Harness integration:

- The high-level PowerPoint Online harness wraps this add-in with browser
  session control, add-in activation, save-state wait, screenshot evidence,
  reopen verification, and cleanup.
- Live SEM27 text/image and table proof evidence is tracked in
  [PowerPoint automation architecture](../../docs/powerpoint-automation-architecture.md).
- This is tier-3 reopen visual proof. It is still not Graph/SharePoint version
  proof.

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

Host-staged smoke command lives in the root [development runbook](../../docs/development.md#live-smoke).

## Template Setup

In PowerPoint:

1. Open the add-in task pane.
2. Select the slide to use as the mock template.
3. Click `Prepare Template`.
4. Click `Run Mock Job` or `Run Pending Job`.
5. Click `Cleanup Template` to remove targets created by this setup.

`Prepare Template` creates and binds:

- `TITLE_MAIN`: text box target.
- `HERO_IMAGE`: image fill target.
- `DATA_TABLE`: table target.

`Prepare Named Targets` creates the same mock shapes with only
`shape.name = TARGET_<TARGET_ID>`. It leaves bindings/tags absent so
`bindNamedTargets` can be smoke-tested. `Cleanup Named Targets` deletes those
mock names and removes matching repaired bindings when present. On a newly
reopened verification session, activate/probe the add-in task pane before
clicking cleanup.

Supported operation kinds are `replaceText`, `replaceImage`, `readTable`,
`replaceTableCell`, and `replaceTableRange`. Table reads return a structured
snapshot; table writes address zero-based cells or rectangular ranges.

`Cleanup Template` deletes only bound shapes carrying the matching `TARGET_ID`
tag, then removes their bindings. Existing authored shapes are skipped.

Target ids use uppercase semantic cues such as `TITLE_MAIN`, `HERO_IMAGE`,
`DATA_TABLE`, or `KPI_TONNES_VALUE`. Binding id and `TARGET_ID` tag must match
the target id. Authored or generated shape names use `TARGET_<TARGET_ID>`.
Set `bindNamedTargets: true` on a job to repair named-only shapes into durable
bindings and tags before apply; this is a deck mutation.
