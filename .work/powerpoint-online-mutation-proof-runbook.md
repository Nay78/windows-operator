# PowerPoint Online Mutation Proof Runbook

Date: 2026-07-05

## Purpose

Run or repeat the final tier-3 edit proof for a SharePoint-hosted deck. SEM27 approval was granted and the proof passed live on 2026-07-05.

This proof intentionally writes to the deck. Even when `cleanupTemplate=true` removes visible test shapes afterward, SharePoint version history can retain the prepare/update/cleanup edits.

## Owner Gate

- Owner: operator/user.
- Needed input before future mutating runs: disposable SharePoint `.pptx` URL, or explicit approval to run against the target production deck.
- Approval packet: `.work/powerpoint-online-final-mutation-approval-packet.md`.
- SEM27 approval was granted once on 2026-07-05 and the final proof passed; repeat SEM27 mutation still needs explicit intent because it writes SharePoint version history.

## Target Contract

Prepared template targets are owned by the Office.js add-in:

- `TITLE_MAIN`: text target, created by `Prepare Template`.
- `HERO_IMAGE`: image target, created by `Prepare Template`.
- `DATA_TABLE`: table target, created by `Prepare Template`.

Use `TITLE_MAIN` for first final proof because it needs no external artifact.
Use `DATA_TABLE` for table read/write proof with `readTable`,
`replaceTableCell`, and `replaceTableRange`.

High-level updates select and verify `evidenceSlideNumber` before `Prepare Template`
when `prepareTemplate=true`. This keeps the temporary bindings on the slide that
will be edited and photographed. If the observed slide differs from the requested
slide, the route returns `blockedSession` before queueing the job or creating
template targets.

Every evidence capture point also verifies `evidenceSlideNumber` before taking a
screenshot. A mismatch returns structured failure (`blockedSession` for final
evidence, `verificationFailed` for reopen evidence, or `cleanupFailed` for
post-cleanup evidence) instead of reporting success with a screenshot of the
wrong slide.

Reopen proof requires both `success=true` and `status=ready` on the reopened
session and captured evidence session. `status=ready` alone is not enough to
claim tier-3 visual proof.

## Request

Endpoint:

```text
POST http://127.0.0.1:43117/v1/powerpoint/online/updates
```

Body template:

```json
{
  "deckUrl": "DECK_URL",
  "sessionId": "ppt-mutation-proof-YYYYMMDDTHHMMZ",
  "job": {
    "jobId": "ppt-mutation-proof-YYYYMMDDTHHMMZ",
    "discoverTargets": true,
    "validateOnly": false,
    "operations": [
      {
        "kind": "replaceText",
        "targetId": "TITLE_MAIN",
        "mode": "plain",
        "text": "Windows Operator live edit proof YYYY-MM-DD HH:MM UTC"
      }
    ],
    "requestedBy": "codex-live-mutation-proof"
  },
  "evidenceSlideNumber": 4,
  "capture": true,
  "allowDeckMutation": true,
  "prepareTemplate": true,
  "cleanupTemplate": true,
  "cleanupTemplateOnFailure": true,
  "templateWaitSeconds": 2,
  "verifyReopen": true,
  "reopenWaitSeconds": 40,
  "cleanupSession": true,
  "openWaitSeconds": 40,
  "jobTimeoutSeconds": 60,
  "saveTimeoutSeconds": 30,
  "savePollSeconds": 1
}
```

## Proof Runner

Preferred command for a disposable deck:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'DECK_URL' \
  --run-id ppt-mutation-proof-YYYYMMDDTHHMMZ \
  --execute \
  --allow-deck-mutation
```

For SEM27, add `--allow-sem27` only after explicit approval:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'SEM27_URL' \
  --run-id ppt-mutation-proof-YYYYMMDDTHHMMZ \
  --execute \
  --allow-deck-mutation \
  --allow-sem27
```

Without `--execute`, the script writes the request artifacts only. With
`--execute`, it writes `request.json`, `response.json`, `windows-after.json`,
and `summary.json` under the run root. It exits successfully only when the
structured result proves tier-3 reopen visual proof, `TITLE_MAIN` replacement,
template cleanup, final session cleanup, and final Edge/Chrome window count `0`.
The default Host HTTP timeout is 420s because the successful live SEM27 proof
took 343.19s end to end.

Safe Host gate check:

```bash
just ppt-final-proof-host-gate
```

This intentionally sends the final executable proof shape with
`allowDeckMutation=false` and expects HTTP `422 powerpoint_validation_failed`.
It also checks `GET /v1/powerpoint/jobs/{runId}` returns `404`, proving the
job was not queued. It records `windows-before.json` and `windows-after.json`;
success requires Edge/Chrome-like window count `0` before and after.

Safe readiness check:

```bash
just ppt-final-proof-readiness
```

This runs the non-mutating prerequisite proof against SEM27. It opens one
PowerPoint Online session, runs `discoverTargets=true` with `validateOnly=true`
and no operations, captures slide 4, reopens the deck for tier-3 visual proof,
and closes the final session. Success requires `officejs-taskpane`, tier-3
reopen proof, at least two evidence captures, and final Edge/Chrome-like window
count `0`.

## Expected Success

- `success=true`
- `status=succeeded`
- `saveProofTier=tier3ReopenVisual`
- `jobRecord.status=succeeded`
- `jobRecord.claimedBy=officejs-taskpane`
- `jobRecord.result.targets[]` contains `targetId=TITLE_MAIN`, `operationKind=replaceText`, `status=succeeded`
- `jobRecord.result.discoveredTargets[]` includes `TITLE_MAIN`
- `templatePreparationSession.status=ready`
- `verificationSession.status=ready`
- `templateCleanupSession.status=ready`
- `sessionCleanupSession.status=closed`
- evidence contains initial, reopened, and post-cleanup screenshots
- final `/v1/windows` Edge/Chrome widget count is `0`

## Expected Visible Evidence

Before cleanup, initial and reopened screenshots should show slide 4 with a temporary text box containing the proof text. Cleanup runs after reopened evidence capture. When `capture=true`, high-level cleanup then captures `powerpoint-online-template-cleanup` evidence on the same slide after the cleanup save is verified; that screenshot should no longer show the prepared template targets.

## Failure Classification

- `blockedSession`: deck/auth/permission/read-only/open problem.
- `blockedAddIn`: task pane activation, local add-in host, or Office.js claim path problem.
- `saveUnverified`: Office.js job succeeded but PowerPoint Online did not return to `saved`.
- `verificationFailed`: reopen or reopened screenshot failed after save.
- `cleanupFailed`: edit proof succeeded but template cleanup failed; manual cleanup may be needed.
- `sessionCleanupFailed`: proof succeeded but browser cleanup was not proven.

## Current Proof State

Final mutating proof now passed against SEM27 after explicit approval:

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- HTTP `200`, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`
- `jobRecord.claimedBy=officejs-taskpane`, `TITLE_MAIN` replaceText succeeded, `TITLE_MAIN` was discovered
- evidence counts: three successful, Linux-visible, distinct image artifacts
- initial screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
- reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
- cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`
- final `/v1/windows` Edge/Chrome-like window filter: `[]`

Table mutating proof also passed against SEM27 after explicit approval:

- response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`
- HTTP `200`, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`
- operations: `readTable`, `replaceTableCell`, and `replaceTableRange` on `DATA_TABLE`
- visual proof showed `67 kt`, `101%`, and `103%` before cleanup and after reopen
- initial table screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`
- reopened table screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`
- cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`
- final `/v1/windows` Edge/Chrome-like window filter: `[]`

Already proven against SEM27 without mutation:

- Office.js claim path: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z`
- Tier-3 reopen visual plumbing: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z`
- Final Edge cleanup after reopen proof: `/v1/windows` Edge/Chrome widget count `0`
- Mutation approval gate for this exact request shape:
  - same request with `allowDeckMutation=false` returned HTTP 422 `powerpoint_validation_failed`
  - detail: `allowDeckMutation must be true for executable jobs or template prepare/cleanup because PowerPoint Online changes are saved to the deck.`
  - `GET /v1/powerpoint/jobs/ppt-mutation-proof-gate-20260704t0937z` returned HTTP 404
  - `/v1/windows` Edge/Chrome widget count stayed `0`
- Template target placement guard:
  - Host service now selects/verifies `evidenceSlideNumber` before `Prepare Template`
  - slide mismatch returns `blockedSession`, `jobRecord.status=notQueued`, no enqueue, no prepare, and no template cleanup
  - Windows Host tests passed 84/84: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T093842Z-653796/result.json`
- Post-cleanup visual proof:
  - when `cleanupTemplate=true`, `capture=true`, and cleanup save is verified, high-level update captures `powerpoint-online-template-cleanup` evidence before final session cleanup
  - Windows Host tests passed 84/84: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T094303Z-659361/result.json`
- Evidence slide guard:
  - requested evidence slide mismatch stops screenshot capture and records `evidence_slide_select_failed`
  - final evidence mismatch returns `blockedSession`; reopen evidence mismatch returns `verificationFailed`
  - Windows Host tests passed 86/86: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T094709Z-662877/result.json`
- Reopen success guard:
  - reopened sessions and reopened evidence must have both `success=true` and `status=ready`
  - tier-3 proof is not claimed for `status=ready` when `success=false`
  - Windows Host tests passed 87/87: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095018Z-665960/result.json`
- Final request contract guard:
  - Core serialization now round-trips the exact final proof request shape: `prepareTemplate=true`, `replaceText TITLE_MAIN`, `verifyReopen=true`, `cleanupTemplate=true`, `cleanupSession=true`, and `allowDeckMutation=true`
  - Windows Core tests passed 26/26: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095258Z-669478/result.json`
- OpenAPI contract guard:
  - `/v1/powerpoint/online/updates` is pinned to `PowerPointOnlineUpdateRequest` and `PowerPointOnlineUpdateResult`
  - request schema exposes final proof fields including `allowDeckMutation`, `prepareTemplate`, `cleanupTemplate`, `verifyReopen`, `evidenceSlideNumber`, and `cleanupSession`
  - result schema exposes proof sessions and `saveProofTier`
  - Windows Core tests passed 27/27: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095611Z-675280/result.json`
- Generated Go client guard:
  - `clients/go/windowsoperator_contract_test.go` constructs and marshals the final proof request shape through generated Go types
  - it also compile-checks `PowerPointOnlineUpdateResult` proof session fields and `tier3ReopenVisual`
  - generation reran with `scripts/generate-go-client.sh`
  - `go test ./...` passed in `clients/go`
  - Windows Core tests still passed 27/27 after generation: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095930Z-680875/result.json`
- Final proof runner guard:
  - `scripts/linux/powerpoint-online-final-proof.py` assembles the exact proof request, writes run artifacts, and refuses to POST unless `--execute --allow-deck-mutation` are present
  - SEM27 execution additionally requires `--allow-sem27`
  - current no-POST SEM27 request artifact: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-prepared-sem27-20260704t110448z/summary.json`
  - dry-run request artifact proof: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-script-dryrun-20260704t1008z/summary.json`
  - SEM27 no-POST gate proof: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-script-sem27-gate-20260704t1008z/summary.json`
  - `python3 -m py_compile scripts/linux/powerpoint-online-final-proof.py` passed
- Final proof Host gate runner:
  - `--verify-host-gate` now sends the final executable proof shape with `allowDeckMutation=false` and expects Host validation to reject before opening Edge
  - live SEM27 gate proof returned HTTP 422 `powerpoint_validation_failed` with Edge-like window count `0` before and after
  - evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t1013z/summary.json`
- No-queue Host gate proof:
  - `--verify-host-gate` now also reads `/v1/powerpoint/jobs/{runId}` and requires HTTP 404
  - live SEM27 gate proof returned HTTP 422 `powerpoint_validation_failed`, job lookup HTTP 404 `powerpoint_job_not_found`, and Edge-like window count `0` before and after
  - evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t1019z/summary.json`
- Runner self-tests:
  - `scripts/linux/powerpoint-online-final-proof-tests.sh` covers final request shape construction, tier-3 proof classifier requirements, Host gate classifier requirements, SEM27 URL detection, dry-run artifact writing, mutual exclusion gate, and SEM27 no-approval gate
  - `scripts/linux/powerpoint-online-final-proof-tests.sh` passed
  - `python3 -m py_compile scripts/linux/powerpoint-online-final-proof.py` passed
- Just command surface:
  - `just ppt-final-proof-test` runs the final proof runner self-tests
  - `just ppt-final-proof-prepare <deck_url>` writes final proof request artifacts without posting
  - `just ppt-final-proof-host-gate` runs the SEM27 no-mutation Host gate proof
  - `just ppt-final-proof-readiness` runs the SEM27 non-mutating Office.js/save/reopen readiness proof
  - `just --list`, `just ppt-final-proof-test`, `just ppt-final-proof-prepare <deck_url>` with a temp exchange root, and `just ppt-final-proof-host-gate` passed
  - Latest Just Host gate evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t105756z/summary.json`
- Final proof readiness runner:
  - `just ppt-final-proof-readiness` live-tested SEM27 without mutation
  - result: HTTP 200, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`, `session.currentSlide=4`, `verificationSession.currentSlide=4`, `sessionCleanupStatus=closed`
  - final `/v1/windows` Edge/Chrome-like count was `0`
  - evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/summary.json`
  - screenshots:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/screenshots/powerpoint-online-update.png`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z-verification/screenshots/powerpoint-online-update.png`
- Final proof classifier hardening:
  - mutating proof success now requires HTTP 200 in addition to tier-3 proof, `TITLE_MAIN` replacement/discovery, template prepare/cleanup, verification, and session cleanup
  - readiness proof success now requires HTTP 200, `verificationStatus=ready`, `sessionCleanupStatus=closed`, `officejs-taskpane`, tier-3 proof, evidence count >= 2, and final Edge/Chrome-like window count `0`
  - evidence counts now distinguish raw evidence entries from successful screenshot evidence; proof requires valid image artifacts with `success=true`, image media type, positive byte count, and an artifact path
  - evidence verification now also checks the Linux-visible artifact file exists, is a regular file, has positive size, and matches the reported artifact byte count
  - evidence verification now also requires enough distinct verified artifact paths, preventing duplicate rows for the same screenshot from satisfying the proof
  - verified evidence now checks file headers for declared screenshot type: PNG signature for `image/png`, JPEG SOI for `image/jpeg`; unsupported image subtypes are not counted as verified proof
  - the latest live readiness response reclassifies with `successfulEvidenceCount=2`, `verifiedEvidenceCount=2`, and `distinctVerifiedEvidenceCount=2`; both verified artifacts are Linux-visible PNG files with matching byte counts, so it still passes the stricter predicate
  - `python3 -m py_compile scripts/linux/powerpoint-online-final-proof.py`, `scripts/linux/powerpoint-online-final-proof-tests.sh`, and `git diff --check` passed

Final mutating SEM27 proof passed after explicit approval:

- summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/response.json`
- result: HTTP `200`, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`
- target proof: `TITLE_MAIN` discovered and replaced; `HERO_IMAGE` discovered as editable image target
- session proof: template prepare `ready`, reopened verification `ready`, template cleanup `ready`, final session cleanup `closed`
- evidence proof: three successful, Linux-visible, distinct PNG artifacts
- visual proof:
  - initial edit: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
  - reopened persistence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
  - cleanup: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`

Residual unproven item: tier-4 SharePoint/Graph version-history proof. Current V1 proof level is tier-3 visual reopen plus saved-state evidence.
