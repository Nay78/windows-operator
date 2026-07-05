# PowerPoint Online Final Mutation Approval Packet

Date: 2026-07-05

## Purpose

Use this packet to authorize the final tier-3 proof that the harness can edit a SharePoint-hosted PowerPoint deck, observe save, reopen the deck, verify the visible edit by screenshot, clean up the test targets, and close Edge.

This was the final completion gate in `.work/powerpoint-online-editing-harness-completion-audit.md`. SEM27 approval was granted once on 2026-07-05 and the final proof passed.

## Completed SEM27 Proof

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- Initial screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
- Cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`
- Final `/v1/windows` Edge/Chrome-like window filter: `[]`
- Runner elapsed time: 343.19s; default HTTP timeout now 420s.

## Preferred Approval

Provide a disposable SharePoint `.pptx` URL:

```text
Use this disposable deck for the final PowerPoint mutation proof: <DECK_URL>
```

Codex will run:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url '<DECK_URL>' \
  --run-id "ppt-mutation-proof-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
  --execute \
  --allow-deck-mutation
```

## SEM27 Approval

Only use this if mutating the production SEM27 deck is acceptable:

```text
I explicitly approve running the guarded mutating PowerPoint proof against SEM27.
I understand it may create SharePoint version history even if visible test shapes are cleaned up.
```

Codex will run:

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
  --run-id "ppt-mutation-proof-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
  --execute \
  --allow-deck-mutation \
  --allow-sem27
```

## What The Proof Writes

The proof uses the Office.js add-in, not browser DOM mutation.

Sequence:

1. Open deck in PowerPoint Online.
2. Select and verify slide 4.
3. Click `Prepare Template`, creating temporary `TITLE_MAIN` and `HERO_IMAGE` targets.
4. Run an Office.js `replaceText` operation on `TITLE_MAIN`.
5. Wait for PowerPoint Online `saved`.
6. Capture slide 4 screenshot.
7. Reopen deck and capture slide 4 screenshot again.
8. Click `Cleanup Template`, deleting only shapes tagged with the add-in `TARGET_ID`.
9. Wait for save, capture post-cleanup screenshot, close session.

## Required Success Evidence

`scripts/linux/powerpoint-online-final-proof.py` must exit `0` and `summary.json` must show:

- HTTP `200`
- `success=true`
- `status=succeeded`
- `saveProofTier=tier3ReopenVisual`
- `jobStatus=succeeded`
- `claimedBy=officejs-taskpane`
- `titleMainTargetSucceeded=true`
- `titleMainDiscovered=true`
- at least three successful, Linux-visible, distinct PNG/JPEG evidence artifacts
- `templatePreparationStatus=ready`
- `verificationStatus=ready`
- `templateCleanupStatus=ready`
- `sessionCleanupStatus=closed`
- final Edge/Chrome-like window count `0`

Human visual inspection must confirm:

- initial screenshot shows proof text on slide 4
- reopened screenshot shows same proof text on slide 4
- post-cleanup screenshot no longer shows temporary test targets

## Safe Checks Already Proven

- No-POST SEM27 final request artifact prepared:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-prepared-sem27-20260704t110448z/summary.json`
- Non-mutating readiness proof passed:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/summary.json`
- Mutation gate rejects SEM27 proof without approval:
  `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t105756z/summary.json`
- Dev JS harness restored to disabled default after live smoke; latest direct check returned HTTP `422 dev_automation_disabled`.
- Current Edge/PowerPoint window filter is `[]`.

## Failure Handling

If proof fails after template preparation or edit:

1. Inspect `summary.json`, `response.json`, and screenshot evidence under the run root.
2. If a browser session remains, call:

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/<sessionId>/cleanup
```

3. If cleanup status is not `ready` or screenshots show leftover test targets, reopen the run session or deck and use the add-in `Cleanup Template` command.
4. Record failure class in roadmap:
   - `blockedSession`
   - `blockedAddIn`
   - `saveUnverified`
   - `verificationFailed`
   - `cleanupFailed`
   - `sessionCleanupFailed`

Do not mark the goal complete unless the required success evidence above is present.
