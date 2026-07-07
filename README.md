# Windows Operator

Windows-first desktop operator for local automation. Repo targets a logged-in
user session on Windows 10 2004+ and Windows 11. Host and Agent run on Windows;
Linux scripts are operator-side helpers. No Windows service or remote bind by
default.

## Projects

- `src/WindowsOperator.Host`: headless REST control plane on loopback `43117`.
- `src/WindowsOperator.Agent`: interactive desktop worker on loopback `43119`.
- `src/WindowsOperator.PowerPointAddIn`: Office.js PowerPoint task pane hosted by Host on `https://localhost:3003`.
- `src/WindowsOperator.Core`: contracts, options, error model, shared orchestration.
- `src/WindowsOperator.Automation`: Win32 window catalog/activation and FlaUI UIA3 automation backend.
- `src/WindowsOperator.Capture`: screenshot backend chain and image encoding policy.
- `src/WindowsOperator.Mcp`: MCP tool catalog plus HTTP/stdio transports.
- `src/WindowsOperator.MailWorker`: short-lived Classic Outlook COM worker.
- `tests/*`: unit and integration coverage.
- `docs/`: development notes, operator runbooks, and automation boundaries.
- `openapi/` and `clients/go/`: committed OpenAPI spec and generated Go client.

## v1 surfaces

Host REST binds `127.0.0.1:43117` by default and proxies desktop automation to the Agent when a desktop session exists.
Host owns the public OpenAPI surface. Agent owns interactive desktop work on
`127.0.0.1:43119`. Queue/artifact/update routes such as
`/v1/powerpoint/jobs...` and `/v1/powerpoint/online/updates` are Host-owned
orchestration routes. For auth status polling, prefer explicit
`/status/{runId}` in automation; `/status/latest` is a convenience path for
manual or single-agent follow-up.

External projects should consume Host REST, `/openapi.json`, bounded OpenAPI
namespace specs, the committed OpenAPI spec, or generated clients. Do not use `scripts/linux/wo`, `Justfile`
recipes, SSH runner scripts, or staged PowerShell as application dependencies.
See [External consumer integration spec](docs/external-consumer-integration.md),
[Go client README](clients/go/README.md), and
[external relay guide](docs/external-consumer-relay.md) for the target contract,
release gates, artifacts, errors, relay, and SDK rules.

- `GET /v1/health`
- `GET /v1/capabilities`
- `GET /v1/windows`
- `GET /v1/desktop/foreground`
- `POST /v1/desktop/screenshot`
- `GET /v1/sessions/{sessionId}`
- `POST /v1/sessions/{sessionId}/screenshot`
- `POST /v1/sessions/{sessionId}/cleanup`
- `POST /v1/windows/{id}/activate`
- `GET /v1/windows/{id}/screenshot`
- `POST /v1/uia/query`
- `POST /v1/uia/click`
- `POST /v1/uia/type`
- `POST /v1/input/click`
- `POST /v1/input/hotkey`
- `POST /v1/browser/edge/reset`
- `POST /v1/browser/edge/open-url`
- `POST /v1/browser/edge/session/start`
- `GET /v1/browser/edge/session/{sessionId}/state`
- `POST /v1/browser/edge/session/{sessionId}/navigate`
- `POST /v1/browser/edge/session/{sessionId}/dom/click`
- `POST /v1/browser/edge/session/{sessionId}/dom/fill`
- `POST /v1/browser/edge/session/{sessionId}/close`
- `POST /v1/browser/edge/session/{sessionId}/screenshot`
- `POST /v1/browser/edge/session/{sessionId}/cleanup`
- `POST /v1/dev/browser/edge/sessions/{sessionId}/eval`
- `POST /v1/dev/powerpoint/online/sessions/{sessionId}/script`
- `POST /v1/auth/microsoft/cleanup`
- `POST /v1/auth/microsoft/authorize-probe`
- `GET /v1/auth/microsoft/authorize-probe/status/latest`
- `GET /v1/auth/microsoft/authorize-probe/status/{runId}`
- `POST /v1/auth/microsoft/device-login`
- `GET /v1/auth/microsoft/device-login/status/latest`
- `GET /v1/auth/microsoft/device-login/status/{runId}`
- `POST /v1/powerpoint/jobs`
- `POST /v1/powerpoint/jobs/claim`
- `POST /v1/powerpoint/jobs/{jobId}/complete`
- `POST /v1/powerpoint/jobs/{jobId}/fail`
- `GET /v1/powerpoint/jobs/{jobId}`
- `GET /v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}`
- `GET /v1/artifacts/{artifactId}`
- `GET /v1/runs/{runId}/artifacts`
- `POST /v1/powerpoint/online/sessions`
- `GET /v1/powerpoint/online/sessions/{sessionId}`
- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/probe`
- `POST /v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job`
- `POST /v1/powerpoint/online/sessions/{sessionId}/cleanup`
- `POST /v1/powerpoint/online/sessions/{sessionId}/save/wait`
- `POST /v1/powerpoint/online/sessions/{sessionId}/screenshot`
- `POST /v1/powerpoint/online/sessions/{sessionId}/slides/select`
- `POST /v1/powerpoint/online/sessions/{sessionId}/template/cleanup`
- `POST /v1/powerpoint/online/sessions/{sessionId}/template/prepare`
- `POST /v1/powerpoint/online/updates`
- `POST /v1/mail/folders`
- `POST /v1/mail/messages/search`
- `POST /v1/mail/attachments/download`
- `GET /v1/mail/runs/{runId}`
- `GET /v1/mail/status`
- `GET /openapi.json`
- `GET /openapi/namespaces`
- `GET /openapi/namespaces/{namespace}.json`

OpenAPI operation tags are namespace labels such as `mail.outlook` and
`powerpoint.online`. Use `GET /openapi/namespaces` and
`GET /openapi/namespaces/{namespace}.json` for bounded local-service discovery;
the namespace spec defaults to stable routes and accepts `surface=all` or
comma-separated surfaces such as `stable,diagnostic`.

MCP tools expose the AI-facing operator subset at `POST /mcp`. Local external
services should use REST/OpenAPI; MCP is optional for AI tool clients.
PowerPoint mutation stays REST-only unless a direct MCP workflow is added.

`/v1/dev/...` routes are disabled by default and exist for local debugging harnesses only.

Each MCP tool carries agent-facing metadata:

- `title`: readable tool label.
- `description`: starts with `Use this when...` and names the intended agent workflow.
- `outputSchema`: JSON Schema for `structuredContent`, generated from shared Core contracts.
- `annotations`: safety/planning hints for read-only, destructive, open-world, and idempotent behavior.
- `_meta`: compact invocation status text for OpenAI-compatible MCP clients.

Tool calls return full machine-readable JSON in `structuredContent`. Text content is a compact status summary, not a JSON dump.

- `operator_health`
- `window_list`
- `window_activate`
- `window_screenshot`
- `uia_query`
- `uia_click`
- `uia_type`
- `input_hotkey`
- `browser_edge_reset`
- `browser_edge_session_start`
- `browser_edge_session_state`
- `browser_edge_session_navigate`
- `browser_edge_session_dom_click`
- `browser_edge_session_dom_fill`
- `browser_edge_session_close`
- `auth_microsoft_cleanup`
- `auth_microsoft_authorize_probe`
- `auth_microsoft_authorize_probe_status`
- `auth_microsoft_device_login`
- `auth_microsoft_device_login_status`
- `mail_list_folders`
- `mail_search_messages`
- `mail_download_attachments`
- `mail_get_run`
- `mail_status`

## Backend choices

- UI automation backend seam exists, but the current implementation ships only `FlaUI.UIA3`.
- Screenshot backend chain is `WindowsGraphicsCapture -> PrintWindow -> GdiBitBlt`;
  WGC is registered as the first seam but currently falls through until WinRT
  capture is validated.
- Default screenshot output is JPEG quality `85`, longest edge `1600px`. PNG available for debugging.
- Workbench screenshots write files under `runs/<runId>/screenshots/` in
  `WINDOWS_OPERATOR_EXCHANGE_ROOT` or `Z:\operator-exchange`; Host paths map
  through `WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT` or
  `/var/lib/windows-server/shared/operator-exchange`.
- Active window appears first in window listings. v1 keeps privacy/token scope on active-window capture flow.

## Provisioning model

Shared repo stays canonical source. Windows host builds from shared source in
place, but mutable state stays local under `%LOCALAPPDATA%\WindowsOperator` by
default:

- `DOTNET_CLI_HOME`
- `NUGET_PACKAGES`
- build outputs under `artifacts\bin` and `artifacts\obj`
- launcher state under `run`
- logs under `logs`

Provision a fresh Windows workstation with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap.ps1 -RepoRoot \\server\share\windows-operator -EnableAutostart
```

Operator autostart uses two Task Scheduler entries. `WindowsOperator.Host` runs
at startup as SYSTEM from a local published copy after a 30 second delay. Host
REST always binds `127.0.0.1:43117`; PowerPoint add-in HTTPS on
`https://localhost:3003` is enabled only when `register-host-autostart.ps1`
stages a built add-in and localhost certificate. `WindowsOperator.Agent` runs
only in the logged-in desktop session, unelevated, after a 30 second delay.
Codex app-server autostart is a separate per-user task.

Codex bootstrap provisions Codex CLI under `%LOCALAPPDATA%\Codex`, using a
local npm prefix/cache and a per-user `Codex.AppServer` scheduled task:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap-codex.ps1 -EnableAutostart -InstallProfile
```

Codex credentials are not provisioned. Run `codex login` manually in the
Windows desktop session. After login, `Codex.AppServer` starts
`codex app-server --listen ws://127.0.0.1:43118` on Windows loopback.

For shell usability, bootstrap also persists `%LOCALAPPDATA%\Codex\npm-global`
on the user `PATH` and writes compatibility shims into
`%APPDATA%\npm\codex.cmd` and `%APPDATA%\npm\codex.ps1`.

## Local dev

Agent-facing harness entrypoint:

```bash
scripts/linux/wo --help
```

Use `scripts/linux/wo` for operator flows. Keep REST/OpenAPI/generated clients
for API users, service integrations, and external project dependencies.

Linux-side harness examples:

```bash
scripts/linux/wo health
scripts/linux/wo smoke
scripts/linux/wo ppt profile
scripts/linux/wo ppt profile-fast
scripts/linux/wo ppt warm
scripts/linux/wo ppt hot start
scripts/linux/wo mail status
scripts/linux/wo mail search --subject '__windows_operator_smoke_no_match__'
scripts/linux/wo mail download --subject 'SEM27' --folder 'mailbox/Inbox' --dry-run
scripts/linux/wo auth microsoft cleanup --dry-run
scripts/linux/wo auth microsoft device-login --device-code ABCD-EFGH --dry-run
```

`wo smoke` writes contract `summary.json` under
`operator-exchange/runs/<run-id>/summary.json` and stores the delegated live
smoke report at `operator-exchange/runs/<run-id>/live-smoke-report.json`.

Regenerate OpenAPI and Go bindings:

```bash
scripts/generate-go-client.sh
```

Check committed OpenAPI, generated Go client, and route inventory drift:

```bash
just contract-check
```

Details: [Go client generation](docs/go-client-generation.md).

Use Windows for actual development and verification.

```powershell
dotnet restore
dotnet build WindowsOperator.sln
dotnet test WindowsOperator.sln
dotnet run --project src/WindowsOperator.Agent
```

On Linux, use the portable solution filter for Linux-safe builds plus Host/MCP test coverage:

```bash
dotnet test WindowsOperator.Portable.slnf
```

Check agent-facing Linux wrappers without a live Windows host:

```bash
just agent-tool-tests
```

`dotnet run` starts loopback REST and a background MCP stdio server in same process.
For desktop-only worker runs, `WindowsOperator.Agent` listens on `127.0.0.1:43119`.

For Windows shared-source runs, prefer:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-agent.ps1 -RepoRoot \\server\share\windows-operator
```

From Linux, run repo-owned Windows scripts through the exchange runner. The VM
wrapper runs base bootstrap and Codex bootstrap together:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/bootstrap-vm.ps1
```

The runner defaults to `administrator@127.0.0.1:22555`. Staging, SSH, and
copy-backed target details live in [operator exchange](docs/operator-exchange.md#linux-runner).

Preferred wrapper for Microsoft device-code login:

```bash
scripts/linux/wo auth microsoft device-login --device-code ABCD-EFGH
```

Lower-level REST example:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/device-login \
  -H 'Content-Type: application/json' \
  -d '{"deviceCode":"ABCD-EFGH"}'
```

The REST operation opens `https://microsoft.com/devicelogin`, pastes the device code, and leaves account/MFA prompts for the user.

SSH fallback:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/login-microsoft-device-code.ps1 -DeviceCode ABCD-EFGH
```

The helper uses the same browser handoff behavior when REST is unavailable.

For Outlook profile recovery when REST mail calls are degraded:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/recover-outlook-mail.ps1 -Mode Profile
```

Agent machine-local overrides belong in
`%LOCALAPPDATA%\WindowsOperator\run\appsettings.Local.json`. Host autostart
writes `%ProgramData%\WindowsOperator\run\host.appsettings.Local.json`.
Template shapes live in [local machine overrides](docs/local-machine-overrides.md).

## Current limits

- WGC class exists as primary seam, but real WinRT interop still needs Windows validation and hardening.
- Elevated/UAC targets are intentionally unsupported in v1. Errors return explicit remediation.
- Remote exposure stays loopback-only. On the NixOS host, access Codex app-server through the SSH tunnel on `127.0.0.1:43118`.
