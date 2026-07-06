# Todo

Backlog. Dated live baselines live in [development notes](development.md#live-smoke).

## External Dependency

- Rebuild/switch NixOS host with pending VM hardening changes.
  - Owned by `/home/alejg/nixos`; tracked here only until it has a matching
    NixOS-side task.
  - `windows-server.service`: no start-limit, restart always
  - `windows-server-virtiofsd.service`: no start-limit, restart always, stale socket cleanup
  - `windows-server-codex-app-server-tunnel.service`: no start-limit

## Email Attachment Automation

- Resolve no-secret Graph viability with existing Entra apps.
  - Candidate ranking and evidence live in [Entra app inspection](entra-app-inspection.md).
  - Current evidence: `ActiveDirectoryClient` mints non-mail delegated tokens
    only; `ams-prd-n8n-mail` needs admin approval; `ams-prd-rpamail` requires
    a secret/assertion; no current registered app gives a working no-secret
    `Mail.Read` path.
  - Remaining decision: get approval for an admin-consented or secret-bearing
    Graph path, or close Graph and keep Outlook/OWA fallback as system truth.

## Windows Provisioning

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

## Nice To Have

- Add structured JSON logging for PowerShell launchers.
- Finish run-id propagation for launcher/log paths outside `windows-run-ps`
  and sync wrappers.
