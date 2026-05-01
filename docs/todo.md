# Todo

Backlog from current Windows VM provisioning and automation session.

## High Priority

- Add Linux-visible operator exchange root.
  - Linux path: `/var/lib/windows-server/shared/operator-exchange`
  - Windows path: `Z:\operator-exchange`
  - Subdirs: `inbox`, `outbox`, `logs`, `downloads`, `runs`, `screenshots`

- Add Linux host tunnel for Windows Operator REST.
  - Host `127.0.0.1:43117`
  - Windows `127.0.0.1:43117`
  - Same wait/reconnect behavior as Codex app-server tunnel

- Verify script runner for Linux-to-Windows debugging on live VM.
  - Path: `scripts/linux/windows-run-ps.sh`
  - Stages only repo-owned PowerShell scripts from `scripts/windows`
  - Writes transcript/stdout/stderr/request/result JSON to `operator-exchange/runs/<run-id>`
  - Returns nonzero on failure

- Rebuild/switch NixOS host with latest VM hardening.
  - `windows-server.service`: no start-limit, restart always
  - `windows-server-virtiofsd.service`: no start-limit, restart always, stale socket cleanup
  - `windows-server-codex-app-server-tunnel.service`: no start-limit

- After host switch, recover live Windows VM and confirm:
  - SSH `127.0.0.1:22555`
  - RDP `127.0.0.1:33895`
  - Operator health `127.0.0.1:43117`
  - Codex app-server `127.0.0.1:43118`

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

- Add screenshot capture smoke against Notepad.
  - Open Notepad
  - List windows
  - Activate
  - Query UIA
  - Type text
  - Capture screenshot

- Add host-side docs for the split:
  - `nixos` owns VM/share/tunnels
  - `windows-operator` owns Windows agent/scripts/automation

## Nice To Have

- Add structured JSON logging for PowerShell launchers.
- Add run-id propagation through launcher, scripts, and logs.
- Add a simple `just` or PowerShell task file once this repo has its own command runner.
- Add a local-machine override template under docs, not active source config.
