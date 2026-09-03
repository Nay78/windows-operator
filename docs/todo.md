# Todo

Backlog. Dated live baselines live in [development notes](development.md#live-smoke).

This file is the intake boundary for unplanned work, including while an active
`.work` campaign owns its execution queue. Capture opportunities here without
editing the active roadmap. During queue review, promote accepted items into
`.work` and retain their disposition here.

## Opportunity Inbox

- [ ] Harden Power Automate reauthentication and connector validation.
  - Status: inbox; captured 2026-07-26.
  - Outcome: recover expired sessions with one command and report actionable
    authentication state before flow operations.
  - Scope: automatic Edge lease renewal; legacy `service.flow.microsoft.com`
    token capture; token class, extension permission, session age, and lease
    expiry diagnostics; safe authenticated-profile reuse; managed credential
    storage; targeted connector reauthentication; API validation; controlled
    attachment smoke test and cleanup.
  - Promotion: move into `.work` after the active campaign releases queue
    ownership or explicit reprioritization occurs.

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

- Re-run VM bootstrap wrapper `bootstrap-vm.ps1` after host recovery.
  - Wraps base `bootstrap.ps1` and Codex `bootstrap-codex.ps1`
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

## OneDrive Runtime Hardening

- [ ] Make Host recovery cancellation and child-process cleanup terminal.
  - Status: inbox; captured 2026-08-10 after same-session console recovery.
  - Outcome: cancellation records non-stale supervisor state and terminates
    timed-out `tscon`, task-control, Agent, and OneDrive child starts.
- [ ] Strengthen wrong-session process identity before termination.
  - Status: inbox; captured 2026-08-10.
  - Outcome: verify Agent command path and OneDrive process owner/token in
    addition to listener port, process name, executable path, and session.
- [ ] Enforce or remove explicit positive OneDrive recovery session IDs.
  - Status: inbox; captured 2026-08-10.
  - Outcome: align `targetSessionId` contract with dynamic resolver behavior
    and add multi-session tests.

## Nice To Have

- Add structured JSON logging for PowerShell launchers.
- Finish run-id propagation for launcher/log paths outside `windows-run-ps`
  and sync wrappers.
