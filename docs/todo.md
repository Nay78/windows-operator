# Todo

Backlog from current Windows VM provisioning and automation session.

## Verified 2026-06-20

- Live Windows VM reachability confirmed:
  - SSH `127.0.0.1:22555`
  - RDP `127.0.0.1:33895`
  - Operator health `http://127.0.0.1:43117/v1/health`
  - Codex app-server tunnel `127.0.0.1:43118` returns the expected WebSocket-upgrade error to plain HTTP

- Windows script runner verified live through `scripts/linux/windows-run-ps.sh`.
  - Stages only repo-owned PowerShell scripts from `scripts/windows`
  - Writes stdout/stderr/request/result JSON to `operator-exchange/runs/<run-id>`
  - Latest Host registration run: `codex-live-register-host-proxy-20260620T214556Z`

- Host/Agent live validation passed:
  - OpenAPI: 39 paths
  - UIA query returned live window elements
  - Desktop and Edge screenshots wrote artifacts under `operator-exchange/runs`
  - Edge session start/click/fill/screenshot/cleanup passed
  - Mail cached negative search returned 0 messages without error
  - PowerPoint job enqueue/get/artifact/claim/fail/get passed
  - PowerPoint add-in HTTPS served `https://localhost:3003/taskpane.html` from the Host scheduled task
  - Notepad-specific live smoke opened Notepad in the logged-in desktop, activated it, typed through UIA, captured a screenshot, and cleaned up
  - Repeatable command: `scripts/linux/live-smoke.py --include-notepad`
  - Latest report: `/var/lib/windows-server/shared/operator-exchange/runs/live-smoke-20260620t221317z/live-smoke-report.json` (`39` passed, `0` failed)

## High Priority

- Decide final operator exchange root shape.
  - Linux path exists: `/var/lib/windows-server/shared/operator-exchange`
  - Windows path: `Z:\operator-exchange`
  - Live subdirs in use: `downloads`, `runs`
  - Decide whether root-level `inbox`, `outbox`, `logs`, and `screenshots` are still required or obsolete

- Rebuild/switch NixOS host with latest VM hardening.
  - `windows-server.service`: no start-limit, restart always
  - `windows-server-virtiofsd.service`: no start-limit, restart always, stale socket cleanup
  - `windows-server-codex-app-server-tunnel.service`: no start-limit

## Email Attachment Automation

- Implement Classic Outlook COM attachment downloader. Done for v1.
  - Folder, subject, date, attachment-presence filters
  - Save attachments under `Z:\operator-exchange\downloads\mail`
  - Write per-run JSON manifest
  - Persist processed message/attachment state
  - Expose REST and MCP tools for AI runtimes
  - Sender/account SMTP filters deferred; Outlook Object Model Guard prompts on those fields

- Add mailbox automation docs and examples.
  - Required Classic Outlook setup
  - New Outlook unsupported warning
  - Troubleshooting and logs

- Resolve no-secret Graph viability with existing Entra apps.
  - Target app first: `ams-prd-rpamail` (`4d7414f8-221b-4a9d-9117-1ca3ade51b21`)
  - Prove live whether existing app can mint delegated `Mail.Read` token without secret
  - Inspect redirect/public-client shape and classify viable auth mode
  - Authorize-probe mode can reuse existing signed-in Edge work profile
  - If not viable, close Graph path and keep Outlook/OWA fallback as system truth

## Windows Provisioning

- Verify `powercfg` guard live on Windows after next bootstrap.
  - Hibernate disabled
  - AC sleep timeout disabled
  - AC disk timeout disabled
  - AC monitor timeout disabled

- Investigate hourly Windows guest exits in Event Log after VM recovers.
  - System log: shutdown/sleep/restart events
  - Power-Troubleshooter
  - Kernel-Power
  - User32 shutdown reason
  - Task Scheduler maintenance triggers

- Re-run `bootstrap-vm.ps1` after host recovery.
  - Confirms .NET restore/build/test
  - Re-registers tasks
  - Applies power policy guard
  - Confirms Codex installed and login status detected

## Operator Quality

- Add endpoint or tool for agent logs/state inspection.
  - Recent run logs
  - Health details
  - Current desktop session
  - Task status

- Add host-side docs for the split:
  - `nixos` owns VM/share/tunnels
  - `windows-operator` owns Windows agent/scripts/automation

## Nice To Have

- Add structured JSON logging for PowerShell launchers.
- Add run-id propagation through launcher, scripts, and logs.
- Add a simple `just` or PowerShell task file once this repo has its own command runner.
- Add a local-machine override template under docs, not active source config.
