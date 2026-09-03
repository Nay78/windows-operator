# OneDrive Files-On-Demand Module Specification

Status: **diagnostic implementation; stable promotion pending live proof**

Target: first safe implementation slice for hydrating selected local OneDrive
or SharePoint-synced files, using them, and dehydrating them immediately after
use so they do not retain VM disk space.

Current implementation status: **implemented in repository**. Core contracts,
Agent control plane, Host proxy, REST/OpenAPI diagnostics, persistence, and
tests exist. Stable promotion remains blocked until a Windows OneDrive
hydrate/use/release matrix passes; no MCP surface is included in this slice.

## Source Basis

This document is the governing human-readable source for the module's scope,
ownership, contracts, invariants, and proof requirements.

| Source | Rank | Use |
| --- | --- | --- |
| This specification | Governing | OneDrive Files-On-Demand behavior, contracts, and validation |
| Current user request and Sage architecture decision | Governing decision input | No Graph access; hydrate-then-dehydrate; Agent-owned module |
| [`AGENTS.md`](../AGENTS.md) | Governing | Windows runtime, safety, live-proof, and source/state boundaries |
| [`feature-namespaces.md`](feature-namespaces.md) | Governing | REST, OpenAPI, service, contract, state, and namespace conventions |
| [`operator-harness-architecture.md`](operator-harness-architecture.md) | Governing | Host/Agent ownership and external-consumer boundary |
| [`operator-error-codes.md`](operator-error-codes.md) | Governing | Stable error envelope and retryability vocabulary |
| [`mail-to-onedrive-automation.md`](mail-to-onedrive-automation.md) | Supporting | Mail attachment upload workflow; separate lifecycle concern |
| Current Windows OneDrive inspection | Supporting execution evidence | Client version, paths, attributes, and disk measurements |
| `openapi/windows-operator.openapi.json` | Generated | Must be updated only after implementation and generation |

Implementation source and generated contracts now trace to this specification.
The mail document
must not be extended to own placeholder hydration or dehydration.

## Recovery schedule

`cen_vuelos` owns the daily recovery cadence for its service-owned OneDrive
operations. At 20:15 America/Santiago it retries only root-relative paths that
`cen_vuelos` recorded after a failed OneDrive operation, one path per
idempotent `POST /v1/files/onedrive/reclaims` request. `off` is the default;
Mini uses `execute`, while `audit` sends dry-run requests. Windows Operator
continues to own approved-root resolution, lease provenance, identity and
user-pin checks, `CfSetPinState`, and local allocation proof. The Agent recovery
pass remains dormant while `OneDriveConfig.PeriodicReclaim` is false. Neither
pass sweeps roots or changes cloud content.

## Goal

Let an operator, AI runtime, or future local consumer:

1. select a file under an approved local OneDrive/SharePoint sync root;
2. hydrate it through the logged-in OneDrive client;
3. consume the materialized bytes through a bounded lease;
4. release the lease and verify that local allocation is reclaimed; and
5. inspect or change the module policy without editing OneDrive private state.

The current primary root is:

```text
C:\Users\Administrator\Geosupport S.A
```

This root represents an externally synced SharePoint location exposed through
the user's OneDrive client.

## Non-goals

- Microsoft Graph, SharePoint REST, or other direct cloud file APIs.
- Downloading file content without local materialization. Files-On-Demand must
  hydrate bytes onto local disk before a consumer can read them.
- Sending files through WhatsApp. A future transfer consumer may use the
  generic lease service, but WhatsApp is not part of this module.
- Deleting SharePoint or OneDrive cloud files.
- Freeing cloud quota. Reclaim means local VM/disk allocation only.
- Editing OneDrive's private database, registry state, or Account > Choose
  folders UI.
- Promising that a file is cloud-fresh or that SharePoint upload/download
  synchronization has completed.
- A background whole-root sweep. Recovery scheduling is allowed only as a
  bounded retry of durable module-owned reclaim records after live capability
  proof; it never discovers files by scanning configured roots.

The module controls local hydration state. It does not implement OneDrive's
account-level selective-sync checkbox policy. Personal-root files may still be
scanned by the OneDrive client even when they are online-only.

## Search Anchors

Domain and user labels:

- `OneDrive Files-On-Demand`
- `hydrate`
- `dehydrate`
- `online-only`
- `Geosupport S.A`
- `files.onedrive`
- `onedrive_unavailable`

Target service and contracts:

- `IOneDriveFilesOnDemandService`
- `OneDriveLeaseRequest`
- `OneDriveLeaseResult`
- `OneDriveLeaseStatusResult`
- `OneDriveConfig`
- `OneDriveReclaimRequest`
- `OneDriveReclaimResult`

Primary implementation traces:

- `src/WindowsOperator.Core/Contracts/`
- `src/WindowsOperator.Core/Services/IOneDriveFilesOnDemandService.cs`
- `src/WindowsOperator.Agent/Services/OneDriveFilesOnDemandService.cs`
- `src/WindowsOperator.Agent/Api/OperatorEndpoints.cs`
- `src/WindowsOperator.Host/Services/DesktopAgentClient.cs`
- `src/WindowsOperator.Host/Api/HostOperatorEndpoints.cs`
- `src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs`
- `openapi/windows-operator.operation-policy.json`
- `tests/`

## Users And Workflows

### Local consumer

1. Request a lease for a configured root and relative path.
2. Service validates containment, reserves disk capacity, and hydrates the
   placeholder by opening and reading it to EOF.
3. Consumer reads the file through `UseHydratedFileAsync`.
4. Service classifies final release from immutable pre-acquire residency evidence.
5. Service preserves preexisting local allocation; otherwise it requests provider
   unpin, observes dehydration asynchronously, and records verification.

### REST caller

Use Host REST on `127.0.0.1:43117`. The public contract returns an opaque
`leaseId`; it does not expose a Windows path or local state layout. Release
returns `202` while state is `releasing`; poll the lease resource until it is
verified or `recoveryRequired`. A disconnect must not be treated as proof that
dehydration succeeded.

### Operator

Inspect status, update policy with an ETag, run a dry-run reclaim, then execute
an explicitly scoped reclaim. Root-wide reclaim and changes to approved roots
are operator-gated actions.

## Architecture And Ownership

```text
local consumer / REST caller
  -> Host REST 127.0.0.1:43117
  -> Host proxy / public OpenAPI
  -> Agent IOneDriveFilesOnDemandService
       -> approved-root and path containment
       -> OneDrive placeholder inspection
       -> local file hydration/read observation
       -> identity-bound CfSetPinState(unpinned) request
       -> allocated-byte and attribute verification
       -> lease/config/reclaim state
  -> logged-in OneDrive client
  -> local OneDrive or SharePoint sync root
```

Agent owns the Windows user-session mechanism, file identity, local state,
concurrency, and recovery. Host owns public REST/OpenAPI, error translation,
capability projection, and Agent proxying. Orchestration owns why and when a
file is needed; it must not learn OneDrive private state or command details.

On `WIN-UUKQS009K4J` only, Host supervises the OneDrive process in the
allowlisted active Administrator desktop session (console after auto-logon or
RDP after reconnect). Agent probes process/session
and root-bound Cloud Files provider readiness independently. Native provider
probing runs in an isolated child so a Cloud Files fault cannot terminate the
Agent. Recovery is bounded and non-destructive; sign-in and authentication
configuration remain operator-controlled.

Do not extend `IMailService`. Mail attachment retrieval and upload remain a
separate orchestration concern. Future mail, browser, or transfer modules call
the generic service contract.

## Requirements

### Scope and path safety

`REQ onedrive.root.geosupport`: The default configured root SHALL be
`C:\Users\Administrator\Geosupport S.A` with alias `geosupport`.

`REQ onedrive.path.containment`: Every operation SHALL accept a configured
root alias plus relative path and SHALL reject absolute, UNC, device, ADS,
traversal, and reparse-point escapes.

`REQ onedrive.identity.binding`: A lease SHALL bind to volume/file identity,
size, and root configuration version and SHALL revalidate identity immediately
before dehydration.

`REQ onedrive.runtime.scope`: Automatic OneDrive recovery SHALL run only when
both machine identity and configuration equal `WIN-UUKQS009K4J`; Legion and
every other computer SHALL fail closed without starting or stopping OneDrive.

`REQ onedrive.runtime.session`: Host SHALL keep OneDrive running in the
allowlisted active Administrator desktop session and SHALL never start it in
session 0. Agent SHALL require both a process in an active Administrator
session and root-bound Files-On-Demand provider readiness. When auto-logon is
configured, RDP SHALL reconnect to that session through single-session policy;
it SHALL not create a competing Agent or OneDrive session.

`REQ onedrive.runtime.recovery`: Recovery SHALL use bounded process starts and
provider polling without clearing account state or automating sign-in. Failure
SHALL return HTTP 423 `onedrive_unavailable` with machine, session, process,
provider reason, authentication-required, and action fields.

### Hydration and consumption

`REQ onedrive.lease.acquire`: Acquire SHALL require a caller `requestId`,
configured `rootId`, `relativePath`, and bounded TTL between 300 and 900
seconds unless policy changes those limits.

`REQ onedrive.hydration.complete`: A lease SHALL become `ready` only after a
sequential read reaches EOF and records logical length, allocated bytes,
attributes, and optional SHA-256.

`REQ onedrive.lease.consumer`: Internal consumers SHALL use
`UseHydratedFileAsync` or an equivalent callback/stream boundary. The public
contract SHALL return an opaque lease identifier, not a local Windows path.

`REQ onedrive.lease.concurrent`: Multiple leases for the same file MAY share
hydrated content, but no lease release SHALL dehydrate while another live lease
exists.

### Release and reclaim

`REQ onedrive.release.dehydrate`: Final release SHALL use an identity-verified
`CfSetPinState(CF_PIN_STATE_UNPINNED)` request, then observe provider-owned
dehydration asynchronously, only when the lease proves zero allocation before
hydration. Positive pre-acquire allocation is preexisting residency and SHALL
be preserved without a provider mutation. Missing legacy allocation evidence
SHALL fail closed with retained local bytes. Direct `CfDehydratePlaceholder`
SHALL only be used when the sync-root policy explicitly allows auto-dehydration.

`REQ onedrive.release.proof`: Release success SHALL require online-only
attributes (`Offline` and `RecallOnDataAccess`) and zero allocated local bytes
for module-owned hydration. A terminal release that records
`release_skipped_preexisting_residency` means the lease ended successfully with
the preexisting local allocation preserved.

`REQ onedrive.release.safety`: The service SHALL leave content hydrated when
the file is changed, dirty, replaced, pinned, open in a conflicting way, or
identity is ambiguous.

`REQ onedrive.reclaim.scope`: Reclaim SHALL default to dry-run and
module-owned files. Periodic whole-root reclaim is disabled by default.

`REQ onedrive.reclaim.local`: Reclaim SHALL report local allocated bytes before
and after. It SHALL never claim to free OneDrive/SharePoint cloud quota.

### Configuration and state

`REQ onedrive.config.local`: Configuration SHALL live under
`%LOCALAPPDATA%\WindowsOperator\files-on-demand\config.json`; credentials,
cookies, tokens, and private cloud URLs SHALL not be stored.

`REQ onedrive.config.atomic`: Updates SHALL require `If-Match`, write
atomically, activate under a service lock, and reject root changes while
affected leases or reclaims are active.

`REQ onedrive.state.restart`: Lease and reclaim state SHALL survive Agent
restart. Ambiguous or interrupted operations SHALL become
`recovery_required` and leave local bytes resident.

`REQ onedrive.request.idempotency`: Repeating a `requestId` with the same
canonical request body SHALL return the original record. Reusing it with a
different body SHALL return a conflict.

`REQ onedrive.renew.idempotency`: Renew SHALL require a caller `requestId`.
Repeating the same renew `requestId` and canonical body SHALL return the first
renew result without extending the lease again; reuse with a changed body SHALL
return `onedrive_idempotency_conflict`.

## Contracts

Namespace/tag: `files.onedrive`.

```text
POST /v1/files/onedrive/leases
GET  /v1/files/onedrive/leases/{leaseId}
POST /v1/files/onedrive/leases/{leaseId}/renew
POST /v1/files/onedrive/leases/{leaseId}/release

GET  /v1/files/onedrive/status
GET  /v1/files/onedrive/config
PUT  /v1/files/onedrive/config

POST /v1/files/onedrive/reclaims
GET  /v1/files/onedrive/reclaims/{runId}
```

### Lease renewal

```json
{
  "requestId": "caller-defined-idempotency-key",
  "ttlSeconds": 300
}
```

Renew request records are durable with the lease. A retry with the same
canonical body returns the original expiry; a changed body is a conflict.

All new routes are initially `diagnostic`. `cen_vuelos` is the intended external
lease consumer for acquire, status, renew, and release; it consumes opaque lease
identifiers and polls release status. This is a contract decision, not consumer
proof. Promote routes to `stable` only after the live matrix and real cen_vuelos
integration proof pass.

### Lease request

```json
{
  "requestId": "caller-defined-idempotency-key",
  "rootId": "geosupport",
  "relativePath": "folder/file.pdf",
  "ttlSeconds": 300,
  "expectedLength": 12345,
  "expectedSha256": "optional-lowercase-hex"
}
```

`expectedLength` and `expectedSha256` are optional preconditions. A mismatch
fails closed with `onedrive_content_changed`.

### Lease result

The result SHALL include:

- opaque `leaseId`;
- `rootId`, normalized `relativePath`, and lifecycle `state`;
- logical length and allocated bytes before/after hydration;
- observed Files-On-Demand attributes;
- checksum when requested or required by policy;
- creation, readiness, expiry, and release timestamps;
- actions, warnings, and structured errors.

The result SHALL NOT include a public local absolute path.

### Configuration defaults

```json
{
  "version": 1,
  "roots": {
    "geosupport": {
      "path": "C:\\Users\\Administrator\\Geosupport S.A",
      "enabled": true,
      "finalRelease": "dehydrate"
    }
  },
  "preserveUserPins": true,
  "reclaimScope": "moduleOwned",
  "periodicReclaim": false,
  "minimumFreeBytes": 10737418240,
  "maximumAcquireBytes": 1073741824,
  "defaultTtlSeconds": 300,
  "maximumTtlSeconds": 900
}
```

`preserveUserPins=true` is a safety default. Changing it is an operator
decision because it can remove offline availability. Personal OneDrive roots
are not inferred into the configuration; adding one requires explicit root
configuration and preflight validation.

### Persistent state

```text
%LOCALAPPDATA%\WindowsOperator\run\files-on-demand\
  leases\<leaseId>.json
  reclaims\<runId>.json
  requests\<requestId>.json
```

Persist the immutable request hash before filesystem effects. Record root
version, volume/file identity, original pin state, sizes, timestamps, and
terminal evidence. Release is idempotent. Before rollback or binary shutdown,
stop new acquisitions and prefer leaving unresolved files hydrated.

## Error Contract

Use the repository's standard error envelope with stable code, category,
retryability, and action details.

| Code | Category/status | Retry |
| --- | --- | --- |
| `onedrive_unavailable` | unavailable / 423 | Yes |
| `onedrive_root_not_found`, `onedrive_file_not_found`, `onedrive_lease_not_found`, `onedrive_reclaim_not_found` | notFound / 404 | No |
| `onedrive_path_blocked`, `onedrive_policy_denied` | validation or permission / 403–422 | No |
| `onedrive_idempotency_conflict`, `onedrive_config_conflict`, `onedrive_lease_conflict`, `onedrive_content_changed` | conflict / 409 | No |
| `onedrive_hydration_timeout`, `onedrive_dehydration_timeout` | timeout / 504 | Yes |
| `onedrive_hydration_failed`, `onedrive_dehydration_failed`, `onedrive_verification_failed` | unavailable or conflict / 423–409 | Per error details |

## Acceptance Criteria

`AC001: Given` a known online-only placeholder under `geosupport`, `when` a
valid lease is acquired and consumed to EOF, `then` the result reports the
logical byte count, observed attributes, and stable checksum when requested.

`AC002: Given` a ready lease with no competing lease, `when` it is released,
`then` the request returns a pending release state and a later status read
reports either verified zero-residency or retained-residency recovery.

`AC003: Given` two active leases for one file, `when` the first releases,
`then` the file remains hydrated; `when` the second releases, `then` the final
dehydration proof is recorded.

`AC004: Given` a reused `requestId`, `when` the canonical body is unchanged,
`then` the original record is returned; `when` the body differs, `then`
`onedrive_idempotency_conflict` is returned.

`AC005: Given` a file is pinned, changed, replaced, or identity is ambiguous,
`when` release runs, `then` dehydration is skipped and the reason is recorded.

`AC006: Given` an Agent restart during hydration, ready, or release, `when`
state reconciliation runs, `then` no unsafe dehydration occurs and each record
reaches a deterministic terminal or `recovery_required` state.

`AC007: Given` a stale config ETag, `when` an update is submitted, `then` the
update is rejected without changing active policy.

`AC008: Given` a reclaim dry-run, `when` it executes, `then` local files and
attributes are unchanged and estimated reclaimable bytes are reported.

`AC009: Given` traversal, absolute, UNC, ADS, device, or reparse-escape input,
`when` a lease is requested, `then` a typed 4xx error is returned and no file
is opened.

`AC010: Given` OneDrive is disconnected or still signing in, `when` a lease is
requested, `then` `onedrive_unavailable` is returned without a false `ready`
state.

`AC011: Given` OneDrive features are enabled on `WIN-UUKQS009K4J`, `when` the
resolved Administrator desktop-session OneDrive process exits, `then` Host restarts
it non-destructively in that same session; the same configuration on any other
computer performs no process mutation.

`AC012: Given` runtime recovery cannot establish both process and provider
readiness, `when` list or hydration is requested, `then` HTTP 423 contains
branchable runtime evidence and does not attempt sign-in.

## Validation Matrix

| Proof | Exercise | Required evidence |
| --- | --- | --- |
| Host baseline | `GET http://127.0.0.1:43117/v1/health` | HTTP 200 and healthy runtime |
| Hydrate/use/release | Known zero-allocation placeholder under Geosupport | EOF read, checksum, final attributes, zero allocation |
| Competing leases | Acquire same file twice | First release defers; final release dehydrates |
| Idempotency | Repeat request ID with same and changed body | Same record, then 409 conflict |
| Pin race | Pin after acquire, before release | Pin preserved; no dehydration |
| Identity race | Replace target before release | Content-changed conflict; replacement untouched |
| Restart | Restart Agent in each non-terminal state | Deterministic recovery; no unsafe eviction |
| Config | Current and stale ETag updates plus restart | Atomic persistence and stale rejection |
| Reclaim | Dry-run, then scoped execution on test file | Dry-run no mutation; exact local bytes reclaimed |
| Path attacks | Invalid path forms and reparse escape | Typed 4xx; no file access |
| Contract parity | Host/Agent routes, namespace OpenAPI, policy, generated client | Matching contracts and surface classification |

The live test candidate must be selected by a bounded read-only scan and its
exact relative path recorded in run evidence. Current baseline evidence:
OneDrive client `26.129.0706.0003`, personal local allocation approximately
`0.007 GiB`, Geosupport local allocation `0 GiB`, and C: free space
approximately `48 GiB`.

## Decisions

### Decision: Agent-owned deep module

Agent owns Files-On-Demand because hydration, file identity, Windows attributes,
logged-in OneDrive state, and local allocation are Windows user-session
mechanics. Host publishes and proxies the stable contract.

### Decision: Generic leases, not transfer-specific downloads

Leases make hydration and release composable without coupling the module to
WhatsApp, mail, or another consumer. A public local path is rejected because it
leaks implementation details and weakens lifecycle guarantees.

### Decision: Local reclaim only

The module may reclaim VM/local disk. Cloud-file deletion and cloud-quota
management require a separate specification and explicit destructive-action
authority.

### Decision: Preserve pins and avoid broad sweeps

V1 preserves user-pinned offline files and limits reclaim to module-owned files.
This may leave some local bytes resident, but avoids silently removing offline
availability or evicting files outside an active lease.

### Decision: `cen_vuelos` owns recovery cadence

`cen_vuelos` owns timing, durable candidate state, retry identifiers, and
operator-visible status. Windows Operator owns the dehydration mechanism and
rejects paths without module-owned residency evidence. Storage Sense is a
temporary broad fallback on Mini only; it is rolled back by operator approval
after a fresh zero-allocation reclaim proof, so two schedulers do not compete.

## Risks And Open Questions

- Hydration and dehydration depend on the OneDrive client being connected and
  healthy; the current UI was observed in a signing-in/disconnected state.
- Reading a file again after release can rehydrate it and consume disk again.
- A file may change remotely or locally between inspection and release.
- Zero allocated bytes may require polling and may be delayed by OneDrive or
  filesystem state.
- cen_vuelos remains an intended external consumer, not a proven caller; lease
  routes must remain diagnostic until its real integration and live matrix pass.
- Whether future consumers need remote byte streaming or only Agent-local
  callbacks remains open; automatic release on HTTP disconnect is not defined.

## Traceability And Handoff

| Requirement group | Proof path | Implementation target |
| --- | --- | --- |
| `onedrive.root.*`, `onedrive.path.*` | Path-attack and root-containment tests | Core path policy; Agent resolver |
| `onedrive.lease.*`, `onedrive.hydration.*` | Hydrate/use/release live test | Core contracts; Agent service |
| `onedrive.release.*`, `onedrive.reclaim.*` | Attribute/allocation and dry-run tests | Agent Files-On-Demand mechanism |
| `onedrive.config.*` | ETag, atomic-write, restart test | Agent config/state store |
| `onedrive.state.*`, `onedrive.request.*` | Restart and idempotency tests | Agent persistence/orchestration |
| REST contracts and errors | Host/Agent parity and OpenAPI checks | Host endpoints, proxy, OpenAPI, error mapping |

Implementation sequence:

1. Add Core contracts, service interface, and error codes.
2. Add Agent path policy, placeholder inspection, hydration, release, and
   persisted lease state.
3. Add Host proxy/routes and capability projection.
4. Add namespace OpenAPI and generated projections.
5. Run diagnostic live validation with a bounded test file.
6. Promote routes to stable only after the acceptance matrix passes.

Recommended next skill: `$autonomous-work` when implementation is authorized.
