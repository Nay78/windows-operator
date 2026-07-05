# PowerPoint Online Update Orchestration Validation

Date: 2026-07-03

Target deck:

`https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`

Status note, 2026-07-05: this file is a historical validation log. Later
work completed the final mutating Office.js proof with save/reopen/cleanup:
`/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.

## Local validation

- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
  - Passed: 54 tests.
- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
  - Passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`
  - Passed.
- `scripts/generate-go-client.sh`
  - Passed.
- `cd clients/go && go test ./...`
  - Passed.
- `npm test --prefix src/WindowsOperator.PowerPointAddIn`
  - Passed: 5 files, 18 tests.
- `npm run typecheck --prefix src/WindowsOperator.PowerPointAddIn`
  - Passed.
- `npm run build --prefix src/WindowsOperator.PowerPointAddIn`
  - Passed.

Known local limitation:

- `dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore --filter PowerPointOnlineServiceTests` and Core test execution are blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`; builds pass.

## Live Windows validation

- Published/restarted Host with:
  - `scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - Final run: `ppt-online-update-preflight-register-host-20260703095113`.
- Checked Host REST:
  - `GET http://127.0.0.1:43117/v1/health`
  - Result: HTTP 200, `runtimeMode=headless-host`.
- Checked live OpenAPI:
  - `GET http://127.0.0.1:43117/openapi.json`
  - Result: 48 paths, `/v1/powerpoint/online/updates` present with `operationId=updatePowerPointOnlinePresentation`.
- Checked add-in static host from Windows:
  - `scripts/windows/probe-url.ps1 -Url https://localhost:3003/taskpane.html -RequiredText "Windows Operator PowerPoint" -TimeoutSeconds 20`
  - Result: HTTP 200, content contains required text.

### Update route Office.js claim proof

Final safe live validation:

- Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-validate-20260703t134919145483350z/summary.json`
- Request:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
  - target deck: SEM27 SharePoint URL above
  - `sessionId=ppt-online-update-validate-20260703t134919145483350z`
  - `job.validateOnly=true`
  - operation: synthetic `replaceText` on target `codex_missing_target`
  - `evidenceSlideNumber=4`
- Result:
  - HTTP 200
  - `success=false`
  - `status=failed`
  - session `status=ready`, `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`
  - job `status=failed`
  - `claimedBy=officejs-taskpane`
  - `claimedDocumentUrl=https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27 - Plan Semanal Servicios Mina.pptx`
  - job result target error `TARGET_NOT_FOUND` for `codex_missing_target`
- Actions included:
  - `addin_activation_click_target:Run Update:MenuItem:offscreen=False:bounds=1068,736,181,33`
  - `addin_activation_observed_ready`
  - `job_enqueued`
  - `addin_run_pending_job_click_requested`
  - `addin_run_pending_job_click_dispatched`
  - `slide_select_thumbnail_click:4:132:675`
  - `slide_click_dispatched:4`
- Cleanup:
  - `POST /v1/powerpoint/online/sessions/ppt-online-update-validate-20260703t134919145483350z/cleanup`
  - Result: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`
  - `Get-Process msedge` count after cleanup: `0`
  - browser window filter after cleanup: `[]`

Interpretation:

- High-level update orchestration now opens the SEM27 deck, activates the add-in task pane, enqueues a Host job, clicks `Run Pending Job`, lets Office.js claim the job, and receives a structured Office.js validation result.
- The failed result is expected and safe: the target id was intentionally nonexistent, so no slide mutation was attempted.
- Remaining live proof gap is a successful mutation against a known binding/target, then save-state and reopen verification.

### Update route negative path

Request:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
- `sessionId=ppt-online-update-live`
- `jobId=ppt-online-live-20260703091001`
- `evidenceSlideNumber=4`
- `jobTimeoutSeconds=3`
- operation: synthetic `replaceText` on target `codex-nonexistent-target`

Observed result:

- HTTP 200.
- `success=false`.
- `status=blockedAddIn`.
- session `status=ready`.
- job record `status=failed`.
- job error `code=ADDIN_TIMEOUT`.
- actions included:
  - `session_started`
  - `job_enqueued`
  - `job_timed_out`
  - `slide_select_thumbnail_click:4:132:675`
  - `slide_click_dispatched:4`
  - `screenshot_requested`
- evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-live/screenshots/powerpoint-online-update-2.png`
  - Visual proof: screenshot shows PowerPoint Online edit mode, slide 4 selected, status bar `Slide 4 of 71`.

Post-run queue proof:

- `GET http://127.0.0.1:43117/v1/powerpoint/jobs/ppt-online-live-20260703091001`
- Result: HTTP 200, persisted `status=failed`, `error.code=ADDIN_TIMEOUT`.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-update-live/cleanup`
- Original result before cleanup hardening: HTTP 200, `success=true`, `status=closed`, warning `cleanup_not_postverified`.
- `GET http://127.0.0.1:43117/v1/windows`
- Result: `edge_like=0`.

### Update route add-in preflight block

Request:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
- `sessionId=ppt-online-update-preflight-live`
- `jobId=ppt-online-preflight-20260703095141`
- `evidenceSlideNumber=4`
- `jobTimeoutSeconds=3`
- `capture=true`
- operation: synthetic `replaceText` on target `codex-nonexistent-target`

Observed result:

- HTTP 200.
- `success=false`.
- `status=blockedAddIn`.
- job record `status=notQueued`.
- `job.expectedDocumentUrl` bound to canonical session URL before preflight.
- session fields:
  - `currentSlide=4`
  - `slideCount=71`
  - `editMode=editing`
  - `saveState=saved`
- actions included:
  - `job_bound_to_session`
  - `addin_probe_requested`
  - `addin_host_probe_ok`
  - `addin_taskpane_not_visible`
  - `addin_command_visible`
  - `addin_probe_blocked:blockedActivation`
  - `slide_select_thumbnail_click:4:132:675`
  - `slide_click_dispatched:4`
  - `screenshot_requested`
- actions did not include:
  - `job_enqueued`
  - `job_timed_out`
- evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-preflight-live/screenshots/powerpoint-online-update.png`
  - Visual proof: screenshot shows PowerPoint Online edit mode, slide 4 selected, status bar `Slide 4 of 71`, and no Windows Operator task pane.

No-enqueue proof:

- `GET http://127.0.0.1:43117/v1/powerpoint/jobs/ppt-online-preflight-20260703095141`
- Result: HTTP 404, `code=powerpoint_job_not_found`.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-update-preflight-live/cleanup`
- Result: HTTP 200, `success=true`, `status=closed`.
- `GET http://127.0.0.1:43117/v1/windows`
- Edge/PowerPoint browser-window filter result: `0`.

### Add-in package diagnostic split

Implemented:

- `PowerPointOnlineAddInProbeResult` now exposes task pane and manifest diagnostics:
  - `taskPaneUrl`
  - `taskPaneReachable`
  - `manifestUrl`
  - `manifestReachable`
  - `manifestId`
  - `manifestVersion`
  - `manifestDisplayName`
  - `manifestSourceLocation`
- The Agent validates `taskpane.html` content and parses `manifest.xml` before drawing task pane activation conclusions.
- `hostReachable=true` now means local package health passed; tenant/user add-in activation remains a separate `taskPaneVisible`/`blockedActivation` fact.

Validation:

- Local focused Host test:
  - `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "HostOperatorEndpointsTests|DesktopAgentClientTests" -m:1`
  - Result: 13 passed.
- Local Core build:
  - `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore -m:1`
  - Result: passed.
- Go client:
  - `go test ./...` in `clients/go`
  - Result: passed.
- Windows VM focused Agent tests:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120306Z-1931882/result.json`
  - Result: 19 passed.
- Windows VM focused Host tests:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120344Z-1932202/result.json`
  - Result: 26 passed.
- Windows VM Core serialization tests:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T120413Z-1932377/result.json`
  - Result: 21 passed.

Live SEM27 proof:

- Static task pane:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-taskpane-probe-20260703t12051783080355z/result.json`
  - Result: HTTP 200, `containsRequiredText=true`.
- Static manifest:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-manifest-probe-20260703t12061783080365z/result.json`
  - Result: HTTP 200, `containsRequiredText=true`.
- Session/probe:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-diagnostics-live-20260703t12061783080404z/summary.json`
  - Start: HTTP 200, `success=true`, `status=ready`, `saveState=saved`.
  - Probe: HTTP 200, `success=false`, `status=blockedActivation`.
  - Package: `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`.
  - Manifest: `manifestId=6f40d8a9-9f7b-4f32-9e3c-7a1d1d11a0a7`, `manifestVersion=1.0.0.0`, `manifestDisplayName=Windows Operator PowerPoint`, `manifestSourceLocation=https://localhost:3003/taskpane.html`.
  - Activation: `taskPaneVisible=false`, `commandVisible=true`.
- Cleanup:
  - `POST /v1/powerpoint/online/sessions/ppt-addin-diagnostics-live/cleanup`
  - Result: `success=true`, `status=closed`.
  - Final Edge/PowerPoint window filter: `0`.
  - Direct `Get-Process msedge` count: `0`.

Activation candidate diagnostics:

- The Agent now preserves activation candidates seen before reveal attempts so blocked results keep useful UIA evidence.
- VM focused test:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T121412Z-1937204/result.json`
  - Result: 20 passed.
- Live SEM27 proof:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-activation-preserve-20260703t12151783080927z/summary.json`
  - Probe returned `status=blockedActivation`, final `commandVisible=false`, `taskPaneVisible=false`.
  - `matchedElements` retained offscreen `Add-ins` group and `InsertAddInFlyout` button candidates.
  - Cleanup returned `status=closed`, Edge/PowerPoint window filter `0`, direct `Get-Process msedge` count `0`.

Home/Add-ins route diagnostic:

- Live UIA showed the add-in reveal route starts from Home, not Insert, for the current PowerPoint Online shell.
- Evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-reveal-20260703t12211783081291z/summary.json`
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-addins-click-20260703t12221783081362z/summary.json`
- Live sideload attempt reached:
  - Home > Add-ins > Advanced... > Upload My Add-in > Browse...
  - selected `Z:\windows-operator\src\WindowsOperator.PowerPointAddIn\manifest.xml`
  - enabled and clicked Upload
  - returned HTTP 200 from the UIA click route
  - still ended with `status=blockedActivation`, `taskPaneVisible=false`, `commandVisible=false`
- Evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-sideload-attempt-20260703t12291783081743z/summary.json`

Current high-level route implication:

- `/v1/powerpoint/online/updates` correctly blocks before enqueue when the task pane is absent.
- The remaining failure is activation/launch of the sideloaded add-in inside PowerPoint Online, not queueing, save wait, screenshot, session cleanup, or local add-in package reachability.

Home-first automation validation:

- Local build:
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore -m:1`
  - Result: passed with existing `NETSDK1188` locale warnings.
- Windows VM focused tests:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T123826Z-1948565/result.json`
  - Result: 21 passed.
- Live SEM27 add-in probe after Agent restart:
  - Run: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-home-first-live-20260703t124019917053669z/summary.json`
  - Result: `status=blockedActivation`, `hostReachable=true`, `taskPaneReachable=true`, `manifestReachable=true`.
  - Actions included `addin_activation_home_tab_click_dispatched`, `addin_activation_click_dispatched`, and `addin_activation_timeout`.
  - Cleanup returned `status=closed`, final Edge/PowerPoint window filter `0`, direct `Get-Process msedge` count `0`.

Cleanup hardening proof:

- Restarted actual Windows VM Agent from shared source with `.local` bypassed:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `ppt-online-vm-agent-restart-cleanup-20260703101613`.
- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - Body: `sessionId=ppt-online-cleanup-verify-vm`, `runId=ppt-online-cleanup-verify-vm-20260703101700`, `capture=false`, `waitSeconds=20`.
  - Result: HTTP 200, `success=true`, `status=ready`, `saveState=saved`, warnings empty.
- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-cleanup-verify-vm/cleanup`
  - Result: HTTP 200, `success=true`, `status=closed`, actions included `session_window_closed`, `powerpoint_online_cleanup`, `powerpoint_online_cleanup_verified_closed`, warnings empty.
- `GET http://127.0.0.1:43117/v1/windows`
  - Edge/PowerPoint browser-window filter result: `0`.

Save-state waiter proof:

- Local validation after adding `POST /v1/powerpoint/online/sessions/{sessionId}/save/wait`:
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
    - Passed.
  - `dotnet build tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
    - Passed.
  - `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`
    - Passed.
  - `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~DesktopAgentClientTests|FullyQualifiedName~HostOperatorEndpointsTests|FullyQualifiedName~PowerPointOnlineUpdateServiceTests"`
    - Passed: 22 tests.
  - `scripts/generate-go-client.sh`
    - Passed.
  - `cd clients/go && go test ./...`
    - Passed.
  - `dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-build --filter "FullyQualifiedName~PowerPointOnlineServiceTests|FullyQualifiedName~RestAndMcpParityTests"`
    - Blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`.
  - `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-build --filter "FullyQualifiedName~ContractSerializationTests"`
    - Blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`.
- Deployed to actual Windows VM with `.local` bypassed:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
    - Run: `ppt-online-savewait-register-host-20260703103053`.
    - Result: `status=succeeded`, Host task registered and started.
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
    - Run: `ppt-online-savewait-restart-agent-20260703103119`.
    - Result: `status=succeeded`, `afterState=Running`.
- Pre-live checks:
  - `GET http://127.0.0.1:43117/v1/health`
    - Result: HTTP 200, `runtimeMode=headless-host`, platform `Microsoft Windows NT 10.0.20348.0`, `uiBackend=FlaUI.UIA3`.
  - `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.
- Opened SEM27 deck:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - Body: `sessionId=ppt-online-savewait-live`, `runId=ppt-online-savewait-live-20260703103210`, `capture=false`, `waitSeconds=20`.
  - Result: HTTP 200, `success=true`, `status=ready`, title `SEM27 - Plan Semanal Servicios Mina.pptx`, `saveState=saved`, warnings empty, errors empty.
- Waited for save:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-savewait-live/save/wait`
  - Body: `{"timeoutSeconds":10,"pollSeconds":1,"capture":false}`.
  - Result: HTTP 200, `success=true`, `status=ready`, `saveState=saved`, actions included `save_wait_observed` and `save_wait_observed:saved`, warnings empty, errors empty.
- RAM hygiene observation:
  - During the run, `GET /v1/windows` showed one Edge window, but title was `SEM27 - Plan Semanal Servicios Mina.pptx and 4 more pages - Work - Microsoft Edge`.
  - This indicated work-profile session restore opened extra tabs. The one-tab hardening proof below fixed this with session-restore suppression, preference normalization, DevTools page pruning, and final `edge_like=0`.
- Cleanup proof:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-savewait-live/cleanup`
  - Result: HTTP 200, `success=true`, `status=closed`, actions included `session_window_closed`, `powerpoint_online_cleanup`, `powerpoint_online_cleanup_verified_closed`, warnings empty.
  - Final `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.

Work-profile one-tab hardening proof:

- Implemented Edge owner hardening:
  - `StartEdge` passes `--no-session-restore`.
  - Browser session startup normalizes work-profile `Preferences` exit state before launch when profile paths are known.
  - Browser session startup prunes non-selected DevTools page targets after launch.
  - Public contracts unchanged.
- Local validation:
  - `dotnet restore tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj`
    - Passed.
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
    - Passed after restore. Warnings only: `NETSDK1188` invalid locale resources in test-platform packages.
  - `dotnet restore tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
    - Passed.
  - `dotnet build tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
    - Passed.
  - `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~DesktopAgentClientTests|FullyQualifiedName~HostOperatorEndpointsTests|FullyQualifiedName~PowerPointOnlineUpdateServiceTests"`
    - Passed: 22 tests.
  - `git diff --check`
    - Passed.
- VM focused Agent test validation:
  - Command over SSH using `C:\Users\Administrator\AppData\Local\WindowsOperator\dotnet-sdk\dotnet.exe`.
  - `dotnet restore tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj`
    - Passed.
  - `dotnet test tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj --no-restore --filter "FullyQualifiedName~EdgeMicrosoftAuthServiceBrowserTargetTests|FullyQualifiedName~EdgeMicrosoftAuthServicePreferencesTests"`
    - Passed: 7 tests.
- Deployed final Agent code to actual Windows VM:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `ppt-online-onetab-final-restart-agent-20260703104917`.
  - Result: `status=succeeded`, `afterState=Running`.
- Pre-live checks:
  - `GET http://127.0.0.1:43117/v1/health`
    - Result: HTTP 200, `runtimeMode=headless-host`, platform `Microsoft Windows NT 10.0.20348.0`, `uiBackend=FlaUI.UIA3`.
  - `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.
- Opened SEM27 deck:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - Body: `sessionId=ppt-online-onetab-final-live`, `runId=ppt-online-onetab-final-live-20260703105005`, `capture=false`, `waitSeconds=20`.
  - Result: HTTP 200, `success=true`, `status=ready`, title `SEM27 - Plan Semanal Servicios Mina.pptx`, `saveState=saved`, warnings empty, errors empty.
  - Actions included:
    - `profile_exit_state_normalized`
    - `remote_debugging_port:55232`
    - `startup_targets_observed:1`
    - `startup_targets_pruned:0`
- One-tab proof:
  - `GET http://127.0.0.1:43117/v1/windows`
    - Result: one Edge window.
    - Title: `SEM27 - Plan Semanal Servicios Mina.pptx - Work - Microsoft Edge`.
    - No `and N more pages` suffix.
  - Windows-loopback DevTools query `http://127.0.0.1:55232/json/list`
    - Result: `targetCount=9`, `pageCount=1`.
    - Only page target title: `SEM27 - Plan Semanal Servicios Mina.pptx`.
- Cleanup proof:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-onetab-final-live/cleanup`
  - Result: HTTP 200, `success=true`, `status=closed`, actions included `session_window_closed`, `powerpoint_online_cleanup`, `powerpoint_online_cleanup_verified_closed`, warnings empty.
  - Final `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.

Automated add-in activation attempt proof:

- Implemented probe activation options:
  - `activateIfNeeded`, default `false`.
  - `activationTimeoutSeconds`, default `10`.
  - High-level `/v1/powerpoint/online/updates` now probes with activation enabled before enqueue.
- Local validation:
  - `scripts/generate-openapi.sh`
    - Passed.
  - `scripts/generate-go-client.sh`
    - Passed.
  - `cd clients/go && go test ./...`
    - Passed.
  - `dotnet restore WindowsOperator.sln`
    - Passed.
  - `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore -m:1`
    - Passed. Warnings only: `NETSDK1188` invalid locale resources in test-platform packages.
  - `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "PowerPointOnlineUpdateServiceTests|DesktopAgentClientTests|HostOperatorEndpointsTests" -m:1`
    - Passed: 23 tests.
- VM focused test validation:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Agent.Tests\WindowsOperator.Agent.Tests.csproj' -Filter 'FullyQualifiedName~PowerPointOnlineServiceTests.ProbeOnlineAddInAsync' -MaxCpuCount 1`
    - Run: `run-20260703T111140Z-1901379`.
    - Passed: 6 tests.
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Core.Tests\WindowsOperator.Core.Tests.csproj' -Filter 'FullyQualifiedName~ContractSerializationTests.PowerPointOnlineAddInProbeRequest_Serializes_ActivationDefaults' -MaxCpuCount 1`
    - Run: `run-20260703T110611Z-1898923`.
    - Passed: 1 test.
- Deployed to actual Windows VM:
  - Host: `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
    - Run: `run-20260703T110717Z-1899479`.
    - Result: `status=succeeded`.
  - Agent: `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
    - Run: `run-20260703T111212Z-1901637`.
    - Result: `status=succeeded`, `afterState=Running`.
- Live SEM27 activation attempt:
  - Pre-check `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
    - `sessionId=ppt-online-addin-reveal-live`
    - Result: `success=true`, `status=ready`, `saveState=saved`.
    - Startup actions: `startup_targets_observed:1`, `startup_targets_pruned:0`.
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-reveal-live/addin/probe`
    - Body: `{"capture":false,"activateIfNeeded":true,"activationTimeoutSeconds":10,"hostTimeoutSeconds":10}`.
    - Result: `success=false`, `status=blockedActivation`, `hostReachable=true`, `taskPaneVisible=false`.
    - Initial action: `addin_command_visible`.
    - Final state: `commandVisible=false`, `matchedCount=0`.
    - Activation actions: `addin_activation_requested`, `addin_activation_insert_tab_click_dispatched`, `addin_activation_overflow_click_dispatched`, `addin_activation_command_not_clickable`.
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-addin-reveal-live/cleanup`
    - Result: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`.
  - Final `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.

Optional reopen verification proof:

- Implemented update contract:
  - `verifyReopen`, default `false`.
  - `reopenWaitSeconds`, default `30`.
  - `verificationSession` on `PowerPointOnlineUpdateResult`.
  - `verificationFailed` on `PowerPointOnlineUpdateStatus`.
- Behavior:
  - Default update flow is unchanged.
  - When `verifyReopen=true` and Office.js job plus save wait succeed, `PowerPointOnlineUpdateService` captures normal evidence, closes the current session, reopens the same document as `{sessionId}-verification`, selects the evidence slide when requested, captures reopened evidence when requested, and returns `verificationFailed` if cleanup/reopen/evidence readiness fails.
  - Cleanup happens before reopen to preserve the one-tab memory constraint.
- Local validation:
  - `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter "PowerPointOnlineUpdateServiceTests|DesktopAgentClientTests|HostOperatorEndpointsTests" -m:1`
    - Passed: 26 tests.
  - `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore -m:1`
    - Passed.
  - `scripts/generate-openapi.sh`
    - Passed.
  - `scripts/generate-go-client.sh`
    - Passed.
  - `cd clients/go && go test ./...`
    - Passed.
  - `git diff --check`
    - Passed.
- Windows VM focused validation:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Host.Tests\WindowsOperator.Host.Tests.csproj' -Filter 'PowerPointOnlineUpdateServiceTests|DesktopAgentClientTests|HostOperatorEndpointsTests' -MaxCpuCount 1`
    - Run: `run-20260703T112803Z-1911447`.
    - Passed: 26 tests.
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Core.Tests\WindowsOperator.Core.Tests.csproj' -Filter 'FullyQualifiedName~ContractSerializationTests' -MaxCpuCount 1`
    - Run: `run-20260703T112827Z-1911639`.
    - Passed: 18 tests.
  - A previous parallel Windows run failed with `CS2012` on shared `obj/bin`; rerunning sequentially passed. Keep Windows dotnet tests sequential for this repo.
- Deployed Host to actual Windows VM:
  - Pre-check `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.
  - Scheduled task state check:
    - `WindowsOperator.Agent` state `4`.
    - `WindowsOperator.Host` state `4`.
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
    - Run: `run-20260703T112919Z-1912060`.
    - Result: `status=succeeded`; Host published to `C:\ProgramData\WindowsOperator\host`; PowerPoint add-in static files published; task registered and started.
  - `GET http://127.0.0.1:43117/v1/health`
    - Result: HTTP 200, `runtimeMode=headless-host`, platform `Microsoft Windows NT 10.0.20348.0`, `uiBackend=FlaUI.UIA3`.
  - `GET http://127.0.0.1:43117/openapi.json`
    - Result: `PowerPointOnlineUpdateRequest` includes `verifyReopen` and `reopenWaitSeconds`; `PowerPointOnlineUpdateResult` includes nullable `verificationSession`; `PowerPointOnlineUpdateStatus` includes `verificationFailed`.
- Live SEM27 negative route proof with `verifyReopen=true`:
  - Request evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-reopen-negative-live/request.json`
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
    - `sessionId=ppt-online-reopen-negative-live`.
    - `evidenceSlideNumber=4`.
    - `capture=true`.
    - `verifyReopen=true`.
    - `reopenWaitSeconds=20`.
  - Result evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-reopen-negative-live/result.json`
  - Observed result:
    - `success=false`.
    - `status=blockedAddIn`.
    - `verificationSession=null`.
    - `jobRecord.status=notQueued`.
    - `session.currentSlide=4`.
    - `session.saveState=saved`.
    - actions included `addin_activation_requested`, `addin_activation_insert_tab_click_dispatched`, `addin_activation_command_not_clickable`, `addin_probe_blocked:blockedActivation`, `slide_select_thumbnail_click:4:132:675`, and `screenshot_requested`.
  - Screenshot evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-reopen-negative-live/screenshots/powerpoint-online-update.png`
    - Visual proof: PowerPoint Online editing mode, SEM27 deck, slide 4 selected.
  - One-tab observation during run:
    - `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `1`.
  - Cleanup:
    - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-reopen-negative-live/cleanup`
    - Result: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`.
  - Final cleanup proof:
    - `GET http://127.0.0.1:43117/v1/windows`
    - Edge/PowerPoint browser-window filter result: `0`.

### Session-bound job identity proof

Request:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
- `sessionId=ppt-online-update-bind`
- `jobId=ppt-online-bind-20260703091719`
- `expectedDocumentUrl` omitted.
- `evidenceSlideNumber=4`
- `jobTimeoutSeconds=3`
- operation: synthetic `replaceText` on target `codex-nonexistent-target`

Observed result:

- HTTP 200.
- `success=false`.
- `status=blockedAddIn`.
- actions included:
  - `session_started`
  - `job_bound_to_session`
  - `job_enqueued`
  - `job_timed_out`
  - `slide_select_thumbnail_click:4:132:675`
  - `slide_click_dispatched:4`
  - `screenshot_requested`
- persisted job record failed with `error.code=ADDIN_TIMEOUT`.
- persisted `job.expectedDocumentUrl` was bound to the canonical session document URL:
  - `https://aminerals-my.sharepoint.com/:p:/r/personal/nmartinez_drs_mineracentinela_cl/_layouts/15/Doc.aspx?sourcedoc=%7BBA878CDB-CE08-495B-BB23-6B8FFC5DBB25%7D&file=SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx&action=edit&mobileredirect=true`
- evidence:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-update-bind/screenshots/powerpoint-online-update.png`
  - Visual proof: screenshot shows PowerPoint Online edit mode, slide 4 selected, status bar `Slide 4 of 71`.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-update-bind/cleanup`
- Result: HTTP 200, `success=true`, `status=closed`.

### Mismatched job identity proof

Request:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
- `sessionId=ppt-online-update-mismatch`
- `jobId=ppt-online-mismatch-20260703091825`
- `expectedDocumentUrl` set to a different Doc.aspx document identity.
- `capture=false`

Observed result:

- HTTP 200.
- `success=false`.
- `status=blockedSession`.
- actions included:
  - `session_started`
  - `job_document_mismatch`
- error `code=powerpoint_validation_failed`.
- returned job record `status=notQueued`.

No-enqueue proof:

- `GET http://127.0.0.1:43117/v1/powerpoint/jobs/ppt-online-mismatch-20260703091825`
- Result: HTTP 404, `code=powerpoint_job_not_found`.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-update-mismatch/cleanup`
- Result: HTTP 200, `success=true`, `status=closed`.
- `GET http://127.0.0.1:43117/v1/windows`
- Result: `edge_like=0`.

## Issue found and fixed

First live update run captured too quickly after slide-select dispatch and screenshot still showed `Slide 1 of 71`.

Fix:

- `PowerPointOnlineUpdateService` now waits 1 second after evidence-slide selection before screenshot capture.
- Host test records and asserts `WaitSeconds=1` for evidence slide select.
- Retest produced slide 4 visual proof.

Second fix:

- Update route now binds a blank `PowerPointUpdateJob.ExpectedDocumentUrl` to the session canonical URL before enqueue.
- If caller supplies `expectedDocumentUrl`, the route compares it against the session canonical/current/deck identities before enqueue.
- Mismatch returns `blockedSession` and leaves no queued job.

Third fix:

- Update route now calls add-in preflight before enqueue.
- If add-in preflight returns anything other than `Ready`, the route returns `blockedAddIn` with a `notQueued` job record and evidence.
- This avoids stale queued jobs and `ADDIN_TIMEOUT` when the task pane is absent.

## Target validation slice

Date: 2026-07-03.

Implementation:

- `PowerPointUpdateJob.validateOnly` added as a boolean contract with default `false`.
- Host queue validation now allows `validateOnly=true` jobs to omit execution payloads (`replaceText.text`, `replaceImage.artifact`) while still requiring `jobId`, `requestedBy`, operation kind, target id, and valid mode/fit values.
- If a validate-only image operation supplies an artifact, Host still validates and stages it. Missing artifacts are allowed only because no mutation will run.
- `PowerPointTargetResult` now carries optional inspection metadata: `found`, `editable`, `type`, and `message`.
- Host result validation accepts `skipped` target status and rejects unknown target result `type` values.
- Host accepts `skipped` target results only for `validateOnly` jobs. Normal executable jobs cannot be completed as succeeded with skipped targets.
- PowerPoint add-in `UpdateEngine` now treats `validateOnly=true` as inspection-only: it asserts the active document, inspects target bindings, returns `skipped` for editable targets, returns failed target records for missing/not-editable targets, and does not resolve artifacts or call mutation.
- OpenAPI and Go client were regenerated. Live schema has `PowerPointUpdateJob.validateOnly` as plain boolean; Go client has `ValidateOnly bool`.

Local validation:

- `npm test --prefix src/WindowsOperator.PowerPointAddIn`: 5 files, 20 tests passed.
- `npm run typecheck --prefix src/WindowsOperator.PowerPointAddIn`: passed.
- `npm run build --prefix src/WindowsOperator.PowerPointAddIn`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter PowerPointJobServiceTests -m:1`: 39 passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore -m:1`: passed.
- `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore --filter ContractSerializationTests -m:1` on Linux: blocked at runtime because `Microsoft.WindowsDesktop.App` is not available on NixOS; compile succeeded before testhost launch.
- `go test ./...` in `clients/go`: passed.
- `git diff --check`: passed.

Windows VM validation:

- `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Host.Tests\WindowsOperator.Host.Tests.csproj' -Filter 'PowerPointJobServiceTests' -MaxCpuCount 1`
  - Result: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T114437Z-1921282/result.json`
  - Test tail: 37 passed.
- Re-run after skipped-target guard:
  - Result: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T114944Z-1924036/result.json`
  - Test tail: 39 passed.
- `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'Z:\windows-operator' -Project 'tests\WindowsOperator.Core.Tests\WindowsOperator.Core.Tests.csproj' -Filter 'ContractSerializationTests' -MaxCpuCount 1`
  - Result: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T114514Z-1921565/result.json`
  - Test tail: 21 passed.
- Host deployed through:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - Result: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T114545Z-1921805/result.json`
- Final deployment after skipped-target guard:
  - Result: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T115011Z-1924236/result.json`

Live REST proof without Edge launch:

- Request: `POST http://127.0.0.1:43117/v1/powerpoint/jobs`
- Run artifacts:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-validateonly-live-20260703t11461783079216z/summary.json`
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-validateonly-live-20260703t11461783079216z/enqueue-request.json`
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-validateonly-live-20260703t11461783079216z/enqueue-response.json`
- Payload:
  - `validateOnly=true`
  - fake expected document URL `https://example.invalid/validate-only.pptx`
  - `replaceText` target omitted `text`
  - `replaceImage` target omitted `artifact`
- Observed result:
  - enqueue HTTP 200.
  - persisted record `status=queued`.
  - persisted `job.validateOnly=true`.
  - persisted text operation had `text=null`.
  - persisted image operation had `artifact=null`.
- Cleanup:
  - `POST http://127.0.0.1:43117/v1/powerpoint/jobs/ppt-validateonly-live-20260703t11461783079216z/fail`
  - final record `status=failed`.
  - final record `error.code=VALIDATION_PROOF_CLEANUP`.
- Edge/RAM proof:
  - `GET http://127.0.0.1:43117/v1/windows`: no Edge/PowerPoint window titles in final window list.
  - Direct Windows check `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

Live REST proof of skipped-target guard:

- Run artifacts:
  - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-validateonly-complete-live-20260703t11511783079460z/summary.json`
- Validate-only positive:
  - enqueue HTTP 200.
  - `job.validateOnly=true`.
  - complete HTTP 200.
  - final record `status=succeeded`.
  - target result `status=skipped`.
- Normal executable negative:
  - enqueue HTTP 200.
  - complete with skipped target returned HTTP 422.
  - error code `powerpoint_validation_failed`.
  - cleanup via fail endpoint returned HTTP 200.
  - final record `status=failed`.
- Edge/RAM proof:
  - `GET http://127.0.0.1:43117/v1/windows`: no Edge/PowerPoint window titles.
  - Direct Windows check `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

## Template cleanup readiness slice

Date: 2026-07-03.

Implementation:

- Add-in task pane now exposes `Cleanup Template`.
- `OfficeTemplateBootstrapper.cleanupMockTargets()` deletes only bound shapes with a matching `TARGET_ID` tag and removes their binding.
- `PowerPointOnlineService` task-pane matching recognizes `Cleanup Template` as a readiness signal.

Validation:

- `npm test --prefix src/WindowsOperator.PowerPointAddIn`: 5 files, 21 tests passed.
- `npm run build --prefix src/WindowsOperator.PowerPointAddIn`: passed.
- `npm run manifest:validate --prefix src/WindowsOperator.PowerPointAddIn`: manifest valid.
- Linux Agent test attempt failed because `Microsoft.WindowsDesktop.App` is not available on NixOS; same focused Agent tests passed on Windows.
- Windows Agent focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T135947Z-1989971/result.json`
  - 23 passed.
- Host/add-in deployment:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T135749Z-1988732/result.json`
- Agent restart:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260703T140027Z-1990278/result.json`

Live SEM27 readiness proof:

- Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-addin-cleanup-button-20260703t1401z`
- `GET http://127.0.0.1:43117/v1/health`: `status=ok`.
- Windows-local `Invoke-WebRequest https://localhost:3003/taskpane.html`: `statusCode=200`, `msedge_count=0`.
- Pre-run `GET http://127.0.0.1:43117/v1/windows`: no Edge/PowerPoint windows.
- `POST /v1/powerpoint/online/sessions`: `success=true`, `status=ready`, `saveState=saved`, startup observed one page target.
- `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`: `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`.
- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe`: `success=true`, `status=ready`, `hostReachable=true`, `taskPaneVisible=true`; matched task pane buttons `Prepare Template`, `Cleanup Template`, and `Run Pending Job`.
- `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`.
- Final `GET /v1/windows`: `[]`.
- Final Windows `Get-Process msedge`: `0`.

## Template lifecycle endpoint slice

Date: 2026-07-04.

Implementation:

- Added `PowerPointOnlineTemplateRequest` with `capture`, `waitSeconds`, `allowDeckMutation`, and `label`.
- Added domain endpoints:
  - `POST /v1/powerpoint/online/sessions/{sessionId}/template/prepare`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup`
- Agent implementation requires a ready PowerPoint Online session, finds visible taskpane buttons, clicks via screen coordinates internally, waits, and returns `PowerPointOnlineSessionResult`.
- If the button is absent, the endpoint returns `success=false`, a `powerpoint_unavailable` error, and action `template_*_button_not_found`; it does not click.
- Host `DesktopAgentClient`, OpenAPI, and Go client were regenerated.

Validation:

- `dotnet build WindowsOperator.sln --no-restore -m:1`: passed with existing NETSDK1188 locale warnings.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter 'DesktopAgentClientTests|HostOperatorEndpointsTests|PowerPointOnlineUpdateServiceTests|PowerPointJobServiceTests' -m:1`: 68 passed.
- Linux `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore --filter 'ContractSerializationTests' -m:1`: compile reached testhost, then blocked because `Microsoft.WindowsDesktop.App` is not available on NixOS.
- `go test ./...` in `clients/go`: passed.
- `npm test --prefix src/WindowsOperator.PowerPointAddIn`: 21 passed.
- `npm run build --prefix src/WindowsOperator.PowerPointAddIn`: passed.
- `npm run manifest:validate --prefix src/WindowsOperator.PowerPointAddIn`: manifest valid.
- Windows Core tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T064757Z-448304/result.json`
  - 21 passed.
- Initial parallel Windows Agent/Host test attempts failed in NuGet restore with `Cannot create a file when that file already exists`; rerun serial.
- Windows Agent tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T064945Z-449214/result.json`
  - 43 passed.
- Windows Host tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T065018Z-449487/result.json`
  - 68 passed.
- Host deployment:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T065123Z-451480/result.json`
- Agent restart:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T065149Z-451680/result.json`
- Live `/openapi.json` exposed:
  - `/v1/powerpoint/online/sessions/{sessionId}/template/cleanup`
  - `/v1/powerpoint/online/sessions/{sessionId}/template/prepare`

Live SEM27 safe negative proof:

- Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-template-endpoint-negative-20260704t0653z`
- Pre-run `GET /v1/windows`: `[]`.
- Pre-run Windows `Get-Process msedge`: `0`.
- `POST /v1/powerpoint/online/sessions`: `success=true`, `status=ready`, `saveState=saved`, startup observed one page target.
- `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`: `currentSlide=4`, `slideCount=71`, `editMode=editing`, `saveState=saved`.
- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe` with `activateIfNeeded=false`: `taskPaneVisible=false`, `commandVisible=true`.
- `POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup`: `success=false`, `status=ready`, action `template_cleanup_button_not_found`, error code `powerpoint_unavailable`.
- No template button was visible, so no click was dispatched and the deck was not mutated by this endpoint proof.
- `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`: `success=true`, `status=closed`, `powerpoint_online_cleanup_verified_closed`.
- Final `GET /v1/windows`: `[]`.
- Final Windows `Get-Process msedge`: `0`.

## Guarded high-level template proof path

Date: 2026-07-04.

Implementation:

- Extended `PowerPointOnlineUpdateRequest`:
  - `prepareTemplate`
  - `cleanupTemplate`
  - `cleanupTemplateOnFailure`
  - `templateWaitSeconds`
- Extended `PowerPointOnlineUpdateResult`:
  - `templatePreparationSession`
  - `templateCleanupSession`
- Added `cleanupFailed` to `PowerPointOnlineUpdateStatus`.
- `/v1/powerpoint/online/updates` now keeps the full mutation proof sequence inside one high-level call when explicitly opted in:
  - open/reuse session
  - activate/probe add-in
  - click `Prepare Template`
  - wait for PowerPoint Online save
  - re-probe the task pane for fresh `Run Pending Job` controls
  - enqueue and run Office.js job
  - wait for save
  - optionally close/reopen and capture verification evidence
  - reactivate add-in
  - click `Cleanup Template`
  - wait for cleanup save
- Cleanup runs on terminal failures when `cleanupTemplate=true` and `cleanupTemplateOnFailure=true`.
- If the Office.js update succeeds but requested cleanup cannot be proven saved, result status is `cleanupFailed` and `success=false` while preserving the succeeded job record.
- Existing callers are unchanged because all new mutation flags default to false.

Validation:

- `dotnet restore WindowsOperator.sln`: passed.
- `dotnet build WindowsOperator.sln --no-restore -m:1`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-build --filter PowerPointOnlineUpdateServiceTests -m:1`: 18 passed.
- Linux `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-build --filter ContractSerializationTests -m:1`: blocked by missing `Microsoft.WindowsDesktop.App 8.0.0` on NixOS; same tests passed on Windows below.
- `scripts/generate-go-client.sh`: regenerated OpenAPI and Go client.
- `go test ./...` in `clients/go`: passed.
- Windows Host tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T070614Z-461477/result.json`
  - 18 passed.
- Windows Core tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T070319Z-458606/result.json`
  - 21 passed.
- `git diff --check`: clean.
- Live Host health:
  - `GET http://127.0.0.1:43117/v1/health`: `status=ok`.
- RAM/browser cleanup evidence:
  - `GET http://127.0.0.1:43117/v1/windows` filtered for Edge/PowerPoint: `[]`.
  - Direct Windows `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

Live mutation status:

- Not run against SEM27 in this slice.
- Reason: the new one-call proof path would create, update, reopen-verify, and delete known template targets in the real SharePoint deck. Even if final visible content is cleaned, SharePoint version history records the edits.

## Final session cleanup in high-level update route

Date: 2026-07-04.

Implementation:

- Extended `PowerPointOnlineUpdateRequest`:
  - `cleanupSession`
- Extended `PowerPointOnlineUpdateResult`:
  - `sessionCleanupSession`
- Added `sessionCleanupFailed` to `PowerPointOnlineUpdateStatus`.
- `/v1/powerpoint/online/updates` now has opt-in final session cleanup for proof runs:
  - template cleanup still runs first when requested
  - final cleanup targets `templateCleanupSession ?? verificationSession ?? session`
  - action `session_cleanup_requested` records the cleanup attempt
  - action `session_cleanup_failed` records a failed proof-close
- If the Office.js update succeeded but final session cleanup cannot be proven, result status is `sessionCleanupFailed` and `success=false` while preserving the succeeded job record.
- If the update already failed for another reason, session cleanup failure is preserved in evidence without masking the original status.
- Existing callers are unchanged because `cleanupSession` defaults to false.

Validation:

- `dotnet build WindowsOperator.sln --no-restore -m:1`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-build --filter PowerPointOnlineUpdateServiceTests -m:1`: 20 passed.
- `scripts/generate-go-client.sh`: regenerated OpenAPI and Go client.
- `go test ./...` in `clients/go`: passed.
- Windows Host focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T071115Z-465195/result.json`
  - 20 passed.
- Windows Core contract tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T071147Z-465439/result.json`
  - 21 passed.
- `dotnet restore WindowsOperator.sln`: passed after Windows-side tests.
- Generated contract evidence:
  - OpenAPI includes `cleanupSession`, `sessionCleanupSession`, and enum `sessionCleanupFailed`.
  - Go client includes `CleanupSession`, `SessionCleanupSession`, and `SessionCleanupFailed`.
- `git diff --check`: clean after documentation updates.
- RAM/browser cleanup evidence:
  - `GET http://127.0.0.1:43117/v1/windows` filtered for Edge/PowerPoint: `[]`.
  - Direct Windows `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

Live mutation status:

- Not run against SEM27 in this slice.
- Reason: full one-call proof would mutate a real SharePoint deck, even though template cleanup should remove the visible test targets afterward.

## Save-proof tier contract

Date: 2026-07-04.

Implementation:

- Added `PowerPointOnlineSaveProofTier`.
- Added required `saveProofTier` to `PowerPointOnlineUpdateResult`.
- Current route semantics:
  - `tier0VisualOpen`: no succeeded Office.js job.
  - `tier1OfficeJsSync`: Office.js job succeeded, but PowerPoint Online `saved` state was not proven.
  - `tier2SavedIndicator`: succeeded Office.js job plus final session `saveState=saved`.
  - `tier3ReopenVisual`: `tier2` plus successful reopen verification with screenshot evidence.
  - `tier4CloudVersion`: reserved; not emitted without SharePoint/Graph version proof.
- `PowerPointOnlineUpdateService` now preserves earlier session observations when slide-select or screenshot observations omit fields such as `saveState`, so proof tier does not regress because a later observation is sparse.

Validation:

- Worker `019f2bfd-2ba8-7862-b65a-2979b5ac1c84` implemented the bounded contract/service/test slice.
- `dotnet build WindowsOperator.sln --no-restore -m:1`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter 'PowerPointOnlineUpdateServiceTests|HostOperatorEndpointsTests' -m:1`: 24 passed.
- `go test ./...` in `clients/go`: passed.
- Linux `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore --filter 'ContractSerializationTests' -m:1`: blocked by missing `Microsoft.WindowsDesktop.App 8.0.0`; run this on Windows VM for final contract proof.
- Windows Host focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T072629Z-476413/result.json`
  - 24 passed.
- Windows Core contract tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T072434Z-474378/result.json`
  - 21 passed.
- `dotnet restore WindowsOperator.sln`: passed after Windows-side tests.
- No live browser was opened for this slice.
- RAM/browser cleanup evidence before and after validation:
  - `GET http://127.0.0.1:43117/v1/windows` filtered for Edge/PowerPoint: `[]`.
  - Direct Windows `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.
- `git diff --check`: clean.

Live mutation status:

- Not run against SEM27 in this slice.
- Reason: this slice is contract/proof semantics only; the full edit proof still mutates a real SharePoint deck.

## Deck mutation approval gate

Date: 2026-07-04.

Implementation:

- Extended `PowerPointOnlineUpdateRequest`:
  - `allowDeckMutation`
- Extended `PowerPointOnlineTemplateRequest`:
  - `allowDeckMutation`
- `/v1/powerpoint/online/updates` rejects executable jobs when `allowDeckMutation=false`.
- Direct template prepare/cleanup and high-level template prepare/cleanup also require `allowDeckMutation=true` because they click Office.js controls that save changes to the deck.
- Validate-only jobs remain allowed with `allowDeckMutation=false`; they can inspect targets without running mutation.
- OpenAPI and Go client now expose `allowDeckMutation`.

Validation:

- `dotnet build WindowsOperator.sln --no-restore -m:1`: passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter 'PowerPointOnlineUpdateServiceTests|DesktopAgentClientTests|HostOperatorEndpointsTests' -m:1`: 36 passed.
- `scripts/generate-go-client.sh`: regenerated OpenAPI and Go client.
- `go test ./...` in `clients/go`: passed.
- Linux `dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore --filter ContractSerializationTests -m:1`: blocked by missing `Microsoft.WindowsDesktop.App 8.0.0` on NixOS; same tests passed on Windows below.
- Windows Agent focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T074017Z-486171/result.json`
  - 42 passed.
- Windows Host focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T074111Z-487162/result.json`
  - 36 passed.
- Windows Core contract tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T074145Z-487523/result.json`
  - 22 passed.
- `dotnet restore WindowsOperator.sln`: passed after Windows-side tests.
- Deployed updated Host to actual Windows VM:
  - Before deploy, `Get-ScheduledTask -TaskName WindowsOperator.Host` returned `State=4`.
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T073549Z-483058/result.json`
  - Result: `status=succeeded`; Host published to `C:\ProgramData\WindowsOperator\host`, PowerPoint add-in static files published, and task `WindowsOperator.Host` registered/started as SYSTEM.
- Live Host health after deploy:
  - `GET http://127.0.0.1:43117/v1/health`: HTTP 200, `status=ok`, `runtimeMode=headless-host`, platform `Microsoft Windows NT 10.0.20348.0`.
- Live approval-gate endpoint proof:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/updates` with SEM27 `deckUrl`, executable `replaceText` job `approval-gate-live-2`, `capture=false`, and `allowDeckMutation=false`.
  - Result: HTTP 422, code `powerpoint_validation_failed`, detail `allowDeckMutation must be true for executable jobs or template prepare/cleanup because PowerPoint Online changes are saved to the deck.`
  - `GET http://127.0.0.1:43117/v1/powerpoint/jobs/approval-gate-live-2`: HTTP 404, code `powerpoint_job_not_found`, proving no job was queued.
- Deployed updated Agent and Host after adding direct template endpoint approval:
  - Agent restart: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T074236Z-488384/result.json`, result `status=succeeded`, task state `Running`.
  - Host publish: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T074322Z-488702/result.json`, result `status=succeeded`, Host published to `C:\ProgramData\WindowsOperator\host`.
  - `GET http://127.0.0.1:43117/v1/health`: HTTP 200, `status=ok`.
- Live direct template approval proof:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/direct-gate-fake/template/prepare` with `allowDeckMutation=false`, `capture=false`, `waitSeconds=0`: HTTP 422, code `powerpoint_validation_failed`.
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/direct-gate-fake/template/cleanup` with `allowDeckMutation=false`, `capture=false`, `waitSeconds=0`: HTTP 422, code `powerpoint_validation_failed`.
  - Both returned detail `allowDeckMutation must be true for template prepare/cleanup because PowerPoint Online changes are saved to the deck.`
- No live browser was opened for this slice.
- RAM/browser cleanup evidence:
  - `GET http://127.0.0.1:43117/v1/windows` filtered for Edge/PowerPoint: `[]`.
  - Direct Windows `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

Live mutation status:

- Not run against SEM27 in this slice.
- Reason: this slice deliberately adds the request-level approval gate needed before future full proof runs mutate a real SharePoint deck.

## Target discovery and activation retry

Date: 2026-07-04.

Implementation:

- Added `PowerPointUpdateJob.discoverTargets`.
- Added `PowerPointDiscoveredTarget`.
- Added `PowerPointUpdateResult.discoveredTargets`.
- Host accepts zero-operation jobs only when `discoverTargets=true`.
- Add-in `PresentationAdapter.discoverTargets()` enumerates Office.js PowerPoint bindings and returns binding-backed target inventory.
- Add-in update engine includes discovery rows with validate-only and zero-operation results without applying mutations.
- Agent add-in activation now attempts reveal/activation whenever `activateIfNeeded=true` and the task pane is absent, even if the first UIA query reports no command or throws a transient UIA error.

Validation:

- `dotnet build WindowsOperator.sln`: passed after restore; existing `NETSDK1188` locale warnings only.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore --filter 'PowerPointJobServiceTests|PowerPointOnlineUpdateServiceTests|DesktopAgentClientTests|HostOperatorEndpointsTests' -m:1`: 81 passed.
- `npm test --prefix src/WindowsOperator.PowerPointAddIn`: 23 passed.
- `npm run build --prefix src/WindowsOperator.PowerPointAddIn`: passed.
- `go test ./...` in `clients/go`: passed.
- Windows Host focused tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T075432Z-497236/result.json`
  - 81 passed.
- Windows Core contract tests:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T075519Z-497922/result.json`
  - 23 passed.
- Windows Agent focused tests after activation retry patch:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T080221Z-503472/result.json`
  - 26 passed.
- Linux Core contract test execution is still blocked by missing `Microsoft.WindowsDesktop.App 8.0.0`; same contract tests passed on Windows above.

Live VM proof:

- Host deployed:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T075613Z-498956/result.json`
  - result `status=succeeded`.
- Live OpenAPI proof:
  - `GET http://127.0.0.1:43117/openapi.json`
  - exposed `discoverTargets`, `PowerPointDiscoveredTarget`, and `discoveredTargets`.
- Live static add-in proof:
  - `GET http://127.0.0.1:43117/taskpane.html`: HTTP 200.
  - `GET http://127.0.0.1:43117/assets/app-nHs5T8Fg.js`: contained `discoverTargets`, `discoveredTargets`, and `TARGET_KIND`.
- Agent restarted after activation retry patch:
  - `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T080321Z-504385/result.json`
  - result `status=succeeded`, after state `Running`.
- Live non-mutating SEM27 discovery attempt:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/updates`
  - job `discover-live-20260704080557`
  - body used `discoverTargets=true`, `validateOnly=true`, zero operations, `allowDeckMutation=false`, `capture=false`, and `cleanupSession=true`.
  - Evidence:
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-discover-targets-live-20260704t080557/summary.json`
    - `/var/lib/windows-server/shared/operator-exchange/runs/ppt-discover-targets-live-20260704t080557/response.json`
  - Result:
    - `success=false`
    - `status=blockedAddIn`
    - package probes succeeded: `addin_taskpane_probe_ok`, `addin_manifest_probe_ok`, `addin_host_probe_ok`
    - activation was attempted: `addin_activation_requested`, `addin_activation_home_tab_click_dispatched`, `addin_activation_overflow_click_dispatched`, `addin_activation_insert_tab_click_dispatched`, `addin_activation_overflow_click_dispatched`
    - no installed real launch command was found: `addin_activation_command_not_clickable`
    - job stayed `status=notQueued`, so no Office.js discovery result could be produced.
  - Cleanup returned `sessionCleanupSession.status=closed`.
  - Final `GET http://127.0.0.1:43117/v1/windows` filtered for Edge/PowerPoint: `[]`.
  - Direct Windows `Get-Process msedge -ErrorAction SilentlyContinue | Measure-Object`: `0`.

Historical blocker:

- At this point, SEM27 discovery still depended on a visible installed `Windows Operator PowerPoint` launch command in the PowerPoint Online profile.
- Later profile/add-in activation recovered, and the final proof discovered `TITLE_MAIN`/`HERO_IMAGE`, edited `TITLE_MAIN`, reopened the deck, and cleaned up.

## Current Residual Gaps

- Real Office.js mutation plus save/reopen/cleanup is proven live for add-in-created tagged targets: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.
- Target validation still validates caller-provided stable binding ids. Free-form discovery/bootstrap of arbitrary existing deck objects remains a separate roadmap item.
- Tier-4 SharePoint/Graph version proof remains unavailable.
