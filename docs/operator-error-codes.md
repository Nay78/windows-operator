# Operator Error Codes

External consumers branch on `code`, not `message`.

Every HTTP `OperatorError` response may include:

- `correlationId`: support/log lookup id for this failure response.
- `retryable`: whether retrying the same operation can reasonably succeed.
- `category`: `validation`, `unavailable`, `conflict`, `timeout`,
  `internal`, `permission`, or `notFound`.

## Codes

| Code | Category | Retryable | Meaning |
| --- | --- | --- | --- |
| `artifact_not_found` | `notFound` | false | Opaque artifact id or run artifact does not exist. |
| `auth_unavailable` | `unavailable` | true | Microsoft authentication browser handoff cannot run. |
| `blank_capture` | `conflict` | true | Capture produced a blank image. |
| `dev_automation_disabled` | `permission` | false | Development automation is disabled by configuration. |
| `dev_automation_validation_failed` | `validation` | false | Development automation request shape is invalid. |
| `dev_raw_js_disabled` | `permission` | false | Raw JavaScript evaluation is disabled. |
| `elevated_target` | `permission` | false | Target window runs elevated and cannot be automated by v1. |
| `locked_desktop` | `unavailable` | true | Desktop session or Desktop Agent is unavailable. |
| `mail_folder_not_found` | `notFound` | false | Outlook folder path was not found. |
| `mail_run_not_found` | `notFound` | false | Mail run id was not found. |
| `mail_unavailable` | `unavailable` | true | Outlook automation is unavailable. |
| `minimized_rdp` | `unavailable` | true | Desktop session is minimized or not presentable. |
| `openapi_namespace_not_found` | `notFound` | false | OpenAPI namespace was not found. |
| `openapi_surface_invalid` | `validation` | false | OpenAPI namespace surface filter is invalid. |
| `powerpoint_job_not_found` | `notFound` | false | PowerPoint job or staged job artifact was not found. |
| `powerpoint_unavailable` | `unavailable` | true | PowerPoint automation is unavailable. |
| `powerpoint_validation_failed` | `validation` | false | PowerPoint request failed validation. |
| `unsupported_control` | `validation` | false | UI Automation target does not expose a supported path. |
| `uipi_blocked` | `permission` | false | Windows blocked input across integrity boundary. |
| `window_not_found` | `notFound` | true | Window handle no longer exists. |
