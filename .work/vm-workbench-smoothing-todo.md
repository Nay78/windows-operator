# VM Workbench Smoothing Todo

Status: partial.

Implemented slice:

- `GET /v1/desktop/foreground`
- `POST /v1/desktop/screenshot`
- `POST /v1/browser/edge/open-url`
- `POST /v1/browser/edge/session/{sessionId}/screenshot`
- `POST /v1/browser/edge/session/{sessionId}/cleanup`

Live smoke on 2026-06-16 proved foreground screenshot, Edge open-url screenshot,
Edge session screenshot, Edge cleanup, and title-miss negative path through Host
REST on `127.0.0.1:43117`.

Remaining scope:

- generic sessions
- blocker detection
- PowerPoint URL open diagnostics
- owned PowerPoint cleanup
- richer run artifact layout: `events.jsonl`, `windows.json`, `state.json`,
  `requests/*.json`, `responses/*.json`

## Context

PPTX live testing on 2026-06-16 showed Windows Operator works for local exchange-path PowerPoint COM edits, but VM interaction still has too much manual plumbing:

- list windows
- choose `hwnd`
- call screenshot endpoint
- decode base64
- save PNG manually
- inspect image manually
- track Edge/PowerPoint process ownership
- close leftover app processes manually

SharePoint file tested:

```text
https://aminerals.sharepoint.com/sites/FdDGOMGDM/Documentos%20compartidos/Foro%20Prog.%20Operativa%20Diaria/20260615_FORO%20PROGRAMACION%20OPERATIVA%20DIARIO%20GMIN%20-%2015%20Junio%20TB%20al%2016%20Junio%20TA.pptx?web=1
```

Observed:

- Edge `Work` profile opened the deck successfully in PowerPoint web.
- Screenshot showed editable PowerPoint web, file title loaded, slide `1 of 124`.
- Desktop PowerPoint COM `presentationUrl` inspect failed with HTTP `423`, detail `0x800A01A8`, and left a blank PowerPoint window.
- Local exchange-path PPTX COM smoke passed for inspect, `setText`, and `replaceImage`.
- After COM operations PowerPoint may stay open and need owned-process cleanup.

## Goal

Add a Windows Operator workbench layer that hides low-level VM interaction details from downstream projects.

Downstream callers should ask for intent:

```text
open URL in Edge work profile
capture foreground/window/session screenshot
detect blockers
cleanup owned session processes
open/diagnose PowerPoint URL
```

They should not handle raw `hwnd`, base64 screenshots, ad hoc PID cleanup, or Office modal taxonomy.

## Proposed API

### Desktop

```text
GET  /v1/desktop/foreground
POST /v1/desktop/screenshot
GET  /v1/desktop/blockers
```

`POST /v1/desktop/screenshot` input:

```json
{
  "target": "foreground | hwnd | title",
  "hwnd": 123,
  "titleContains": "PowerPoint",
  "label": "sharepoint-open",
  "saveArtifact": true
}
```

Return:

```json
{
  "success": true,
  "artifactPath": "Z:\\operator-exchange\\runs\\...\\sharepoint-open.png",
  "hostArtifactPath": "/var/lib/windows-server/shared/operator-exchange/runs/.../sharepoint-open.png",
  "hwnd": 123,
  "title": "PowerPoint",
  "pixelWidth": 1296,
  "pixelHeight": 776,
  "backend": "PrintWindow",
  "capturedAtUtc": "..."
}
```

### Sessions

```text
POST /v1/sessions
GET  /v1/sessions/{sessionId}
POST /v1/sessions/{sessionId}/screenshot
POST /v1/sessions/{sessionId}/cleanup
```

Session metadata:

- `sessionId`
- `kind`
- `createdAtUtc`
- `ownedProcessIds`
- `hwnds`
- `title`
- `url`
- `artifactRoot`
- `actions`
- `warnings`
- `errors`

Cleanup must close only owned processes/windows.

### Browser

Keep existing Edge session endpoints, but add artifact-aware screenshot and cleanup shortcuts.

Useful shortcut:

```text
POST /v1/browser/edge/open-url
```

Input:

```json
{
  "url": "https://...",
  "profileMode": "Work",
  "waitSeconds": 12,
  "capture": true,
  "sessionId": "optional"
}
```

Return current state plus optional screenshot artifact.

### PowerPoint

Add diagnostic open endpoint:

```text
POST /v1/powerpoint/open
```

Input:

```json
{
  "presentationUrl": "https://...",
  "presentationPath": "C:\\...",
  "exchangePath": "deck.pptx",
  "readOnly": true,
  "capture": true,
  "closeAfter": true
}
```

Return:

- opened/not opened
- presentation name/path if available
- slide count if available
- COM error code/detail
- visible PowerPoint windows
- detected blockers
- screenshot artifact path
- owned process ids
- cleanup result when `closeAfter=true`

## Blocker Detection

Implement a small detector over visible windows and UIA elements.

Initial blockers:

- Office sign-in required
- license dialogs
- file locked/read-only prompts
- unsaved changes prompts
- upload pending/sync prompts
- PowerPoint blank shell after failed URL open
- browser auth/login page

Return normalized records:

```json
{
  "kind": "office_sign_in | license_dialog | save_prompt | blank_powerpoint | browser_login | unknown",
  "severity": "info | warning | blocking",
  "window": { "hwnd": 123, "title": "..." },
  "recommendedAction": "close | authenticate | retry_with_exchange_path | inspect_browser"
}
```

## Artifact Layout

Use exchange run directory:

```text
Z:\operator-exchange\runs\<run-id>\
/var/lib/windows-server/shared/operator-exchange/runs/<run-id>/
```

Files:

- `events.jsonl`
- `windows.json`
- `state.json`
- `screenshots/<label>.png`
- `requests/*.json`
- `responses/*.json`

## Acceptance

- One call can capture foreground screenshot and return both Windows and host artifact paths.
- Edge work-profile open can return title, URL, screenshot artifact, and cleanup handle.
- Edge session cleanup closes owned Edge session windows.
- Generic session cleanup closes only owned Edge/PowerPoint processes.
- PowerPoint URL open failure returns structured diagnostics instead of only `0x800A01A8`.
- Blank PowerPoint shell is detected as a blocker/diagnostic state.
- Downstream PPTX tests no longer need to manually decode screenshot base64 for desktop/Edge screenshots.
- Existing low-level endpoints keep working for compatibility.

## Notes For PPTX

After this lands upstream, PPTX smoke should call intent-level helpers:

```text
open SharePoint deck in Edge work profile
capture evidence screenshot
inspect/edit local exchange-path deck through PowerPoint COM
cleanup owned session
```

PPTX should not own VM workbench/session logic.
