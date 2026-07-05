# PowerPoint Online Session Observation Validation

Date: 2026-07-03

Target deck:

`https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`

## Scope

Phase 2 session observations:

- `currentSlide`
- `slideCount`
- `editMode`
- `saveState`

PowerPoint Online iframe body text was empty through DevTools, but UI Automation exposed the PowerPoint status bar and mode/save controls. `PowerPointOnlineService` now hides that UIA probing behind `PowerPointOnlineSessionResult`.

## Local validation

- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
  - Passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`
  - Passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
  - Passed: 51 tests.
- `scripts/generate-go-client.sh`
  - Passed.
- `cd clients/go && go test ./...`
  - Passed.
- `git diff --check`
  - Passed.

Linux test execution limitation:

- `dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore --filter PowerPointOnlineServiceTests` and Core test execution are blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0`; builds pass.

## Live Windows validation

Deploy/restart:

- `scripts/windows/register-host-autostart.ps1 -RepoRoot 'Z:\windows-operator'`
  - Run: `ppt-online-phase2-register-host-20260703093005`
  - Result: succeeded.
- `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `ppt-online-phase2-restart-agent-20260703093024`
  - Result: succeeded.

Contract proof:

- `GET http://127.0.0.1:43117/openapi.json`
- Result: `PowerPointOnlineSessionResult` includes `currentSlide`, `slideCount`, `editMode`, and `saveState`.

Session proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
- `sessionId=ppt-online-phase2-live`
- `runId=ppt-online-phase2-live-20260703093119`
- Result:
  - `success=true`
  - `status=ready`
  - `saveState=saved`
  - action `powerpoint_online_uia_observed`

Slide-selection proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-phase2-live/slides/select`
- Body: `slideNumber=4`, `capture=true`, `waitSeconds=1`, `label=phase2-slide4-observed`
- Result:
  - `success=true`
  - `status=ready`
  - `currentSlide=4`
  - `slideCount=71`
  - `editMode=editing`
  - `saveState=saved`
  - actions included `powerpoint_online_uia_observed`, `slide_select_thumbnail_click:4:132:675`, `slide_click_dispatched:4`
  - warning `No visible DOM match.`

Evidence:

- `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-phase2-live-20260703093119/screenshots/phase2-slide4-observed.png`
- Visual proof: screenshot shows PowerPoint Online edit mode, slide 4 selected, status bar `Slide 4 of 71`, and saved cloud indicator.

Cleanup proof:

- `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-phase2-live/cleanup`
- Result:
  - `success=true`
  - `status=closed`
  - `currentSlide=4`
  - `slideCount=71`
  - `editMode=editing`
  - `saveState=saved`
- `GET http://127.0.0.1:43117/v1/windows`
- Edge/PowerPoint browser-window filter result: `0`.

## Current Scope Limits

- Initial open sometimes observes `saveState=saved` before status-bar slide text appears; slide fields become available after selecting/capturing a slide.
- Observation depends on current PowerPoint Online UIA labels: `Slide N of M`, `SaveStatusButton`, and `ModeSwitcher`.
- Historical note: add-in activation and real Office.js mutation were still blocked when this slice landed. Later live proof completed Office.js mutation, reopen verification, cleanup, and final Edge cleanup: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.
