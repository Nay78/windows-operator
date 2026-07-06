# External Consumer Integration Spec

## Purpose

Make Windows Operator useful from another project without copying harness logic,
shelling out to repo scripts, or depending on machine-local Windows state.

The target integration boundary is:

```text
external project -> generated client or REST -> authenticated relay if needed -> Host REST -> Agent -> Windows app
```

Host REST and OpenAPI are the product contract. CLI, Just recipes, SSH runner
scripts, and staged PowerShell are operator tooling.

## Current State

- Host publishes REST on `127.0.0.1:43117`.
- Host serves `GET /openapi.json`.
- Committed OpenAPI spec lives at `openapi/windows-operator.openapi.json`.
- Generated Go client lives under `clients/go`.
- `scripts/linux/wo` exists for operator flows and smoke evidence.

## Target Contract

External consumers depend only on:

- Host REST routes under `/v1/*`.
- `GET /openapi.json` for live contract inspection.
- Committed OpenAPI specs pinned to a commit or release tag.
- Generated clients published from the same pinned spec.
- Stable result/error semantics documented in this file and OpenAPI.

External consumers must not depend on:

- `scripts/linux/wo`.
- `Justfile` recipes.
- SSH runner scripts.
- staged PowerShell.
- Windows repo paths.
- Task Scheduler names.
- exchange-root or `%LOCALAPPDATA%` layout.
- COM, UIA, DevTools, Office.js, or browser profile details.

## REST Surface Classes

Stable routes:

- Normal `/v1/*` business and runtime routes.
- Routes in generated clients by default.
- Covered by compatibility and release policy.

Diagnostic routes:

- Routes intended for readiness, recovery, or operator evidence.
- May be used by external CI/runbooks.
- Should not be required for normal application runtime paths.

Development routes:

- `/v1/dev/...`.
- Disabled by default.
- Excluded from external-consumer support unless explicitly promoted.

OpenAPI should mark route class through tags or vendor extensions:

```json
{
  "x-windows-operator-surface": "stable"
}
```

Allowed values:

- `stable`
- `diagnostic`
- `development`

## Versioning

Use SemVer for the external contract after the first release tag.

- MAJOR: breaking REST/OpenAPI or generated-client change.
- MINOR: backwards-compatible routes, fields, enum values, or helpers.
- PATCH: bug fixes, docs, implementation changes with no contract change.

`openapi.info.version` must match the released contract version. Development
builds may use a pre-release suffix.

Explicit contract version source:

```text
src/WindowsOperator.Core/Contracts/OperatorContractVersion.cs
```

Breaking changes include:

- removing or renaming a route, request field, result field, enum value, or
  error code
- changing a required field to optional when callers depend on presence
- changing an optional field to required
- changing field type, format, units, or default behavior
- changing HTTP status/error semantics
- replacing stable artifact IDs or URLs with local paths

Non-breaking changes include:

- adding optional request fields
- adding result fields
- adding enum values when callers are documented to tolerate unknown values
- adding new routes
- adding generated-client helpers without changing raw generated types

## Contract Drift Gate

Before an external integration test run, consumers may compare their pinned
contract to the live Host contract:

```bash
curl http://127.0.0.1:43117/openapi.json > live.openapi.json
```

Project release gates must compare generated output against source:

```bash
scripts/check-openapi-contract.sh
scripts/generate-go-client.sh
cd clients/go && go test ./...
cd ../..
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build
git diff --check
git status --short
```

Target release gate additions:

- `scripts/check-openapi-contract.sh` includes offline hook points for OpenAPI
  lint via `WINDOWS_OPERATOR_LINT_CMD`.
- `scripts/check-openapi-contract.sh` includes breaking-change hook shape via
  `WINDOWS_OPERATOR_BREAKING_CMD` and `WINDOWS_OPERATOR_PREVIOUS_TAG`.
- Live Host `GET /openapi.json` parity check when Windows runtime is available.

Release checklist:

```text
docs/external-consumer-release.md
```

## Error Contract

All product errors should use `OperatorError`:

```json
{
  "code": "PowerPointValidationFailed",
  "message": "PowerPoint edit request is invalid.",
  "remediation": "Inspect the presentation, fix selectors or paths, then retry.",
  "details": {
    "detail": "Target TITLE_MAIN was not found."
  }
}
```

Current branchable fields:

- `correlationId`: stable ID for logs and support.
- `retryable`: boolean.
- `category`: one of `validation`, `unavailable`, `conflict`, `timeout`,
  `internal`, `permission`, `notFound`.

Callers should branch on `code`, not `message`.

Stable code table:

```text
docs/operator-error-codes.md
```

## Long-Running Work

External automation should use explicit IDs:

- `runId` for auth, mail, and evidence workflows.
- `jobId` for PowerPoint queue/update workflows.
- `sessionId` for browser, workbench, and PowerPoint session lifecycle.

`status/latest` routes are manual convenience only. Concurrent consumers must
use explicit IDs.

Target status shape:

```json
{
  "id": "ppt-update-20260706T120000Z",
  "kind": "powerpoint.update",
  "status": "running",
  "startedAtUtc": "2026-07-06T12:00:00Z",
  "updatedAtUtc": "2026-07-06T12:00:10Z",
  "result": null,
  "error": null
}
```

Each long-running route should document:

- accepted terminal statuses
- polling route
- timeout behavior
- cleanup behavior
- idempotency rules for reused IDs

## Artifact Contract

External consumers should receive opaque artifact refs, not machine-local paths.

Target shape:

```json
{
  "artifactId": "opaque-id",
  "href": "/v1/artifacts/opaque-id",
  "mediaType": "image/jpeg",
  "bytes": 123456,
  "sha256": "hex",
  "createdAtUtc": "2026-07-06T12:00:00Z",
  "expiresAtUtc": null
}
```

Target routes:

```text
GET /v1/artifacts/{artifactId}
GET /v1/runs/{runId}/artifacts
```

Rules:

- `artifactId` is opaque to consumers.
- `href` is relative to the Host or relay base URL.
- Local `path`, `hostPath`, `absolutePath`, and exchange layout are debug
  metadata only and must not be required for application runtime use.
- Artifact content routes set `Content-Type`, `Content-Length` when known, and
  cache headers appropriate for local/private data.
- Sensitive artifacts must not be exposed through a broad unauthenticated relay.

## Capability Discovery

External projects should not infer capability from failed workflow calls.

Target route:

```text
GET /v1/capabilities
```

Target shape:

```json
{
  "contractVersion": "0.1.0",
  "host": {
    "status": "ok",
    "runtimeMode": "headless-host"
  },
  "features": {
    "powerpoint.online.update": {
      "available": true,
      "surface": "stable"
    },
    "mail.outlook.download": {
      "available": true,
      "surface": "stable"
    }
  }
}
```

Feature names should be domain-scoped and stable.

## Relay Contract

Host should remain loopback-only by default.

External services outside the Windows machine should access Host through an
authenticated relay or a trusted local caller. Relay owns:

- authentication
- authorization
- route allowlist
- rate limiting
- TLS/public bind
- audit logging
- secret redaction

Relay should not own:

- Windows automation logic
- Office.js or COM behavior
- request/result translation beyond auth, base URL, and artifact URL rewriting
- retries that hide Operator terminal errors

Relay logs must redact:

- tokens
- cookies
- authorization headers
- device codes
- passwords
- raw mailbox contents unless explicitly enabled

Relay guide:

```text
docs/external-consumer-relay.md
```

## Generated Clients

Go is the first supported generated client.

Raw generated clients must preserve OpenAPI shape. Hand-written helpers are
allowed only when they hide repeated consumer complexity:

- `DecodeOperatorError`
- `WaitForRun`
- `WaitForPowerPointJob`
- `DownloadArtifact`
- `CheckContractVersion`

Helpers should live beside the generated client and must not replace raw route
access.

Client docs should include:

- install from release tag
- base URL setup
- health/capability check
- error decoding
- PowerPoint update
- mail attachment download
- artifact download
- contract drift check

First Go client docs:

```text
clients/go/README.md
```

## Consumer Workflows

### Health

1. Call `GET /v1/health`.
2. Verify `status=ok`.
3. Optionally compare `restBaseUrl` with configured base URL.
4. For feature-level readiness, call `GET /v1/capabilities`.

### PowerPoint Update

1. Construct `PowerPointOnlineUpdateRequest`.
2. Pass explicit `sessionId` and `job.jobId`.
3. Use `/v1/powerpoint/online/updates` for normal edits.
4. Inspect `success`, `status`, `saveProofTier`, `warnings`, and `errors`.
5. Fetch evidence through artifact refs, not local paths.
6. Use session cleanup policy from the request/result.

### Mail Attachment Download

1. Pass explicit `runId`.
2. Prefer exact folder path and bounded filters.
3. Use `dryRun=true` for discovery.
4. Use `GET /v1/mail/runs/{runId}` for repeatable result reads.
5. Fetch attachments through artifact refs from the result.

## Implementation Roadmap

P0:

- Add this spec to AGENTS/README/architecture references.
- Add route surface tags or vendor extensions to OpenAPI.
- Add release/version policy and make `openapi.info.version` release-owned.
- Add Go client README and examples.
- Add contract drift and generated-client checks to release workflow.

P1:

- Add artifact ref shape and artifact download/list routes.
- Stop requiring local path fields in external-facing artifact results.
- Add documented `OperatorError` code table and target fields.
- Add `GET /v1/capabilities`.
- Add relay guide/template.

P2:

- Add optional Go helpers for errors, polling, artifacts, and version checks.
- Add additional generated clients only when a real consumer needs them.
- Add deprecation metadata and compatibility tests for stable routes.

## Acceptance

External integration is production-ready when:

- A fresh external repo can install a tagged generated client.
- It can call health, run a PowerPoint update or mail download, and retrieve
  artifacts without referencing this repo's scripts or paths.
- Contract drift fails clearly before integration tests mutate Windows state.
- Errors are branchable by stable code.
- Runtime access can be exposed through a documented authenticated relay.
- Breaking changes require explicit version/release action.
