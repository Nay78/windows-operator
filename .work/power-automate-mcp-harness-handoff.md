# Power Automate MCP Harness Handoff

Status: superseded initial handoff. The implementation now exceeds this
three-route slice; current external-contract hardening is tracked in
`.work/external-consumer-integration-roadmap.md`. Retain this file as evidence,
not as an active completion claim.

## Source Truth

1. User request: implement harness support for browser-backed Power Automate flow creation using logged-in Windows/Edge account, no Entra app.
2. `docs/mail-to-onedrive-automation.md`: Power Automate is bootstrap option; token-capturing extension stays scoped to an operator-owned Edge session.
3. `docs/feature-namespaces.md`: stable user-facing domain routes; Host public OpenAPI, Agent owns interactive desktop work.
4. `kaael1/mcp-power-automate`: local MCP bridge plus Chromium extension, loopback default `127.0.0.1:17373`, extension loaded from Edge.

## Implementation Slice

Add Agent-backed harness surface:

- `GET /v1/power-automate/mcp/status`
- `POST /v1/power-automate/mcp/start`
- `POST /v1/power-automate/mcp/edge`

Host proxies same routes to Desktop Agent. Agent owns process launch and Edge desktop context.

## Safety Contract

- Never log bearer tokens, cookies, request headers, or browser storage.
- Status/start may inspect Node/npm/Edge/loopback bridge.
- Do not create or mutate real Power Automate flows in this slice.
- Do not install browser extensions persistently. Use unpacked load arguments only.

## Target Behavior

Status result should report:

- Node/npm/Edge paths and versions when discoverable.
- Bridge host/port and health/context probe state.
- Default package spec, extension path if resolved.
- Warnings/errors/actions, timestamp.

Start result should:

- Start `npx -y @kaael1/mcp-power-automate@0.4.1` in Windows user context when not already listening.
- Write process/log state under `%LOCALAPPDATA%\WindowsOperator\run\power-automate-mcp`.
- Return status after launch.

Edge result should:

- Resolve extension path.
- Launch Edge to `https://make.powerautomate.com/` with `--load-extension=<path>`.
- Prefer work profile by default for logged-in account.
- Return process id/session action data without exposing secrets.

## Validation

- Unit tests for Host proxy path and OpenAPI namespace/paths.
- Live safe Windows check: status route returns dependency state; edge dry-run returns HTTP 200 without launching Edge.
- If package execution hangs or tenant auth blocks readiness, report exact command/result.

## Evidence

- Local build: `dotnet build WindowsOperator.sln --no-restore` passed.
- Local tests: Host endpoint/proxy filter passed 22 tests; MCP tests passed 15 tests.
- Contract checks: `scripts/check-readme-route-inventory.sh`, `scripts/check-openapi-contract.sh`, and `scripts/linux/wo-tests.sh` passed.
- Linux Agent test gap: `tests/WindowsOperator.Agent.Tests` cannot execute on Linux because `Microsoft.WindowsDesktop.App` runtime is missing.
- Windows target `DESKTOP-6BT2OFE` (`Microsoft Windows NT 10.0.26200.0`) Host OpenAPI has 63 paths including all three `/v1/power-automate/mcp/...` routes.
- CLI proof through short-lived SSH tunnel to target Host:
  - `pa-mcp-final-start-dry-run`: `wo power-automate mcp start --dry-run` returned HTTP 200 and skipped extension path resolution.
  - `pa-mcp-final-status`: `wo power-automate mcp status` returned HTTP 200 with Node/npm/npx/Edge paths and bridge not listening.
  - `pa-mcp-final-edge-dry-run`: `wo power-automate mcp edge --dry-run` returned HTTP 200 without loading Edge or resolving the npm extension path.
- Deployment note: Linux default `127.0.0.1:43117` currently reports a different Windows host (`Microsoft Windows NT 10.0.20348.0`) than `.windows-operator.local.env` SSH target. Verified target-local REST through explicit SSH tunnel.
