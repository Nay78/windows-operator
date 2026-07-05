# PowerPoint Online Reopen Discovery Live Proof

Date: 2026-07-04

## Purpose

Prove the high-level PowerPoint Online update route can reach tier-3 reopen visual evidence without mutating the SharePoint deck.

This is not the final edit proof. It verifies the open -> Office.js job -> saved indicator -> evidence -> cleanup -> reopen -> evidence -> final cleanup machinery while using `validateOnly=true` and zero operations.

## Request

Endpoint:

```text
POST http://127.0.0.1:43117/v1/powerpoint/online/updates
```

Key fields:

- `deckUrl`: SEM27 SharePoint URL
- `sessionId`: `ppt-onecall-reopen-discovery-20260704t0928z`
- `job.jobId`: `ppt-onecall-reopen-discovery-20260704t0928z`
- `job.discoverTargets`: `true`
- `job.validateOnly`: `true`
- `job.operations`: `[]`
- `allowDeckMutation`: `false`
- `verifyReopen`: `true`
- `evidenceSlideNumber`: `4`
- `capture`: `true`
- `cleanupSession`: `true`

## Result

- `success=true`
- `status=succeeded`
- `saveProofTier=tier3ReopenVisual`
- `jobRecord.status=succeeded`
- `jobRecord.claimedBy=officejs-taskpane`
- `discoveredTargets=[]`
- first session: `currentSlide=4`, `status=ready`
- verification session: `currentSlide=4`, `status=ready`
- `sessionCleanupSession.status=closed`
- evidence count: `2`
- final `/v1/windows` Edge/Chrome widget count: `0`

## Evidence

- First run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z`
- Reopen run root: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification`
- First screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z/screenshots/powerpoint-online-update.png`
- Reopened screenshot: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-onecall-reopen-discovery-20260704t0928z-verification/screenshots/powerpoint-online-update.png`

Both screenshots were visually inspected. They show SEM27 slide 4 selected after the initial run and again after reopen.

## Implementation Cleanup

The live result exposed duplicated verification start actions in the aggregated `actions` list. `PowerPointOnlineUpdateService.VerifyReopenAsync` now lets the successful evidence merge carry reopen start actions once, while blocked reopen still reports start actions before `verification_reopen_failed`.

Proof:

- Local Host tests: `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj` passed 83/83.
- Windows Host tests: `/var/lib/windows-server/shared/operator-exchange/runs/run-20260704T093129Z-642474/result.json` passed 83/83.

## Historical Scope Limit

Historical scope limit: this proved tier-3 reopen visual plumbing for a non-mutating Office.js job only. The later SEM27 proof covers visible edit persistence and cleanup for add-in-created targets: `/var/lib/windows-server/shared/operator-exchange/runs/ppt-mutation-proof-sem27-long-20260705t010320z/summary.json`.
