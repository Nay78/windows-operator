# PowerPoint Automation Target Architecture

Goal: let external services request precise PowerPoint slide edits through a stable REST API while Windows Operator hides PowerPoint COM, desktop session, file handling, and validation details.

## Decision

Use PowerPoint desktop COM for PowerPoint-native edits that need the actual Office object model. Do not automate browser clicks for slide mutation.

Microsoft Office automation must run in the logged-in interactive desktop session. The Headless Host must not create PowerPoint COM objects. Host proxies requests to the Desktop Agent, and the Agent owns all COM work.

Use Open XML later for safe server-side edits when the requested operation can be done without opening PowerPoint. Keep the edit-plan contract stable across both implementations.

## Runtime Topology

```text
external service
  -> authenticated Linux/local relay
  -> Windows Host REST 127.0.0.1:43117
  -> Desktop Agent REST 127.0.0.1:43119
  -> PowerPoint COM in logged-in desktop session
  -> presentation save/export
```

Rules:

- Host route is loopback-only and performs validation, proxying, and timeout policy.
- Agent route performs desktop/session checks and owns PowerPoint COM lifetime.
- One PowerPoint COM operation runs at a time per desktop user.
- Long-running jobs write status and artifacts under `%LOCALAPPDATA%\WindowsOperator` and `Z:\operator-exchange` only when Linux needs results.
- Browser automation is allowed for opening/auth handoff only, not for editing slides.

## REST Namespace

Use the `powerpoint` domain namespace:

```text
POST /v1/powerpoint/inspect
POST /v1/powerpoint/edit
GET  /v1/powerpoint/jobs/{jobId}
```

Do not add MCP tools unless AI runtimes need direct PowerPoint mutation. External services should call REST.

## Inspect Flow

`POST /v1/powerpoint/inspect` opens or downloads a presentation, reads stable slide and shape metadata, and returns an inventory that callers can use to build edit plans.

Request:

```json
{
  "presentationUrl": "https://tenant.sharepoint.com/sites/team/report.pptx",
  "includeText": true,
  "includeHidden": false
}
```

Result:

```json
{
  "success": true,
  "presentation": {
    "name": "report.pptx",
    "slideCount": 12
  },
  "slides": [
    {
      "index": 4,
      "slideId": 257,
      "title": "Executive Summary",
      "tags": {
        "WO_SLIDE": "EXEC_SUMMARY"
      },
      "shapes": [
        {
          "id": 12,
          "name": "Summary.Status",
          "type": "TextBox",
          "tags": {
            "WO_FIELD": "STATUS"
          },
          "hasText": true,
          "hasTable": false,
          "hasChart": false,
          "visible": true
        }
      ]
    }
  ],
  "warnings": [],
  "errors": []
}
```

## Edit Plan Format

`POST /v1/powerpoint/edit` accepts a typed edit plan. The plan describes intent and targets, not COM paths.

Request:

```json
{
  "presentationUrl": "https://tenant.sharepoint.com/sites/team/report.pptx",
  "mode": "powerpointDesktopCom",
  "dryRun": true,
  "saveMode": "overwrite",
  "edits": [
    {
      "id": "summary-status",
      "op": "replaceText",
      "target": {
        "slide": {
          "tag": {
            "WO_SLIDE": "EXEC_SUMMARY"
          }
        },
        "shape": {
          "tag": {
            "WO_FIELD": "STATUS"
          }
        }
      },
      "find": "{{STATUS}}",
      "value": "On track",
      "assert": {
        "exactlyOneTarget": true
      }
    },
    {
      "id": "daily-chart-image",
      "op": "replaceImage",
      "target": {
        "slide": {
          "slideId": 257
        },
        "shape": {
          "name": "Summary.Chart.Daily"
        }
      },
      "imagePath": "Z:\\operator-exchange\\charts\\daily.png",
      "fit": "contain"
    }
  ]
}
```

Result:

```json
{
  "success": true,
  "dryRun": true,
  "jobId": "20260501-153012-8f5c",
  "presentationPath": "C:\\Users\\operator\\AppData\\Local\\WindowsOperator\\run\\powerpoint\\work\\report.pptx",
  "edits": [
    {
      "id": "summary-status",
      "matched": 1,
      "changed": 1,
      "before": "{{STATUS}}",
      "after": "On track",
      "warnings": [],
      "errors": []
    }
  ],
  "warnings": [],
  "errors": []
}
```

## Targeting Model

Prefer targets in this order:

1. Slide tag and shape tag.
2. Slide ID and shape name.
3. Slide title and shape name.
4. Slide index only for temporary or generated decks.
5. Text search only with explicit `assert.exactlyOneTarget`.

Supported slide selectors:

```json
{ "slideId": 257 }
{ "index": 4 }
{ "title": "Executive Summary" }
{ "tag": { "WO_SLIDE": "EXEC_SUMMARY" } }
```

Supported shape selectors:

```json
{ "name": "Summary.Status" }
{ "id": 12 }
{ "tag": { "WO_FIELD": "STATUS" } }
{ "altText": "status field" }
{ "textContains": "{{STATUS}}" }
```

Template rule: name important shapes in the PowerPoint Selection Pane and tag important slides/shapes. Use names like:

```text
Summary.Title
Summary.Kpi.Production
Summary.Chart.Daily
Risks.Table.Main
```

Do not depend on coordinates, z-order, or default names like `Rectangle 3`.

## Operations

Initial supported operations:

```text
replaceText
setText
setTableCell
replaceImage
setShapeVisible
setShapeFill
exportPdf
```

Later operations:

```text
setChartData
duplicateSlide
deleteSlide
moveSlide
hideSlide
setSpeakerNotes
```

Each operation must:

- Resolve targets before mutation.
- Honor `dryRun`.
- Return per-edit match count and change count.
- Fail if an assertion is violated.
- Avoid silently editing multiple objects unless explicitly requested.

## COM Execution Rules

- Run COM work on a single STA worker inside Desktop Agent.
- Create and release PowerPoint COM objects inside the operation scope.
- Disable prompts where safe, but expect PowerPoint can still show modal UI.
- Bound every operation with a timeout and return a structured timeout error.
- Close presentations opened by the operator unless caller requests `leaveOpen`.
- Do not open untrusted macro-enabled files unless explicitly allowed.
- Never run PowerPoint COM from Host, service account, SYSTEM, or Linux.

## File Handling

Supported inputs:

- `presentationUrl`: SharePoint/OneDrive URL opened by desktop PowerPoint or resolved by future Graph helper.
- `presentationPath`: Windows-local path.
- `exchangePath`: path under `Z:\operator-exchange`.

Save modes:

```text
overwrite
copy
exportPdfOnly
```

For `copy`, write output under `%LOCALAPPDATA%\WindowsOperator\run\powerpoint\outputs` and optionally copy final artifacts to `Z:\operator-exchange`.

## Validation

Before mutation:

- Confirm Desktop Agent is running in an interactive session.
- Confirm PowerPoint installed and launchable.
- Confirm input file exists or URL can be opened.
- Resolve every edit target.
- Enforce path allowlist for local images and exports.
- Reject ambiguous targets unless `allowMultiple` is true.

After mutation:

- Save or export.
- Optionally reopen for inspection.
- Return final inventory hash and artifact paths.

## Source Notes

Useful Microsoft references:

- Office server-side automation warning: https://support.microsoft.com/en-au/topic/considerations-for-server-side-automation-of-office-48bcfe93-8a89-47f1-0bce-017433ad79e2
- `Slide.SlideID`: https://learn.microsoft.com/en-us/office/vba/api/PowerPoint.Slide.SlideID
- `Shape.Name`: https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.name
- `Shape.Tags`: https://learn.microsoft.com/en-us/office/vba/api/powerpoint.shape.tags
- `TextRange`: https://learn.microsoft.com/en-us/office/vba/api/PowerPoint.TextRange
- `Table.Cell`: https://learn.microsoft.com/en-us/office/vba/api/PowerPoint.Table
- `ChartData.Workbook`: https://learn.microsoft.com/en-us/office/vba/api/powerpoint.chartdata.workbook
