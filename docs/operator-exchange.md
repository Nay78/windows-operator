# Operator Exchange

Goal: make Windows-side automation artifacts directly available to Linux tools without copying repo source or placing mutable state in shared source.

## Paths

- Linux: `/var/lib/windows-server/shared/operator-exchange`
- Windows: `Z:\operator-exchange`

This directory should be declared by NixOS as part of the Windows VM virtio-fs share. Windows scripts should treat it as an exchange/output area, not as source code.

## Layout

```text
operator-exchange/
  inbox/
  outbox/
  logs/
  downloads/
    mail/
  runs/
    <run-id>/
      command.ps1
      stdout.txt
      stderr.txt
      transcript.txt
      result.json
      request.json
  screenshots/
```

## Rules

- Keep code in `Z:\windows-operator`.
- Keep Windows build/cache state in `%LOCALAPPDATA%\WindowsOperator`.
- Put Linux-consumed output in `Z:\operator-exchange`.
- Do not write NuGet, bin, obj, or Codex credentials into exchange.
- Use unique run IDs for automation runs.
- Treat `runs/<run-id>/command.ps1` as a staged copy. Source of truth stays under `Z:\windows-operator\scripts\windows`.
- Never execute scripts directly from `operator-exchange/inbox`.

## Linux Runner

Helper:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/some-script.ps1 --arg value
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
- `WINDOWS_OPERATOR_RUN_ID`

By default the runner uses `administrator@127.0.0.1:22555` and `/run/secrets/ssh_automation_key` when the secret exists.

## REST Tunnel

Add host tunnel:

- Host: `127.0.0.1:43117`
- Guest: `127.0.0.1:43117`

Then Linux tools can call:

```bash
curl http://127.0.0.1:43117/v1/health
```

This should remain loopback-only.
