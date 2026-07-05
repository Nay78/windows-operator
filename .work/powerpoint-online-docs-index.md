# PowerPoint Online Harness Docs Index

Date: 2026-07-05

## Current Sources

Read these first:

1. `docs/powerpoint-automation-architecture.md` - current architecture, runtime boundary, public contracts, validation rules.
2. `.work/powerpoint-online-editing-harness-completion-audit.md` - completion status and final live proof evidence.
3. `.work/powerpoint-online-mutation-proof-runbook.md` - repeatable mutating proof command and success criteria.
4. `.work/powerpoint-online-editing-harness-roadmap.md` - full design history and public-surface roadmap.
5. `.codex/skills/office-js-powerpoint-debug/references/office-js-powerpoint.md` - live Office.js and PowerPoint Online field notes.

Do not use older handoff/validation notes below as current status unless a
current source links to a specific section. They are retained as evidence and
root-cause trail only.

## Module Docs

- `src/WindowsOperator.PowerPointAddIn/README.md` - add-in package entry point.
- `src/WindowsOperator.PowerPointAddIn/docs/architecture.md` - add-in queue/Office.js boundary.
- `src/WindowsOperator.PowerPointAddIn/docs/spec.md` - lower-level job contract.
- `src/WindowsOperator.PowerPointAddIn/docs/vm-connection.md` - local hosting/runtime notes.

## Final Live Proof: Text/Image Targets

- Run id: `ppt-mutation-proof-sem27-long-20260705t010320z`
- Summary: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`
- Initial edit screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/screenshots/powerpoint-online-update.png`
- Reopened persistence screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-update.png`
- Post-cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z-verification/screenshots/powerpoint-online-template-cleanup.png`

Final proof result: HTTP `200`, `success=true`, `status=succeeded`,
`saveProofTier=tier3ReopenVisual`, `claimedBy=officejs-taskpane`,
`titleMainTargetSucceeded=true`, three successful/verified/distinct image
artifacts, cleanup complete, final Edge/Chrome-like window count `0`.

## Final Live Proof: Table Editing

- Run id: `ppt-table-onecall-sem27-20260705t0453z`
- Response: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/response.json`
- Initial table edit screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z/screenshots/powerpoint-online-update.png`
- Reopened table persistence screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-update.png`
- Post-cleanup screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-table-onecall-sem27-20260705t0453z-verification/screenshots/powerpoint-online-template-cleanup.png`

Table proof result: HTTP `200`, `success=true`, `status=succeeded`,
`saveProofTier=tier3ReopenVisual`, `jobRecord.status=succeeded`. The job ran
`readTable`, `replaceTableCell`, and `replaceTableRange` on `DATA_TABLE`;
screenshots before and after reopen showed `67 kt`, `101%`, and `103%`.
Cleanup complete, final Edge/Chrome-like window count `0`.

## Historical Evidence Notes

These files are useful for audit trail and root-cause context. They are not the
current status source when they contain older historical-limit language.

Historical handoffs superseded by current architecture/audit:

- `.work/powerpoint-online-session-harness-handoff.md`
- `.work/powerpoint-online-update-orchestration-handoff.md`

Historical validation/evidence notes superseded by final live proofs:

- `.work/powerpoint-online-session-harness-validation.md`
- `.work/powerpoint-online-session-observation-validation.md`
- `.work/powerpoint-online-addin-activation-gap.md`
- `.work/powerpoint-online-addin-preflight-validation.md`
- `.work/powerpoint-online-update-orchestration-validation.md`
- `.work/powerpoint-online-onecall-discovery-live.md`
- `.work/powerpoint-online-reopen-discovery-live.md`
- `.work/powerpoint-online-slide-navigation-hardening.md`

Historical approval packet:

- `.work/powerpoint-online-final-mutation-approval-packet.md`

## Residual Limits

- Arbitrary existing object targeting is not done; proven mutation uses
  add-in-created tagged text, image, and table targets.
- Tier-4 SharePoint/Graph version proof is not done; current strongest proof is
  PowerPoint Online saved state plus reopen visual evidence.
- Repeat mutation against production decks needs explicit intent because visible
  cleanup does not erase SharePoint version history.
