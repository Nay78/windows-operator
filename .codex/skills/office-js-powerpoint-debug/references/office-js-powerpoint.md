# Office.js PowerPoint Online Field Notes

Keep entries short and evidence-backed. Add a dated entry whenever Office.js or PowerPoint Online behavior changes implementation choices.

## 2026-07-04

### Observed behavior

Historical blocker: PowerPoint Online add-in activation was the main live blocker. The SEM27 zero-op discovery run reached the deck but did not queue the Office.js job because the `Run Update` command was not visible in the current Edge work profile.

### Implication

Typed update/discovery APIs are still preferred, but live debugging needs named DevTools scripts to inspect visible ribbon commands, taskpane frames, save indicators, and page DOM without mutating the deck.

### Working command/script

Use named dev scripts after starting/reusing a PowerPoint Online session:

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/dev/powerpoint/online/sessions/SESSION_ID/script \
  -H 'content-type: application/json' \
  -d '{"scriptId":"ppt.ribbon.commands","timeoutSeconds":5,"captureScreenshot":true}'
```

Raw JS remains last resort and requires dev automation enabled
(`WINDOWS_OPERATOR_DEV_AUTOMATION=1` or `DevAutomation:Enabled=true`), raw JS
allowed (`WINDOWS_OPERATOR_DEV_RAW_JS=1` or `DevAutomation:AllowRawJs=true`),
plus request body `allowUnsafeRawJs=true`.

### Evidence path

Historical blocked add-in evidence before activation recovery: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-discover-targets-live-20260704t080557/summary.json`.

## 2026-07-04 Dev JS harness smoke

### Observed behavior

SEM27 loaded in PowerPoint Online as a ready session with slide count 71, edit mode `editing`, and save state `saved`. `ppt.dom.snapshot` saw the top document and the `WacFrame_PowerPoint_0` iframe, but `contentDocument` was unavailable from the evaluated page context. `ppt.ribbon.commands` succeeded but returned `commandCount: 0` from the top document even though the screenshot showed the normal PowerPoint ribbon.

### Implication

Top-page scripts are useful for URL/title/frame/window proof and screenshots. Ribbon and slide internals may require frame-aware CDP execution, UIA, Office.js add-in context, or a dedicated WAC target rather than plain top-document DOM queries.

### Working command/script

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/dev/powerpoint/online/sessions/dev-js-sem27-20260704t0853z/script \
  -H 'content-type: application/json' \
  -d '{"scriptId":"ppt.dom.snapshot","timeoutSeconds":8,"captureScreenshot":false}'

curl -sS -X POST http://127.0.0.1:43117/v1/dev/powerpoint/online/sessions/dev-js-sem27-20260704t0853z/script \
  -H 'content-type: application/json' \
  -d '{"scriptId":"ppt.ribbon.commands","timeoutSeconds":8,"captureScreenshot":true,"label":"dev-js-ribbon2"}'
```

### Evidence path

- DOM audit: `/var/lib/windows-server/shared/operator-exchange/runs/dev-js-dev-js-sem27-20260704t0853z/ppt.dom.snapshot-20260704t085728953z.json`
- Ribbon audit: `/var/lib/windows-server/shared/operator-exchange/runs/dev-js-dev-js-sem27-20260704t0853z/ppt.ribbon.commands-20260704t085735819z.json`
- Screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/dev-js-dev-js-sem27-20260704t0853z/screenshots/dev-js-ribbon2.png`

## 2026-07-04 Office.js Discovery Recovered

### Observed behavior

SEM27 add-in activation succeeded in the work profile via `Home` overflow `Updater -> Run Update`. The task pane exposed `Prepare Template`, `Cleanup Template`, and `Run Pending Job`. A one-call high-level update request starting from `deckUrl`, with `discoverTargets=true`, `validateOnly=true`, and no operations was claimed by `officejs-taskpane`, completed with `status=succeeded`, returned `discoveredTargets=[]`, observed `saveState=saved`, selected slide 4, captured evidence, and cleaned up the only Edge window.

### Implication

The add-in runtime and Host job bridge can execute non-mutating Office.js against the live SharePoint deck. Empty discovery showed SEM27 had no preexisting binding-backed harness targets at that point. The later mutating proof used add-in-created targets after explicit approval because template preparation writes to the SharePoint file and may leave version history.

### Working command/script

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/powerpoint/online/updates \
  -H 'content-type: application/json' \
  -d '{
    "deckUrl":"https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
    "sessionId":"ppt-onecall-discovery-20260704t0908z",
    "job":{
      "jobId":"ppt-onecall-discovery-20260704t0908z",
      "discoverTargets":true,
      "validateOnly":true,
      "operations":[],
      "requestedBy":"codex-live-proof"
    },
    "evidenceSlideNumber":4,
    "capture":true,
    "allowDeckMutation":false,
    "cleanupSession":true
  }'
```

### Evidence path

- Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z`
- Screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z/screenshots/powerpoint-online-update.png`

## 2026-07-04 Slide Navigation Verification

### Observed behavior

On live SEM27, top-page DOM selectors could not find a visible slide thumbnail for slide 4, so the harness used the thumbnail coordinate fallback. UIA then observed `currentSlide=4`, `slideCount=71`, edit mode `editing`, and save state `saved`.

### Implication

DOM dispatch alone is not reliable proof of slide selection in PowerPoint Online. Slide selection must be post-click verified with UIA, use bounded keyboard correction for nearby slide mismatches, and fail structured when the observed slide still differs from the requested slide.

### Working command/script

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"slideNumber":4,"capture":true,"waitSeconds":2,"label":"slide-nav-4"}' \
  http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-slide-nav-20260704t0921z/slides/select
```

### Evidence path

- Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z`
- Screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z/screenshots/slide-nav-4.png`
- Windows focused test run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T091903Z-619656/result.json`

## 2026-07-04 Non-Mutating Tier-3 Reopen Proof

### Observed behavior

SEM27 completed a zero-operation Office.js discovery job with `verifyReopen=true`. The add-in claimed and succeeded the job, PowerPoint Online returned `saved`, the harness selected/captured slide 4, closed the session, reopened the deck, selected/captured slide 4 again, and closed the final Edge session. The result reported `saveProofTier=tier3ReopenVisual` and final Edge/Chrome widget count was `0`.

### Implication

Reopen visual proof plumbing works live when the Office.js job is non-mutating. This proved session cleanup/reopen/evidence mechanics, but not edit persistence. The later mutating proof closed the edit-persistence gap for add-in-created targets.

### Working command/script

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"deckUrl":"SEM27_URL","sessionId":"ppt-onecall-reopen-discovery-20260704t0928z","job":{"jobId":"ppt-onecall-reopen-discovery-20260704t0928z","discoverTargets":true,"validateOnly":true,"operations":[],"requestedBy":"codex-live-reopen-proof"},"evidenceSlideNumber":4,"capture":true,"allowDeckMutation":false,"verifyReopen":true,"reopenWaitSeconds":40,"cleanupSession":true,"openWaitSeconds":40,"jobTimeoutSeconds":60,"saveTimeoutSeconds":30,"savePollSeconds":1}' \
  http://127.0.0.1:43117/v1/powerpoint/online/updates
```

### Evidence path

- First run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z`
- Reopen run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification`
- First screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification/screenshots/powerpoint-online-update.png`

## 2026-07-04 Final Proof Readiness

### Observed behavior

The reusable final proof readiness command completed against SEM27 without mutation. It sent `discoverTargets=true`, `validateOnly=true`, no operations, `allowDeckMutation=false`, `verifyReopen=true`, and `cleanupSession=true`. Office.js claimed the job as `officejs-taskpane`, returned `status=succeeded`, captured slide 4 before and after reopen, reported `saveProofTier=tier3ReopenVisual`, closed the final Edge session, and produced two distinct Linux-visible PNG evidence files with matching byte counts.

### Implication

This proved the profile/add-in/save/reopen path immediately before mutating proof approval. By itself it did not prove edit persistence because it intentionally avoided template preparation and deck mutation; the 2026-07-05 SEM27 mutating proof below closes that gap for add-in-created targets.

### Working command/script

```bash
just ppt-final-proof-readiness
```

### Evidence path

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/summary.json`
- Initial screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z-verification/screenshots/powerpoint-online-update.png`

## 2026-07-05 Mutating Tier-3 Reopen Proof

### Observed behavior

After explicit SEM27 mutation approval, the high-level update route completed the full Office.js path against the live SharePoint deck. Template preparation created `TITLE_MAIN` and `HERO_IMAGE` on slide 4, `replaceText` changed `TITLE_MAIN` to `Windows Operator live edit proof 2026-07-05T01:03:20.183225Z`, PowerPoint Online reported `saved`, the deck reopened with the proof text still visible on slide 4, `Cleanup Template` removed both temporary targets, and final Edge/Chrome-like window count was `0`.

### Implication

The V1 harness now has live proof for SharePoint-hosted deck mutation through Office.js plus tier-3 visual reopen persistence and cleanup. Keep using add-in-created tagged targets for reliable mutation proof; arbitrary existing object targeting and tier-4 SharePoint/Graph version proof remain outside this proven path.

### Working command/script

```bash
scripts/linux/powerpoint-online-final-proof.py \
  --deck-url 'SEM27_URL' \
  --run-id ppt-mutation-proof-sem27-long-20260705t010320z \
  --execute \
  --allow-deck-mutation \
  --allow-sem27 \
  --http-timeout-seconds 420
```

The run took 343.19 seconds, so the proof runner default HTTP timeout is 420 seconds.

### Evidence path

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- Initial screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
- Cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`

## 2026-07-05 Table Editing Tier-3 Reopen Proof

### Observed behavior

SEM27 table editing succeeded through the high-level PowerPoint Online update
route. Template preparation created `DATA_TABLE`; `readTable` returned the
initial 3x3 values `Metric/Plan/Actual`, `Tonnes/0/0`, and
`Availability/0%/0%`; `replaceTableCell` and `replaceTableRange` wrote
`67 kt`, `101%`, and `103%`. PowerPoint Online reported `saved`, the deck
reopened with the table values still visible on slide 4, `Cleanup Template`
removed the temporary table, and final Edge/Chrome-like window count was `0`.

During earlier one-call attempts, the task pane `Run Pending Job` button was
visible in screenshot evidence while UIA sometimes missed the button node. The
Agent now exposes a typed run-pending route and uses a narrow sibling-geometry
fallback from visible `Cleanup Template` or `Prepare Template` buttons. Host
marks a queued job failed with `ADDIN_RUN_COMMAND_FAILED` when the run-pending
command fails after enqueue, preventing stale queued jobs from being claimed by
later sessions.

### Implication

Binding/tag-backed PowerPoint tables are first-class harness targets for
reading, single-cell writes, rectangular range writes, save wait, reopen visual
proof, and cleanup. Arbitrary unbound table discovery/editing is still outside
the proven path.

### Working command/script

```bash
curl -sS -X POST http://127.0.0.1:43117/v1/powerpoint/online/updates \
  -H 'content-type: application/json' \
  -d '{"deckUrl":"SEM27_URL","sessionId":"ppt-table-onecall-sem27-20260705t0453z","job":{"jobId":"ppt-table-onecall-sem27-20260705t0453z","discoverTargets":true,"operations":[{"kind":"readTable","targetId":"DATA_TABLE"},{"kind":"replaceTableCell","targetId":"DATA_TABLE","rowIndex":1,"columnIndex":1,"text":"67 kt"},{"kind":"replaceTableRange","targetId":"DATA_TABLE","startRowIndex":2,"startColumnIndex":1,"values":[["101%","103%"]]}],"requestedBy":"codex-live-table-proof"},"evidenceSlideNumber":4,"capture":true,"allowDeckMutation":true,"prepareTemplate":true,"verifyReopen":true,"cleanupTemplate":true,"cleanupSession":true,"openWaitSeconds":40,"jobTimeoutSeconds":90,"saveTimeoutSeconds":45,"savePollSeconds":1,"reopenWaitSeconds":40}'
```

### Evidence path

- Response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`
- Initial table screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`
- Reopened table screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`
- Cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`

## 2026-07-05 Named Template Target Repair Proof

### Observed behavior

SEM27 named-target repair succeeded through typed PowerPoint Online endpoints.
`Prepare Named Targets` created mock shapes with only
`shape.name = TARGET_<TARGET_ID>`. A high-level update with
`bindNamedTargets=true` repaired `TITLE_MAIN` and `DATA_TABLE` into
binding/tag targets, changed the title text, read the initial 3x3 table, wrote
`82 kt`, `111%`, and `113%`, reached `saveProofTier=tier3ReopenVisual`, and
returned four operation results with `source=repairedName`, `shapeName`,
`bound=true`, and `tagged=true`. The reopened verification screenshot captured
slide 4 after save/reopen.

Cleanup on the reopened verification session failed until the add-in task pane
was activated again. Running the typed add-in probe with `activateIfNeeded=true`
before `Cleanup Named Targets` made cleanup succeed, cleanup save reached
`saved`, both sessions closed, and final Edge/Chrome-like window delta was `0`.

### Implication

Authored `TARGET_<TARGET_ID>` shape names are now first-class template targets
for text and table operations when callers opt into `bindNamedTargets=true` and
`allowDeckMutation=true`. On a newly reopened verification session, do not call
named cleanup cold; probe/activate the add-in first.

### Working command/script

Use typed endpoints in this order:

```text
POST /v1/powerpoint/online/sessions
POST /v1/powerpoint/online/sessions/{sessionId}/slides/select
POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe
POST /v1/powerpoint/online/sessions/{sessionId}/template/prepare
POST /v1/powerpoint/online/updates
POST /v1/powerpoint/online/sessions/{verificationSessionId}/addin/probe
POST /v1/powerpoint/online/sessions/{verificationSessionId}/template/cleanup
POST /v1/powerpoint/online/sessions/{verificationSessionId}/save/wait
POST /v1/powerpoint/online/sessions/{verificationSessionId}/cleanup
```

The update request set `bindNamedTargets=true`, `allowDeckMutation=true`,
`verifyReopen=true`, and operations for `TITLE_MAIN` and `DATA_TABLE`.

### Evidence path

- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/summary.json`
- Update response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/06-update-response.json`
- Initial screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z-verification/screenshots/powerpoint-online-update.png`
- Cleanup proof response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-named-target-sem27-final-20260705t074928z/08-cleanup-response.json`
