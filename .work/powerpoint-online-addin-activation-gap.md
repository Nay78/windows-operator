# PowerPoint Online Add-in Activation Gap

Date: 2026-07-03

Target deck:

`https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`

Status note, 2026-07-05: this file is a historical activation-debug log.
Later work completed add-in activation, Office.js mutation, reopen persistence,
cleanup, and final browser cleanup: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.

## What is working

- Host add-in static site is live:
  - `https://localhost:3003/taskpane.html`
  - `https://localhost:3003/manifest.xml`
  - Windows probe returned HTTP 200 and found `Windows Operator PowerPoint`.
- Add-in package diagnostics are live:
  - task pane content is verified separately from manifest XML.
  - manifest fields are parsed into the probe result.
  - `hostReachable=true` now means both task pane content and manifest XML passed.
- Host REST queue and update orchestration are live:
  - `/v1/powerpoint/online/updates` enqueues a job, times out safely when no task pane claims it, marks the job failed with `ADDIN_TIMEOUT`, and captures slide evidence.
- Add-in preflight route is live:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe`
  - It checks the static host, UIA command/task-pane visibility, and returns machine-readable `blockedActivation` when the task pane is unavailable.

## Live activation probe

Opened PowerPoint Online session:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
- `sessionId=ppt-online-addin-activation`
- Result: HTTP 200, `success=true`, `status=ready`.
- Start screenshot:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-activation/screenshots/addin-activation-start.png`

Inspected Insert ribbon:

- Clicked Insert tab.
- Screenshot:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-activation/screenshots/addin-activation-insert-tab.png`
- Observation: visible commands included New Slide, Text Box, Pictures, Video, Shapes, Stock Images, SmartArt, Chart, New Comment, Links/Symbols/overflow. No visible `Windows Operator PowerPoint`, Add-ins, or My Add-ins command.

Inspected Insert overflow:

- Clicked `...` overflow.
- Screenshot:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-activation/screenshots/addin-activation-insert-overflow.png`
- Observation: overflow contained Links and Symbols only.

Inspected far-right ribbon menu:

- Clicked the ribbon layout dropdown.
- Screenshot:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-activation/screenshots/addin-activation-ribbon-more.png`
- Observation: menu only contained ribbon layout options.

Cleanup:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-activation/cleanup`
- Result: HTTP 200, `success=true`, `status=closed`.
- `GET http://127.0.0.1:43117/v1/windows`
- Result: `edge_like=0`.

## Gap

The local add-in host is working, but PowerPoint Online does not expose an installed/sideloaded Windows Operator add-in command in this tenant/session. Because the task pane cannot be opened, a real Office.js edit cannot be proven yet.

Updated live preflight:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-probe-live/addin/probe`
- Result:
  - `status=blockedActivation`
  - `hostReachable=true`
  - `taskPaneVisible=false`
  - `commandVisible=true`
- Evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-probe-live-20260703094520/screenshots/addin-preflight-slide4.png`
- UIA click on `InsertAddInFlyout` dispatched, but follow-up screenshot still showed no task pane/flyout:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-probe-live-20260703094520/screenshots/addin-flyout-after-uia-click.png`

Concrete missing approval/state:

- Tenant or user sideload path for the manifest at:
  - `src/WindowsOperator.PowerPointAddIn/manifest.xml`
- Or an installed app entry for `Windows Operator PowerPoint` in PowerPoint Online.
- After installation, the next live proof should:
  - open task pane in the deck,
  - enqueue a no-op or target-inspection job,
  - observe add-in claim/complete/fail through `/v1/powerpoint/jobs/*`,
  - then proceed to target bootstrap and real edit proof.

## Automated Activation Attempt Update

Implemented:

- `PowerPointOnlineAddInProbeRequest` now has:
  - `activateIfNeeded` default `false`
  - `activationTimeoutSeconds` default `10`
- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` remains read-only by default.
- When `activateIfNeeded=true`, the Agent:
  - probes the add-in host,
  - detects task-pane and command visibility through UIA,
  - selects the Home tab first when the Add-ins command is offscreen,
  - falls back to Insert and ribbon overflow when needed,
  - clicks a visible/enabled add-in command when one is available,
  - polls for task-pane visibility,
  - returns `blockedActivation` with activation actions instead of enqueueing stale jobs.
- `PowerPointOnlineUpdateService` now calls the probe with `activateIfNeeded=true`.

Live proof on VM:

- Host deployed:
  - `scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - run `run-20260703T110717Z-1899479`
- Agent restarted:
  - `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - run `run-20260703T111212Z-1901637`
- Opened target deck:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - `sessionId=ppt-online-addin-reveal-live`
  - result `success=true`, `status=ready`, `saveState=saved`
  - actions included `startup_targets_observed:1`, `startup_targets_pruned:0`
- Activation probe:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-reveal-live/addin/probe`
  - body: `{"capture":false,"activateIfNeeded":true,"activationTimeoutSeconds":10,"hostTimeoutSeconds":10}`
  - result:
    - `success=false`
    - `status=blockedActivation`
    - `hostReachable=true`
    - initial action `addin_command_visible`
    - final `commandVisible=false`
    - `taskPaneVisible=false`
  - activation actions:
    - `addin_activation_requested`
    - `addin_activation_insert_tab_click_dispatched`
    - `addin_activation_overflow_click_dispatched`
    - `addin_activation_command_not_clickable`
- Cleanup:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-reveal-live/cleanup`
  - result `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`
  - final `GET http://127.0.0.1:43117/v1/windows`: `edgeLikeCount=0`

Historical gap at that point:

- Automation can now attempt the visible ribbon path. The tenant/session still does not expose a visible task pane for `Windows Operator PowerPoint` after activation.
- Remaining blocker is tenant/user add-in activation, not local package reachability, manifest validity, or route plumbing.

## Home/Add-ins Route Discovery

Live UIA exploration on 2026-07-03 found the PowerPoint Online add-in path for the SEM27 deck:

- Initial session was ready with slide `1 of 71` and `saveState=saved`.
- The Home tab was visible while the Home tabpanel and `InsertAddInFlyout` Add-ins button were initially offscreen.
- Clicking Home made the Home tabpanel and `InsertAddInFlyout` visible.
- Clicking Add-ins opened the add-ins flyout.
- The flyout contained `Advanced...`, `More Add-ins`, `My Add-ins`, and search controls, but no `Windows Operator PowerPoint` entry.
- Clicking `Advanced...` opened the Office Add-ins dialog.
- The dialog contained `Upload My Add-in`.
- Clicking `Upload My Add-in` opened the Upload Add-in dialog.
- Clicking `Browse...` opened the Windows file picker.

Evidence:

- Home reveal:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-reveal-20260703t12211783081291z/summary.json`
- Home > Add-ins flyout:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-addins-click-20260703t12221783081362z/summary.json`
- Advanced dialog:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-advanced-click-20260703t12231783081432z/summary.json`
- Upload dialog:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-upload-click-20260703t12251783081546z/summary.json`
- Browse picker:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-browse-picker-20260703t12271783081649z/summary.json`

## Home-first Automation Patch

Implemented:

- `TryRevealAddInCommandAsync` now clicks a visible Home tab before Insert/overflow when the activation candidate is offscreen.
- New action:
  - `addin_activation_home_tab_click_dispatched`
- Insert and ribbon overflow remain fallback reveal paths.
- Probe matched elements now include both the initially offscreen Add-ins button and the later visible Add-ins flyout evidence.

Validation:

- Local build:
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore -m:1`
  - Result: passed with existing `NETSDK1188` locale warnings.
- Windows VM focused tests:
  - `scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj' -Filter 'PowerPointOnlineServiceTests|PowerPointOnlineAddInHostProbeTests' -MaxCpuCount 1`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123826Z-1948565/result.json`
  - Result: 21 passed.
- Agent restart:
  - `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123905Z-1948880/result.json`
  - Result: succeeded.
- Live SEM27 probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-first-live-20260703t124019917053669z/summary.json`
  - Start: `success=true`, `status=ready`, `saveState=saved`.
  - Probe: `success=false`, `status=blockedActivation`.
  - Package: `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`.
  - Actions:
    - `addin_activation_home_tab_click_dispatched`
    - `addin_activation_click_dispatched`
    - `addin_activation_timeout`
  - Matched UIA evidence retained offscreen `InsertAddInFlyout`, visible `InsertAddInFlyout`, and the opened `Ribbon-InsertAddInFlyoutDropdown` menu.
  - Cleanup: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`.
  - Final Edge/PowerPoint window filter: `0`.
  - Direct `Get-Process msedge` count: `0`.

## Sideload Attempt

Live sideload attempt:

- Manifest selected:
  - `Z:\windows-operator\src\WindowsOperator.PowerPointAddIn\manifest.xml`
- File picker accepted the path.
- Upload Add-in dialog enabled `Upload`.
- UIA click on `DialogInstall` returned HTTP 200 and `success=true`.
- After upload, PowerPoint returned to the ribbon with `Add-ins` visible.

## Activation Resolved For Installed Command

Later live runs found the installed manifest command under the Home ribbon overflow:

- Ribbon group: `Updater`
- Menu item: `Run Update`
- The menu item can be bottom-clipped in the 1296x776 Edge window; activation uses a top-biased click for `MenuItem` bounds.
- UIA can transiently fail with `0x80040201`; activation reveal now retries short-lived UIA failures instead of treating the first failure as final.
- In real activation runs, generic `Add-ins` fallback is no longer clicked when the specific `Run Update` command cannot be found.

Validation:

- Live activation proof:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-ready-live-20260703t131943894048445z/summary.json`
  - Result: `success=true`, `status=ready`, `taskPaneVisible=true`, `commandVisible=true`, `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`
  - Cleanup returned `status=closed`; direct Edge process count `0`.
- Final high-level update proof:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-validate-20260703t134919145483350z/summary.json`
  - Actions included `addin_activation_click_target:Run Update:MenuItem:offscreen=False:bounds=1068,736,181,33`, `addin_activation_observed_ready`, `addin_run_pending_job_click_dispatched`.
  - Office.js claimed the queued job as `officejs-taskpane` and returned expected `TARGET_NOT_FOUND` for a synthetic missing target.

Historical gap at that point:

- Activation is no longer the primary blocker for this profile/session.
- Historical missing proof was an edit against a known binding/target in the deck, followed by save-state and reopen verification. Later final proof used prepared `TITLE_MAIN`/`HERO_IMAGE` targets and passed.
- The task pane containers stayed zero-sized:
  - `WACTaskPaneContainerRight`
  - `tabbedTaskPaneContainer`
- Follow-up probe returned:
  - `success=false`
  - `status=blockedActivation`
  - `taskPaneVisible=false`
  - `commandVisible=false`
  - actions included `addin_activation_click_dispatched` and `addin_activation_timeout`
- Cleanup returned `status=closed`, final Edge title count `0`.

Evidence:

- `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-sideload-attempt-20260703t12291783081743z/summary.json`

Updated blocker:

- The UI path and local package are no longer unknown.
- Missing proof is PowerPoint Online accepting and launching the sideloaded add-in in this tenant/profile.
- Next diagnostic should capture post-upload browser/Office dialog errors or inspect whether Office rejects `https://localhost:3003/taskpane.html` trust, manifest origin, tenant sideload policy, or add-in launch state.

## Package Diagnostic Update

Implemented:

- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` now returns:
  - `taskPaneUrl`
  - `taskPaneReachable`
  - `manifestUrl`
  - `manifestReachable`
  - `manifestId`
  - `manifestVersion`
  - `manifestDisplayName`
  - `manifestSourceLocation`
- The Agent probes `taskpane.html` and `manifest.xml` from Windows, validates the task pane marker, parses manifest XML, and only reports `hostReachable=true` when both parts pass.

Live proof on VM:

- Build:
  - `npm run build --prefix src/WindowsOperator.PowerPointAddIn`
  - result: passed.
- Host deployed:
  - `scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - run `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120449Z-1932751/result.json`
  - result: succeeded, add-in static files published.
- Agent restarted:
  - `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - run `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120509Z-1932884/result.json`
  - result: `afterState=Running`.
- Static task pane probe:
  - `scripts/windows/probe-url.ps1 -Url https://localhost:3003/taskpane.html -RequiredText 'Windows Operator PowerPoint' -TimeoutSeconds 20`
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-taskpane-probe-20260703t12051783080355z/result.json`
  - result: HTTP 200, `containsRequiredText=true`.
- Static manifest probe:
  - `scripts/windows/probe-url.ps1 -Url https://localhost:3003/manifest.xml -RequiredText 'Windows Operator PowerPoint' -TimeoutSeconds 20`
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-manifest-probe-20260703t12061783080365z/result.json`
  - result: HTTP 200, `containsRequiredText=true`.
- Live SEM27 add-in probe:
  - run `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`
  - session start: HTTP 200, `success=true`, `status=ready`, `saveState=saved`.
  - probe: HTTP 200, `success=false`, `status=blockedActivation`.
  - package: `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`.
  - manifest: `manifestId=6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7`, `manifestVersion=1.0.0.0`, `manifestDisplayName=Windows Operator PowerPoint`, `manifestSourceLocation=https://localhost:3003/taskpane.html`.
  - activation: `taskPaneVisible=false`, `commandVisible=true`.
  - actions: `addin_taskpane_probe_ok`, `addin_manifest_probe_ok`, `addin_host_probe_ok`, `addin_taskpane_not_visible`, `addin_command_visible`.
- Cleanup:
  - `POST /v1/powerpoint/online/sessions/ppt-addin-diagnostics-live/cleanup`
  - result: `success=true`, `status=closed`, action `powerpoint_online_cleanup_verified_closed`.
  - final Edge/PowerPoint window filter: `0`.
  - direct `Get-Process msedge` count: `0`.

## Activation Candidate Preservation Update

Implemented:

- The add-in probe now preserves activation candidates seen before reveal attempts.
- This avoids a misleading result where actions say `addin_command_visible`, but final `matchedElements` is empty because Insert/overflow reveal hid the candidate.

Validation:

- Local build:
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore -m:1`
  - Result: passed with existing `NETSDK1188` locale warnings.
- Windows VM focused tests:
  - `scripts/windows/run-dotnet-test.ps1 -Project tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj -Filter 'PowerPointOnlineServiceTests|PowerPointOnlineAddInHostProbeTests' -MaxCpuCount 1`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T121412Z-1937204/result.json`
  - Result: 20 passed.
- Agent restarted:
  - `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T121433Z-1937407/result.json`
  - Result: succeeded.
- Live SEM27 activation probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-activation-preserve-20260703t12151783080927z/summary.json`
  - Result: `success=false`, `status=blockedActivation`, `taskPaneVisible=false`, `commandVisible=false`.
  - Actions: `addin_command_visible`, `addin_activation_insert_tab_click_dispatched`, `addin_activation_overflow_click_dispatched`, `addin_activation_command_not_clickable`.
  - Preserved matched elements:
    - offscreen `Add-ins` group.
    - offscreen `Add-ins` button with automation id `InsertAddInFlyout`.
  - Cleanup: `success=true`, `status=closed`, Edge/PowerPoint window filter `0`, direct `Get-Process msedge` count `0`.
