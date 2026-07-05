# PowerPoint Online Add-in Preflight Validation

Date: 2026-07-03

Target deck:

`https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`

Status note, 2026-07-05: this file is a historical preflight validation log.
Later work completed add-in activation, Office.js mutation, reopen persistence,
cleanup, and final browser cleanup: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.

## Scope

Phase 3 readiness/preflight route:

`POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe`

The route classifies add-in state without enqueueing jobs or mutating slides.

## Local validation

- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
  - Passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`
  - Passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
  - Passed: 52 tests.
- `scripts/generate-go-client.sh`
  - Passed.
- `cd clients/go && go test ./...`
  - Passed.
- `git diff --check`
  - Passed.

## Live Windows validation

Deploy/restart:

- `scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - Run: `ppt-online-addin-probe-register-host-20260703094357`
  - Result: succeeded.
- `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `ppt-online-addin-probe-restart-agent-20260703094417`
  - Result: succeeded.

Contract proof:

- `GET http://127.0.0.1:43117/openapi.json`
- Result: `/v1/powerpoint/online/sessions/{sessionId}/addin/probe` exists with `operationId=probePowerPointOnlineAddIn`.

Static host proof:

- `scripts/windows/probe-url.ps1 -Url https://localhost:3003/taskpane.html -RequiredText 'Windows Operator PowerPoint' -TimeoutSeconds 20`
- Run: `ppt-online-addin-probe-url-20260703094500`
- Result: HTTP 200, `containsRequiredText=true`.

Package diagnostic proof:

- The preflight route now probes both package entry points:
  - `https://localhost:3003/taskpane.html`
  - `https://localhost:3003/manifest.xml`
- Result fields added to `PowerPointOnlineAddInProbeResult`:
  - `taskPaneUrl`
  - `taskPaneReachable`
  - `manifestUrl`
  - `manifestReachable`
  - `manifestId`
  - `manifestVersion`
  - `manifestDisplayName`
  - `manifestSourceLocation`
- Host reachability means both task pane marker validation and manifest XML parsing passed.

Session setup:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
- `sessionId=ppt-online-addin-probe-live`
- `runId=ppt-online-addin-probe-live-20260703094520`
- Result: `success=true`, `status=ready`, `saveState=saved`.

Slide state:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-probe-live/slides/select`
- Body: `slideNumber=4`, `capture=false`, `waitSeconds=1`
- Result:
  - `success=true`
  - `status=ready`
  - `currentSlide=4`
  - `slideCount=71`
  - `editMode=editing`
  - `saveState=saved`

Add-in preflight:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-probe-live/addin/probe`
- Body: `addInBaseUrl=https://localhost:3003`, `capture=true`, `hostTimeoutSeconds=10`, `label=addin-preflight-slide4`
- Result:
  - `success=false`
  - `status=blockedActivation`
  - `hostReachable=true`
  - `taskPaneVisible=false`
  - `commandVisible=true`
  - actions: `addin_probe_screenshot_requested`, `addin_host_probe_ok`, `addin_taskpane_not_visible`, `addin_command_visible`
  - error: `powerpoint_unavailable`, detail `PowerPoint add-in task pane not visible for session 'ppt-online-addin-probe-live'.`
- Matched UIA evidence:
  - `Add-ins`, automation id `InsertAddInFlyout`, control type `Button`

Screenshot evidence:

- `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-probe-live-20260703094520/screenshots/addin-preflight-slide4.png`
- Visual proof: slide 4 is selected, deck is in editing mode, no Windows Operator task pane is visible.

Activation click probe:

- UIA click on automation id `InsertAddInFlyout` returned `success=true`, `message=Click dispatched.`
- Follow-up screenshot:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-addin-probe-live-20260703094520/screenshots/addin-flyout-after-uia-click.png`
- Visual result: no add-in flyout/task pane opened.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-probe-live/cleanup`
- Result: `success=true`, `status=closed`.
- `GET http://127.0.0.1:43117/v1/windows`
- Edge/PowerPoint browser-window filter result: `0`.

## Package Diagnostic Validation Update

Local and VM validation:

- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "HostOperatorEndpointsTests|DesktopAgentClientTests" -m:1`
  - Passed: 13 tests.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore -m:1`
  - Passed.
- `go test ./...` in `clients/go`
  - Passed.
- Windows VM `PowerPointOnlineServiceTests|PowerPointOnlineAddInHostProbeTests`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120306Z-1931882/result.json`
  - Passed: 19 tests.
- Windows VM `HostOperatorEndpointsTests|DesktopAgentClientTests|PowerPointOnlineUpdateServiceTests`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120344Z-1932202/result.json`
  - Passed: 26 tests.
- Windows VM `ContractSerializationTests`
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120413Z-1932377/result.json`
  - Passed: 21 tests.

Live SEM27 proof:

- Static task pane probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-taskpane-probe-20260703t12051783080355z/result.json`
  - Result: HTTP 200, `containsRequiredText=true`.
- Static manifest probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-manifest-probe-20260703t12061783080365z/result.json`
  - Result: HTTP 200, `containsRequiredText=true`.
- Live add-in diagnostics:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`
  - Start: `success=true`, `status=ready`, `saveState=saved`.
  - Probe: `success=false`, `status=blockedActivation`.
  - Package: `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`.
  - Manifest: `manifestId=6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7`, `manifestVersion=1.0.0.0`, `manifestDisplayName=Windows Operator PowerPoint`, `manifestSourceLocation=https://localhost:3003/taskpane.html`.
  - Activation: `taskPaneVisible=false`, `commandVisible=true`.
- Cleanup:
  - Cleanup returned `success=true`, `status=closed`.
  - Edge/PowerPoint window filter: `0`.
  - direct `Get-Process msedge` count: `0`.

Activation-candidate preservation:

- Fixed probe diagnostics to retain activation candidates observed before Insert/overflow reveal attempts.
- VM focused test:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T121412Z-1937204/result.json`
  - Result: 20 passed.
- Live SEM27 proof:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-activation-preserve-20260703t12151783080927z/summary.json`
  - Result: `status=blockedActivation`, `taskPaneVisible=false`, final `commandVisible=false`.
  - `matchedElements` retained the offscreen `Add-ins` group and offscreen `InsertAddInFlyout` button seen before reveal.
  - Cleanup and direct Edge process count: `0`.

Home/Add-ins activation path:

- Live UIA run showed the correct reveal path for this PowerPoint Online shell:
  - `Home` tab visible.
  - Home tabpanel and `InsertAddInFlyout` initially offscreen.
  - Clicking Home made `InsertAddInFlyout` visible.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-reveal-20260703t12211783081291z/summary.json`.
- Clicking Add-ins opened the flyout:
  - visible menu included `Advanced...`, `More Add-ins`, `My Add-ins`, and add-in search.
  - no `Windows Operator PowerPoint` entry was present.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-addins-click-20260703t12221783081362z/summary.json`.
- Clicking `Advanced...` opened Office Add-ins with `Upload My Add-in`.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-advanced-click-20260703t12231783081432z/summary.json`.
- Clicking `Upload My Add-in` opened the Upload Add-in dialog and `Browse...` opened the Windows file picker.
  - Evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-upload-click-20260703t12251783081546z/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-browse-picker-20260703t12271783081649z/summary.json`
- Selecting `Z:\windows-operator\src\WindowsOperator.PowerPointAddIn\manifest.xml` enabled Upload, and the Upload click returned HTTP 200.
- Post-upload state still did not expose the `Windows Operator PowerPoint` task pane or command.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-sideload-attempt-20260703t12291783081743z/summary.json`.

Home-first activation automation:

- Local build:
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore -m:1`
  - Result: passed with existing `NETSDK1188` locale warnings.
- Windows VM focused tests:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123826Z-1948565/result.json`
  - Result: 21 passed.
- Agent restart:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123905Z-1948880/result.json`
  - Result: succeeded.
- Live SEM27 probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-first-live-20260703t124019917053669z/summary.json`
  - Result: `status=blockedActivation`, `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`, `taskPaneVisible=false`, `commandVisible=true`.
  - Actions included `addin_activation_home_tab_click_dispatched`, `addin_activation_click_dispatched`, and `addin_activation_timeout`.
  - Matched elements retained the offscreen Add-ins button, visible Add-ins button, and opened `Ribbon-InsertAddInFlyoutDropdown` menu.
  - Cleanup returned `status=closed`, final Edge/PowerPoint window filter `0`, direct `Get-Process msedge` count `0`.

## Historical Scope Limit

At this point, the harness classified add-in readiness and had a live mapped sideload path, but the actual Office.js edit path still needed tenant/profile/add-in state that made the `Windows Operator PowerPoint` task pane available inside PowerPoint Online. Later proof resolved this for SEM27 in the current work profile.

Updated status after installed-command discovery:

- The task pane is now activatable in this profile/session through Home overflow `Run Update`.
- Activation hardening:
  - top-biased click for bottom-clipped `Run Update` menu items
  - short retry for transient UIA `0x80040201`
  - no generic `Add-ins` click fallback for real activation runs
- Live ready proof:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-ready-live-20260703t131943894048445z/summary.json`
  - Result: `status=ready`, `taskPaneVisible=true`, `commandVisible=true`, package probes all reachable.
- Live update proof:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-validate-20260703t134919145483350z/summary.json`
  - Result: Office.js claimed the queued validate-only job as `officejs-taskpane` and returned `TARGET_NOT_FOUND` for synthetic target `codex_missing_target`.

Historical gap at that point:

- Needed a known binding/target in a deck or scratch/template deck, then a non-validate mutation and save-state/reopen proof. Later final proof used prepared `TITLE_MAIN`/`HERO_IMAGE` targets and passed.

## Update integration

The high-level update route now uses the preflight before enqueue:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
- `sessionId=ppt-online-update-preflight-live`
- `jobId=ppt-online-preflight-20260703095141`
- Result:
  - `status=blockedAddIn`
  - `jobRecord.status=notQueued`
  - `addin_host_probe_ok`
  - `addin_taskpane_not_visible`
  - `addin_probe_blocked:blockedActivation`
  - no `job_enqueued`
  - no `job_timed_out`
- No-enqueue proof:
  - `GET /v1/powerpoint/jobs/ppt-online-preflight-20260703095141`
  - HTTP 404, `powerpoint_job_not_found`
- Evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-preflight-live/screenshots/powerpoint-online-update.png`
