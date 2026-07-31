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
- The pre-release contract remains `0.1.0`. The `1.0.0` version begins only
  after the comprehensive v1 acceptance gates and explicit release approval.
- The current contract contains 67 operations: 55 stable, 10 diagnostic, and 2
  development.

## Target Contract

External consumers depend only on:

- Host REST routes under `/v1/*`.
- `GET /openapi.json` for live contract inspection.
- `GET /openapi/namespaces` and namespace specs for bounded discovery.
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

OpenAPI tags carry bounded discovery namespaces such as `mail.outlook` and
`powerpoint.online`. OpenAPI marks route class with a vendor extension:

```json
{
  "tags": ["mail.outlook"],
  "x-windows-operator-namespace": "mail.outlook",
  "x-windows-operator-surface": "stable"
}
```

Allowed values:

- `stable`
- `diagnostic`
- `development`

Namespace discovery:

```text
GET /openapi/namespaces
GET /openapi/namespaces/{namespace}.json?surface=stable
```

The namespace spec defaults to stable operations. `surface` accepts
`stable`, `diagnostic`, `development`, `all`, or comma-separated values such as
`stable,diagnostic`.

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
curl -fsS http://127.0.0.1:43117/v1/capabilities
curl -fsS http://127.0.0.1:43117/openapi.json > live.openapi.json
diff -u openapi/windows-operator.openapi.json live.openapi.json
```

Project release gates must compare generated output against source:

```bash
scripts/check-openapi-contract.sh
scripts/check-readme-route-inventory.sh
scripts/generate-go-client.sh
cd clients/go && go test ./...
cd ../..
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build
git diff --check
git status --short
```

Current release gate support:

- `scripts/check-openapi-contract.sh` includes offline hook points for OpenAPI
  lint via `WINDOWS_OPERATOR_LINT_CMD`.
- `scripts/check-openapi-contract.sh` includes breaking-change hook shape via
  `WINDOWS_OPERATOR_BREAKING_CMD` and `WINDOWS_OPERATOR_PREVIOUS_TAG`.
- `scripts/check-readme-route-inventory.sh` verifies public route docs cover
  every committed OpenAPI method/path entry.
- Live Host exact `GET /openapi.json` parity check when Windows runtime is
  available.

Release checklist:

```text
docs/external-consumer-release.md
```

## Error Contract

All product errors should use `OperatorError`:

```json
{
  "code": "powerpoint_validation_failed",
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

## Normative Runtime Semantics

The operation policy at
`openapi/windows-operator.operation-policy.json` owns operation-specific
lifecycle, idempotency, retry, timeout, cancellation, concurrency, exposure,
sensitivity, fixture, cleanup, and proof requirements. These rules govern all
published operations:

- `CONTRACT.VERSION`: `/v1/capabilities`, live OpenAPI, committed OpenAPI, and
  generated clients identify one contract version. Executable build identity
  is separate and recorded with live evidence.
- `CONTRACT.ERROR`: every non-success product response uses `OperatorError`.
  Callers branch on `code`, `category`, and `retryable`; they never parse
  `message`.
- `CONTRACT.IDS`: consumers provide explicit `runId`, `jobId`, or `sessionId`
  where exposed. Reuse follows the operation policy's `idempotency` value.
- `CONTRACT.RETRY`: callers retry only safe reads, explicitly idempotent
  cleanup, caller-keyed requests, or typed retryable failures as allowed by the
  operation policy. No blind retry follows an unknown mutation outcome.
- `CONTRACT.TIMEOUT`: server work is bounded by each operation's
  `timeoutPolicy`. Client cancellation stops request waiting; it does not imply
  rollback unless the operation documents cancellation semantics.
- `CONTRACT.POLLING`: concurrent consumers poll by explicit ID. Terminal status
  or typed error ends polling; `status/latest` is operator convenience, not a
  concurrency contract.
- `CONTRACT.CONCURRENCY`: serialization scope is explicit per desktop, session,
  atomic job claim, parallel read, or independent request. Callers must not
  infer locking from timing.
- `CONTRACT.ARTIFACT`: artifact IDs are opaque. Consumers use `href`, validate
  declared byte length and SHA-256 where present, and treat content as private.
- `CONTRACT.LISTS`: current list/search operations are bounded by request
  filters or server limits and do not expose a pagination-token contract.
  Adding pagination later must be additive.
- `CONTRACT.CAPABILITY`: consumers inspect `/v1/capabilities` before optional
  workflows. An unavailable feature returns a typed error; absence is never
  inferred from message text.
- `CONTRACT.SURFACE`: stable operations receive v1 compatibility guarantees.
  Diagnostic and development operations remain typed and tested but are
  excluded from ordinary relay allowlists and stable generated clients.

Mechanical traceability:

- policy coverage: `python3 scripts/check-operation-policy.py`
- contract/client drift: `scripts/check-openapi-contract.sh`
- live proof: `scripts/linux/v1-contract-conformance.py`
- release gates: `docs/external-consumer-release.md`

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
  "build": {
    "informationalVersion": "1.0.0+abcdef123456",
    "assemblyVersion": "1.0.0.0",
    "sourceRevision": "abcdef123456"
  },
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

`contractVersion` identifies API compatibility. `build` identifies executable
serving request. `sourceRevision` is `unavailable` when build does not embed
revision; field is never omitted. Record full `build` object with live
conformance evidence.

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

## Implementation Status

Complete:

- This spec is linked from AGENTS, README, and harness architecture docs.
- OpenAPI operations carry tags and `x-windows-operator-surface`.
- OpenAPI operations carry `x-windows-operator-namespace`, namespace tags, and
  namespace-filtered spec routes.
- `openapi.info.version` is sourced from `OperatorContractVersion.Value`.
- `clients/go/README.md` documents generated-client usage.
- Contract drift and generated-client checks are in release docs and
  `scripts/check-openapi-contract.sh`.
- External-facing results use artifact refs, with list/download routes for run
  artifacts.
- `OperatorError` branch fields and target codes are documented.
- `GET /v1/capabilities` exposes contract version, executable build identity,
  and feature availability.
- `docs/external-consumer-relay.md` documents the relay boundary.
- Go helpers cover errors, artifact download, polling, and contract-version
  checks.
- The operation policy owns all 67 operations and their surface, semantics,
  fixtures, cleanup, gates, and live-evidence state.
- The first-party authenticated relay template and deterministic tests exist.
- Stable-only Go generation excludes diagnostic and development operations.

Release-blocking:

- Live proof is 44/67 verified. The 39-operation safe sweep, reversible
  raw-JavaScript proof, and four cached Microsoft-auth status reads pass against
  the Legion runtime. Raw JavaScript returned to disabled-by-default. The
  remaining 23 operations need credentials, tenant content, or
  consequential-mutation approval.
- RC-pinned fresh-consumer proof cannot run before the frozen RC exists.
- Relay deployment, credential provisioning, and release tag/push require
  explicit operator authority.

Deferred:

- Additional generated clients beyond Go.
- Post-release compatibility maintenance beyond the v1 baseline.

## Acceptance

External integration is production-ready when:

- All 67 published operations meet their operation-level live proof rows.
- A fresh external repo can install the tagged `v1.0.0` generated client.
- It can call health, run a PowerPoint update or mail download, and retrieve
  artifacts without referencing this repo's scripts or paths.
- Contract drift fails clearly before integration tests mutate Windows state.
- Errors are branchable by stable code.
- Runtime access can be exposed through a documented authenticated relay.
- Breaking changes require explicit version/release action.
