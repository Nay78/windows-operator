# Outlook Mail Automation Target Architecture

Goal: external callers request mail intent while Windows Operator owns Outlook refresh, attach/start/close behavior, retries, and recovery.

## Decision

Mail callers should not need to know whether Outlook is open, stale, syncing, blocked by COM, or needs recovery. The operator should abstract those Windows details behind mail REST and MCP operations.

Default behavior:

- Attach to existing Classic Outlook when available.
- Leave user-visible Outlook open after normal operations.
- Auto-sync when mail cache is stale.
- Retry once after a safe recovery when COM or cache behavior looks stale.
- Restart or kill Outlook only inside bounded recovery policy, not on every read.

## Runtime Topology

```text
external service / MCP caller
  -> Host REST 127.0.0.1:43117
  -> Desktop Agent REST 127.0.0.1:43119
  -> OutlookMailCoordinator
  -> OutlookComSession
  -> Classic Outlook COM in logged-in desktop session
```

Rules:

- Host never creates Outlook COM objects.
- Desktop Agent owns all Outlook COM and UI recovery.
- Mail operations serialize through one STA COM queue and one local mutex.
- External services call mail intent endpoints only. They do not call sync/recover as a prerequisite.
- Recovery scripts may remain for break-glass operations, but REST/MCP callers do not drive recovery.

## REST Surface

Keep existing intent routes:

```text
POST /v1/mail/folders
POST /v1/mail/messages/search
POST /v1/mail/attachments/download
GET  /v1/mail/runs/{runId}
GET  /v1/mail/status
```

Target behavior:

- `POST /v1/mail/folders` uses refresh/recovery policy by default and returns an envelope with actions.
- Search and download should use refresh/recovery policy by default.
- Sync and recovery are internal policy, not public caller workflow.

## Request Policy

Add caller overrides without exposing Outlook mechanics as required knowledge:

```json
{
  "freshness": "auto"
}
```

Values:

- `freshness:auto`: sync when stale or when target folder is missing.
- `freshness:cached`: skip sync for latency-sensitive reads.
- `freshness:fresh`: sync before read.

Defaults:

```json
{
  "freshness": "auto",
  "syncFreshnessSeconds": 300,
  "syncWaitSeconds": 45,
  "retryAfterRecovery": true
}
```

## Result Envelope

Move new POST responses toward envelopes so callers can see what the operator did without driving it.

Folder result:

```json
{
  "success": true,
  "folders": [
    {
      "depth": 1,
      "path": "mailbox/Alimentacion",
      "name": "Alimentacion",
      "childCount": 0
    }
  ],
  "actions": [
    "attached_existing_outlook",
    "auto_sync_started",
    "auto_sync_waited_seconds:45",
    "folders_read"
  ],
  "warnings": [],
  "errors": [],
  "lastSyncUtc": "2026-05-02T14:20:45Z",
  "recovered": false,
  "completedAtUtc": "2026-05-02T14:20:50Z"
}
```

Search/download results should include the same `actions`, `warnings`, `errors`, `lastSyncUtc`, and `recovered` fields.

## State

Persist policy state under local Windows state:

```text
%LOCALAPPDATA%\WindowsOperator\run\mail-sync-state.json
```

Shape:

```json
{
  "lastSyncAttemptUtc": "2026-05-02T14:20:00Z",
  "lastSyncSuccessUtc": "2026-05-02T14:20:45Z",
  "lastFolderReadUtc": "2026-05-02T14:20:50Z",
  "lastFolderFingerprint": "sha256...",
  "lastError": null
}
```

Use this state only for freshness decisions and diagnostics. Do not store credentials.

## Coordination Modules

Target module split:

```text
OutlookMailCoordinator
  owns refresh policy, retry policy, recovery policy, and action logging

OutlookComSession
  owns attach/start/close semantics and Outlook process ownership

OutlookMailComService
  owns folder/search/download/sync mechanics against a live COM session
```

Policy stays in the coordinator. COM details stay in session/service internals.

## Outlook Session Policy

Default ownership behavior:

```text
existing visible Outlook
  -> attach
  -> run operation
  -> leave open

no Outlook
  -> start Outlook
  -> run operation
  -> close only operator-owned Outlook

headless stale Outlook
  -> close/kill through recovery policy
  -> retry
```

Do not require humans to close Outlook before mail automation. Use visible Outlook when it is healthy.

Close/restart only when:

- COM attach fails.
- Outlook has a blocking modal or safe-mode prompt.
- Sync/read times out.
- Headless Outlook is stuck.
- Internal recovery policy escalates.
- Local break-glass recovery is invoked outside the REST/MCP mail intent flow.

## Automatic Read Flow

Folder list:

```text
request arrives
  -> enqueue on STA dispatcher
  -> acquire Outlook mail mutex
  -> attach/start Outlook session
  -> if freshness policy says stale, sync
  -> read folders
  -> update folder fingerprint and sync state
  -> return envelope
```

Search/download:

```text
request arrives
  -> enqueue and acquire mutex
  -> attach/start Outlook session
  -> if freshness policy says stale, sync
  -> resolve folder
  -> if folder missing and freshness:auto:
       force sync
       retry folder resolve once
  -> perform search/download
  -> return envelope/result
```

Recovery retry:

```text
operation fails with recoverable Outlook error
  -> record warning
  -> run soft recovery
  -> retry once
  -> if still failing and policy allows escalation:
       restart Outlook
       retry once
  -> return final success or structured mail_unavailable
```

## Recovery Levels

Recovery should be internal and bounded:

```text
none
  no recovery, return error

soft
  close operator-owned/headless Outlook only
  clear stale Outlook temp files when idle

restart
  close visible Outlook cleanly when automation desktop is dedicated
  reopen and retry

force
  kill Outlook processes only after timeout/escalation
```

Default for normal mail calls should be `soft`. Escalation to `restart` or `force` should require config that marks the Windows desktop as automation-dedicated.

## Configuration

Supported config:

```json
{
  "Mail": {
    "SyncFreshnessSeconds": 300,
    "SyncWaitSeconds": 45,
    "ForceSyncWhenFolderMissing": true,
    "AllowAttachToVisibleOutlook": true,
    "CloseOwnedOutlookOnly": true,
    "AllowAutomaticSoftRecovery": true,
    "AllowAutomaticRestart": false,
    "AllowAutomaticForceKill": false
  }
}
```

Use stricter defaults unless the VM is dedicated to automation.

## Error Semantics

Return final errors only after operator has exhausted allowed automatic policy.

Good final error:

```json
{
  "code": "mail_unavailable",
  "message": "Outlook mailbox automation is unavailable.",
  "details": {
    "detail": "Folder 'Alimentacion' was not present after auto sync and one recovery retry.",
    "actions": "attached_existing_outlook; auto_sync_started; soft_recovery; retry_failed",
    "lastSyncUtc": "2026-05-02T14:20:45Z"
  }
}
```

Bad final error:

```text
Close Outlook before using mail automation.
```

That leaks operator internals to caller.
