# PowerPoint Online Slide Navigation Hardening

Date: 2026-07-04

## Scope

Harden `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select` so callers can trust the selected slide instead of trusting a DOM click or thumbnail coordinate dispatch.

## Changes

- `PowerPointOnlineService.SelectOnlineSlideAsync` now observes the selected slide after DOM/thumbnail click.
- The result is verified against the requested slide number before returning success.
- If a thumbnail click selects a nearby slide, the service sends bounded `pageup` or `pagedown` keys and verifies again.
- Out-of-range, unobserved, and mismatched selections return explicit actions/warnings/errors instead of silent success.
- `HotkeyInputService` now accepts `pageup`, `page_up`, `pgup`, `pagedown`, `page_down`, and `pgdn`.

## Unit Proof

- Local build: `dotnet build tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj`
- Windows focused test run: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T091903Z-619656/result.json`
- Result: `Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28`.

## Live Proof

Run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z`

Start:

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"deckUrl":"https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1","sessionId":"ppt-slide-nav-20260704t0921z","capture":false,"waitSeconds":40,"runId":"ppt-slide-nav-20260704t0921z"}' \
  http://127.0.0.1:43117/v1/powerpoint/online/sessions
```

Select:

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"slideNumber":4,"capture":true,"waitSeconds":2,"label":"slide-nav-4"}' \
  http://127.0.0.1:43117/v1/powerpoint/online/sessions/ppt-slide-nav-20260704t0921z/slides/select
```

Observed result:

- `success=true`
- `status=ready`
- `currentSlide=4`
- `slideCount=71`
- `editMode=editing`
- `saveState=saved`
- actions included `slide_select_dom_unavailable:4`, `slide_select_thumbnail_click:4:132:675`, and `slide_select_verified:4`
- screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide-nav-20260704t0921z/screenshots/slide-nav-4.png`
- cleanup returned `success=true`, `status=closed`
- final `/v1/windows` Edge/Chrome widget count: `0`

## Scope Limit

This proof covers non-mutating slide navigation and evidence capture. Editable target mutation, save persistence, and reopen verification are covered by the later SEM27 final proof: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.
