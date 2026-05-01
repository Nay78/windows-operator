# Email Attachment Automation

Goal: download files from email without Microsoft Graph access.

## Ranking

1. Classic Outlook COM + PowerShell.
2. IMAP with OAuth if tenant/client setup allows it.
3. Outlook Web UI automation with Edge/Playwright.
4. Power Automate Desktop.
5. EWS only for legacy environments; avoid for new Exchange Online work.

## Recommended Path

Use Classic Outlook in the Windows VM and automate it from the logged-in desktop session.

Why:

- Uses the user's already-authorized Outlook profile.
- Avoids Graph and tenant app registration.
- Fully code-editable PowerShell.
- Debug output can be written to `Z:\operator-exchange`.
- Fits current interactive scheduled task model.

## Constraints

- Classic Outlook required. New Outlook does not support COM/VBA automation.
- User must be logged into Windows desktop.
- Outlook profile must be configured and able to access mailbox.
- Automation should not run as a Windows service.
- Automation must run through the desktop Agent, not SSH or Host process COM.
- Do not read sender email/account SMTP fields in v1; Outlook Object Model Guard prompts on those fields.

## AI Runtime Surface

REST surface on host loopback:

```text
GET  http://127.0.0.1:43117/v1/mail/folders
POST http://127.0.0.1:43117/v1/mail/folders
POST http://127.0.0.1:43117/v1/mail/messages/search
POST http://127.0.0.1:43117/v1/mail/attachments/download
GET  http://127.0.0.1:43117/v1/mail/runs/<run-id>
GET  http://127.0.0.1:43117/v1/mail/status
POST http://127.0.0.1:43117/v1/mail/sync
POST http://127.0.0.1:43117/v1/mail/recover
GET  http://127.0.0.1:43117/openapi.json
```

Local MCP HTTP endpoint:

```text
POST http://127.0.0.1:43117/mcp
```

Mail tools:

- `mail_list_folders`
- `mail_search_messages`
- `mail_download_attachments`
- `mail_get_run`
- `mail_status`
- `mail_sync`
- `mail_recover`

Folder list, search, and download support `syncBeforeRead` plus `syncWaitSeconds` when stale folder names or server-side message moves are possible. Search and download also support folder path, subject substring, received-time bounds, attachment presence, max result/message limits, selected message IDs, selected attachment indexes, explicit run IDs, and dry runs.

Use `mail_sync` for an explicit standalone refresh. Use `syncBeforeRead` when the read must refresh Outlook first. Both paths start Classic Outlook send/receive and sync objects, then wait up to `75` seconds for the local Outlook cache to catch up.

## Proposed Output

```text
Z:\operator-exchange\downloads\mail\<account>\<yyyy-mm-dd>\...
Z:\operator-exchange\runs\mail-download-<timestamp>\result.json
```

`result.json` should include:

- account
- folder
- filters
- message subject
- sender
- received time
- Outlook EntryID
- saved attachment paths
- skipped attachment reasons
- errors

## State

Persist processed state under local Windows state:

```text
%LOCALAPPDATA%\WindowsOperator\run\mail-download-state.json
```

State keys:

- message EntryID
- attachment file name
- attachment size if available
- saved timestamp

Do not store credentials in state.

## First Proof Of Concept

PowerShell script shape:

```powershell
param(
    [string]$Folder = "Inbox",
    [string]$SubjectContains,
    [string]$SenderContains,
    [string]$OutputRoot = "Z:\operator-exchange\downloads\mail",
    [switch]$UnreadOnly
)

$outlook = New-Object -ComObject Outlook.Application
$namespace = $outlook.GetNamespace("MAPI")
$inbox = $namespace.GetDefaultFolder(6)
```

Then filter messages, call `Attachment.SaveAsFile(...)`, write JSON manifest, and mark/move only after successful save.

Implemented v1 keeps mailbox read-only: no mark-read, move, delete, or body reads.

## Operational Safety

Classic Outlook COM shares the user's Outlook profile and OST. Treat it as an exclusive resource:

- Do not keep interactive Outlook open while mail automation runs.
- Mail automation must serialize access with a local mutex.
- REST mail calls run Outlook COM in a short-lived worker process. If COM or RPC wedges, the Agent kills the worker process tree and leaves the next request with fresh COM state.
- Startup must reject visible interactive Outlook and clear stale headless `OUTLOOK.EXE` instances before creating COM.
- Shutdown must release COM, request Outlook quit, wait briefly, then kill leftover headless Outlook processes.
- Worker startup must have a watchdog timeout. If Outlook shows hidden recovery UI or hangs, kill only worker-owned/headless Outlook and return a bounded mail error.
- When Outlook is idle after cleanup, remove stale `~*.tmp` files from `%LOCALAPPDATA%\Microsoft\Outlook`.
- If Outlook must stay open for humans while automation runs, move automation to a separate Windows user/profile or replace COM with Graph/EWS.

## Recovery

Use mail recovery when Outlook has stale folder names, stuck reminders, Autodiscover prompts, or COM errors such as `0x800706BE`.

REST:

```bash
curl -X POST http://127.0.0.1:43117/v1/mail/sync \
  -H 'Content-Type: application/json' \
  -d '{"waitSeconds":30}'

curl -X POST http://127.0.0.1:43117/v1/mail/folders \
  -H 'Content-Type: application/json' \
  -d '{"syncBeforeRead":true,"syncWaitSeconds":30}'

curl -X POST http://127.0.0.1:43117/v1/mail/recover \
  -H 'Content-Type: application/json' \
  -d '{"mode":"profile"}'
```

SSH fallback:

```bash
scripts/linux/windows-run-ps.sh scripts/windows/recover-outlook-mail.ps1 -Mode Profile
```

Modes:

- `basic`: kill headless Outlook only and clear stale temp files.
- `profile`: run `outlook.exe /cleanreminders` and `/resetnavpane`, then close Outlook cleanly. It refuses to touch already visible Outlook.
- `force`: kill visible Outlook too. Use only when the automation desktop is dedicated and Outlook is stuck.

## Debugging

- Write a transcript per run.
- Write JSON result per run.
- Keep raw stderr/stdout.
- Keep a sample message manifest for test fixtures.
- Use Windows Event Log only for Outlook/profile-level failures.
- If Event Log shows `Microsoft Outlook: Rejected Safe Mode action`, open Classic Outlook interactively in RDP once, resolve the safe-mode/profile prompt, close Outlook cleanly, then retry automation.
