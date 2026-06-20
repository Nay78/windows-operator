# Feature Namespaces

Goal: keep new automation features discoverable, stable, and hard to misuse.

## Rule

Namespace by user-facing domain first, transport second.

Good:

- REST: `/v1/mail/...`
- REST: `/v1/auth/microsoft/...`
- REST: `/v1/browser/edge/...` for direct browser sessions
- REST: `/v1/powerpoint/...`
- MCP: `mail_list_folders`
- MCP: `auth_microsoft_device_login`
- Contracts: `MailFoldersResult`, `MicrosoftDeviceLoginRequest`
- Scripts: `login-microsoft-device-code.ps1`

Bad:

- REST: `/v1/edge/login`
- REST: `/v1/browser/device-code`
- MCP: `edge_login`
- Contracts: `BrowserLoginRequest`

Reason: callers care about business intent. Edge, Outlook COM, UIA, scheduled tasks, SSH, and PowerShell are implementation details.

## REST

Use stable `/v1/<domain>/<provider-or-resource>/<action>` paths.

Rules:

- Domain is short and durable: `mail`, `auth`, `browser`, `powerpoint`, `windows`, `uia`, `input`.
- Provider appears when behavior is provider-specific: `auth/microsoft`.
- Browser session endpoints use implementation namespace only when callers explicitly request browser control. Auth flows stay under `auth/microsoft`.
- Action is explicit and boring: `device-login`, `claim`, `complete`, `download`.
- Keep HTTP verbs meaningful. Use `GET` only for read-only status operations. Use `POST` for desktop actions, browser launches, refresh-aware reads, downloads, and anything with side effects.
- Keep Host and Agent routes identical. Host may proxy, Agent owns desktop work.

Examples:

```text
POST /v1/auth/microsoft/device-login
POST /v1/browser/edge/session/start
POST /v1/powerpoint/jobs
POST /v1/powerpoint/jobs/claim
POST /v1/powerpoint/jobs/{jobId}/complete
POST /v1/powerpoint/jobs/{jobId}/fail
GET  /v1/powerpoint/jobs/{jobId}
GET  /v1/mail/status
POST /v1/mail/folders
POST /v1/mail/attachments/download
```

## MCP

Use `<domain>_<provider?>_<action>` tool names.

Rules:

- Match REST domain/action vocabulary.
- Add provider only when needed.
- Avoid transport or implementation names.
- Input schema should mirror REST request contract.

Examples:

```text
auth_microsoft_device_login
mail_list_folders
mail_search_messages
mail_download_attachments
```

## Contracts

Use typed request/result contracts named by domain and action.

Rules:

- Request/result pair for each operation: `MicrosoftDeviceLoginRequest` and `MicrosoftDeviceLoginResult`.
- Domain objects stay in `WindowsOperator.Core.Contracts`.
- Service interfaces use domain verbs: `StartMicrosoftDeviceLoginAsync`, `ListMailFoldersAsync`.
- Results include timestamp and enough action/error detail for operators.
- Do not expose implementation paths, registry keys, COM object names, browser process ids, or scheduled task mechanics unless that is the feature's purpose.

## Services

Use deep domain services where desktop mechanism is hidden behind small methods.

Examples:

- `IMailService` owns Outlook COM worker policy.
- `IMicrosoftAuthService` should own Edge handoff policy.
- `IOperatorFacade` exposes domain operations, not browser/UIA details.

Do not add shallow wrappers just to rename existing UIA calls. Add service boundary only when it hides real platform work or gives a test seam.

## Scripts

Scripts are operational helpers, not primary API for external services.

Rules:

- Name scripts by domain and action: `login-microsoft-device-code.ps1`.
- Keep scripts idempotent when possible.
- Scripts may schedule interactive desktop work, but REST should be preferred for external service integration.
- Script parameters should match contract names when a REST equivalent exists.
- Never require external services to know Windows repo paths, scheduled task names, or exchange layout unless they are explicitly using the Linux runner.

## State

Feature state belongs under `%LOCALAPPDATA%\WindowsOperator`.

Suggested layout:

```text
%LOCALAPPDATA%\WindowsOperator\
  run\
    auth\microsoft\
    mail-worker\
  logs\
    auth-microsoft-device-login.log
    mail-*.log
```

Linux-consumed outputs belong under `Z:\operator-exchange`. Credentials and browser profiles must not be written there.

## External Services

External services should use REST, not SSH or staged scripts, when a Host route exists.

Preferred shape:

```text
external service -> authenticated relay -> 127.0.0.1:43117 -> Host -> Agent -> desktop mechanism
```

Rules:

- Keep Windows Host loopback-only.
- Put authentication/rate limiting at Linux relay or trusted local caller.
- Allow only needed routes through any relay.
- Redact secrets and device codes from logs.
- Keep long-running auth/token polling in the external service. Operator only performs desktop/browser handoff.

## Adding New Feature Checklist

1. Pick domain namespace before writing code.
2. Add request/result contracts in `Core.Contracts`.
3. Add facade/service method using domain names.
4. Add Agent route; add matching Host proxy route.
5. Add MCP tool only if AI runtimes need it.
6. Add script only if operators need CLI/SSH fallback.
7. Update `OperatorOpenApi`, tests, and docs.
8. Verify on Windows when desktop/browser/COM behavior is involved.
