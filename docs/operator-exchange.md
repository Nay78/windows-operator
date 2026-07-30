# Operator Exchange

Goal: make Windows-side automation artifacts directly available to Linux tools
without treating the exchange root as source code or placing mutable state in
shared source.

## Paths

- Linux default: `/var/lib/windows-server/shared/operator-exchange`
- Windows VM shared-drive default: `Z:\operator-exchange`
- Windows SSH-copy default: `C:\ProgramData\WindowsOperator\exchange`

The VM directory should be declared by NixOS as part of the Windows VM virtio-fs
share. SSH-copy machines use machine-local Windows exchange state and copy
runner results back to the Linux exchange root. Windows scripts should treat
either exchange location as output/state, not source code.

## Authority And Overrides

Exchange paths are cross-process contracts. Do not change one default without
checking every producer and consumer below.

| Surface | Owner | Override | Default |
| --- | --- | --- | --- |
| Linux CLI summaries and lease files | `scripts/linux/windows_operator_harness.py`, `scripts/linux/wo`, profile runners | `WINDOWS_OPERATOR_EXCHANGE_ROOT` | `/var/lib/windows-server/shared/operator-exchange` |
| Linux SSH runner artifacts | `scripts/linux/windows-run-ps.sh` | `WINDOWS_OPERATOR_EXCHANGE_ROOT` | `/var/lib/windows-server/shared/operator-exchange` |
| Windows staging path for Linux runner | `scripts/linux/windows-run-ps.sh` | `WINDOWS_OPERATOR_WINDOWS_EXCHANGE` | `Z:\operator-exchange` for `shared`; `C:\ProgramData\WindowsOperator\exchange` for `ssh-copy` |
| Agent workbench writes | `WorkbenchOptions.ExchangeRoot` through Agent config/env | `WINDOWS_OPERATOR_EXCHANGE_ROOT`, `-ExchangeRoot` | `Z:\operator-exchange` |
| Agent host-visible artifact paths | `WorkbenchOptions.HostExchangeRoot` through Agent config/env | `WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT`, `-HostExchangeRoot` | `/var/lib/windows-server/shared/operator-exchange` |
| Host artifact reads | `ExchangeArtifactService` through Host config/env | `WINDOWS_OPERATOR_EXCHANGE_ROOT`, `-ExchangeRoot` | `Z:\operator-exchange` or registered override |

For VM shared-drive runs, Windows writes to `Z:\operator-exchange` and Host/Agent
can publish Linux-visible paths with `HostExchangeRoot=/var/lib/windows-server/shared/operator-exchange`.
For SSH-copy runs, pass both `-ExchangeRoot` and `-HostExchangeRoot` as
`C:\ProgramData\WindowsOperator\exchange` when registering the Windows services;
Linux runner copyback keeps Linux-side artifacts under `WINDOWS_OPERATOR_EXCHANGE_ROOT`.

## Current Layout

```text
operator-exchange/
  downloads/
    mail/
  sessions/
    <session-id>.json
  powerpoint-online-sessions/
    <session-id>.json
  runs/
    <run-id>/
      command.ps1
      stdout.txt
      stderr.txt
      transcript.txt
      result.json
      request.json
```

`sessions/` and `powerpoint-online-sessions/` hold lightweight session indexes.
Large artifacts and screenshots still belong under `runs/<run-id>/`.
`inbox/`, `outbox/`, `logs/`, root-level `screenshots/`, and ad hoc legacy
smoke directories are not part of the current live layout. Add them only when a
caller needs that contract.

## Rules

- Keep code in the Windows repo root: `Z:\windows-operator` on the VM share, or
  `WINDOWS_OPERATOR_WINDOWS_REPO_ROOT` on SSH-synced targets.
- Keep Windows build/cache state in `%LOCALAPPDATA%\WindowsOperator`.
- Put Linux-consumed output in the configured Windows exchange root:
  `Z:\operator-exchange` for VM shared-drive runs, or
  `C:\ProgramData\WindowsOperator\exchange` for SSH-copy runs.
- Do not write NuGet, bin, obj, or Codex credentials into exchange.
- Use unique run IDs for automation runs.
- Treat `runs/<run-id>/command.ps1` as a staged copy. Source of truth stays
  under the configured Windows repo root in `scripts\windows`.
- Never execute scripts directly from `operator-exchange/inbox`.

## Linux Runner

Helper:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/probe-url.ps1 \
  -Url http://127.0.0.1:43117/v1/health
```

Behavior:

- Accept only repo-relative `.ps1` paths under `scripts/windows/`.
- Copy the repo script to `operator-exchange/runs/<run-id>/command.ps1`.
- Write `request.json` with repo-relative source path, Windows source path, arguments, and SHA256.
- Wait for Windows SSH on host `127.0.0.1:22555`.
- Run `scripts/windows/run-staged-repo-script.ps1` over SSH.
- Verify staged `command.ps1` hash matches repo source hash before execution.
- Capture stdout, stderr, exit code, timing, and command line.
- Write artifacts to `operator-exchange/runs/<run-id>`.
- Print `result.json` path for follow-up tools.

Useful overrides:

- `WINDOWS_OPERATOR_EXCHANGE_ROOT`
- `WINDOWS_OPERATOR_WINDOWS_EXCHANGE`
- `WINDOWS_OPERATOR_WINDOWS_REPO_ROOT`
- `WINDOWS_OPERATOR_SSH_USER`
- `WINDOWS_OPERATOR_SSH_HOST`
- `WINDOWS_OPERATOR_SSH_TARGET`
- `WINDOWS_OPERATOR_SSH_PORT`
- `WINDOWS_OPERATOR_SSH_IDENTITY_FILE`
- `WINDOWS_OPERATOR_SSH_TIMEOUT`
- `WINDOWS_OPERATOR_RUN_ID`

By default the runner uses `administrator@127.0.0.1:22555` and
`/run/secrets/ssh_automation_key` when the secret exists.

For per-machine defaults, create `.windows-operator.local.env` in the repo root.
The file is ignored by git and loaded by both Linux helpers before built-in
defaults:

```bash
: "${WINDOWS_OPERATOR_SSH_HOST:=<tailscale-host>}"
: "${WINDOWS_OPERATOR_SSH_USER:=<windows-user>}"
: "${WINDOWS_OPERATOR_SSH_PORT:=22}"
: "${WINDOWS_OPERATOR_SSH_IDENTITY_FILE:=/run/secrets/ssh_automation_key}"
: "${WINDOWS_OPERATOR_SSH_TIMEOUT:=120}"
: "${WINDOWS_OPERATOR_WINDOWS_REPO_ROOT:=C:\\src\\windows-operator}"
: "${WINDOWS_OPERATOR_RUN_TRANSPORT:=ssh-copy}"
```

### Non-VM Tailscale Targets

For physical Windows machines, there is no `Z:` shared drive. Sync the repo over
SSH, then run commands with copy-backed staging:

```bash
export WINDOWS_OPERATOR_SSH_HOST=<tailscale-host>
export WINDOWS_OPERATOR_SSH_PORT=22
export WINDOWS_OPERATOR_SSH_USER=<windows-user>
export WINDOWS_OPERATOR_WINDOWS_REPO_ROOT='C:\src\windows-operator'
export WINDOWS_OPERATOR_RUN_TRANSPORT=ssh-copy

scripts/linux/windows-sync-repo.sh
scripts/linux/windows-run-ps.sh scripts/windows/bootstrap.ps1 \
  -RepoRoot 'C:\src\windows-operator' \
  -EnableAutostart \
  -ExchangeRoot 'C:\ProgramData\WindowsOperator\exchange' \
  -HostExchangeRoot 'C:\ProgramData\WindowsOperator\exchange'
```

`ssh-copy` keeps Linux-side artifacts under `WINDOWS_OPERATOR_EXCHANGE_ROOT`,
stages `request.json` and `command.ps1` under
`C:\ProgramData\WindowsOperator\exchange` on the Windows machine, runs the
repo-owned executor, then copies `result.json` and `transcript.txt` back.

Repo-owned PowerShell profile sync:

```bash
scripts/linux/windows-sync-powershell-profile.sh
```

This syncs the repo, then writes a managed source block into the Windows user's
PowerShell profile files for `WindowsPowerShell` and `PowerShell`. The target
profile files remain machine-local state; the actual aliases/functions live in
`profiles\powershell\profile.ps1` inside the synced repo. Override targets with
`WINDOWS_OPERATOR_POWERSHELL_PROFILE_TARGETS`, separated by semicolons.

Top-level sync shortcut:

```bash
just sync
```

`just sync` probes configured targets, skips offline machines, and runs repo
plus PowerShell profile sync on reachable machines. Set
`WINDOWS_OPERATOR_SYNC_PROFILE=0` to narrow that top-level behavior. Configure multiple targets with
`WINDOWS_OPERATOR_SYNC_TARGETS`, separated by whitespace:

```bash
export WINDOWS_OPERATOR_SYNC_TARGETS='alejg@legion9-win:22 other-win'
just sync
```

Use `just sync-plan` to inspect target resolution without connecting.
Windows Operator does not render, install, sync, or verify Codex configuration.
Edit Codex files directly on each machine.

Keep Host REST loopback-only on non-VM targets too. Use an SSH local forward
instead of binding unauthenticated Operator REST to the tailnet:

```bash
ssh -N -L 43127:127.0.0.1:43117 <windows-user>@<tailscale-host>
curl http://127.0.0.1:43127/v1/health
```

## REST Tunnel

Add host tunnel:

- Host: `127.0.0.1:43117`
- Guest: `127.0.0.1:43117`

Then Linux tools can call:

```bash
curl http://127.0.0.1:43117/v1/health
```

This should remain loopback-only.
