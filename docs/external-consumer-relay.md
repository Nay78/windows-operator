# External Consumer Relay Guide

Host REST stays loopback-only by default:

```text
127.0.0.1:43117
```

Use a trusted relay when callers outside the Windows machine need access.

## Relay Responsibilities

Relay owns:

- authentication
- authorization
- TLS and public bind
- route allowlist
- rate limiting
- audit logging
- secret redaction
- optional artifact URL rewriting

Relay must not own:

- Windows automation logic
- Office.js, COM, UIA, DevTools, or browser behavior
- Operator domain error translation
- retries that hide terminal `OperatorError` responses

## Route Allowlist

Default external app allowlist:

```text
GET  /v1/health
GET  /v1/capabilities
GET  /v1/artifacts/{artifactId}
GET  /v1/runs/{runId}/artifacts
POST /v1/powerpoint/online/updates
POST /v1/mail/folders
GET  /v1/mail/status
POST /v1/mail/messages/search
POST /v1/mail/attachments/download
GET  /v1/mail/runs/{runId}
```

Keep `/v1/dev/*`, `status/latest`, and PowerPoint add-in probe/run-pending-job
routes out of ordinary app relays unless an operator runbook explicitly needs
them.

## Auth Model

Recommended shape:

```text
external caller -> relay auth -> route allowlist -> http://127.0.0.1:43117
```

Use short-lived bearer tokens or mTLS. Bind relay authorization to route,
method, caller, and target workflow. Treat artifact routes as private data.

## Redaction

Relay logs must redact:

- `Authorization` headers
- cookies
- OAuth tokens
- Microsoft device codes
- passwords
- raw mailbox contents
- attachment content
- PowerPoint deck URLs when tenant policy requires it

Log `OperatorError.code`, `category`, `retryable`, `correlationId`, HTTP
status, method, route template, and duration.

## Artifact URLs

Host returns relative artifact `href` values:

```json
{ "href": "/v1/artifacts/opaque-id" }
```

Relay may rewrite those to its public base URL. It must not expose or require
`path`, `hostPath`, `absolutePath`, exchange-root layout, or Windows paths.

## Rate Limits

Use tighter limits for mutation routes than read routes. Recommended defaults:

```text
GET  /v1/health:                  60/min/caller
GET  /v1/capabilities:            30/min/caller
GET  /v1/artifacts/*:             120/min/caller
POST /v1/powerpoint/online/*:      6/min/caller
POST /v1/mail/*:                  12/min/caller
```

Return relay-owned `429` without changing Host `OperatorError` payloads from
upstream responses.
