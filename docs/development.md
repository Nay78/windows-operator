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
