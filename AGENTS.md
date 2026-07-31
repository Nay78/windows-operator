# Windows Operator Agent Notes

This repo is the Codex session root for Windows-side automation work.

Default posture: operate the Windows computer from this repo. If SSH, sync,
run-script, REST, or desktop automation can do the work safely, use it instead
of giving the user manual Windows instructions. Ask only when credentials,
admin approval, MFA, or destructive action blocks progress.

Small safeguards:

- Do not create, delete, overwrite, chmod, or append SSH keys/authorized_keys
  without saying exactly which local/remote path and principal will change.
- Prefer adding a new key line over replacing an existing key file. Back up
  existing auth files before mutation when possible.
- Never print private keys, tokens, cookies, or Codex auth contents. Report
  presence, path, fingerprint, or account only.
- Before changing autostart, firewall, PATH, registry, scheduled tasks, or
  service state, name the target and verify current state first.
- For remote file writes outside this repo or `%LOCALAPPDATA%\WindowsOperator`,
  state the target path and whether it is machine-local state or shared source.

## Scope

- Source of truth: `/home/alejg/proj/run/windows-operator`
- Windows shared path: `Z:\windows-operator`
- Windows SSH-copy source path: `C:\src\windows-operator` unless `WINDOWS_OPERATOR_WINDOWS_REPO_ROOT` overrides it
- NixOS repo: `/home/alejg/nixos`, only for VM/share/tunnel declarations
- Do not move this repo into `nixos`
- Do not put machine-specific config in shared source

## Runtime

- Headless Host runs at boot and owns REST on Windows loopback `127.0.0.1:43117`.
- Desktop Agent runs in logged-in Windows desktop session and owns UI automation on Windows loopback `127.0.0.1:43119`.
- No elevation by default for desktop automation.
- Codex app-server binds Windows loopback `127.0.0.1:43118`.
- Host REST/OpenAPI is the stable external-project boundary. CLI, Just, SSH
  runner scripts, and staged PowerShell are operator/developer harnesses.
- Linux health checks use Host REST tunnel `127.0.0.1:43117`; do not treat failed Linux curls to `127.0.0.1:43119` as Desktop Agent failure. `43119` is Windows loopback for Host-to-Agent traffic unless an explicit temporary SSH debug tunnel is created.
- Autostart uses Task Scheduler tasks:
  - `WindowsOperator.Host` (startup, SYSTEM, headless REST/proxy)
  - `WindowsOperator.Agent` (logon, unelevated, local published runtime)
  - `Codex.AppServer`

## State Model

Shared source is read/write code. Mutable Windows state stays local:

- `%LOCALAPPDATA%\WindowsOperator`
- `%LOCALAPPDATA%\WindowsOperator\agent` (published Desktop Agent runtime)
- `%ProgramData%\WindowsOperator\host` (published Host runtime)
- `%LOCALAPPDATA%\Codex`
- `NUGET_PACKAGES`
- `DOTNET_CLI_HOME`
- `artifacts\bin`
- `artifacts\obj`
- logs and run state

Shared exchange root:

- Linux: `/var/lib/windows-server/shared/operator-exchange`
- Windows: `Z:\operator-exchange`

Use exchange root for files other Linux tools need: downloads, run logs, screenshots, JSON results.

## Commands

Windows VM bootstrap wrapper:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap-vm.ps1
```

Windows agent run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-agent.ps1 -RepoRoot Z:\windows-operator
```

Windows Agent runtime publication and task registration:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\register-agent-autostart.ps1 -RepoRoot Z:\windows-operator
```

Windows host registration:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\register-host-autostart.ps1 -RepoRoot Z:\windows-operator
```

Windows health:

```powershell
Invoke-RestMethod http://127.0.0.1:43117/v1/health
```

Linux host checks use the NixOS Operator REST tunnel on `127.0.0.1:43117`.

## Development Rules

- Response axiom: if no root/admin permission or architectural decision blocks action, return a concrete solution path, not a half-baked blocker report.
- Verification axiom: do not call a fix done because code compiles, schemas regenerate, mocks pass, or "plumbing works"; prove the user-visible behavior against the live Windows runtime whenever the feature depends on Windows, desktop apps, browser state, COM, tunnels, scheduled tasks, or external services.
- Negative-path axiom: when a live success path needs real credentials, tokens, MFA, mailbox contents, or third-party approval, run a safe negative live test with synthetic input and prove the expected real failure mode instead of stopping at dry-run.
- Evidence axiom: final responses must name the exact live endpoint/command exercised, the observed status/result, and any remaining gap. If verification is impossible, say what blocked it and what concrete evidence is missing.
- Dry-run axiom: dry-run verifies only serialization, routing, and command construction. Never present dry-run as proof that browser, COM, Outlook, PowerPoint, or external authentication behavior works.
- Edit source in this repo.
- Keep generated artifacts out of shared source.
- Keep PowerShell scripts idempotent.
- Keep Windows-specific verification on Windows.
- Prefer code-first automation over UI automation when possible.
- For email attachment download, prefer Classic Outlook COM before Power Automate Desktop or web UI scraping.
- For Power Automate cloud-flow writes, use the browser-token/API/MCP path. Do not mutate the Power Automate designer through UIA/screen input unless the user explicitly asks for break-glass UI automation.
- Capture unplanned work in `docs/todo.md`, including while an active `.work` campaign owns its execution queue. Promote accepted items into `.work` during queue review.
- Namespace new feature surfaces using [Feature namespaces](docs/feature-namespaces.md).
- External-project axiom: external projects depend on Host REST, OpenAPI, and
  generated clients, not `scripts/linux/wo`, `Justfile`, SSH runner scripts, or
  staged PowerShell. If another project needs a stable workflow, expose it in
  the REST/client contract or document it as operator-only. See
  [Operator harness target architecture](docs/operator-harness-architecture.md#external-project-integration).

## Deep Module Principles

- Prefer deep modules: small stable interface, substantial hidden implementation.
- Avoid shallow wrappers that add names without reducing caller complexity.
- Keep policy at orchestration boundaries; keep mechanism inside focused modules.
- Hide Windows quirks behind contracts: COM, UIA, Win32, Task Scheduler, paths, and registry details should not leak upward.
- Make APIs boring and hard to misuse: typed options, explicit results, deterministic paths, clear errors.
- Let modules own their state format. Callers should not know filenames, registry keys, or COM object shapes unless that is the module purpose.
- Push complexity down when it simplifies most callers. Do not push complexity up to keep internals pretty.
- Keep seams few and meaningful. Add an interface only when there are real alternate implementations or a test boundary.
- Preserve source/state split. Shared source APIs should not assume local Windows state layout except through config/options.
- Prefer one good script with clear parameters and logs over many tiny scripts chained by convention.
- Comments explain non-obvious intent, invariants, and platform traps. Do not narrate obvious code.

## Project Docs

- [Development notes](docs/development.md)
- [Current backlog](docs/todo.md)
- [Feature namespaces](docs/feature-namespaces.md)
- [External consumer integration spec](docs/external-consumer-integration.md)
- [External consumer relay guide](docs/external-consumer-relay.md)
- [External consumer release checklist](docs/external-consumer-release.md)
- [Operator error codes](docs/operator-error-codes.md)
- [Operator harness target architecture](docs/operator-harness-architecture.md)
- [Go client generation](docs/go-client-generation.md)
- [Local machine overrides](docs/local-machine-overrides.md)
- [Linux/Windows exchange plan](docs/operator-exchange.md)
- [Email attachment automation plan](docs/email-attachment-automation.md)
- [Outlook mail automation target architecture](docs/outlook-mail-automation-architecture.md)
- [PowerPoint automation target architecture](docs/powerpoint-automation-architecture.md)
- [Power BI Desktop operator specification](docs/powerbi-desktop-operator-spec.md)
- [Codex adapter notes](docs/codex-adapter.md)
