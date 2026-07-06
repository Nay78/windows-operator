# PowerPoint Online Session Harness Handoff

Date: 2026-07-03

## Source Truth

Governing:

- `.work/powerpoint-online-editing-harness-roadmap.md`
- `docs/powerpoint-automation-architecture.md`
- `docs/feature-namespaces.md`
- Existing Edge/workbench contracts and routes under `src/WindowsOperator.Core`, `src/WindowsOperator.Agent`, and `src/WindowsOperator.Host`

Supporting:

- `docs/development.md`
- `.work/vm-workbench-smoothing-todo.md`
- Live evidence from 2026-07-03: provided SharePoint deck opened in Edge work profile, slide 4 selected, screenshot written to `/var/lib/windows-server/shared/operator-exchange/runs/ppt-slide4/screenshots/slide4.png`.

## Progress Status

Status: historical handoff. The first session harness slice is implemented and
live-validated; see `.work/powerpoint-online-session-harness-validation.md` and
the current PowerPoint Online route inventory in `README.md`/OpenAPI.

At handoff time, existing primitives could open, navigate, screenshot, and
drive Edge sessions. The missing owner was a PowerPoint-domain service/API that
hides those primitives and reports PowerPoint Online session state.

## Objective

Implement first useful slice: domain-level PowerPoint Online session harness that can open a SharePoint-hosted PowerPoint Online deck, classify readiness/blockers, select a slide, capture evidence, and cleanup by stable `/v1/powerpoint/online/*` routes.

## Write Scope

Worker may edit:

- `src/WindowsOperator.Core/Contracts/PowerPointOnline*.cs`
- `src/WindowsOperator.Core/Services/IPowerPointOnlineService.cs`
- `src/WindowsOperator.Core/Contracts/OperatorOpenApi.cs`
- `src/WindowsOperator.Core/Services/IOperatorFacade.cs` only if needed for existing routing style
- `src/WindowsOperator.Core/Services/OperatorFacade.cs` only if needed for existing routing style
- `src/WindowsOperator.Agent/Api/OperatorEndpoints.cs`
- `src/WindowsOperator.Agent/Hosting/OperatorApp.cs`
- `src/WindowsOperator.Agent/Services/PowerPointOnlineService.cs`
- `src/WindowsOperator.Host/Api/HostOperatorEndpoints.cs`
- `src/WindowsOperator.Host/Services/DesktopAgentClient.cs`
- `src/WindowsOperator.Host/Services/HostOperatorFacade.cs`
- `tests/WindowsOperator.Agent.Tests/*PowerPointOnline*`
- `tests/WindowsOperator.Agent.Tests/Fakes/*`
- `tests/WindowsOperator.Agent.Tests/RestAndMcpParityTests.cs`
- `tests/WindowsOperator.Core.Tests/ContractSerializationTests.cs`
- `tests/WindowsOperator.Host.Tests/DesktopAgentClientTests.cs`
- `openapi/windows-operator.openapi.json`
- `clients/go/windowsoperator.gen.go`
- minimal docs/runbook update if needed for new route visibility

Worker must not edit:

- `.work/*`
- PowerPoint add-in implementation
- PowerPoint job queue behavior
- mail/auth unrelated surfaces
- VM/NixOS/tunnel provisioning

## Acceptance Criteria

- New routes exist on Agent and Host:
  - `POST /v1/powerpoint/online/sessions`
  - `GET /v1/powerpoint/online/sessions/{sessionId}`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/screenshot`
  - `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`
- Start request opens/reuses Edge work-profile session with the provided deck URL.
- Result includes status, deck URL, current browser URL/title, `browserSessionId`, `hwnd`, optional artifact root/evidence, actions, warnings, and errors.
- State classifier distinguishes at least:
  - `ready`
  - `blocked_auth`
  - `blocked_permission`
  - `blocked_readonly`
  - `blocked_office_error`
  - `failed`
  - `closed`
- Slide selection accepts 1-based `slideNumber`; initial implementation may use deterministic keyboard/thumbnail fallback, but must hide mechanics behind `IPowerPointOnlineService`.
- Screenshot route writes evidence artifact via existing workbench screenshot path.
- Cleanup closes underlying Edge session or marks session closed consistently.
- OpenAPI contains new schemas/routes.
- Tests cover contracts and service behavior with fake Edge/workbench dependencies.

## Validation

Required local:

```bash
dotnet test tests/WindowsOperator.Agent.Tests/WindowsOperator.Agent.Tests.csproj --no-restore
dotnet test tests/WindowsOperator.Core.Tests/WindowsOperator.Core.Tests.csproj --no-restore
dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-restore
```

Required live by orchestrator after worker returns:

```bash
curl -sS http://127.0.0.1:43117/v1/health
curl -sS http://127.0.0.1:43117/openapi.json
POST /v1/powerpoint/online/sessions with provided deck URL
POST /v1/powerpoint/online/sessions/{sessionId}/slides/select slide 4
POST /v1/powerpoint/online/sessions/{sessionId}/screenshot
```

Live success requires screenshot artifact showing slide 4 or classified blocker with screenshot evidence.

## Risks

- Worktree is already dirty. Worker must preserve unrelated changes.
- Existing browser route state may use `EdgeMicrosoftAuthService`; avoid broad auth regressions.
- PowerPoint Online DOM is unstable. Keep UI mechanics private and first slice modest.
- True editing/add-in activation remains later slice; do not claim edit proof from session harness alone.

## Approval Needed

No operator approval needed for first slice: routes are additive and follow accepted `.work` roadmap/user instruction. Current specs/docs should not be updated as current behavior until live validation passes.
