# PowerPoint Online Session Harness Validation

Date: 2026-07-03

Target deck:

`https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1`

## Local validation

- `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore`
  - Passed after cleanup verification hardening.
- `dotnet restore tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`
  - Passed after `--no-restore` build found a missing local NuGet cache package.
- `dotnet build tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
  - Passed.
- `dotnet build tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore`
  - Passed.
- `cd clients/go && go test ./...`
  - Passed.
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore`
  - Passed: 43 tests.
- `dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore --filter PowerPointOnlineServiceTests`
  - Blocked on Linux by missing `Microsoft.WindowsDesktop.App 8.0.0` runtime. Build passed.

## Live Windows validation

- Restarted `WindowsOperator.Agent` with:
  - `scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Result: task state `Running`, run `ppt-online-restart-agent-patched-20260703045115`.
- Started the online session through Host REST:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - Body: `sessionId=ppt-online-goal`, `runId=ppt-online-goal-patched`, `capture=true`, `waitSeconds=20`.
  - Result: HTTP 200, `success=true`, `status=ready`, Edge hwnd `5636942`.
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-goal-patched/screenshots/ppt-online-patched-start.png`.
- Selected slide 4 through Host REST:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-goal/slides/select`
  - Body: `slideNumber=4`, `capture=true`, `waitSeconds=1`, `label=ppt-online-patched-slide4`.
  - Result: HTTP 200, `success=true`, `status=ready`.
  - Actions included `slide_select_dom_unavailable:4`, `slide_select_thumbnail_click:4:132:675`, `slide_click_dispatched:4`.
  - Warning: `No visible DOM match.`
  - Evidence: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-online-goal-patched/screenshots/ppt-online-patched-slide4.png`.
  - Visual proof: screenshot shows PowerPoint Online edit mode, left rail slide 4 selected, status bar `Slide 4 of 71`.
- Cleaned up the online session:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-goal/cleanup`
  - Original result before cleanup hardening: HTTP 200, `success=true`, `status=closed`, warning `cleanup_not_postverified`.
- Checked desktop windows after cleanup:
  - `GET http://127.0.0.1:43117/v1/windows`
  - Result: `edge_like=0`.
- Restarted the actual Windows VM Agent from shared source:
  - `WINDOWS_OPERATOR_LOCAL_ENV=/dev/null scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 -TaskName WindowsOperator.Agent -WaitSeconds 45`
  - Run: `ppt-online-vm-agent-restart-cleanup-20260703101613`.
  - Health after restart: platform `Microsoft Windows NT 10.0.20348.0`.
- Re-ran one-tab cleanup proof:
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions`
  - Body: `sessionId=ppt-online-cleanup-verify-vm`, `runId=ppt-online-cleanup-verify-vm-20260703101700`, `capture=false`, `waitSeconds=20`.
  - Result: HTTP 200, `success=true`, `status=ready`, `saveState=saved`, no warnings.
  - `POST http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-online-cleanup-verify-vm/cleanup`
  - Result: HTTP 200, `success=true`, `status=closed`, actions included `session_window_closed`, `powerpoint_online_cleanup`, `powerpoint_online_cleanup_verified_closed`, warnings empty.
  - `GET http://127.0.0.1:43117/v1/windows`
  - Edge/PowerPoint browser-window filter result: `0`.

## Historical Scope Limits

- Slide selection uses a live-calibrated thumbnail rail coordinate fallback when DOM is unavailable. It is proven for visible slide 4 in the current PowerPoint Online shell, but not for hidden/scrolled slide rail layouts.
- Later harness slices added machine-readable selected-slide observation through UIA, with screenshot evidence retained as visual proof.
- Cleanup now returns `status=closed` with service-level verified-close action when the workbench close result reports `IsAlive=false`.
