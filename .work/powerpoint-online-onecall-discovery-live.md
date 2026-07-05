# PowerPoint Online One-Call Discovery Live Proof

Date: 2026-07-04

## Purpose

Prove the high-level PowerPoint Online update harness can open a SharePoint-hosted deck, activate the Office.js add-in, execute a non-mutating job, observe save state, select slide 4, capture evidence, and clean up the browser session without caller-side browser/UIA choreography.

## Command Shape

Endpoint:

```text
POST http://127.0.0.1:43117/v1/powerpoint/online/updates
```

Request facts:

- `deckUrl`: SEM27 SharePoint PowerPoint URL.
- `sessionId`: `ppt-onecall-discovery-20260704t0908z`
- `job.jobId`: `ppt-onecall-discovery-20260704t0908z`
- `job.discoverTargets`: `true`
- `job.validateOnly`: `true`
- `job.operations`: `[]`
- `evidenceSlideNumber`: `4`
- `capture`: `true`
- `allowDeckMutation`: `false`
- `cleanupSession`: `true`

## Result

Route result:

- `success=true`
- `status=succeeded`
- `saveProofTier=tier2SavedIndicator`
- warnings: `No visible DOM match.` from DOM slide-select fallback; geometry thumbnail click succeeded.
- errors: none.

Session result:

- `status=ready` before cleanup.
- `currentSlide=4`
- `slideCount=71`
- `editMode=editing`
- `saveState=saved`

Job result:

- `status=succeeded`
- `claimedBy=officejs-taskpane`
- `claimedDocumentUrl=https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27 - Plan Semanal Servicios Mina.pptx`
- `discoveredTargets=[]`
- `targets=[]`
- `claimedAtUtc=2026-07-04T09:09:02.6498812+00:00`
- `completedAtUtc=2026-07-04T09:09:03.2922129+00:00`

Cleanup:

- `sessionCleanupSession.status=closed`
- final `/v1/windows` Chrome widget count: `0`

## Evidence

- Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z`
- Screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z/screenshots/powerpoint-online-update.png`
- Screenshot file check: `PNG image data, 1296 x 776, 8-bit/color RGBA, non-interlaced`
- Session snapshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z/powerpoint-online-session.json`

## Historical Scope Limit

Historical scope limit: this 2026-07-04 run proved the non-mutating Office.js bridge and tier-2 saved-indicator proof only. The later final proof covers visible mutation, reopen persistence, and cleanup for add-in-created targets: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.

Still outside the proven path: SharePoint/Graph version proof and arbitrary existing object targeting.
