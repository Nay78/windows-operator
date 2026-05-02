# Development Notes

## Runtime model

`WindowsOperator.Host` runs headless at startup and owns REST on `127.0.0.1:43117`. `WindowsOperator.Agent` runs inside the logged-in desktop session and owns UI automation on `127.0.0.1:43119`. Autostart uses Task Scheduler: startup for Host, logon for Agent.

## Platform target

- Minimum supported OS: Windows 10 2004
- Primary target: Windows 11
- Desktop session required for automation and screenshots
- Shared repo path stays source of truth. Windows-local mutable state lives under `%LOCALAPPDATA%\WindowsOperator` unless overridden.

## Provisioning

Fresh Windows host:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap.ps1 -RepoRoot \\server\share\windows-operator -EnableAutostart
```

Bootstrap creates local state directories for .NET home, NuGet cache, build outputs, logs, and run wrappers. Local machine overrides belong in `%LOCALAPPDATA%\WindowsOperator\run\appsettings.Local.json`.

VM bootstrap also installs Codex CLI and registers `Codex.AppServer`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap-codex.ps1 -EnableAutostart
```

Codex mutable state lives under `%LOCALAPPDATA%\Codex`. Run `codex login` manually in the Windows desktop session; provisioning never writes credentials. The task starts `codex app-server --listen ws://127.0.0.1:43118` only after login is present. Linux host access uses the NixOS SSH tunnel on `127.0.0.1:43118`.

Bootstrap also makes `codex` usable from normal Windows shells by persisting `%LOCALAPPDATA%\Codex\npm-global` on the user `PATH` and placing forwarding shims in `%APPDATA%\npm`.

## Microsoft device login

Use this when a Microsoft device-code flow prints a code and needs browser handoff in the Windows desktop session:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/device-login \
  -H 'Content-Type: application/json' \
  -d '{"deviceCode":"ABCD-EFGH"}'
```

The REST endpoint proxies from Host to Agent. Agent opens Edge at `https://microsoft.com/devicelogin`, pastes the device code, submits it, and stops there. The user completes Microsoft account and MFA prompts in Edge. External services should use this REST endpoint instead of SSH.

SSH fallback:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/login-microsoft-device-code.ps1 -DeviceCode ABCD-EFGH
```

The helper schedules itself into the logged-in desktop session and runs the same Edge handoff when REST is unavailable.

## PowerPoint automation

PowerPoint slide mutation target architecture lives in [PowerPoint automation target architecture](powerpoint-automation-architecture.md).

High-level rule: external services send typed edit plans through REST, Host proxies, and Desktop Agent executes PowerPoint COM inside the logged-in Windows desktop session. Browser automation may open/authenticate a PowerPoint link, but slide edits use the PowerPoint object model, not web UI clicks.

## Outlook mail automation

Outlook mail refresh and recovery target architecture lives in [Outlook mail automation target architecture](outlook-mail-automation-architecture.md).

High-level rule: external callers request mail intent only. Windows Operator owns Outlook attach/start/close behavior, automatic sync, stale-folder retry, and bounded recovery.

## Manual smoke flow

1. Start Notepad.
2. Confirm Host health on `http://127.0.0.1:43117/v1/health`.
3. Run `powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-agent.ps1 -RepoRoot \\server\share\windows-operator`.
4. Call `GET /v1/windows` through Host and capture Notepad `hwnd`.
5. Call `POST /v1/windows/{id}/activate`.
6. Call `POST /v1/uia/query` with control filters for the edit control.
7. Call `POST /v1/uia/type` with text payload.
8. Call `GET /v1/windows/{id}/screenshot`.

## Test split

- Unit tests cover contracts, config, REST/MCP parity, encoding policy, and error mapping.
- `WindowsOperator.Portable.slnf` runs Linux-safe Core/MCP tests without WindowsDesktop runtime.
- Integration tests require a real Windows desktop session and are gated behind `WINDOWS_OPERATOR_RUN_INTEGRATION=1`.
