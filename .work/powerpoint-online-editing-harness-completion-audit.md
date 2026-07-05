# PowerPoint Online Editing Harness Completion Audit

Date: 2026-07-05

Docs entry point: `.work/powerpoint-online-docs-index.md`.

## Scope

Objective: reliable PowerPoint Online editing harness for SharePoint-hosted decks.

Source ranking used for this audit:

1. `.work/powerpoint-online-editing-harness-roadmap.md`
2. `docs/powerpoint-automation-architecture.md`
3. `.work/powerpoint-online-mutation-proof-runbook.md`
4. `docs/feature-namespaces.md`
5. `openapi/windows-operator.openapi.json` and contract tests
6. current Workbench/Edge/PowerPoint source
7. live VM evidence under `/var/lib/windows-server/shared/operator-exchange/runs`

## Requirement Status

| Requirement | Current status | Evidence |
| --- | --- | --- |
| Domain-level PowerPoint Online session API opens SharePoint deck | Proven | `POST /v1/powerpoint/online/sessions`; live SEM27 proofs in roadmap |
| Select slide 4 and capture screenshot evidence | Proven | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z/screenshots/slide-nav-4.png` |
| Hide Edge/UIA/DevTools details behind PowerPoint harness | Proven for public route shape | namespace in roadmap and OpenAPI, service boundary in `PowerPointOnlineService` and `PowerPointOnlineUpdateService` |
| Office.js is preferred mutation path | Proven by design and tests | architecture doc, add-in update engine, no browser DOM slide mutation path; add-in Vitest 24/24 and build passed on 2026-07-05 |
| Add-in activation and Office.js claim path | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-validate-20260703t134919145483350z/summary.json` |
| Non-mutating update/readiness with save and reopen visual proof | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/summary.json` |
| Final proof request contract and generated clients | Proven by tests/generation | Windows Core contract/OpenAPI tests 27/27: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T110032Z-803870/result.json`; Go client test evidence in roadmap |
| Dev JS debug harness, disabled by default | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-dev-harness-smoke-20260704t105257z/summary.json`; restored `422 dev_automation_disabled` afterward |
| Mutation safety gate for SEM27 without approval | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t105756z/summary.json` |
| Apply a visible Office.js edit, wait for saved, reopen, and verify persisted edit | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`; screenshots show proof text before cleanup and after reopen |
| Read and write PowerPoint table cells/ranges | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`; screenshots show `DATA_TABLE` values before cleanup and after reopen |
| Cleanup after mutating proof | Proven live | `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`; final Edge/Chrome-like window count `0` |

Current Windows-side service test sweep:

- Core contract/OpenAPI tests: 29/29 passed at `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043732Z-304750/result.json`
- Host proxy/job/update orchestration tests: 96/96 passed at `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043752Z-304974/result.json`
- Agent PowerPoint Online/dev automation/parity tests: 47/47 passed at `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043817Z-305603/result.json`
- Focused Agent run-pending fallback tests: 30/30 passed at `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T044519Z-316581/result.json`
- Focused Host stale-queue failure tests: 28/28 passed at `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T045226Z-329483/result.json`

Current local add-in/client checks:

- `go test ./...` in `clients/go` passed.
- `npm test -- --run` in `src/WindowsOperator.PowerPointAddIn` passed 24/24 tests.
- `npm run build` in `src/WindowsOperator.PowerPointAddIn` passed.
- `npm run manifest:validate` in `src/WindowsOperator.PowerPointAddIn` passed; validator reported `The manifest is valid.`

## Completion Decision

Goal is complete for the requested harness roadmap.

The final SEM27 proof ran through Office.js with `allowDeckMutation=true`: template prepare created `TITLE_MAIN` and `HERO_IMAGE`, `replaceText` changed `TITLE_MAIN`, PowerPoint Online reported saved, the deck reopened, slide 4 still showed the proof text, cleanup removed the temporary targets, and the final Edge/Chrome-like window count was `0`.

The table proof also ran through Office.js with `allowDeckMutation=true`:
template prepare created `DATA_TABLE`, `readTable` returned its initial 3x3
values, `replaceTableCell` and `replaceTableRange` wrote `67 kt`, `101%`, and
`103%`, PowerPoint Online reported saved, the deck reopened with the values
still visible, cleanup removed the temporary table, and the final
Edge/Chrome-like window count was `0`.

## Final Proof

Command shape used after explicit SEM27 mutation approval:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'SEM27_URL' \
  --run-id ppt-mutation-proof-sem27-long-20260705t010320z \
  --execute \
  --allow-deck-mutation \
  --allow-sem27 \
  --http-timeout-seconds 420
```

Success summary:

- summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- `httpStatus=200`, `success=true`, `status=succeeded`
- `saveProofTier=tier3ReopenVisual`
- `jobStatus=succeeded`, `claimedBy=officejs-taskpane`
- `titleMainTargetSucceeded=true`, `titleMainDiscovered=true`
- evidence counts `3/3/3` successful/verified/distinct
- `templatePreparationStatus=ready`, `verificationStatus=ready`, `templateCleanupStatus=ready`, `sessionCleanupStatus=closed`
- final Edge/Chrome-like window count `0`

Visual evidence:

- initial edit screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
- reopened persistence screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
- post-cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`

## Table Proof

Command shape used after explicit SEM27 mutation approval:

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/powerpoint/online/updates \
  -H 'content-type: application/json' \
  -d '{"deckUrl":"SEM27_URL","sessionId":"ppt-table-onecall-sem27-20260705t0453z","job":{"jobId":"ppt-table-onecall-sem27-20260705t0453z","discoverTargets":true,"operations":[{"kind":"readTable","targetId":"DATA_TABLE"},{"kind":"replaceTableCell","targetId":"DATA_TABLE","rowIndex":1,"columnIndex":1,"text":"67 kt"},{"kind":"replaceTableRange","targetId":"DATA_TABLE","startRowIndex":2,"startColumnIndex":1,"values":[["101%","103%"]]}],"requestedBy":"codex-live-table-proof"},"evidenceSlideNumber":4,"capture":true,"allowDeckMutation":true,"prepareTemplate":true,"verifyReopen":true,"cleanupTemplate":true,"cleanupSession":true,"openWaitSeconds":40,"jobTimeoutSeconds":90,"saveTimeoutSeconds":45,"savePollSeconds":1,"reopenWaitSeconds":40}'
```

Success summary:

- response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`
- `httpStatus=200`, `success=true`, `status=succeeded`
- `saveProofTier=tier3ReopenVisual`
- `jobStatus=succeeded`
- operations succeeded: `readTable`, `replaceTableCell`, `replaceTableRange`
- initial table read returned `Metric/Plan/Actual`, `Tonnes/0/0`, `Availability/0%/0%`
- evidence showed persisted values `67 kt`, `101%`, and `103%`
- `templatePreparationSession.status=ready`, `verificationSession.status=ready`, `templateCleanupSession.status=ready`, `sessionCleanupSession.status=closed`
- final Edge/Chrome-like window count `0`

Visual evidence:

- initial table edit screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`
- reopened table persistence screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`
- post-cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`

## Residual Limits

- Tier-4 SharePoint/Graph version-history proof is still unavailable without Graph or another cloud version API.
- Arbitrary existing deck/object targeting is not solved; the proven live edit path uses add-in-created tagged `TITLE_MAIN`, `HERO_IMAGE`, and `DATA_TABLE` targets.
- The first live SEM27 execution needed longer than the previous 240s client timeout. The runner default is now 420s because the successful run took 343.19s.
