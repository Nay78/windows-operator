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
| `auth_run_not_found` | `notFound` | false | Microsoft authentication handoff run id was not found. |
| `auth_unavailable` | `unavailable` | true | Microsoft authentication browser handoff cannot run. |
| `blank_capture` | `conflict` | true | Capture produced a blank image. |
| `browser_session_not_found` | `notFound` | false | Owned Edge browser session id was not found. |
| `dev_automation_disabled` | `permission` | false | Development automation is disabled by configuration. |
| `dev_automation_validation_failed` | `validation` | false | Development automation request shape is invalid. |
| `dev_raw_js_disabled` | `permission` | false | Raw JavaScript evaluation is disabled. |
| `elevated_target` | `permission` | false | Target window runs elevated and cannot be automated by v1. |
| `internal_error` | `internal` | true | Unexpected server failure; use the correlation id for log lookup. |
| `invalid_request` | `validation` | false | HTTP body, route value, or query value could not be bound to the documented request. |
| `locked_desktop` | `unavailable` | true | Desktop session or Desktop Agent is unavailable. |
| `onedrive_config_conflict` | `conflict` | false | OneDrive Files-On-Demand configuration ETag is stale. |
| `onedrive_content_changed` | `conflict` | false | OneDrive file identity or expected content changed during a lease. |
| `onedrive_dehydration_failed` | `conflict` | false | Local OneDrive dehydration failed; bytes remain resident for safety. |
| `onedrive_dehydration_timeout` | `timeout` | true | Local OneDrive dehydration did not reach verified online-only state before timeout. |
| `onedrive_file_not_found` | `notFound` | false | Requested file under a configured OneDrive root was not found. |
| `onedrive_hydration_failed` | `unavailable` | true | OneDrive could not materialize the requested file locally. |
| `onedrive_hydration_timeout` | `timeout` | true | OneDrive hydration did not complete before timeout or cancellation. |
| `onedrive_idempotency_conflict` | `conflict` | false | A request id was reused with a different request body. |
| `onedrive_lease_conflict` | `conflict` | false | OneDrive lease lifecycle operation is invalid for its current state. |
| `onedrive_lease_not_found` | `notFound` | false | OneDrive lease id was not found. |
| `onedrive_path_blocked` | `validation` | false | OneDrive path escaped the configured root or violated path policy. |
| `onedrive_policy_denied` | `permission` | false | OneDrive operation was rejected by configured local policy. |
| `onedrive_reclaim_not_found` | `notFound` | false | OneDrive reclaim run id was not found. |
| `onedrive_root_not_found` | `notFound` | false | Configured OneDrive root id was not found or disabled. |
| `onedrive_unavailable` | `unavailable` | true | OneDrive process, root, or Files-On-Demand session is unavailable. |
| `onedrive_verification_failed` | `conflict` | false | OneDrive local state could not be verified after an operation. |
| `mail_folder_not_found` | `notFound` | false | Outlook folder path was not found. |
| `mail_run_not_found` | `notFound` | false | Mail run id was not found. |
| `mail_unavailable` | `unavailable` | true | Outlook automation is unavailable. |
| `minimized_rdp` | `unavailable` | true | Desktop session is minimized or not presentable. |
| `method_not_allowed` | `validation` | false | Route exists, but the requested HTTP method is not supported. |
| `openapi_namespace_not_found` | `notFound` | false | OpenAPI namespace was not found. |
| `openapi_surface_invalid` | `validation` | false | OpenAPI namespace surface filter is invalid. |
| `power_automate_mcp_unavailable` | `unavailable` | true | Power Automate MCP bridge, browser token, or cloud dependency is unavailable. |
| `power_automate_mcp_validation_failed` | `validation` | false | Power Automate MCP request or flow definition failed validation. |
| `powerpoint_job_not_found` | `notFound` | false | PowerPoint job or staged job artifact was not found. |
| `powerpoint_session_not_found` | `notFound` | false | PowerPoint Online session id was not found. |
| `powerpoint_unavailable` | `unavailable` | true | PowerPoint automation is unavailable. |
| `powerpoint_validation_failed` | `validation` | false | PowerPoint request failed validation. |
| `route_not_found` | `notFound` | false | Requested API route does not exist. |
| `unsupported_control` | `validation` | false | UI Automation target does not expose a supported path. |
| `uipi_blocked` | `permission` | false | Windows blocked input across integrity boundary. |
| `window_not_found` | `notFound` | true | Window handle no longer exists. |
| `workbench_session_not_found` | `notFound` | false | Workbench session id was not found. |
