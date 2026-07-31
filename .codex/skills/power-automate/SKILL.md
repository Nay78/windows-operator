---
name: power-automate
description: Operate and debug Microsoft Power Automate cloud flows through Windows Operator's browser-token/API/MCP harness. Use when checking or starting the local bridge, opening or cleaning its owned Edge session, capturing Power Automate context, reading a flow, validating or dry-running a flow definition, creating or updating a flow, extending the repo's Power Automate surface, or producing live Windows proof.
---

# Power Automate

Use the repo's code-first Power Automate boundary. Keep credentials and browser
tokens inside the Windows loopback bridge.

## Establish Source Truth

1. Work from the checkout containing this skill. Confirm it with
   `git rev-parse --show-toplevel`; do not assume an earlier workspace path.
2. Read root `AGENTS.md`.
3. Read the Power Automate section of `docs/mail-to-onedrive-automation.md`.
4. Inspect current contracts, endpoints, CLI flags, tests, and OpenAPI before
   changing behavior. Treat `.work/` handoffs as historical evidence unless the
   repo explicitly declares one current.

Use Host REST/OpenAPI as the stable external boundary. Use `scripts/linux/wo`
as the operator convenience client.

## Choose the Path

- Read, create, or update a cloud flow through the browser-token/API/MCP
  harness.
- Never mutate the Power Automate designer through UIA or screen automation
  unless the user explicitly authorizes break-glass operation.
- Do not silently fall back from API/MCP failure to designer automation.
- Prefer Classic Outlook COM for Windows email attachment automation when the
  task does not require a cloud flow.
- Use Power Automate Desktop only when its local desktop runtime is the actual
  target or an explicitly accepted fallback.

## Prepare the Browser Context

Run commands from the repo root:

```bash
scripts/linux/wo power-automate mcp status
scripts/linux/wo power-automate mcp start --dry-run
scripts/linux/wo power-automate mcp start
scripts/linux/wo power-automate mcp edge
```

Run `start` only when status reports the bridge unhealthy or not listening.
Inspect its dry-run before starting the process. Open Edge only when captured
context is absent, stale, or for the wrong environment.

The Edge command launches an operator-owned window with the token-capturing
extension. Ask the user to complete sign-in or MFA in that window when required,
then open or refresh the target flow. Re-run `status`; require a healthy bridge
and captured context before flow operations.

Keep the bridge bound to loopback. Keep the package spec pinned to the
repo-declared version. Never print, copy, persist in repo artifacts, or expose
tokens, cookies, authorization headers, or captured session content.

## Read Before Mutation

Use an explicit flow ID:

```bash
scripts/linux/wo power-automate mcp flow-read --flow-id <flow-id>
```

For live mutation, do not rely on the browser's inferred active target. Confirm
the signed-in account and tenant in the owned Edge window. Require the read
result's `envId` to match the intended environment, then confirm flow ID and
display name.

The harness accepts raw flow JSON; it is not a semantic flow builder. Derive
changes from the current definition or a confirmed reference flow. Do not invent
connector schemas, connection references, operation IDs, or environment IDs.
`flow.json` must be a JSON object containing `connectionReferences` and
`definition`, either at the root or inside a root `flow` object. Do not pass a
solution export or package wrapper.

The CLI stores full responses under the configured exchange root. Preserve the
read response as before-state evidence, but treat flow definitions and connection
metadata as sensitive. Restrict access and retention; never add them to the repo
unless the user explicitly requests a sanitized fixture.

## Dry-Run, Validate, Then Write

An update starts with dry-run:

```bash
scripts/linux/wo power-automate mcp flow-update \
  --flow-id <flow-id> \
  --flow-json-file <flow.json> \
  --validate-before \
  --validate-after \
  --dry-run
```

Review actions, warnings, errors, validation, and the proposed after-state. A
dry-run proves request parsing and routing; it does not prove a tenant write.

Only add `--no-dry-run` when the user explicitly requested the external
mutation and the exact target is confirmed. Keep before/after validation enabled
for updates.

Creating a flow requires an explicit display name:

```bash
scripts/linux/wo power-automate mcp flow-update \
  --create \
  --display-name <name> \
  --flow-json-file <flow.json> \
  --validate-after \
  --dry-run
```

Review the create plan before repeating with `--no-dry-run`. Do not claim
pre-write validation for create when the service skipped it.

## Verify Live

After a live write:

1. Require a successful update result with no unreviewed errors.
2. Read the exact flow ID again and compare relevant before/after fields.
3. Require successful post-write validation when requested.
4. For user-visible behavior, execute or observe a real Windows/Power Automate
   test and capture the run result. API acceptance alone is insufficient.
5. If MFA, authorization, connector binding, or environment access blocks
   success, run the strongest safe negative live test and report the precise
   failure. Do not report the flow complete.

Clean up only the operator-owned Edge lease through
`POST /v1/power-automate/mcp/edge/cleanup`. This route currently has no
`scripts/linux/wo` subcommand; call it through the trusted local Host loopback
boundary, or through the authenticated allowlisted relay when remote. Do not
close unrelated browser windows.

Final reporting names the exact endpoint or CLI command, target identifier,
dry-run/live mode, HTTP or operation result, evidence artifact, and remaining
gap.

## Extend the Repo Surface

When changing implementation:

1. Trace Core contracts, Agent service and endpoint, Host proxy and endpoint,
   OpenAPI namespace, generated or handwritten clients, CLI, tests, and docs.
2. Keep routes under the `power-automate.mcp` feature namespace.
3. Preserve loopback-only token custody, pinned dependency provenance,
   dry-run defaults, explicit target selection, and fail-closed behavior.
4. Add focused contract/service/endpoint tests. Run the narrowest relevant
   suites, then broader validation proportional to risk.
5. Obtain live Windows proof for runtime claims. A green Linux test suite does
   not prove browser capture or tenant mutation.
