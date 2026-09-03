# OneDrive Files-On-Demand Implementation Handoff

Last-Validated-Checkpoint:
  Checkpoint-ID: 20260806T1545-placeholder-elevation-auth-boundary
  Evidence-Ref: .work/onedrive-files-on-demand-evidence.md#current
  Current-Relation: current; bounded diagnostic implementation with isolated runtime proof and explicit release-reclaim failure
  Current-Scope: Core contracts, Agent lease/recovery service, STA lifecycle, Host proxy, REST/OpenAPI, tests, policy, evidence
  Current-Fingerprint: working-tree; no commit created
  Current-Validation: Windows Agent/Core/Host targeted suites pass from explicit source root `C:\src\windows-operator`; isolated Agent `43129` health/config/status pass; desktop-authenticated acquire/read-to-EOF succeeds; release returns HTTP 202 with persisted `state=releasing` and `unpin_requested`, then reaches `recoveryRequired` because the VM retains 21909 bytes; `CfGetPlaceholderInfo` confirms `OnDiskDataSize=21909`, `ValidatedDataSize=21909`, `ModifiedDataSize=0`, `PinState=2`, and `InSyncState=1`; no managed Host/Agent touched
  Current-Review: Sage review says stable NO; use identity-bound `CfSetPinState(UNPINNED)` as the async provider request, poll outside the service gate, and expose pending release state; current root lacks `AUTO_DEHYDRATION_ALLOWED`, so direct dehydration is prohibited and zero-allocation proof remains open. Latest OneDrive auth is not healthy (`0x8004E4CF`, broker account without cached access token). OneDrive and Explorer both run elevated under the unfiltered built-in Administrator; Microsoft documents this as unsupported and explains missing OneDrive Explorer actions. OneDrive Startup Task is already least-privilege, and no valid non-admin interactive account exists for an A/B test. Active reversible policy is `FilesOnDemandEnabled=1` plus `DehydrateSyncedTeamSites=1`, without zero-allocation result.
  Target-Maturity: repository-candidate
  Achieved-Maturity: repository-candidate
  Completion-Matrix-Ref: .work/onedrive-files-on-demand-evidence.md#current
  Current-Residuals: Current interactive account sign-in is unstable; elevated OneDrive/Explorer runtime is unsupported; supported async unpin and SharePoint dehydration policy retain bytes; release status is pending/terminal through GET with HTTP 202/OpenAPI polling metadata; no full competing-lease/pin/restart/reclaim matrix; cen_vuelos is intended external consumer but has no real integration proof; raw response/checksum artifacts absent
  In-Flight: none
  Dirty-State: inherited repository changes plus campaign-owned OneDrive implementation/spec/evidence; no commit or deployment
  Next-Safe-Slice: complete stable pending-release HTTP contract and root capability inspection, then rerun positive lifecycle/restart/race matrix; retain diagnostic policy until zero-allocation proof passes
  Active-Gates: STABLE-POSITIVE-MATRIX; CONCRETE-CONSUMER-PROOF; LIFECYCLE-TEST-MATRIX

Project: windows-operator
Roadmap: docs/onedrive-files-on-demand-spec.md
Last validated checkpoint: 20260806T0229-postread-fix-still-zero-allocation
Evidence index: .work/onedrive-files-on-demand-evidence.md
Highest-value safe slice: restore provider-side hydration, then prove positive lifecycle behavior on the isolated Agent and Host runtimes.
Required acceptance evidence: hydrate, consume to EOF, release, online-only attributes, zero allocated bytes, competing leases, pin/dirty/identity safety, restart/idempotency, reclaim, concrete consumer, and Host proof.
Proof dependencies: logged-in Windows OneDrive session with the selected SharePoint placeholder available for hydration; isolated .NET preflight root is available for source test reruns; durable raw run artifacts for promotion-grade evidence.
Operator gates: approve any future managed Host `43117` deployment or restart separately; no cloud deletion or quota mutation in scope.
Authorized effects: local implementation, tests, isolated runtime launches, and diagnostic REST calls only.
