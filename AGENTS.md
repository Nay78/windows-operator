# Windows Operator Agent Notes

This repo is the Codex session root for Windows-side automation work.

## Scope

- Source of truth: `/home/alejg/proj/windows-operator`
- Windows path: `Z:\windows-operator`
- NixOS repo: `/home/alejg/nixos`, only for VM/share/tunnel declarations
- Do not move this repo into `nixos`
- Do not put machine-specific config in shared source

## Runtime

- Headless Host runs at boot and owns REST on Windows loopback `127.0.0.1:43117`.
- Desktop Agent runs in logged-in Windows desktop session and owns UI automation on Windows loopback `127.0.0.1:43119`.
- No elevation by default for desktop automation.
- Codex app-server binds Windows loopback `127.0.0.1:43118`.
- Autostart uses per-user Task Scheduler tasks:
  - `WindowsOperator.Host` (startup, SYSTEM, headless REST/proxy)
  - `WindowsOperator.Agent`
  - `Codex.AppServer`

## State Model

Shared source is read/write code. Mutable Windows state stays local:

- `%LOCALAPPDATA%\WindowsOperator`
- `%LOCALAPPDATA%\Codex`
- `NUGET_PACKAGES`
- `DOTNET_CLI_HOME`
- `artifacts\bin`
- `artifacts\obj`
- logs and run state

Planned shared exchange root:

- Linux: `/var/lib/windows-server/shared/operator-exchange`
- Windows: `Z:\operator-exchange`

Use exchange root for files other Linux tools need: downloads, run logs, screenshots, JSON results.

## Commands

Windows bootstrap:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap-vm.ps1
```

Windows agent run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-agent.ps1 -RepoRoot Z:\windows-operator
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
- Edit source in this repo.
- Keep generated artifacts out of shared source.
- Keep PowerShell scripts idempotent.
- Keep Windows-specific verification on Windows.
- Prefer code-first automation over UI automation when possible.
- For email attachment download, prefer Classic Outlook COM before Power Automate Desktop or web UI scraping.

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
- [Linux/Windows exchange plan](docs/operator-exchange.md)
- [Email attachment automation plan](docs/email-attachment-automation.md)
- [Codex adapter notes](docs/codex-adapter.md)
