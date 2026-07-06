# External Consumer Integration Roadmap

Date: 2026-07-06

## Objective

Bring Windows Operator to the external-consumer target defined in:

```text
docs/external-consumer-integration.md
```

Success means another repo can depend on a tagged REST/OpenAPI/generated-client
contract, call supported workflows, retrieve artifacts, and detect contract
drift without using `scripts/linux/wo`, `Justfile`, SSH runner scripts, staged
PowerShell, or machine-local Windows paths.

## Boundary

Owning boundary:

```text
Host REST + Core contracts + OpenAPI + generated clients
```

The fix belongs there because external consumers should get a stable product
surface. CLI and Just stay operator tooling. Windows, COM, UIA, Office.js,
browser, retry, artifact path, and local state details stay hidden behind Host
contracts.

## Current State

- Host REST serves `GET /openapi.json`.
- Committed spec: `openapi/windows-operator.openapi.json`.
- Go client: `clients/go`.
- Route inventory: 58 OpenAPI paths.
- Contract docs define external-consumer target.
- Route surface metadata exists on all OpenAPI operations.
- `GET /v1/capabilities` reports contract/features before workflow calls.
- `GET /v1/artifacts/{artifactId}` and `GET /v1/runs/{runId}/artifacts`
  expose opaque artifact refs.
- Go client README/helpers/smoke exist.
- Relay and release checklist docs exist.
- First release tag is not cut yet; release doc defines gates for it.

## Execution Rules

- Keep external-consumer work in REST/Core/OpenAPI/client boundary.
- Do not promote CLI/Just/script behavior into external app dependencies.
- Preserve backwards compatibility while adding target fields/routes.
- Prefer additive API changes until first explicit major release.
- Each REST behavior change needs OpenAPI regeneration and generated-client
  validation.
- Runtime-dependent behavior needs live Windows proof before done.

## Implementation Ledger

| Slice | State | Owner | Evidence |
| --- | --- | --- | --- |
| E0 Spec and references | complete | main | `docs/external-consumer-integration.md`; AGENTS/README/development/architecture references added. |
| E1 Surface metadata | complete | worker + main review | `x-windows-operator-surface` and matching tags added to all OpenAPI operations; host OpenAPI metadata test passed; `GET /v1/health` remains stable. |
| E2 Version and release gates | complete | worker + main review | Contract version source and drift gate added; `scripts/check-openapi-contract.sh`, Go client tests, portable build, host OpenAPI tests, and `git diff --check` passed. Core test execution blocked on Linux missing `Microsoft.WindowsDesktop.App 8.0.0`. |
| E3 Error contract hardening | complete | main | `OperatorError` now includes optional `correlationId`, `retryable`, `category`; `OperatorErrorCategory` added; Host/Agent errors enrich responses; `docs/operator-error-codes.md`; live `GET /v1/artifacts/invalid` returned HTTP 404 `artifact_not_found`, `notFound`, `retryable=false`, string correlation id. |
| E4 Capability discovery | complete | main | `CapabilitiesResult` contracts, Host/Agent routes, OpenAPI/client regeneration, README inventory; live `GET /v1/capabilities` over `http://127.0.0.1:43127` returned HTTP 200, contract `0.1.0`, explicit `powerpoint.online.update` and `mail.outlook.download` availability. |
| E5 Artifact contract | complete | main | `ArtifactRef`, `ArtifactListResult`, `IArtifactService`, Host artifact/list routes, exchange-root store, checksums/ETag/media type; `WorkbenchArtifactRef` and mail attachment results now include artifact refs; Host autostart writes `Workbench.ExchangeRoot` for SYSTEM-safe artifact serving. |
| E6 Go client readiness | complete | main | `clients/go/README.md`; helpers `DecodeOperatorError`, `CheckContractVersion`, `DownloadArtifact`, `WaitForRun`, `WaitForPowerPointJob`; `cd clients/go && go test ./...` passed. |
| E7 External consumer smoke | complete | main | `scripts/external-consumer-smoke.sh` creates a temp external Go module and uses generated client/runtime only; live run against `http://127.0.0.1:43127` proved health, capabilities, negative `OperatorError`, and artifact download. |
| E8 Relay guide/template | complete | main | `docs/external-consumer-relay.md` defines loopback Host, route allowlist, auth, redaction, artifact URL rewrite, timeout/rate-limit boundaries. |
| E9 Release packaging | complete | main | `docs/external-consumer-release.md` defines `v0.1.0` tag gate, module path, pre-tag checks, live Host checks, fresh consumer proof; `scripts/check-openapi-contract.sh` passed and skips breaking check until first release tag exists. |

## Dependency Order

```text
E1 -> E2 -> E3
E1 -> E4
E2 + E5 -> E6
E3 + E4 + E5 + E6 -> E7
E5 + E7 -> E8
E2 + E6 + E7 -> E9
```

Artifact work is the largest implementation slice. It should be designed before
client examples, because examples should not teach consumers to use local paths.

## Slice Details

### E1 Surface Metadata

Boundary:

- `src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs`
- `openapi/windows-operator.openapi.json`

Work:

- Add route class metadata: `stable`, `diagnostic`, `development`.
- Prefer OpenAPI tags plus `x-windows-operator-surface`.
- Classify `/v1/dev/...` as `development`.
- Classify normal product routes as `stable`.
- Classify readiness/recovery-only routes as `diagnostic` only when they should
  not be ordinary app dependencies.
- Add a test that every OpenAPI route has a known surface class.

Validation:

```text
scripts/generate-go-client.sh
dotnet test WindowsOperator.Portable.slnf --no-build
cd clients/go && go test ./...
git diff --check
```

Live proof:

- Not required; metadata-only.

### E2 Version and Release Gates

Boundary:

- OpenAPI contract generation
- release scripts/checks

Work:

- Add single contract version source used by OpenAPI `info.version`.
- Document pre-release vs release version rules.
- Add `scripts/check-openapi-contract.sh` or equivalent.
- Check generated spec/client are up to date.
- Add OpenAPI lint.
- Add breaking-change check against latest release tag once first tag exists.
- Add release checklist doc or script output.

Validation:

```text
scripts/generate-go-client.sh
cd clients/go && go test ./...
dotnet build WindowsOperator.Portable.slnf --no-restore
git diff --check
git status --short
```

Live proof:

- Optional live parity: compare `GET /openapi.json` with committed spec when
  Windows Host is available.

### E3 Error Contract Hardening

Boundary:

- `WindowsOperator.Core.Contracts.OperatorError`
- `WindowsOperator.Core.OperatorErrors`
- Host/Agent error serialization

Work:

- Add optional fields:
  - `correlationId`
  - `retryable`
  - `category`
- Keep existing `code`, `message`, `remediation`, `details` fields.
- Define category enum or documented string set.
- Add error code table doc.
- Ensure product errors return `OperatorError`.
- Ensure callers branch on `code`, not `message`.

Validation:

```text
dotnet test WindowsOperator.Portable.slnf --no-build
scripts/generate-go-client.sh
cd clients/go && go test ./...
git diff --check
```

Live proof:

- Safe negative call against a route that returns a product error.
- Final evidence should include endpoint, HTTP status, `code`, `category`, and
  `retryable`.

### E4 Capability Discovery

Boundary:

- Core contracts
- Host endpoint
- Agent capability provider or Host proxy check

Interface:

```text
GET /v1/capabilities
```

Result shape:

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
    }
  }
}
```

Work:

- Add `CapabilitiesResult`, `CapabilityFeature`, and related contracts.
- Add Host route.
- Include Host-only and Agent-backed feature availability.
- Include reason/details for unavailable features without leaking platform
  internals.
- Add OpenAPI and generated-client coverage.
- Add README route inventory entry.

Validation:

```text
dotnet test WindowsOperator.Portable.slnf --no-build
scripts/generate-go-client.sh
cd clients/go && go test ./...
git diff --check
```

Live proof:

```text
curl http://127.0.0.1:43117/v1/capabilities
```

Expected:

- HTTP 200.
- `contractVersion` present.
- `powerpoint.online.update` and `mail.outlook.download` have explicit
  availability.

### E5 Artifact Contract

Boundary:

- Artifact contract types in Core
- Host artifact serving
- Exchange-root-backed artifact store
- Existing screenshot/mail/PowerPoint result contracts

Target interface:

```text
GET /v1/artifacts/{artifactId}
GET /v1/runs/{runId}/artifacts
```

Target ref:

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

Work:

- Introduce shared `ArtifactRef` and `ArtifactListResult`.
- Add artifact ID encoding/decoding that does not expose host filesystem paths.
- Add artifact store over exchange root.
- Add content route with media type, length, checksum where possible.
- Add run artifact listing.
- Add `href`/`artifactId` to screenshot, mail, and PowerPoint evidence results.
- Keep old local path fields initially as debug/backcompat metadata.
- Update docs to tell consumers to use artifact refs.

Validation:

```text
dotnet test WindowsOperator.Portable.slnf --no-build
scripts/generate-go-client.sh
cd clients/go && go test ./...
git diff --check
```

Live proof:

- Capture or produce one safe artifact.
- Fetch it through `GET /v1/artifacts/{artifactId}`.
- Verify content type, nonzero bytes, and checksum when available.
- Prove no external-consumer flow needs `path`, `hostPath`, or `absolutePath`.

### E6 Go Client Readiness

Boundary:

- `clients/go`
- generated client docs and optional helpers

Work:

- Add `clients/go/README.md`.
- Add examples for:
  - health
  - capabilities
  - `OperatorError` decode
  - PowerPoint update
  - mail download dry-run
  - artifact download
  - contract drift check
- Add helper functions only where they hide repeated complexity:
  - `DecodeOperatorError`
  - `CheckContractVersion`
  - `DownloadArtifact`
  - `WaitForRun`
  - `WaitForPowerPointJob`
- Keep raw generated route access intact.

Validation:

```text
cd clients/go && go test ./...
go test ./...
git diff --check
```

Live proof:

- Use generated client, not CLI, for health/capabilities.
- Use generated client to fetch an artifact after E5.

### E7 External Consumer Smoke

Boundary:

- test harness for generated-client usage
- no dependency on CLI/Just for application path

Work:

- Add an external-consumer smoke command or test.
- It should use the generated client directly.
- Safe default path:
  - health
  - capabilities
  - known negative product error
  - artifact fetch if an artifact can be produced safely
- Optional deep path:
  - PowerPoint update validate-only or explicit test deck mutation
  - mail dry-run

Validation:

```text
cd clients/go && go test ./...
```

Live proof:

- Run against live Host.
- Final evidence names exact endpoints or generated-client calls, observed
  status, and any skipped mutation path.

### E8 Relay Guide/Template

Boundary:

- docs and optional relay sample
- auth/rate-limit/logging outside Host

Work:

- Add relay guide with:
  - Host remains loopback-only
  - relay route allowlist
  - auth model
  - redaction rules
  - artifact URL rewriting
  - rate limit guidance
- Optional sample config/template for a trusted reverse proxy.
- Document that relay must not translate domain errors or own Windows
  automation retries.

Validation:

```text
git diff --check
```

Live proof:

- Not required for docs-only.
- If sample relay is executable, prove health route through relay with auth.

### E9 Release Packaging

Boundary:

- release workflow
- tags
- generated client install path

Work:

- Define first release tag shape.
- Confirm `clients/go/go.mod` module path is final.
- Run release gates.
- Prove fresh external repo can:
  - `go get github.com/alejg/windows-operator/clients/go@<tag>`
  - call health
  - decode an `OperatorError`
  - fetch artifact after E5

Validation:

```text
scripts/generate-go-client.sh
cd clients/go && go test ./...
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build
git diff --check
git status --short
```

Live proof:

- Fresh external repo or temp module uses tagged client against live Host.

## Refactor Notes

No major refactor is required.

New deep modules likely worth adding:

- Artifact store/service: hides exchange root, path normalization, media type,
  checksum, and route-safe IDs.
- Capability provider: hides Host/Agent readiness checks and feature naming.
- Contract/release checker script: hides generator and drift commands.

Avoid:

- moving operator workflow state into REST
- making CLI the SDK
- exposing exchange paths as stable API
- adding generated-client helpers that are only pass-through wrappers

## Completion Evidence

Local validation:

```text
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build --nologo
dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-build --filter "OpenApi_|CapabilitiesRoute|ArtifactRoutes|ArtifactRoute" --nologo
scripts/check-openapi-contract.sh
cd clients/go && go test ./...
git diff --check
```

All commands passed on 2026-07-06. `scripts/check-openapi-contract.sh` reported
`contract check passed. version 0.1.0` and skipped breaking-change comparison
because no release tag exists yet.

Live Windows proof:

- Synced source to `C:\src\windows-operator`.
- Repaired corrupt Alejg-local .NET SDK under
  `%LOCALAPPDATA%\WindowsOperator\dotnet-sdk`.
- Published Host to `%ProgramData%\WindowsOperator\host`.
- Registered/restarted scheduled task `WindowsOperator.Host`.
- Wrote Host config
  `%ProgramData%\WindowsOperator\run\host.appsettings.Local.json` with
  `Workbench.ExchangeRoot = C:\ProgramData\WindowsOperator\exchange`.
- Used temporary SSH REST tunnel
  `http://127.0.0.1:43127 -> legion9-win:127.0.0.1:43117` because the existing
  Linux `127.0.0.1:43117` listener still served an older Host surface.
- `GET /openapi.json`: HTTP 200, version `0.1.0`, 58 paths, new capability and
  artifact routes present.
- `GET /v1/capabilities`: HTTP 200, contract `0.1.0`, Host `degraded` because
  Desktop Agent was unavailable; PowerPoint/mail feature records still explicit.
- `GET /v1/artifacts/invalid`: HTTP 404, code `artifact_not_found`, category
  `notFound`, `retryable=false`.
- Produced safe artifact
  `C:\ProgramData\WindowsOperator\exchange\runs\external-consumer-smoke-20260706T070617Z\external\proof.txt`.
- `GET /v1/runs/external-consumer-smoke-20260706T070617Z/artifacts`: HTTP 200,
  one opaque `artifactId`, `mediaType=text/plain`, `bytes=69`,
  SHA-256 `c7b15ad41fa637b0b7011b9b9662f69f75015e9513c40e133d18f7b1f7482aa2`.
- `GET /v1/artifacts/{artifactId}`: HTTP 200, `Cache-Control: private,
  max-age=60`, matching `ETag`, matching downloaded SHA-256.
- `scripts/external-consumer-smoke.sh` with
  `WINDOWS_OPERATOR_BASE_URL=http://127.0.0.1:43127` and
  `WINDOWS_OPERATOR_SMOKE_RUN_ID=external-consumer-smoke-20260706T070617Z`:
  health `degraded`, capabilities `0.1.0`, negative code `locked_desktop`
  category `unavailable` retryable `true`, artifact download `69` bytes.

Residual notes:

- Linux `127.0.0.1:43117` was not used as final proof because it still pointed
  at an older Host surface. Temporary tunnel `43127` proved the updated Windows
  Host directly.
- Desktop Agent was unavailable during Host capability proof, so Agent-backed
  workflows reported unavailable rather than executing PowerPoint/mail mutation.
  Capability discovery now exposes that state before consumers call workflows.

Completion challenge:

- Re-checked ledger for pending E-slices: E0-E9 complete.
- Re-checked OpenAPI route count/surface metadata: 58 paths, 0 operations
  missing `x-windows-operator-surface`.
- Re-checked external docs for stale "capabilities/artifacts not implemented"
  wording and corrected it.
- No higher-value safe next slice remains inside this goal before release tag
  creation or Desktop Agent availability work, both outside the completed
  external-consumer contract slice.

## Done Definition

External-consumer path is real when:

- OpenAPI classifies every route.
- Contract version/release gate exists.
- Generated Go client has docs/examples and passes tests.
- External app path uses generated client or REST only.
- Artifacts are retrievable through REST refs.
- Errors are branchable by stable `code`.
- Capabilities are discoverable before workflow calls.
- Live smoke proves no application dependency on CLI, Just, SSH, staged
  PowerShell, or local Windows paths.
