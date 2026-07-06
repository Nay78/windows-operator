# Development Notes

## Runtime model

`WindowsOperator.Host` runs headless at startup after a 30 second delay and owns
REST on `127.0.0.1:43117`. `WindowsOperator.Agent` runs inside the logged-in
desktop session and owns UI automation on `127.0.0.1:43119`. Autostart uses
Task Scheduler: delayed startup for Host, delayed logon for Agent.

## Harness layering

REST is the most stable composition boundary. Add durable capabilities as
typed `/v1/*` primitives or domain workflows, keep them in OpenAPI, and preserve
their request/result contracts for external services and generated clients.

External projects integrate through Host REST, OpenAPI, and generated clients.
Do not make `scripts/linux/wo`, `Justfile` recipes, SSH runner scripts, or
staged PowerShell a dependency surface for external application code.
See [External consumer integration spec](external-consumer-integration.md) for
the target consumer contract and release gates.

CLI scripts own agent/operator flows: safe defaults, run IDs, lease files, TTL,
cleanup traps, summaries, artifact paths, and evidence aggregation. They should
compose REST rather than duplicate Windows automation behavior.

Preferred harness entrypoint is `scripts/linux/wo`. Put agent-facing examples
there first. Keep direct REST examples for API users and low-level debugging.

`Justfile` recipes are the unstable developer and agent command menu. Keep them
thin: discoverable names, common defaults, and shortcuts into CLI scripts.
Do not put session ownership, proof policy, retries, or JSON state machines
inline in Just recipes.

See [Operator harness target architecture](operator-harness-architecture.md)
for the full REST/CLI/Just layering contract.
See [Operator harness CLI contract](operator-harness-cli-contract.md) for the
shared harness flag, exit code, summary, and live-proof rules.

## Platform target

- Minimum supported OS: Windows 10 2004
- Primary target: Windows 11
- Desktop session required for automation and screenshots
- Shared repo path stays source of truth. Windows-local mutable state lives
  under `%LOCALAPPDATA%\WindowsOperator` unless overridden.

## VM workbench evidence

Use the workbench routes when an external Linux-side tool needs durable visual
evidence instead of inline screenshot base64.

```bash
curl http://127.0.0.1:43117/v1/desktop/foreground
curl -X POST http://127.0.0.1:43117/v1/desktop/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"target":"foreground","runId":"smoke","label":"foreground"}'
curl -X POST http://127.0.0.1:43117/v1/browser/edge/open-url \
  -H 'Content-Type: application/json' \
  -d '{"url":"https://example.com","capture":true,"runId":"smoke","label":"edge-open"}'
curl http://127.0.0.1:43117/v1/sessions/smoke
curl -X POST http://127.0.0.1:43117/v1/sessions/smoke/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"label":"session"}'
```

Desktop Agent writes screenshots, session state, window snapshots, and workbench
events under `WINDOWS_OPERATOR_EXCHANGE_ROOT` or `Z:\operator-exchange`.
Host-facing artifact refs map the same relative path under
`WINDOWS_OPERATOR_HOST_EXCHANGE_ROOT` or
`/var/lib/windows-server/shared/operator-exchange`.

## Provisioning

Fresh Windows host:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap.ps1 -RepoRoot \\server\share\windows-operator -EnableAutostart
```

Bootstrap creates local state directories for .NET home, NuGet cache, build
outputs, logs, and run wrappers. Agent local machine overrides belong in
`%LOCALAPPDATA%\WindowsOperator\run\appsettings.Local.json`. Host autostart
overrides are generated under
`%ProgramData%\WindowsOperator\run\host.appsettings.Local.json` by
`scripts/windows/register-host-autostart.ps1`. Pass `-ExchangeRoot` and
`-HostExchangeRoot` when registering Host on copy-backed/Tailscale machines so
SYSTEM reads artifacts from machine-local state such as
`C:\ProgramData\WindowsOperator\exchange`; do not rely on per-user mapped drives
being visible to the scheduled task. See
[Local machine overrides](local-machine-overrides.md) for template shapes.

For a non-VM Tailscale machine, sync source over SSH and use copy-backed command staging:

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
ssh -N -L 43127:127.0.0.1:43117 <windows-user>@<tailscale-host>
curl http://127.0.0.1:43127/v1/health
```

For interactive shell conveniences on the Windows user, sync repo-owned shell
and Codex profile state:

```bash
just sync
```

Sync target selection, direct profile/Codex helpers, and opt-outs are documented
in [operator exchange](operator-exchange.md#linux-runner). Edit aliases and
functions in `profiles/powershell/profile.ps1`, then run sync again.

VM bootstrap also installs Codex CLI and registers `Codex.AppServer`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\bootstrap-codex.ps1 -EnableAutostart -InstallProfile
```

Codex install/log/npm state lives under `%LOCALAPPDATA%\Codex`. Codex
configuration and auth use `CODEX_HOME`, defaulting here to
`%USERPROFILE%\.codex`, matching Codex's documented default shape.
`-InstallProfile` writes a Windows-safe profile: `config.toml`, `AGENTS.md`,
`rules/default.rules`, and read-only agent definitions. It does not copy
`auth.json`, history, session databases, plugin caches, or Linux/Nix-specific
MCP paths. Run `codex login` manually in the Windows desktop session;
provisioning never writes credentials. The task starts
`codex app-server --listen ws://127.0.0.1:43118` only after login is present.
Linux host access uses the NixOS SSH tunnel on `127.0.0.1:43118`.

Bootstrap also makes `codex` usable from normal Windows shells by persisting
`%LOCALAPPDATA%\Codex\npm-global` on the user `PATH` and placing forwarding
shims in `%APPDATA%\npm`.

## Live validation checklist

Use this after changing Host/Agent routes, scheduled tasks, tunnels, browser
automation, mail, or PowerPoint queue behavior. These checks prove live Windows
runtime behavior, not only serialization.

Run the full safe smoke:

```bash
scripts/linux/wo smoke
```

`wo smoke` writes contract `summary.json` to
`operator-exchange/runs/<run-id>/summary.json`, stores delegated report
`operator-exchange/runs/<run-id>/live-smoke-report.json`, captures desktop/Edge
screenshots, cleans Edge sessions, and marks its synthetic PowerPoint job failed
after claim so nothing stays queued.

Targeted manual checks:

```bash
curl http://127.0.0.1:43117/v1/health
curl http://127.0.0.1:43117/openapi.json | jq '.paths | length'
curl -i http://127.0.0.1:43118/
curl -X POST http://127.0.0.1:43117/v1/uia/query \
  -H 'Content-Type: application/json' \
  -d '{"controlType":"Window","includeOffscreen":false,"maxResults":5}'
curl -X POST http://127.0.0.1:43117/v1/browser/edge/session/start \
  -H 'Content-Type: application/json' \
  -d '{"sessionId":"smoke-edge","startUrl":"https://example.com","profileMode":"temp","pageLoadSeconds":5}'
curl -X POST http://127.0.0.1:43117/v1/mail/messages/search \
  -H 'Content-Type: application/json' \
  -d '{"subjectContains":"__windows_operator_smoke_no_match__","maxResults":1,"freshness":"cached"}'
job_id="smoke-ppt-$(date -u +%Y%m%dT%H%M%SZ)"
curl -X POST http://127.0.0.1:43117/v1/powerpoint/jobs \
  -H 'Content-Type: application/json' \
  -d "{\"jobId\":\"$job_id\",\"requestedBy\":\"smoke\",\"operations\":[{\"kind\":\"replaceImage\",\"targetId\":\"live-image-target\",\"artifact\":{\"artifactId\":\"pixel\",\"url\":\"data:image/png;base64,AQID\",\"mediaType\":\"image/png\"}}]}"
```

Expected results:

- Host health returns `status=ok` and `runtimeMode=headless-host`.
- OpenAPI path count matches the generated contract for the checkout; compare
  `curl http://127.0.0.1:43117/openapi.json | jq '.paths | length'` with
  `jq '.paths | length' openapi/windows-operator.openapi.json`.
- README route inventory contains every committed OpenAPI path:

  ```bash
  scripts/check-readme-route-inventory.sh
  ```

  `README.md` also lists `GET /openapi.json`; that serving endpoint is expected
  to be absent from the generated OpenAPI `paths` object.
- Codex app-server tunnel returns HTTP `400` with an Upgrade-header message when queried without WebSocket upgrade.
- UIA query returns window elements, not a bare `500`.
- Edge session reaches `page_ready`; clean it with `POST /v1/browser/edge/session/{sessionId}/cleanup`.
- Cached mail negative search returns `200` with zero messages when mailbox cache is healthy.
- PowerPoint queue accepts the job; then claim/fail or complete it so no smoke job remains queued.

If Linux `curl http://127.0.0.1:43117/v1/health` returns an empty reply, verify the Windows-side Host task before checking Desktop Agent:

```powershell
Get-ScheduledTask -TaskName WindowsOperator.Host
Start-ScheduledTask -TaskName WindowsOperator.Host
Invoke-RestMethod http://127.0.0.1:43117/v1/health
```

For runner-based restarts, use the repo helper:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/restart-scheduled-task.ps1 \
  -TaskName WindowsOperator.Host \
  -WaitSeconds 45
```

## Microsoft device login

Use this when a Microsoft device-code flow prints a code and needs browser
handoff in the Windows desktop session:

```bash
scripts/linux/wo auth microsoft device-login --device-code ABCD-EFGH
```

Lower-level REST example:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/device-login \
  -H 'Content-Type: application/json' \
  -d '{"deviceCode":"ABCD-EFGH"}'
```

The REST endpoint proxies from Host to Agent. Agent opens Edge at
`https://microsoft.com/devicelogin`, pastes the device code, submits it, and
reports browser observation status. External services should treat only
`status=browserAccepted` as completed handoff; `timedOut`, `failed`,
`invalidCode`, `needsUserAction`, and unresolved `submitted` return
`success:false`.

SSH fallback:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/login-microsoft-device-code.ps1 -DeviceCode ABCD-EFGH
```

The helper schedules itself into the logged-in desktop session and runs the same
Edge handoff when REST is unavailable.

Device-code signed-in Edge profile reuse:

```bash
scripts/linux/wo auth microsoft device-login --device-code ABCD-EFGH -- --reuse-existing-profile
```

Lower-level REST example:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/device-login \
  -H 'Content-Type: application/json' \
  -d '{"deviceCode":"ABCD-EFGH","reuseExistingProfile":true}'
```

Use this when the account picker or consent page must reuse the already signed-in
Work profile instead of a fresh temporary Edge profile.

Cleanup stale Microsoft-auth Edge windows:

```bash
scripts/linux/cleanup-microsoft-auth-edge.sh
```

This closes lingering Edge Microsoft-auth windows opened by prior device-login
or authorize-probe runs. Add `--dry-run` to inspect match counts only, or
`--preserve-recent-seconds 60` to keep very recent auth windows open.

Graph `Mail.Read` device-code probe:

```bash
scripts/linux/test-microsoft-graph-mail-read.sh \
  --tenant-id <tenant-id> \
  --client-id <client-id> \
  --handoff windows-script
```

This helper keeps token polling outside Windows Operator, as intended by the
auth architecture, while reusing the Windows desktop browser handoff.

Graph auth-code redirect probe:

```bash
scripts/linux/wo auth microsoft authorize-probe --authorize-url 'https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize?...'
```

Lower-level REST example:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/authorize-probe \
  -H 'Content-Type: application/json' \
  -d '{"authorizeUrl":"https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize?..."}'
```

This endpoint opens the authorize URL in Edge, watches the live page URL/title
through Edge DevTools, and returns whether a redirect/code/error was observed or
the page stayed blocked on user action.

Authorize-probe signed-in Edge profile reuse:

```bash
scripts/linux/wo auth microsoft authorize-probe --authorize-url 'https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize?...' -- --reuse-existing-profile
```

Lower-level REST example:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/authorize-probe \
  -H 'Content-Type: application/json' \
  -d '{"authorizeUrl":"https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize?...","reuseExistingProfile":true}'
```

Use this when tenant auth must reuse the already signed-in Work profile instead
of a fresh temporary Edge profile.

Edge work-profile rule:

- On this VM, signed-in Edge profile directory is `Default` under
  `%LOCALAPPDATA%\\Microsoft\\Edge\\User Data`.
- Browser session and Microsoft auth work-mode launches should pass explicit
  `--profile-directory=Default`.
- Relying on generic work/default launch without explicit profile selection can
  open Entra at login instead of the already signed-in tenant session.

Auth guardrails:

- Public/delegated flows first. If app reaches token `200` without secret, keep
  testing.
- If token leg returns `invalid_client` / `client_secret` / `client_assertion`,
  stop that app under current constraints.
- If browser reaches `Need admin approval`, stop that app unless tenant/admin
  state changes.
- Token endpoint success is proof. Browser progression alone is not proof.
- `AADSTS500113` on authorization-code flow means the tested redirect/reply URI
  is not registered. Avoid that redirect path unless the exact redirect URI is
  already configured in Entra.
- Device-code flow does not rely on redirect URI, so auth-code redirect failure
  does not automatically kill device-code.
- App-only/client-credentials paths only help if the actual secret or
  certificate value is already available locally.

## Entra app auditor

Linux-side Entra inspection/probing helper:

```bash
python3 scripts/linux/audit_entra_apps.py \
  --tenant-id <tenant-id> \
  --host-base-url http://127.0.0.1:43117 \
  --output-root artifacts/entra-audit
```

Behavior:

- uses browser session REST endpoints, not SSH UI scripts
- writes resumable state to `run.json`, `summary.json`, `apps.jsonl`
- writes raw per-app artifacts under `artifacts/<client-id>/`
- `--metadata-only` skips OAuth probes
- `--probe-candidates-only` reuses persisted candidates only
- `--resume` continues prior run state in place

## PowerPoint automation

PowerPoint slide mutation target architecture lives in
[PowerPoint automation target architecture](powerpoint-automation-architecture.md).

High-level rule: external services enqueue typed update jobs through Host REST.
Host owns queue and artifact staging. The Office.js add-in claims jobs and
mutates the active presentation through `PowerPoint.run`. Browser automation
and desktop COM do not mutate slides.

## Outlook mail automation

Outlook mail refresh and recovery target architecture lives in
[Outlook mail automation target architecture](outlook-mail-automation-architecture.md).

High-level rule: external callers request mail intent only. Windows Operator
owns Outlook attach/start/close behavior, automatic sync, stale-folder retry,
and bounded recovery.

## Live smoke

General live smoke:

```bash
scripts/linux/wo smoke
```

Full desktop mutation smoke:

```bash
scripts/linux/wo smoke -- --include-notepad
```

Deep smoke with live auth boundary and real Outlook freshness path:

```bash
scripts/linux/wo smoke -- --include-notepad --include-auth-live-negative --include-fresh-mail
```

For Host-staged PowerPoint add-in verification without local Vite or an SSH
tunnel, add the Windows-side probe:

```bash
scripts/linux/wo smoke -- --include-notepad --include-auth-live-negative --include-fresh-mail --powerpoint-addin-windows-probe
```

The Notepad path launches Notepad through the Windows script runner as the
logged-in interactive user, then verifies the visible behavior through Host
REST: window catalog, activation, UIA `Document` query, UIA type, screenshot
artifact, and cleanup.

The smoke also verifies the PowerPoint add-in taskpane at
`https://127.0.0.1:3003/taskpane.html` unless `--skip-powerpoint-addin` is
passed. From Linux that URL only works when a local temporary Vite HTTPS server
or an explicit SSH tunnel is present. For Host-staged add-in HTTPS on Windows
loopback, pass `--powerpoint-addin-windows-probe` to verify
`https://localhost:3003/taskpane.html` through `scripts/windows/probe-url.ps1`.

Optional live paths:

- `--include-auth-live-negative` opens a synthetic Microsoft authorize URL in
  Edge, verifies the login/error boundary, then closes the auth window.
- `--include-fresh-mail` runs a slower no-match Outlook search with
  `freshness:fresh`, proving the real worker/sync path without requiring
  mailbox contents.

Baseline evidence:

- Windows VM reachability on 2026-06-28: SSH `127.0.0.1:22555`, RDP
  `127.0.0.1:33895`, Host health on `127.0.0.1:43117`, and Codex app-server
  tunnel `127.0.0.1:43118`.
- Host registration baseline run:
  `repo-align-register-host-addin-default2-20260628t223804z`.
- Host-staged add-in HTTPS on 2026-06-28:
  `/var/lib/windows-server/shared/operator-exchange/runs/repo-align-live-smoke-final-20260628t225023z/live-smoke-report.json`
  with 44 passed and 0 failed.
- Older same-day and reboot baseline reports are archived in
  `/tmp/windows-operator-repo-alignment-20260628.md`.
- Windows power guard after bootstrap: AC sleep, hibernate, disk, and display
  timeouts were `0`; hibernation was disabled.

## Manual smoke flow

Use this for endpoint diagnosis when `scripts/linux/wo smoke` is too broad.

1. Start Notepad.
2. Confirm Host health on `http://127.0.0.1:43117/v1/health`.
3. Run `powershell -ExecutionPolicy Bypass -File .\scripts\windows\run-agent.ps1 -RepoRoot \\server\share\windows-operator`.
4. Call `GET /v1/windows` through Host and capture Notepad `hwnd`.
5. Call `POST /v1/windows/{id}/activate`.
6. Call `POST /v1/uia/query` with control filters for the edit control.
7. Call `POST /v1/uia/type` with text payload.
8. Call `GET /v1/windows/{id}/screenshot`.
9. Close the Notepad process or run `scripts/windows/stop-notepad-smoke.ps1` through `scripts/linux/windows-run-ps.sh`.

## Test split

- Unit tests cover contracts, config, REST/MCP parity, encoding policy, and error
  mapping.
- `WindowsOperator.Portable.slnf` builds the Linux-safe Core/Host/MCP/OpenAPI
  set and runs Host/MCP tests; Core.Tests require WindowsDesktop runtime and
  belong on Windows.
- Windows testhost execution needs the .NET 8 runtimes that match project refs:
  `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App`.
  SDK-only layouts under `%LOCALAPPDATA%\WindowsOperator\dotnet-sdk` are not enough.
- When running tests through the staged Linux runner, pass explicit `-RepoRoot`
  so project paths resolve against the Windows source copy:
  `scripts/linux/windows-run-ps.sh scripts/windows/run-dotnet-test.ps1 -RepoRoot 'C:\src\windows-operator' -Project tests\WindowsOperator.Mcp.Tests\WindowsOperator.Mcp.Tests.csproj`.
- Integration tests require a real Windows desktop session and are gated behind
  `WINDOWS_OPERATOR_RUN_INTEGRATION=1`.
