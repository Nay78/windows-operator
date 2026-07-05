# PowerPoint Online Editing Harness Roadmap

Date: 2026-07-03

Last curated: 2026-07-05. Current docs entry point:
`.work/powerpoint-online-docs-index.md`.

This file is a design/history record. Use the docs index and completion audit for
current completion status and final proof evidence.

## Purpose

Build a reliable harness for editing SharePoint-hosted PowerPoint decks through a Windows VM, with PowerPoint Online as the visible document runtime and Office.js as the preferred mutation runtime.

The harness should let callers say: open this deck, go to this slide, apply these edits, wait until the deck is saved, and return evidence. Callers should not coordinate Edge sessions, click coordinates, task panes, add-in polling, artifact roots, or save-state retries.

## Current Progress

Working pieces:

- Windows Operator Host is reachable at `http://127.0.0.1:43117`.
- Edge work-profile sessions can open SharePoint PowerPoint Online decks.
- Browser/desktop primitives exist:
  - `POST /v1/browser/edge/open-url`
  - `POST /v1/browser/edge/session/start`
  - `GET /v1/browser/edge/session/{sessionId}/state`
  - `POST /v1/browser/edge/session/{sessionId}/navigate`
  - `POST /v1/browser/edge/session/{sessionId}/dom/click`
  - `POST /v1/browser/edge/session/{sessionId}/dom/fill`
  - `POST /v1/browser/edge/session/{sessionId}/screenshot`
  - `POST /v1/input/click`
  - `POST /v1/input/hotkey`
  - `POST /v1/uia/*`
- The VM opened this deck in PowerPoint Online editing mode:
  - `https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`
- Slide 4 was selected and captured through the VM:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide4/screenshots/slide4.png`
- Existing PowerPoint job queue works as a lower-level mutation contract:
  - `POST /v1/powerpoint/jobs`
  - `POST /v1/powerpoint/jobs/claim`
  - `POST /v1/powerpoint/jobs/{jobId}/complete`
  - `POST /v1/powerpoint/jobs/{jobId}/fail`
  - `GET /v1/powerpoint/jobs/{jobId}`
  - `GET /v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}`
- Host PowerPoint queue live probe passed on 2026-07-03: enqueue, artifact fetch, claim, fail, get-final all returned `200`.
- Add-in code supports `replaceText`, `replaceImage`, `readTable`,
  `replaceTableCell`, and `replaceTableRange` through `PowerPoint.run`.
- Add-in tests, typecheck, build, and manifest validation pass.
- Dedicated PowerPoint Online domain session API exists:
  - open/reuse session
  - select slide
  - screenshot
  - cleanup
- Live session observation now reports `currentSlide`, `slideCount`, `editMode`, and `saveState` from UI Automation.
- Live Phase 2 proof on 2026-07-03 selected slide 4 of the SEM27 deck and returned `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`.
- High-level `/v1/powerpoint/online/updates` exists and safely composes session open/reuse, job binding, queueing, timeout handling, and screenshot evidence.
- Add-in preflight route exists:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe`
  - Live proof returned `status=blockedActivation`, `hostReachable=true`, `taskPaneVisible=false`, `commandVisible=true` for the SEM27 deck.
- Add-in package diagnostics now split local package health from tenant/session activation:
  - task pane static content is probed at `https://localhost:3003/taskpane.html`.
  - manifest XML is probed at `https://localhost:3003/manifest.xml`.
  - result fields include `taskPaneUrl`, `taskPaneReachable`, `manifestUrl`, `manifestReachable`, `manifestId`, `manifestVersion`, `manifestDisplayName`, and `manifestSourceLocation`.
  - Live VM proof on 2026-07-03 for the SEM27 deck returned `taskPaneReachable=true`, `manifestReachable=true`, `manifestId=6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7`, `manifestVersion=1.0.0.0`, `manifestDisplayName=Windows Operator PowerPoint`, `manifestSourceLocation=https://localhost:3003/taskpane.html`, but `taskPaneVisible=false`.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`.
- High-level update route now runs add-in preflight before enqueue and returns `blockedAddIn` with `jobRecord.status=notQueued` when the task pane is absent.
- Add-in activation attempt is now automated behind the probe:
  - callers opt in with `activateIfNeeded=true`
  - high-level `/v1/powerpoint/online/updates` opts in before enqueue
  - the Agent clicks Home first when the Add-ins command is offscreen, because live PowerPoint Online exposes the Add-ins button from the Home ribbon for this deck
  - Insert and ribbon overflow remain fallback reveal paths
  - focused Windows VM tests passed: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123826Z-1948565/result.json`
  - live SEM27 proof returned `blockedActivation` after `addin_activation_home_tab_click_dispatched`, `addin_activation_click_dispatched`, and `addin_activation_timeout`
  - evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-first-live-20260703t124019917053669z/summary.json`
  - live UIA proof showed Home makes `InsertAddInFlyout` visible, then Home > Add-ins opens a flyout with `Advanced...`, and `Advanced...` opens the Office Add-ins dialog with `Upload My Add-in`
  - probe diagnostics preserve initially observed activation candidates even when Insert/overflow reveal loses them; live proof preserved offscreen `Add-ins` group/button candidates with automation id `InsertAddInFlyout`.
- The sideload UI path is now mapped live:
  - `Home` tab -> `Add-ins` -> `Advanced...` -> `Upload My Add-in` -> `Browse...`
  - the Windows file picker accepted `Z:\windows-operator\src\WindowsOperator.PowerPointAddIn\manifest.xml`
  - the Upload button enabled and the click returned HTTP 200
  - no `Windows Operator PowerPoint` task pane or installed command appeared after upload
  - evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-reveal-20260703t12211783081291z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-addins-click-20260703t12221783081362z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-advanced-click-20260703t12231783081432z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-upload-click-20260703t12251783081546z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-browse-picker-20260703t12271783081649z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-sideload-attempt-20260703t12291783081743z/summary.json`
- Installed add-in command activation is now live:
  - actual launch command appears under Home overflow as `Updater` -> `Run Update`
  - activation uses top-biased clicking for bottom-clipped menu items and retries transient UIA `0x80040201`
  - generic `Add-ins` fallback is avoided for real activation runs when `Run Update` is unavailable
  - live ready proof: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-ready-live-20260703t131943894048445z/summary.json`
- High-level update route now triggers the add-in taskpane after enqueue:
  - Host injects `IOperatorFacade` into `PowerPointOnlineUpdateService`
  - after `job_enqueued`, it finds/clicks visible `Run Pending Job`
  - focused Host tests prove matched-element and UIA-query discovery paths
- Host and add-in document URL guards now normalize SharePoint canonical `_layouts/Doc.aspx` URLs against Office document paths:
  - `sourcedoc` exact match when available
  - same host + filename fallback for canonical/file-path variants
  - live SEM27 claim used canonical expected URL and Office.js claimed with `claimedDocumentUrl=https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27 - Plan Semanal Servicios Mina.pptx`
- Office.js validate-only claim path is live end-to-end:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-validate-20260703t134919145483350z/summary.json`
  - `/v1/powerpoint/online/updates` opened SEM27, activated `Run Update`, enqueued a validate-only job, clicked `Run Pending Job`, and Office.js claimed the job as `officejs-taskpane`
  - expected result was `status=failed` with target error `TARGET_NOT_FOUND` for synthetic target `codex_missing_target`
  - cleanup returned `status=closed`, final Edge process count `0`
- Office.js non-mutating discovery path is live end-to-end again after profile/add-in activation recovered:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-state-20260704t0904z`
  - `/v1/powerpoint/online/sessions` opened SEM27 in one Edge window.
  - `/v1/powerpoint/online/sessions/{sessionId}/addin/probe` activated `Run Update` and observed the task pane.
  - `/v1/powerpoint/online/updates` reused the session, queued `discoverTargets=true` and `validateOnly=true` with no operations, clicked `Run Pending Job`, and Office.js claimed/completed the job as `officejs-taskpane`.
  - result was `status=succeeded`, `saveProofTier=tier2SavedIndicator`, `discoveredTargets=[]`, `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`.
  - screenshot evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-state-20260704t0904z/screenshots/powerpoint-online-update.png`
  - cleanup returned `status=closed`; final `/v1/windows` Chrome widget count was `0`.
- One-call high-level non-mutating discovery proof now works from deck URL:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z`
  - request used `deckUrl`, `discoverTargets=true`, `validateOnly=true`, `allowDeckMutation=false`, `evidenceSlideNumber=4`, `capture=true`, and `cleanupSession=true`.
  - `/v1/powerpoint/online/updates` opened SEM27, activated the add-in, queued the Office.js job, clicked `Run Pending Job`, waited for `saved`, selected slide 4, captured evidence, and closed the session.
  - result was `success=true`, `status=succeeded`, `saveProofTier=tier2SavedIndicator`, `claimedBy=officejs-taskpane`, `discoveredTargets=[]`, `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`.
  - screenshot evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z/screenshots/powerpoint-online-update.png`
  - final `/v1/windows` Chrome widget count was `0`.
- One-call high-level non-mutating reopen proof now works from deck URL:
  - run roots `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z` and `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification`
  - request used `deckUrl`, `discoverTargets=true`, `validateOnly=true`, `allowDeckMutation=false`, `verifyReopen=true`, `evidenceSlideNumber=4`, `capture=true`, and `cleanupSession=true`.
  - `/v1/powerpoint/online/updates` opened SEM27, activated the add-in, completed the Office.js discovery job, observed `saved`, selected/captured slide 4, closed the first session, reopened the deck, selected/captured slide 4 again, and closed the final session.
  - result was `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`, `discoveredTargets=[]`, `session.currentSlide=4`, `verificationSession.currentSlide=4`, and two screenshot evidence artifacts.
  - screenshots:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z/screenshots/powerpoint-online-update.png`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification/screenshots/powerpoint-online-update.png`
  - final `/v1/windows` Edge/Chrome widget count was `0`.
- Slide selection is now post-click verified:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select` observes UIA state after DOM/thumbnail selection and fails with a structured PowerPoint error when the observed slide differs from the requested slide.
  - If a thumbnail click lands on a nearby observed slide, the Agent sends bounded `pageup`/`pagedown` correction keys and verifies the final slide.
  - Focused Windows Agent tests passed 28/28: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T091903Z-619656/result.json`.
  - Live SEM27 proof selected slide 4 with top-page DOM unavailable, thumbnail fallback, UIA verification, and one screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z/screenshots/slide-nav-4.png`.
  - cleanup returned `status=closed`; final `/v1/windows` Edge/Chrome widget count was `0`.
- Add-in template targets are now reversible:
  - task pane exposes `Cleanup Template` beside `Prepare Template` and `Run Pending Job`
  - cleanup deletes only bound shapes carrying the matching `TARGET_ID` tag, then removes the binding
  - authored or mismatched shapes are skipped instead of deleted
- Domain template lifecycle endpoints now exist:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/template/prepare`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup`
  - callers provide `capture`, `waitSeconds`, `allowDeckMutation`, and `label`; UIA button lookup/click mechanics stay inside the Agent service
  - direct prepare/cleanup calls are rejected unless `allowDeckMutation=true`
  - OpenAPI and Go client were regenerated with `PowerPointOnlineTemplateRequest`
- Live one-tab add-in readiness proof on SEM27:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-cleanup-button-20260703t1401z`
  - `POST /v1/powerpoint/online/sessions` opened one Edge session; startup observed one page target
  - `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select` selected slide 4 and observed `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` activated `Run Update` and observed task pane controls `Prepare Template`, `Cleanup Template`, and `Run Pending Job`
  - `/cleanup` returned `status=closed`; final window filter was `[]` and direct Windows `msedge_count=0`
- Live safe negative proof for the new template cleanup endpoint:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-template-endpoint-negative-20260704t0653z`
  - live `/openapi.json` exposed both template lifecycle paths
  - one SEM27 Edge session opened with startup target count `1`
  - slide 4 was selected: `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`
  - add-in probe ran with `activateIfNeeded=false` and returned `taskPaneVisible=false`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup` returned `success=false`, `status=ready`, action `template_cleanup_button_not_found`, and error `powerpoint_unavailable`, proving the endpoint routes and refuses to click when no visible taskpane button exists
  - cleanup returned `status=closed`; final window filter was `[]` and direct Windows `msedge_count=0`
- Save-state waiter route exists:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/save/wait`
  - Live VM proof on 2026-07-03 against the SEM27 deck returned `success=true`, `status=ready`, `saveState=saved`, action `save_wait_observed:saved`, and empty warnings/errors.
- High-level update orchestration now calls the save-state waiter after a succeeded Office.js job and returns `saveUnverified` if PowerPoint Online does not report `saved`.
- High-level update orchestration now supports optional reopen verification:
  - request fields `verifyReopen` and `reopenWaitSeconds`
  - result field `verificationSession`
  - status `verificationFailed`
  - on successful Office.js job plus saved state, it captures normal evidence, closes the current session, reopens the same document as `{sessionId}-verification`, selects the evidence slide, and captures reopened evidence
  - focused Linux and Windows VM tests passed; live SEM27 route proof with `verifyReopen=true` accepted the contract and blocked before reopen because add-in preflight failed
- High-level update orchestration now supports a guarded test-template proof path:
  - request fields `prepareTemplate`, `cleanupTemplate`, `cleanupTemplateOnFailure`, `templateWaitSeconds`, and `cleanupSession`
  - request field `allowDeckMutation`; executable jobs and high-level template prepare/cleanup are rejected unless this is true
  - result fields `templatePreparationSession`, `templateCleanupSession`, and `sessionCleanupSession`
  - statuses `cleanupFailed` and `sessionCleanupFailed`
  - when opted in, `/v1/powerpoint/online/updates` prepares known template bindings before enqueue, waits for save, re-probes the task pane for fresh controls, runs the Office.js job, optionally verifies reopen, then reactivates the add-in and cleans template targets with another save wait
  - optional final session cleanup closes the last active proof session after template cleanup, so one-call proof runs can prove no operator-owned Edge session remains
  - cleanup runs on terminal failures when `cleanupTemplate=true` and `cleanupTemplateOnFailure=true`
  - focused Host tests prove prepare-before-enqueue, cleanup-after-reopen, mutation approval rejection, validate-only allowance, and `cleanupFailed` reporting; Windows VM tests passed on 2026-07-04
  - high-level prepare now selects and verifies `evidenceSlideNumber` before clicking `Prepare Template`, so generated `TITLE_MAIN`/`HERO_IMAGE` targets land on the slide being edited and photographed; mismatch returns `blockedSession` before enqueue or mutation. Windows Host tests passed 84/84: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T093842Z-653796/result.json`
  - high-level cleanup now captures `powerpoint-online-template-cleanup` evidence after cleanup save is verified when `capture=true`, before optional final session cleanup; Windows Host tests passed 84/84: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T094303Z-659361/result.json`
  - high-level evidence capture now treats requested slide mismatch as proof failure instead of masking it with a later screenshot. Final evidence mismatch returns `blockedSession`; reopen evidence mismatch returns `verificationFailed`; post-cleanup evidence mismatch returns `cleanupFailed`. Windows Host tests passed 86/86: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T094709Z-662877/result.json`
  - reopen proof now requires both `success=true` and `status=ready` for the reopened session/evidence before claiming `tier3ReopenVisual`; Windows Host tests passed 87/87: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095018Z-665960/result.json`
  - Core contract tests now round-trip the exact final mutation proof request shape, including `replaceText` on `TITLE_MAIN`, template prepare/cleanup, reopen verification, session cleanup, and `allowDeckMutation=true`; Windows Core tests passed 26/26: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095258Z-669478/result.json`
  - OpenAPI contract tests now pin `/v1/powerpoint/online/updates` to `PowerPointOnlineUpdateRequest`/`PowerPointOnlineUpdateResult` and assert final proof request/result fields are present in generated schemas; Windows Core tests passed 27/27: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095611Z-675280/result.json`
  - Go client generation has been rerun, and a generated-client contract test now compiles/marshals the final proof request through `PowerPointOnlineUpdateRequest` plus proof-session response fields; `go test ./...` passed in `clients/go`, and Windows Core tests still passed 27/27: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T095930Z-680875/result.json`
  - Final proof runner now exists at `scripts/linux/powerpoint-online-final-proof.py`; it assembles the guarded request, writes request/response/window/summary artifacts, refuses to POST without `--execute --allow-deck-mutation`, and requires `--allow-sem27` for the SEM27 deck. Dry-run and SEM27 gate proofs wrote summaries under `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-script-dryrun-20260704t1008z` and `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-script-sem27-gate-20260704t1008z`.
  - The proof runner also has `--verify-host-gate`, which sends the final executable proof shape with `allowDeckMutation=false` and expects Host to return `422 powerpoint_validation_failed` before Edge opens. It now also verifies `GET /v1/powerpoint/jobs/{runId}` returns 404, proving the job was not queued. Live SEM27 proof on the restored default Agent runtime passed with Edge-like window count `0` before and after: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t105756z/summary.json`
  - Runner self-tests now cover final request shape construction, tier-3 proof classifier requirements, Host gate classifier requirements, SEM27 URL detection, dry-run artifact writing, mutual exclusion gate, and SEM27 no-approval gate; `scripts/linux/powerpoint-online-final-proof-tests.sh` and `python3 -m py_compile scripts/linux/powerpoint-online-final-proof.py` passed.
  - Repeatable Just targets now exist: `just ppt-final-proof-test`, `just ppt-final-proof-prepare <deck_url>`, `just ppt-final-proof-host-gate`, and `just ppt-final-proof-readiness`. Readiness runs a non-mutating SEM27 Office.js discovery job with reopen visual proof and final browser cleanup before mutation approval. `just --list`, `just ppt-final-proof-test`, `just ppt-final-proof-prepare <deck_url>` with a temp exchange root, and `just ppt-final-proof-host-gate` passed; the latest Just Host gate evidence is `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-host-gate-20260704t105756z/summary.json`.
  - Live `just ppt-final-proof-readiness` passed against SEM27 without mutation: HTTP 200, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`, slide 4 captured before and after reopen, cleanup closed, and final Edge/Chrome-like window count `0`. Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-final-proof-readiness-20260704t104319z/summary.json`.
  - Final proof runner classifiers now reject false positives where the response body looks successful but HTTP status is not 200, readiness reopen status is not `ready`, readiness cleanup is absent/not `closed`, screenshot evidence lacks successful image artifacts, those artifacts are not visible on Linux with the reported byte count, multiple evidence rows point to the same verified screenshot, or the local file header does not match declared PNG/JPEG media type. The latest live readiness response reclassifies with `successfulEvidenceCount=2`, `verifiedEvidenceCount=2`, and `distinctVerifiedEvidenceCount=2`, passing the stricter predicate with two distinct Linux-visible PNG files; runner tests/compile/diff-check passed.
  - Final mutating SEM27 proof passed after explicit approval on 2026-07-05. `scripts/linux/powerpoint-online-final-proof.py --execute --allow-deck-mutation --allow-sem27 --http-timeout-seconds 420` returned HTTP 200, `success=true`, `status=succeeded`, `saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`, `titleMainTargetSucceeded=true`, `titleMainDiscovered=true`, three successful/verified/distinct PNG evidence files, template cleanup `ready`, session cleanup `closed`, and final Edge/Chrome-like window count `0`. Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.
  - Visual inspection confirmed slide 4 showed the proof text in the initial screenshot, still showed it after reopening the deck, then no longer showed the temporary targets after cleanup. Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`, `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`, and `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`.
  - The first live mutating run completed the Office.js edit and reopened proof but exceeded the old 240s proof-runner HTTP timeout. The rerun completed in 343.19s, so the proof runner default HTTP timeout is now 420s.
  - Table editing is now a first-class harness feature. Template preparation creates `DATA_TABLE`; Host/OpenAPI/Go/add-in contracts support `readTable`, `replaceTableCell`, and `replaceTableRange`; `readTable` is allowed without deck mutation while table writes require `allowDeckMutation=true`.
  - Live SEM27 table proof passed on 2026-07-05 through `POST /v1/powerpoint/online/updates`: `readTable` returned the initial 3x3 table, writes changed visible values to `67 kt`, `101%`, and `103%`, reopen evidence still showed those values, cleanup removed the temporary table, and final Edge/Chrome-like window count was `0`. Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`, `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`, `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`, and `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`.
  - High-level update orchestration now uses the typed Agent route `POST /v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job`. The Agent keeps a narrow sibling-button fallback for the live UIA quirk where `Run Pending Job` is visible but absent from the UIA tree, and Host fails queued jobs with `ADDIN_RUN_COMMAND_FAILED` when the click fails after enqueue.
  - Dev JS harness live smoke passed with temporary `WINDOWS_OPERATOR_DEV_AUTOMATION=1`, then restored the default disabled gate. The smoke opened one SEM27 session, selected slide 4, ran `ppt.dom.snapshot` and `ppt.ribbon.commands` through `POST /v1/dev/powerpoint/online/sessions/{sessionId}/script`, captured a screenshot, cleaned the session, and returned Edge/Chrome-like window count `0`. Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-dev-harness-smoke-20260704t105257z/summary.json`.
  - After the smoke, `WINDOWS_OPERATOR_DEV_AUTOMATION` was removed from the Windows `Administrator` user environment, `WindowsOperator.Agent` was restarted, and the same dev endpoint returned HTTP 422 `dev_automation_disabled`; final Edge/Chrome-like window filter was `[]`.
  - live Host endpoint proof on 2026-07-04 returned `422 powerpoint_validation_failed` for an executable SEM27 update with `allowDeckMutation=false`; `GET /v1/powerpoint/jobs/approval-gate-live-2` returned `404`, final window filter was `[]`, and direct Windows `msedge_count=0`
  - live Host endpoint proof on 2026-07-04 returned `422 powerpoint_validation_failed` for direct `/template/prepare` and `/template/cleanup` requests with `allowDeckMutation=false`; no real session lookup or click was needed
- Fresh Windows VM contract/service test sweep on 2026-07-05:
  - Core contract/OpenAPI tests passed 29/29: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043732Z-304750/result.json`
  - Host proxy/job/update orchestration tests passed 96/96: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043752Z-304974/result.json`
  - Agent PowerPoint Online/dev automation/parity tests passed 47/47: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T043817Z-305603/result.json`
  - Focused Agent run-pending fallback tests passed 30/30: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T044519Z-316581/result.json`
  - Focused Host stale-queue failure tests passed 28/28: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260705T045226Z-329483/result.json`
- Local generated-client/add-in checks on 2026-07-05:
  - `go test ./...` passed in `clients/go`
  - `npm test -- --run` passed 24/24 in `src/WindowsOperator.PowerPointAddIn`
  - `npm run build` passed in `src/WindowsOperator.PowerPointAddIn`
  - `npm run manifest:validate` passed and reported `The manifest is valid.`
- Session cleanup now uses the returned workbench session state as post-close verification. Live VM proof on 2026-07-03 returned `status=closed`, action `powerpoint_online_cleanup_verified_closed`, empty warnings, and `edge_like=0`.
- Work-profile Edge startup now suppresses session restore for operator-owned browser sessions:
  - launch arg `--no-session-restore`
  - best-effort work-profile `Preferences` exit-state normalization before launch
  - DevTools page-target pruning after launch
  - Live VM proof on 2026-07-03 opened the SEM27 deck with one Edge window, no `and N more pages` title suffix, and DevTools `pageCount=1`; cleanup returned `edge_like=0`.

Gaps:

- No stable slide/object targeting layer exists for arbitrary existing decks. The proven live mutation path uses add-in-created tagged targets (`TITLE_MAIN`, `HERO_IMAGE`, `DATA_TABLE`).
- No tier-4 SharePoint/Graph version-history proof exists yet; current strongest proof is tier-3 reopen visual evidence plus PowerPoint Online `saved`.
- Reversible test-target cleanup is proven live on SEM27, but SharePoint version history may still retain the prepare/update/cleanup edits.
- Final mutation proof runbook exists at `.work/powerpoint-online-mutation-proof-runbook.md`, with a reusable runner at `scripts/linux/powerpoint-online-final-proof.py` and Just wrappers for self-test, request preparation, SEM27 Host-gate proof, and SEM27 non-mutating readiness proof. Current completion audit lives at `.work/powerpoint-online-editing-harness-completion-audit.md`; approval packet lives at `.work/powerpoint-online-final-mutation-approval-packet.md`.
- The exact final mutation proof request shape was gate-tested with `allowDeckMutation=false`: Host returned HTTP 422 before opening Edge or queueing a job, `GET /v1/powerpoint/jobs/ppt-mutation-proof-gate-20260704t0937z` returned 404, and Edge/Chrome widget count stayed `0`. The reusable proof runner now repeats that safety check through `--verify-host-gate`, including the no-queue `404` assertion. Core and Go client tests also pin serialization, OpenAPI schema exposure, generated Go request fields, and proof-session response fields for that proof shape.
- Existing docs correctly warn that browser DOM/click mutation should not be the slide-editing contract.

## Boundary

Owning boundary: `powerpoint/online` domain service.

Reason: PowerPoint Online work is a PowerPoint-domain workflow, not a generic browser workflow. Edge, DevTools, UIA, screenshot backends, task pane loading, save-state polling, and SharePoint URL normalization should be hidden behind a PowerPoint Online harness. Existing browser endpoints remain primitives for diagnostics and unusual manual control.

Keep mutation ownership split:

- `PowerPointOnlineHarness` owns document/session orchestration, slide navigation, add-in activation, save-state observation, evidence capture, and recovery.
- Existing `PowerPointJobService` owns durable job queue, validation, artifact staging, and result records.
- Office.js add-in owns actual slide mutation through `PowerPoint.run`.
- Browser DOM/click automation may operate shell controls and evidence capture, but should not become the public slide mutation mechanism.

## Public Surface

Use domain namespace:

```text
POST /v1/powerpoint/online/sessions
GET  /v1/powerpoint/online/sessions/{sessionId}
POST /v1/powerpoint/online/sessions/{sessionId}/slides/select
POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe
POST /v1/powerpoint/online/sessions/{sessionId}/save/wait
POST /v1/powerpoint/online/sessions/{sessionId}/template/prepare
POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup
POST /v1/powerpoint/online/sessions/{sessionId}/screenshot
POST /v1/powerpoint/online/sessions/{sessionId}/cleanup
POST /v1/powerpoint/online/updates
```

Existing lower-level queue endpoints stay:

```text
POST /v1/powerpoint/jobs
GET  /v1/powerpoint/jobs/{jobId}
```

### `PowerPointOnlineSessionStartRequest`

```csharp
public sealed record PowerPointOnlineSessionStartRequest
{
    public required string DeckUrl { get; init; }
    public string? SessionId { get; init; }
    public BrowserEdgeProfileMode ProfileMode { get; init; } = BrowserEdgeProfileMode.Work;
    public bool Capture { get; init; } = true;
    public int WaitSeconds { get; init; } = 30;
}
```

Contract:

- Opens or reuses an operator-owned Edge session in the Windows desktop session.
- Normalizes SharePoint redirect URLs and records canonical document URL when observable.
- Waits until PowerPoint Online editor is ready or returns a structured blocker.
- Does not require callers to know Edge profile directory, page-load polling, or screenshot paths.

### `PowerPointOnlineSessionResult`

```csharp
public sealed record PowerPointOnlineSessionResult
{
    public required bool Success { get; init; }
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public required string DeckUrl { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? Title { get; init; }
    public int? CurrentSlide { get; init; }
    public int? SlideCount { get; init; }
    public string? EditMode { get; init; }
    public string? SaveState { get; init; }
    public string? BrowserSessionId { get; init; }
    public long? Hwnd { get; init; }
    public WorkbenchRunRef? ArtifactRoot { get; init; }
    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();
}
```

Status vocabulary:

```text
opening
ready
blocked_auth
blocked_permission
blocked_readonly
blocked_office_error
closed
failed
```

### `PowerPointOnlineSlideSelectRequest`

```csharp
public sealed record PowerPointOnlineSlideSelectRequest
{
    public required int SlideNumber { get; init; }
    public bool Capture { get; init; } = true;
    public int WaitSeconds { get; init; } = 15;
}
```

Contract:

- Selects a 1-based slide number in the open PowerPoint Online deck.
- Handles thumbnail rail virtualization, focus, scroll, and retries internally.
- Returns updated `PowerPointOnlineSessionResult` with screenshot evidence when requested.

### `PowerPointOnlineUpdateRequest`

```csharp
public sealed record PowerPointOnlineUpdateRequest
{
    public string? SessionId { get; init; }
    public string? DeckUrl { get; init; }
    public required PowerPointUpdateJob Job { get; init; }
    public int? EvidenceSlideNumber { get; init; }
    public bool Capture { get; init; } = true;
    public int OpenWaitSeconds { get; init; } = 30;
    public int JobTimeoutSeconds { get; init; } = 60;
    public int PollSeconds { get; init; } = 1;
    public int SaveTimeoutSeconds { get; init; } = 30;
    public int SavePollSeconds { get; init; } = 1;
    public bool VerifyReopen { get; init; }
    public int ReopenWaitSeconds { get; init; } = 30;
    public bool PrepareTemplate { get; init; }
    public bool CleanupTemplate { get; init; }
    public bool CleanupTemplateOnFailure { get; init; } = true;
    public int TemplateWaitSeconds { get; init; } = 2;
    public bool AllowDeckMutation { get; init; }
    public bool CleanupSession { get; init; }
}
```

Contract:

- Opens or reuses a PowerPoint Online session.
- Ensures the active deck matches `Job.ExpectedDocumentUrl` when provided.
- Ensures add-in/task pane is available.
- Enqueues the existing `PowerPointUpdateJob`.
- Waits for Office.js result via existing `PowerPointJobRecord`.
- Waits for PowerPoint Online save-state evidence.
- Captures requested slide evidence.
- Optionally reopens the deck and re-captures evidence.
- Optionally prepares/cleans reversible template targets for guarded live proof.
- Requires `allowDeckMutation=true` before any executable Office.js job or high-level template prepare/cleanup action. Validate-only jobs remain allowed without mutation approval.
- Optionally closes the final active proof session and reports that cleanup.

### `PowerPointOnlineUpdateResult`

```csharp
public sealed record PowerPointOnlineUpdateResult
{
    public required bool Success { get; init; }
    public required PowerPointOnlineUpdateStatus Status { get; init; }
    public required PowerPointOnlineSessionResult Session { get; init; }
    public PowerPointOnlineSessionResult? VerificationSession { get; init; }
    public PowerPointOnlineSessionResult? TemplatePreparationSession { get; init; }
    public PowerPointOnlineSessionResult? TemplateCleanupSession { get; init; }
    public PowerPointOnlineSessionResult? SessionCleanupSession { get; init; }
    public required PowerPointJobRecord JobRecord { get; init; }
    public IReadOnlyList<DesktopScreenshotResult> Evidence { get; init; } = Array.Empty<DesktopScreenshotResult>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperatorError> Errors { get; init; } = Array.Empty<OperatorError>();
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

Update status vocabulary:

```text
Succeeded
Failed
BlockedSession
BlockedAddIn
SaveUnverified
VerificationFailed
CleanupFailed
SessionCleanupFailed
```

### `PowerPointSlideEvidence`

```csharp
public sealed record PowerPointSlideEvidence
{
    public required int SlideNumber { get; init; }
    public required DesktopScreenshotResult Screenshot { get; init; }
    public string? SaveState { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string? Notes { get; init; }
}
```

## Hidden Depth

The harness should absorb:

- Work-profile selection and account/session reuse.
- SharePoint and PowerPoint Online URL normalization:
  - original `pptx?web=1`
  - redirected `/:p:/r/...`
  - `_layouts/15/Doc.aspx?...`
  - `sourcedoc`, `file`, `action=edit`, `mobileredirect`
- Auth blockers, permission blockers, read-only mode, tenant consent pages, and file-not-found pages.
- PowerPoint Online readiness:
  - editor shell loaded
  - ribbon ready
  - slide thumbnails populated
  - canvas rendered
  - edit mode active
- Slide navigation:
  - thumbnail rail virtualization
  - scroll offsets
  - selected thumbnail detection
  - keyboard fallback
  - zoom and DPI differences
- Add-in readiness:
  - HTTPS static host reachable from Windows
  - manifest available
  - task pane open
  - same-origin job API reachable
  - Office.js requirements met
- Job orchestration:
  - enqueue
  - claim
  - wait for complete/fail
  - timeout and cleanup
  - translate add-in errors into stable `OperatorError`
- Save observation:
  - "Saving..."
  - "Saved"
  - "Saved to OneDrive"
  - conflict/error banners
  - transient offline/retry states
- Evidence:
  - full-window screenshot
  - future slide-canvas crop
  - artifact root under `operator-exchange/runs/<run-id>`
  - state snapshots for debugging
- Recovery:
  - browser reload
  - stale hwnd
  - DevTools disconnect
  - closed tab
  - task pane crash
  - stuck save state

## Targeting Model

Primary targeting: Office.js bindings/tags.

Do not make browser canvas coordinates the public target model. Coordinates are acceptable only inside the harness for shell navigation and for temporary manual/debug flows.

Recommended target workflow:

1. Template authoring or bootstrap creates stable target ids in shapes.
2. `PowerPointUpdateJob` references those ids.
3. Add-in inspects and mutates targets through `PowerPoint.run`.
4. Harness captures evidence and save-state verification.

For existing decks without targets:

- Add a discovery/bootstrap phase before broad edits.
- Allow a supervised "prepare targets" operation that creates or binds target ids to selected shapes.
- Store a target manifest per deck only as local operator state unless the deck itself receives bindings/tags.
- Do not infer permanent targets from z-order, default shape names, or click coordinates.

## Save-Proof Tiers

Use explicit tiers so callers know what was proven.

```text
tier0_visual_open       Deck opened and screenshot captured.
tier1_officejs_sync     Office.js update completed successfully in active presentation.
tier2_saved_indicator   PowerPoint Online reported saved/no pending save.
tier3_reopen_visual     Deck was reopened and affected slides were captured again.
tier4_cloud_version     SharePoint/Graph version proof; not available until credentials/API path exists.
```

Default completion target for this harness: `tier3_reopen_visual`.

High-level route contract should expose this as typed `saveProofTier` on `PowerPointOnlineUpdateResult`. Current reachable values: tier0 through tier3. Tier4 stays reserved until cloud version proof exists.

## Roadmap

### Phase 1: Online Session Harness

Deliver:

- Core contracts:
  - `PowerPointOnlineSessionStartRequest`
  - `PowerPointOnlineSessionResult`
  - `PowerPointOnlineSlideSelectRequest`
  - `PowerPointSlideEvidence`
- Service:
  - `IPowerPointOnlineService`
  - `PowerPointOnlineService`
- Routes:
  - `POST /v1/powerpoint/online/sessions`
  - `GET /v1/powerpoint/online/sessions/{sessionId}`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/screenshot`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`

Validation:

- Unit tests with fake `IEdgeBrowserService` and fake screenshot service.
- Live smoke opens a known SharePoint deck, selects slide 4, captures screenshot, cleans up or leaves session per option.
- Negative live test with synthetic inaccessible URL returns `blocked_auth`, `blocked_permission`, or `failed` with stable evidence.

### Phase 2: Readiness and Save-State Detection

Deliver:

- PowerPoint Online state probe script using DevTools DOM plus screenshot fallback.
- Stable fields: `EditMode`, `SaveState`, `CurrentSlide`, `SlideCount`.
- Error classification for auth, permission, readonly, Office error, and stale session.

Validation:

- Live test against the current SEM27 deck detects `ready`, `editing`, slide count near 71, and current slide after selection.
- Synthetic blocked URL returns classified blocker instead of generic browser failure.

### Phase 3: Add-in Online Activation

Deliver:

- Harness method to prove add-in host and task pane are reachable from the same browser session.
- Fix current host/static add-in mismatch if needed.
- Document install/sideload path for PowerPoint Online in this tenant/session.
- Live task-pane smoke in PowerPoint Online:
  - open deck
  - open add-in
  - claim synthetic job
  - fail safely or complete no-op

Validation:

- `https://localhost:3003/taskpane.html` reachable from the same Windows VM target as Host REST.
- `https://localhost:3003/manifest.xml` reachable from the same Windows VM target as Host REST and parsed into manifest diagnostics.
- Add-in can call same-origin or configured Host REST.
- Live no-op/add-in heartbeat proves Office.js is executing in active deck.

Progress:

- Package diagnostics are implemented in the add-in probe. Host reachability is considered healthy only when task pane content and manifest XML both pass.
- Static Windows probes passed for:
  - `https://localhost:3003/taskpane.html`
  - `https://localhost:3003/manifest.xml`
- Live SEM27 add-in probe returned:
  - `status=blockedActivation`
  - `hostReachable=true`
  - `taskPaneReachable=true`
  - `manifestReachable=true`
  - `commandVisible=true`
  - `taskPaneVisible=false`
  - manifest id `6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7`
  - Evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`
- Activation candidate preservation proof:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-activation-preserve-20260703t12151783080927z/summary.json`
  - Result: `status=blockedActivation`, `taskPaneVisible=false`, `commandVisible=false` after reveal, but `matchedElements` retained the earlier offscreen `Add-ins` group and `InsertAddInFlyout` button.
  - Cleanup returned `status=closed`; final Edge/PowerPoint window filter and direct `Get-Process msedge` count were `0`.
- Cleanup proof:
  - `POST /v1/powerpoint/online/sessions/ppt-addin-diagnostics-live/cleanup` returned `success=true`, `status=closed`.
  - final Edge/PowerPoint window filter was `0`.
  - direct `Get-Process msedge` count was `0`.

### Phase 4: Target Bootstrap and Inspection

Deliver:

- Operation to prepare stable targets in a selected slide or template.
- Target inspection endpoint or add-in operation that reports existing target ids and kinds.
- Target manifest artifact for human review.

Progress:

- `PowerPointUpdateJob.validateOnly` is now a boolean queue contract for target inspection without mutation.
- Validate-only jobs can carry intended update operations with only stable target ids and operation kind. Host accepts missing execution payloads because the add-in will inspect only.
- The add-in returns target inspection records through normal `PowerPointTargetResult` rows with `status=skipped` for editable targets and failed rows for missing/not-editable targets.
- Inspection metadata now travels through REST/OpenAPI/Go: `found`, `editable`, `type`, `message`.
- `PowerPointUpdateJob.discoverTargets` now allows non-mutating binding discovery, including zero-operation jobs, and `PowerPointUpdateResult.discoveredTargets` returns simple target inventory rows.
- Host completion accepts skipped target rows only for `validateOnly` jobs. Executable jobs cannot report skipped targets as a successful edit.
- This is still binding-level discovery, not full arbitrary slide/object traversal.
- 2026-07-04 implementation/validation:
  - add-in discovery contract, Host validation, OpenAPI, and Go client are implemented.
  - add-in production build and tests passed; Windows Host focused tests passed 81/81; Windows Core contract tests passed 23/23.
  - Agent activation retry now attempts Home/overflow reveal even when the first UIA query throws or reports no command; Windows Agent focused tests passed 26/26.
  - live SEM27 zero-operation discovery attempt used `discoverTargets=true`, `validateOnly=true`, and `allowDeckMutation=false`; it opened one session and cleaned it up, but ended `blockedAddIn` because the installed `Run Update` command was not visible in the profile.
  - evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-discover-targets-live-20260704t080557/summary.json`.
- Later 2026-07-04 live proof recovered add-in activation in the work profile and completed zero-operation discovery:
  - evidence root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-state-20260704t0904z`
  - request: `discoverTargets=true`, `validateOnly=true`, `allowDeckMutation=false`, `evidenceSlideNumber=4`, `cleanupSession=true`
  - result: `success=true`, `status=succeeded`, `saveProofTier=tier2SavedIndicator`, `claimedBy=officejs-taskpane`, `discoveredTargets=[]`
  - final window check: `chrome_widget_count 0`.
- One-call high-level route proof from deck URL also passed:
  - evidence root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-discovery-20260704t0908z`
  - request: `deckUrl`, `discoverTargets=true`, `validateOnly=true`, `allowDeckMutation=false`, `evidenceSlideNumber=4`, `cleanupSession=true`
  - result: `success=true`, `status=succeeded`, `saveProofTier=tier2SavedIndicator`, `claimedBy=officejs-taskpane`, `discoveredTargets=[]`
  - final window check: `chrome_widget_count 0`.

Validation:

- Create or detect targets in a disposable deck.
- Reopen deck and confirm targets remain addressable.
- Negative test for missing target returns `blocked_targeting` or job failure with stable target error.
- 2026-07-03 VM/local proof in `.work/powerpoint-online-update-orchestration-validation.md`:
  - add-in tests/build passed.
  - Host `PowerPointJobServiceTests` passed locally and on Windows VM.
  - Core serialization tests passed on Windows VM.
  - live Host REST accepted a validate-only job with missing text/artifact, then failed it for cleanup.
  - live Host REST accepted validate-only skipped completion and rejected normal-job skipped completion.
  - Edge process count after proof: `0`.

### Phase 5: High-Level Update Orchestration

Deliver:

- `POST /v1/powerpoint/online/updates`.
- Orchestration:
  - open/reuse deck session
  - verify active document
  - ensure add-in ready
  - enqueue existing `PowerPointUpdateJob`
  - wait for job result
  - wait for saved state
  - capture evidence
  - optional reopen validation
- Result:
  - `PowerPointOnlineUpdateResult`
  - save-proof tier
  - screenshot artifacts

Validation:

- Live edit on a disposable SharePoint deck:
  - replace text target
  - replace image target
  - wait saved
  - reopen
  - capture evidence
- Queue-only dry run remains separate and is not presented as edit proof.

### Phase 6: Hardening

Deliver:

- Timeouts and retry policy inside `PowerPointOnlineService`.
- Session cleanup policy.
- Run logs/state snapshots under `operator-exchange/runs/<run-id>`.
- OpenAPI and Go client regeneration.
- Development docs and live smoke entry.

Progress:

- Cleanup no longer emits `cleanup_not_postverified` after a successful workbench close. `IsAlive=false` maps to verified closed; `IsAlive=true` maps to failed with `cleanup_still_alive` and a structured `powerpoint_unavailable` error.
- Live proof used actual Windows VM Host `Microsoft Windows NT 10.0.20348.0`, session `ppt-online-cleanup-verify-vm`, run `ppt-online-cleanup-verify-vm-20260703101700`, and final `/v1/windows` Edge filter `0`.
- High-level update orchestration now has opt-in `cleanupSession`; successful proof runs can close the verification or primary session and return `sessionCleanupSession`. If the Office.js update otherwise succeeded but final session cleanup cannot be proven, the route returns `sessionCleanupFailed` with the succeeded job record preserved.

Validation:

- Full live smoke route for PowerPoint Online.
- Crash/reload recovery test.
- Permission/read-only negative tests.
- Repeat run proves no stale jobs remain queued and no orphan Edge sessions remain unless explicitly preserved.

## Module Placement

Core contracts:

```text
src/WindowsOperator.Core/Contracts/PowerPointOnline*.cs
src/WindowsOperator.Core/Services/IPowerPointOnlineService.cs
```

Agent implementation:

```text
src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs
```

Host proxy/facade:

```text
src/WindowsOperator.Host/Api/HostOperatorEndpoints.cs
src/WindowsOperator.Host/Services/DesktopAgentClient.cs
src/WindowsOperator.Host/Services/HostOperatorFacade.cs
```

OpenAPI:

```text
src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs
openapi/windows-operator.openapi.json
clients/go/windowsoperator.gen.go
```

Tests:

```text
tests/WindowsOperator.Agent.Tests/PowerPointOnlineServiceTests.cs
tests/WindowsOperator.Agent.Tests/RestAndMcpParityTests.cs
tests/WindowsOperator.Core.Tests/ContractSerializationTests.cs
```

Live smoke:

```text
scripts/linux/live-smoke.py
```

## Caller Impact

Before:

- Caller opens Edge, chooses profile, waits for page, clicks thumbnails, calls screenshot, enqueues PowerPoint job, hopes add-in is ready, polls job status, interprets save state, captures evidence.

After:

- Caller starts a PowerPoint Online session and asks for slide/update/evidence by PowerPoint-domain intent.
- Browser and Office quirks stay inside `PowerPointOnlineService`.
- Existing low-level browser routes remain available for diagnostics.

## Risks and Decisions

Decision: do not build browser DOM/canvas mutation as the primary edit contract.

Reason: PowerPoint Online canvas internals are implementation details. Direct DOM/click editing would be fragile, hard to verify, and likely to push vendor quirks into every caller. Use Office.js for mutation and browser automation for hosting, navigation, and evidence.

Decision: keep existing `/v1/powerpoint/jobs` queue rather than replace it.

Reason: it already owns validation, artifact staging, record persistence, and result semantics. Online harness should compose it.

Decision: make save proof explicit.

Reason: Office.js sync and SharePoint cloud persistence are different facts. Callers need to know whether we proved in-document sync, saved indicator, reopen evidence, or cloud version.

Risk: PowerPoint Online add-in installation may require tenant/admin setup.

Mitigation: Phase 3 must classify this cleanly. If add-in activation is blocked, session/evidence harness still ships, and update orchestration reports `blocked_addin` with concrete evidence.

Current status: add-in activation has both negative and positive live evidence. On 2026-07-04 at `ppt-discover-targets-live-20260704t080557`, the profile exposed only generic Add-ins paths and target discovery could not run. Later the same day, run `ppt-addin-state-20260704t0904z` exposed `Updater -> Run Update`, opened the task pane, and completed non-mutating Office.js discovery. On 2026-07-05, explicit SEM27 approval enabled live text/image and table mutation proofs with save/reopen/cleanup evidence. Durable install/profile-state proof is still not strong enough to call the profile stable across resets, and repeat mutation against production decks still needs explicit intent because it writes SharePoint version history.

Risk: arbitrary decks lack stable target ids.

Mitigation: roadmap includes target bootstrap/inspection. Do not promise safe arbitrary edits until target manifest exists.

## Validation Standard

Minimum "done" for visible edit work:

- Live Windows VM run.
- Real SharePoint-hosted PowerPoint deck.
- PowerPoint Online editor visible.
- Add-in applies an edit through Office.js or returns classified blocker.
- Save-state observed.
- Affected slide screenshot captured.
- Reopen validation captured unless caller explicitly requests weaker proof.

Dry-run validates only serialization/routing. It is not edit proof.

## Codex Goal Seed

Objective:

Build the PowerPoint Online editing harness described in `.work/powerpoint-online-editing-harness-roadmap.md`: expose domain-level `/v1/powerpoint/online/*` session and update APIs, compose existing Edge session and PowerPoint job/add-in infrastructure, prove live slide selection/evidence first, then prove Office.js edit/save/reopen verification on a SharePoint-hosted deck.

Architect inheritance:
Before implementation loops, identify the owning boundary for each non-trivial slice. Prefer root-cause fixes in the module/API/data contract that owns behavior over scattered caller patches. Keep public interfaces small and stable; hide retries, parsing, normalization, vendor quirks, state, and compatibility behavior inside the owner. Do not add new abstractions unless they hide real complexity or match existing repo patterns. For architecture forks, broad simplification, spec/source conflicts, or sustained alignment, route to the appropriate architecture/autonomous skill before implementing.
