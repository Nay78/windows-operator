# External Consumer Relay Guide

Host REST stays loopback-only by default:

```text
127.0.0.1:43117
```

Use a trusted relay when callers outside the Windows machine need access.

First-party template:

```text
src/WindowsOperator.Relay
```

The relay rejects non-loopback `UpstreamBaseUrl` values. Windows Operator Host
therefore remains bound to `127.0.0.1:43117`; only the authenticated relay gets
a public listener.

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

The first-party template uses bearer-token SHA-256 digests. Plain bearer tokens
never enter configuration. Each caller owns an independent route/method
allowlist and per-route fixed-window request limit.

Create a digest without printing the token:

```bash
printf %s "$WINDOWS_OPERATOR_RELAY_TOKEN" | sha256sum
```

Copy `src/WindowsOperator.Relay/appsettings.example.json` outside shared source,
replace its placeholder digest, set the public HTTPS URL, configure the server
certificate through normal ASP.NET Core/Kestrel facilities, then start:

```bash
WINDOWS_OPERATOR_RELAY_CONFIG=/secure/path/appsettings.json \
  dotnet run --project src/WindowsOperator.Relay/WindowsOperator.Relay.csproj
```

Do not commit the resulting configuration. The digest, TLS private key, and
deployment-specific caller policy remain machine-local secrets/state.

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

First-party relay audit records only caller ID, method, configured route
template, response status, duration, and those safe `OperatorError` fields.
It never forwards relay `Authorization` or `Cookie` headers to Host and never
logs request/response bodies, concrete artifact IDs, bearer tokens, or arbitrary
headers.

## Artifact URLs

Host returns relative artifact `href` values:

```json
{ "href": "/v1/artifacts/opaque-id" }
```

Relay may rewrite those to its public base URL. It must not expose or require
`path`, `hostPath`, `absolutePath`, exchange-root layout, or Windows paths.

First-party relay recursively rewrites relative `/v1/artifacts/{id}` `href`
values in successful JSON responses. Artifact list and download routes still
require caller authentication and explicit allowlist entries.

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

## Validation

Deterministic integration coverage:

```bash
dotnet test tests/WindowsOperator.Relay.Tests/WindowsOperator.Relay.Tests.csproj
```

Coverage proves authenticated health/capabilities forwarding, unauthorized and
disallowed rejection before Host, per-caller rate limits, audit redaction,
upstream `OperatorError` passthrough, private artifact access, and public
artifact `href` rewriting.

Template inclusion in the portable build:

```bash
dotnet build WindowsOperator.Portable.slnf
```

Local live proof, 2026-07-23:

- unauthenticated and incorrect-token health returned `401 relay_unauthorized`;
- authenticated health returned `200`;
- non-allowlisted `/v1/windows` returned `403 relay_route_forbidden`;
- authenticated artifact listing returned `200`;
- artifact `href` values were rewritten to the relay base URL;
- authenticated private artifact download returned `200` and 990 bytes.

This proves template behavior against the live campaign Host through a
loopback-only relay. It does not authorize or claim public deployment.

The repository supplies code, tests, and configuration shape only. Public DNS,
TLS certificates, bearer-token provisioning, firewall changes, service
registration, and relay deployment require explicit operator authorization.
