# Mail To OneDrive Automation

Goal: copy attachments from already-categorized or already-routed Outlook mail
into OneDrive without requiring a new Entra app registration.

This feature covers the missing `mail -> OneDrive` step. It assumes mailbox
classification already exists through Outlook or Exchange rules.

Placeholder hydration, lease lifecycle, and local disk reclamation are governed
by [`onedrive-files-on-demand-spec.md`](onedrive-files-on-demand-spec.md). This
document remains limited to mail attachment upload into a local sync folder.

## Current State

Windows Operator now exposes a narrow Power Automate MCP bridge and API-backed
flow read/update/create surface. Creation uses the update request with
`create:true`. It does not yet expose first-class semantic
cloud-flow models.

Existing support:

- Outlook COM mail status, folder list, message search, and attachment download.
- Host REST and MCP routes under `/v1/mail/...`.
- Generic Edge/browser automation for portal work.
- Microsoft auth browser handoff for user-delegated sign-in flows.
- Power Automate Desktop is installed on the Windows VM.
- Power Automate MCP flow reads and writes use captured
  browser tokens and Power Automate backend APIs, not designer UI automation.

Missing support:

- No generated semantic Power Automate cloud-flow builder/update model.
- No installed `pac` CLI or Power Platform PowerShell modules on the checked VM.

## Power Automate Write Policy

Cloud-flow writes must use the browser-token/API/MCP path. The harness must
fail closed when token capture, MCP context, or backend API calls are
unavailable.

Power Automate designer UI automation is allowed only as explicit break-glass
work. It can verify visible state, run Flow checker, or capture screenshots, but
it must not silently create or mutate flows as a fallback for a failed API path.

## Recommended V1

Use existing Outlook COM automation and write matching attachments directly into
the local OneDrive sync folder.

Why:

- Uses the logged-in Windows account and configured Outlook profile.
- Avoids Entra app registration.
- Avoids Power Automate portal UI automation.
- Keeps feature code-editable and testable inside this repo.
- Reuses existing mail worker recovery, run IDs, result manifests, and exchange
  artifacts.
- OneDrive sync provides cloud upload after the local file write succeeds.

Known local OneDrive root from live inspection:

```text
C:\Users\Administrator\OneDrive - Grupo Minero Antofagasta Minerals
```

OneDrive sync health still needs explicit live verification. A local write only
proves the file reached the sync folder, not that upload completed.

## User Workflow

Agent-facing examples:

```text
scripts/linux/wo mail onedrive rules list
scripts/linux/wo mail onedrive rules add --folder "mailbox/Invoices/ACME" --attachment "*.pdf" --dest "Automation/Mail/ACME"
scripts/linux/wo mail onedrive run --rule acme-invoices
scripts/linux/wo mail onedrive run --all
scripts/linux/wo mail onedrive status --run-id <run-id>
```

The user or tenant rules place messages into folders or categories. Windows
Operator only reads that classification and copies attachments.

## Rule Model

Store rules in local Windows Operator state, not shared source:

```text
%LOCALAPPDATA%\WindowsOperator\mail-onedrive-rules.json
```

Example:

```json
{
  "version": 1,
  "rules": [
    {
      "id": "acme-invoices",
      "enabled": true,
      "folder": "mailbox/Invoices/ACME",
      "category": null,
      "subjectContains": null,
      "attachmentGlob": "*.pdf",
      "destination": "Automation/Mail/ACME",
      "dedupe": "messageEntryIdAttachmentNameSize"
    }
  ]
}
```

Rules should be boring and hard to misuse:

- `folder`: Outlook folder path. Preferred when existing rules already route
  mail.
- `category`: optional Outlook category filter for users who classify in-place.
- `subjectContains`: optional extra guard, not primary routing.
- `attachmentGlob`: file-name filter.
- `destination`: path relative to approved OneDrive root.
- `dedupe`: deterministic processed-state key.

Do not allow absolute destination paths from callers.

## Runtime Behavior

```text
caller
  -> scripts/linux/wo mail onedrive ...
  -> Host REST 127.0.0.1:43117
  -> Desktop Agent REST 127.0.0.1:43119
  -> Outlook mail worker
  -> Classic Outlook COM
  -> local OneDrive sync folder
```

Run steps:

1. Resolve and validate rule.
2. Resolve approved OneDrive root.
3. Search matching Outlook folder/category.
4. Skip previously processed attachments.
5. Save attachments to a temporary file in the destination directory.
6. Atomically move into final file name.
7. Record processed state.
8. Write `result.json` under exchange run artifacts.
9. Optionally verify OneDrive sync status when supported.

## REST Surface

Target namespace stays under the user-facing mail domain:

```text
GET    /v1/mail/onedrive/rules
POST   /v1/mail/onedrive/rules
DELETE /v1/mail/onedrive/rules/{ruleId}
POST   /v1/mail/onedrive/runs
GET    /v1/mail/onedrive/runs/{runId}
GET    /v1/mail/onedrive/status
```

MCP tools:

```text
mail_onedrive_list_rules
mail_onedrive_add_rule
mail_onedrive_delete_rule
mail_onedrive_run
mail_onedrive_get_run
mail_onedrive_status
```

## Result Shape

```json
{
  "success": true,
  "runId": "mail-onedrive-20260707T000000Z",
  "ruleId": "acme-invoices",
  "messagesMatched": 3,
  "attachmentsSaved": 3,
  "attachmentsSkipped": 1,
  "oneDriveRoot": "C:\\Users\\Administrator\\OneDrive - Grupo Minero Antofagasta Minerals",
  "savedFiles": [
    {
      "path": "C:\\Users\\Administrator\\OneDrive - Grupo Minero Antofagasta Minerals\\Automation\\Mail\\ACME\\invoice-123.pdf",
      "relativePath": "Automation/Mail/ACME/invoice-123.pdf",
      "messageEntryId": "...",
      "attachmentName": "invoice-123.pdf"
    }
  ],
  "warnings": [],
  "errors": []
}
```

## State

Processed-state file:

```text
%LOCALAPPDATA%\WindowsOperator\run\mail-onedrive-state.json
```

State keys:

- rule ID
- Outlook EntryID
- attachment file name
- attachment size when available
- saved file relative path
- saved timestamp

Do not store credentials, cookies, tokens, or private URLs.

## Safety

- Require explicit folder, category, or subject guard before copying.
- Require destination under approved OneDrive root.
- Use unique names or deterministic conflict policy.
- Never overwrite existing user files unless caller explicitly requests it.
- Default to dry-run for broad rules.
- Keep generated downloads and logs out of shared source.
- Redact message body and sender SMTP fields unless a later feature explicitly
  needs them.

## Power Automate Option

Power Automate remains useful for a later cloud-native implementation:

- One long-lived cloud flow reads a `rules.json` file from OneDrive.
- The flow uses Office 365 Outlook `When a new email arrives (V3)`.
- The flow writes matching attachments through OneDrive for Business `Create file`.
- The agent edits only `rules.json`, not flow designer steps.

This option still needs a bootstrap path. Under current constraints, bootstrap
would be either manual once, generic browser automation, or new harness support
for Power Platform CLI/PowerShell plus user-delegated auth. It should not be the
first implementation path.

### Browser-Backed MCP Candidate

`kaael1/mcp-power-automate` is a good candidate for agent-created Power
Automate flows under the no-new-Entra-app constraint.

Observed from source inspection:

- It is a local MCP server plus Chromium extension.
- The README explicitly supports loading the extension from `edge://extensions`.
- The extension is Manifest V3 and uses Chromium APIs available in Microsoft
  Edge, including `chrome.sidePanel`.
- Microsoft Edge supports `chrome.sidePanel` for sidebar extensions.
- The extension captures Power Automate, Power Platform/BAP, and Dataverse
  tokens from the logged-in browser session.
- The bridge listens on Windows loopback `127.0.0.1:17373`.
- Public commands include `create_flow`, `create_flow_in_solution`,
  `validate_flow`, `apply_flow_update`, and `revert_last_update`.
- Write operations are intentionally scoped and keep local snapshots/backups.

Edge adaptation work is mostly harness integration, not an extension rewrite:

1. Start the MCP bridge in the Windows desktop user context, not Linux loopback.
2. Resolve the packaged extension path with
   `npx -y @kaael1/mcp-power-automate extension-path`.
3. Load that unpacked extension into the Edge profile used for Power Automate.
4. Open or focus a Power Automate flow page so the extension captures browser
   context and tokens.
5. Add harness health checks for `http://127.0.0.1:17373/v1/health` and
   `get_context`.
6. Add an operator-only route or script that starts the bridge, opens Edge with
   the extension loaded, and reports readiness.
7. Keep the token-capturing extension scoped to the operator-owned Edge session.

Implemented harness surface:

```text
GET  /v1/power-automate/mcp/status
POST /v1/power-automate/mcp/start
POST /v1/power-automate/mcp/edge
POST /v1/power-automate/mcp/flows/read
POST /v1/power-automate/mcp/flows/update
```

Operator CLI:

```text
scripts/linux/wo power-automate mcp status
scripts/linux/wo power-automate mcp start --dry-run
scripts/linux/wo power-automate mcp edge --dry-run
scripts/linux/wo power-automate mcp flow-read --flow-id <flow-id>
scripts/linux/wo power-automate mcp flow-update --flow-id <flow-id> --flow-json-file flow.json --dry-run
scripts/linux/wo power-automate mcp flow-update --create --display-name <name> --flow-json-file flow.json --no-dry-run
```

`/edge` loads the local token-capturing extension into the operator-owned Edge
session and keeps captured context on Windows loopback.

Live VM readiness notes:

- Microsoft Edge exists at
  `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`.
- Node exists at `C:\Program Files\nodejs\node.exe` with version `v24.1.0`.
- npm exists at `C:\Program Files\nodejs\npm.cmd` with version `11.3.0`.
- The npm registry latest observed for `@kaael1/mcp-power-automate` was `0.4.1`,
  while repository `main` declared `1.0.0`; pin a version or git SHA for
  reproducibility.

## Verification

Done means live proof, not compilation only:

1. Confirm Outlook status through `GET /v1/mail/status`.
2. Confirm OneDrive root exists and is writable.
3. Add a dry-run rule against a known folder.
4. Run against a synthetic or safe real test message.
5. Confirm file exists under the local OneDrive folder.
6. Confirm OneDrive sync uploaded the file, or report that sync proof is missing.
7. Confirm rerun skips already processed attachment.
8. Confirm result manifest includes saved/skipped counts and file paths.
