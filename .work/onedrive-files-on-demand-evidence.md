# OneDrive Files-On-Demand Evidence

Index:

```text
20260806T1545-placeholder-elevation-auth-boundary | placeholder-elevation-auth-boundary | #current | working-tree | current
20260806T1322-async-provider-release | async-provider-release | #async-provider-release | working-tree | historical
20260806T0229-postread-fix-still-zero-allocation | postread-fix-still-zero-allocation | #postread-fix-still-zero-allocation | working-tree | historical
20260806T0218-module-live-release-timeout | module-live-release-timeout | #module-live-release-timeout | working-tree | historical
20260806T0045-provider-recovered         | provider-recovered         | #provider-recovered | working-tree | historical
20260805T2325-provider-auth-gate         | provider-auth-gate         | #provider-auth-gate | working-tree | historical
20260805T2305-reclaim-bound-review       | reclaim-bound-review       | #reclaim-bound-review | working-tree | historical
20260805T2250-stability-hardening-review | stability-hardening-review | #reclaim-bound-review | working-tree | historical
20260805T2215-stable-hardening-negative  | stable-hardening-negative  | #hardening-negative | working-tree | historical
20260805T1718-live-diagnostic            | live-diagnostic            | #historical-live | working-tree | historical
```

## Packet: current

Checkpoint: `20260806T1545-placeholder-elevation-auth-boundary`
Milestone: `onedrive-files-on-demand`
Scope: Sage async-release correction, identity-bound provider unpin, SharePoint dehydration policy, placeholder allocation proof, and live runtime/authentication boundary investigation.
Files-Artifacts: `scripts/windows/bootstrap.ps1`; `scripts/windows/run-agent.ps1`; `scripts/windows/register-agent-autostart.ps1`; `scripts/windows/register-host-autostart.ps1`; `src/WindowsOperator.Agent/Services/OneDriveFilesOnDemandService.cs`; `.work/onedrive-files-on-demand-evidence.md`; `.work/onedrive-files-on-demand-handoff.md`.
Target-Maturity: repository-candidate
Achieved-Maturity: repository-candidate
Validation: Windows targeted Agent tests pass; isolated Agent `43129` health 200; clean desktop-authenticated acquire/read-to-EOF returned HTTP 200; release returned HTTP 202 promptly with `state=releasing` and `unpin_requested`; GET after the bounded async wait persisted `recoveryRequired` with `onedrive_dehydration_timeout`; immediate and delayed `fsutil file queryAllocRanges` checks still reported `Offset 0x0 Length 0x5595` (21909 bytes). `CfGetPlaceholderInfo` independently reports `OnDiskDataSize=21909`, `ValidatedDataSize=21909`, `ModifiedDataSize=0`, `PinState=2`, and `InSyncState=1`.
Review: Sage stable promotion NO; current sync-root policy modifier is `0x9` with auto-dehydration absent. Direct `CfDehydratePlaceholder` returned `0x8007016A`; only async provider unpin is used. WAM broker re-registration plus interactive desktop launch briefly restored `LastSignInResult=0`, then regressed to `0x8004E4CF`; latest OneDrive log says a broker account exists but no access token is cached. OneDrive PID `12164` and Explorer PIDs `3948`, `4064`, and `4208` all have elevated tokens. Explorer exposes no `Free up space` verb. Reversible `HKLM\SOFTWARE\Policies\Microsoft\OneDrive` settings `FilesOnDemandEnabled=1` and `DehydrateSyncedTeamSites=1` are active, but did not produce zero allocation. Zero-allocation, root-capability, race, lifecycle, and consumer gates remain open.
Fingerprint: working-tree; no commit created.
Recoverability: verified:source remains in working tree; isolated preflight state is machine-local under `C:\ProgramData\WindowsOperator\stability-preflight`; temporary task removed after validation; managed Host/Agent untouched.

### Live validation

- Target: Windows VM `administrator@127.0.0.1:22555`; isolated interactive Agent `http://127.0.0.1:43129`; managed `43117` and `43119` untouched.
- Exact candidate unchanged: `Contrato GS-312 Centinela - 2 - Entrega Producto Topografia\\2026\\07 Julio\\20260723\\01-Esperanza\\20260723_AVA_REE_SME-SME1_PCP\\5. Validacion\\control.png` under `C:\Users\Administrator\Geosupport S.A`.
- Acquire response: HTTP 200, `state=ready`, actions `lease_reserved`, `hydrated`, `read_to_eof`; clean run used account `nmartinez.drs@mineracentinela.cl` in the interactive desktop session; no content bytes printed.
- Release response: HTTP 202 immediately with `state=releasing`, action `unpin_requested`, and a lease `Location`; GET after 30s persisted `recoveryRequired` and `onedrive_dehydration_timeout`; immediate and delayed allocation checks remained 21909 bytes. No false success reported.
- Provider evidence: account `nmartinez.drs@mineracentinela.cl`, desktop launch `LastSignInResult=0`, SharePoint mount cache maps to `C:\Users\Administrator\Geosupport S.A\Contrato GS-312 Centinela - 2 - Entrega Producto Topografia`, registered sync root provider status `IDLE`, hydration primary `FULL=2`, modifier `0x9` (`VALIDATION_REQUIRED` + `ALLOW_FULL_RESTART_HYDRATION`), no `AUTO_DEHYDRATION_ALLOWED`.
- Placeholder evidence: `CfGetPlaceholderInfo` reports full on-disk allocation equal to validated cloud data (`21909/21909`), zero modified bytes, `PinState=2` (`UNPINNED`), and `InSyncState=1` (`IN_SYNC`). This rules out dirty local data and a filesystem allocation-measurement artifact.
- Runtime boundary evidence: Windows Server 2022; OneDrive `26.134.0713.0004`; `CldFlt.sys` `10.0.20348.587`; shell extension registered. Token inspection reports OneDrive and Explorer elevated because the built-in Administrator is not filtered (`FilterAdministratorToken` absent, `EnableLUA=1`). Read-only Explorer verbs omit `Free up space` and `Always keep on this device`.
- Account/task evidence: local users are `Administrator`, `CodexSandboxOffline`, and `CodexSandboxOnline`; only `Administrator` has the active desktop session. OneDrive Startup Task for the built-in Administrator is already `RunLevel=LeastPrivilege`; its process is nevertheless elevated because that account receives a full token when `FilterAdministratorToken` is absent. No valid non-admin interactive account is available for an auth-preserving A/B test.
- Authentication evidence: `dsregcmd /status` reports `AzureAdJoined=NO`, `WorkplaceJoined=YES`, `AzureAdPrt=NO`, and `WamDefaultSet=ERROR`; latest OneDrive log records `Found the broker account` followed by `No access token found in the cache`.
- Storage Sense evidence: enabled for the user with a 30-day cloud threshold, but the last recorded trigger/failure is `2025-12-22`; no machine Storage Sense policy is present. It was not started because it is a broad cleanup operation, not a targeted OneDrive proof.
- Policy evidence: prior `HKLM\SOFTWARE\Policies\Microsoft\OneDrive` key was absent (export reported key-not-found); active policy readback `FilesOnDemandEnabled=1`, `DehydrateSyncedTeamSites=1`; policy restart did not dehydrate the existing test placeholder.
- Runtime prep: isolated SDK `8.0.423`, shared Core/ASP.NET/Desktop `8.0.29`; probes now require 8.x; Host probe intentionally requires only Core/ASP.NET because Host targets `net8.0`.
- Final source validation: Windows Agent, Core, and Host test runs all succeeded with exit code 0 using explicit source root `C:\src\windows-operator`; Linux Agent build, OpenAPI contract, operation-policy, and `git diff --check` also passed.

### Completion matrix

| Requirement | Evidence | Implemented | Locally-Validated | Externally-Verified | Remaining-Dependency |
| --- | --- | --- | --- | --- | --- |
| hydrate selected file | isolated Agent acquire/read HTTP 200 | yes | pass | pass:isolated Agent | repeat matrix |
| release and verify zero local allocation | pending HTTP 200 -> terminal recovery; 21909 allocated bytes remain | yes | partial | fail:safety gate | provider async dehydration behavior |
| post-dehydrate identity safety | metadata-only post-check; no content-opening read | yes | partial:compile/tests only | partial:live release | replacement/pin race matrix |
| runtime readiness | 8.x SDK/shared runtime checks and parser validation | yes | pass | pass:isolated runtime | managed bootstrap/deploy decision |
| concrete consumer and lifecycle matrix | no production caller; broad live matrix absent | no | blocked | blocked | consumer and matrix |

Gates-At-Checkpoint: `LIVE-ONEDRIVE-PROVIDER` hydrate pass; `MODULE-LIVE-RUNTIME` pass in isolated preflight; `STABLE-POSITIVE-MATRIX` blocked by zero-allocation release; `CONCRETE-CONSUMER-PROOF` blocked; `LIFECYCLE-TEST-MATRIX` incomplete; `HOST-LIVE-PROXY` not rerun in this slice.
Current-Relation: current

## Historical packet: `20260806T0218-module-live-release-timeout`

Checkpoint: `20260806T0218-module-live-release-timeout`
Milestone: `onedrive-files-on-demand`
Scope: runtime preflight hardening, isolated Windows source tests, and live module acquire/release proof.
Files-Artifacts: `scripts/windows/bootstrap.ps1`; `scripts/windows/run-agent.ps1`; `scripts/windows/register-agent-autostart.ps1`; `scripts/windows/register-host-autostart.ps1`; `src/WindowsOperator.Agent/Services/OneDriveFilesOnDemandService.cs`; `.work/onedrive-files-on-demand-evidence.md`; `.work/onedrive-files-on-demand-handoff.md`.
Target-Maturity: repository-candidate
Achieved-Maturity: repository-candidate
Validation: isolated .NET 8.0.423 SDK with Core/ASP.NET/Desktop 8.0.29; Windows Agent 155/155, Core 62/62, Host 124/124; four Windows PowerShell parser checks pass; isolated Agent `43129` health 200, configuration/status 200; live acquire returned `ready`, 21909 logical bytes, `read_to_eof`; live release returned HTTP 504 after the bounded timeout and persisted `recoveryRequired`; final attributes were `A O U`, but `fsutil file queryAllocRanges` still reported one range of length 21909.
Review: stable promotion NO. Runtime readiness is prepared; zero-allocation release gate fails safely. Routes remain diagnostic.
Fingerprint: working-tree; no commit created.
Recoverability: verified:source remains in working tree; isolated preflight state is machine-local under `C:\ProgramData\WindowsOperator\stability-preflight`; managed Host/Agent were not restarted or replaced.

### Live validation

- Target: Windows VM `administrator@127.0.0.1:22555`; isolated interactive Agent `http://127.0.0.1:43129`; managed `43117` and `43119` untouched.
- Exact candidate: `Contrato GS-312 Centinela - 2 - Entrega Producto Topografia\\2026\\07 Julio\\20260723\\01-Esperanza\\20260723_AVA_REE_SME-SME1_PCP\\5. Validacion\\control.png` under `C:\Users\Administrator\Geosupport S.A`.
- Acquire response: HTTP 200, `state=ready`, `logicalLength=21909`, `allocatedBytesBeforeHydration=0`, `allocatedBytesAfterHydration=21909`, actions `lease_reserved`, `hydrated`, `read_to_eof`; no content bytes printed.
- Release response: HTTP 504; persisted `state=recoveryRequired`, `onedrive_dehydration_timeout`; attributes changed to `A O U`, but allocated range remained `Offset 0x0 Length 0x5595` (21909 bytes).
- Direct `attrib +u -p` after the timeout also produced `A O U`; it did not prove zero allocation. This distinction blocks stable release claims.
- Runtime preflight root was installed only at `C:\ProgramData\WindowsOperator\stability-preflight`; no managed service restart, deployment, cloud deletion, or quota mutation.

### Completion matrix

| Requirement | Evidence | Implemented | Locally-Validated | Externally-Verified | Remaining-Dependency |
| --- | --- | --- | --- | --- | --- |
| hydrate selected file | isolated Agent acquire/read-to-EOF HTTP 200 | yes | pass | pass:isolated Agent | repeat matrix |
| release and verify zero local allocation | HTTP 504; `A O U` but 21909 allocated bytes | yes | partial | fail:safety gate | provider allocation behavior |
| runtime readiness | SDK/runtimes installed in preflight root; script parse checks pass | yes | pass | pass:isolated runtime | managed bootstrap/deploy decision |
| competing leases, pin, identity, restart, reclaim | source safety paths and bounded tests only | yes | partial | blocked | full matrix |
| concrete consumer | no production caller of `UseHydratedFileAsync` | no | blocked | blocked | consumer implementation |

Gates-At-Checkpoint: `LIVE-ONEDRIVE-PROVIDER` hydrate pass; `MODULE-LIVE-RUNTIME` pass in isolated preflight; `STABLE-POSITIVE-MATRIX` blocked by zero-allocation release; `CONCRETE-CONSUMER-PROOF` blocked; `LIFECYCLE-TEST-MATRIX` incomplete; `HOST-LIVE-PROXY` not rerun in this slice.
Current-Relation: historical
Replacement: `20260806T0229-postread-fix-still-zero-allocation`

## Historical packet: `20260806T0045-provider-recovered`

Checkpoint: `20260806T0045-provider-recovered`
Milestone: `onedrive-files-on-demand`
Scope: Sage follow-up fixes plus live OneDrive provider reset, account reconnection, and provider hydration/release proof.
Files-Artifacts: `scripts/enrich-operation-policy.py`; `openapi/windows-operator.operation-policy.json`; `src/WindowsOperator.Agent/Services/OneDriveFilesOnDemandService.cs`; `tests/WindowsOperator.Agent.Tests/OneDriveFilesOnDemandServiceTests.cs`; `.work/onedrive-files-on-demand-evidence.md`; `.work/onedrive-files-on-demand-handoff.md`.
Target-Maturity: repository-candidate
Achieved-Maturity: repository-candidate
Validation: same source checks as prior checkpoint; direct provider read-to-EOF 21909 bytes and immediate `attrib +u -p` returned `A O U`; no fresh Windows module test run because VM lacks Microsoft.AspNetCore.App/.NET SDK.
Review: Sage final review: stable promotion NO; critical 0, major 4. Provider gate now passes direct proof; module live runtime, concrete consumer, and lifecycle matrix remain blocked.
Fingerprint: working-tree; no commit created.
Recoverability: verified:source remains in working tree; isolated Windows archive/run state retained under `C:\ProgramData\WindowsOperator\sync` and `C:\ProgramData\WindowsOperator\onedrive-stable-*`.

### Live validation carried forward

- Target: Windows VM `administrator@127.0.0.1:22555`; transient Agent `http://127.0.0.1:43129`; isolated Host `http://127.0.0.1:43128`.
- Exact candidate relative path: `Contrato GS-312 Centinela - 2 - Entrega Producto Topografia\\2026\\07 Julio\\20260723\\01-Esperanza\\20260723_AVA_REE_SME-SME1_PCP\\5. Validacion\\control.png` under root `C:\Users\Administrator\Geosupport S.A`.
- Agent `GET http://127.0.0.1:43129/v1/files/onedrive/status`: HTTP 200 initially `available=true`; after the known placeholder request failed, HTTP 200 `available=false`, `recoveryRequiredLeaseCount=1`, provider-unavailable warning.
- Agent `POST http://127.0.0.1:43129/v1/files/onedrive/leases`: HTTP 423, code `onedrive_unavailable`, detail `OneDrive cloud provider denied or delayed hydration.` No absolute local path leaked.
- Host `GET http://127.0.0.1:43128/v1/health`: HTTP 200. Host status proxy: HTTP 200. Host lease proxy: HTTP 423 `onedrive_unavailable` with redacted detail.
- Isolated Agent and Host stopped cleanly; no `StaComDispatcher` shutdown exception.
- Managed Host `43117` was not restarted or replaced. No cloud deletion, quota mutation, non-dry-run reclaim, or production deployment.
- Durable raw response/log artifact: not recorded. Source checksum: not recorded; working-tree fingerprint only. These are evidence gaps for promotion.
- Historical pre-reset diagnosis: `OneDrive.exe` exited and logs contained `DRX_E_AUTH_NO_VALID_CREDENTIALS` / `auth_no_valid_credentials`.
- Historical pre-reconnection retry: direct one-byte read failed with `MethodInvocationException`; no content was printed. New Agent route `43129` could not be relaunched because VM lacks `Microsoft.AspNetCore.App`; managed `43117`/`43119` return 404 for the new route.
- Repair attempt: backed up `C:\Users\Administrator\AppData\Local\Microsoft\OneDrive\settings\Business1` to `C:\ProgramData\WindowsOperator\onedrive-reset-backup-20260805-2335`, ran supported `/reset`, relaunched OneDrive, and selected the Windows-connected `@mineracentinela.cl` account through Desktop Agent UI.
- Provider recovery proof after the user-confirmed signed-in state: direct read of the exact placeholder succeeded to EOF with `readToEofBytes=21909`; immediate `attrib +u -p` returned attributes to `A O U`. No content bytes were printed. This proves provider hydration/release, not the new module lifecycle.

### Completion matrix

| Requirement | Evidence | Implemented | Locally-Validated | Externally-Verified | Operator-Decision | Remaining-Dependency |
| --- | --- | --- | --- | --- | --- | --- |
| hydrate selected file | direct provider read-to-EOF 21909 bytes after account reconnection | yes | pass | pass:provider | pending:module live runtime | new Agent route |
| consume materialized bytes | identity-bound callback exists; no ready lease | yes | pass | blocked | pending:positive live matrix | successful hydration |
| release and verify local reclaim | direct `attrib +u -p` returned `A O U`; allocation not measured | yes | partial | pass:provider | pending:module live runtime | module zero-allocation proof |
| recovery, competing leases, restart, reclaim safety | bounded reclaim and recovery hardening; broad direct tests absent | yes | blocked:Windows test rerun | blocked | pending:lifecycle matrix | same-source Windows tests and live proof |
| external Host contract | isolated health/status/423 forwarding proof | yes | pass | pass:isolated Host | not-required | stable promotion review |

Residuals: Direct provider hydrate/release passes; new Agent route cannot run because VM lacks Microsoft.AspNetCore.App; no module zero-allocation proof, competing-lease/pin/restart/reclaim matrix, or concrete production consumer proof; routes remain diagnostic; path-based `attrib +u -p` retains an unavoidable replacement race between separate identity checks; raw response/log/checksum artifacts not recorded.
Gates-At-Checkpoint: `LIVE-ONEDRIVE-PROVIDER` passed direct provider proof; `MODULE-LIVE-RUNTIME` blocked; `STABLE-POSITIVE-MATRIX` blocked; `CONCRETE-CONSUMER-PROOF` blocked; `LIFECYCLE-TEST-MATRIX` blocked by Windows SDK/runtime absence; `HOST-LIVE-PROXY` passed in isolated runtime.
Current-Relation: historical
Replacement: `20260806T0218-module-live-release-timeout`

## Historical packet: `20260805T2325-provider-auth-gate`

Scope: OneDrive reset and authentication diagnosis before user-confirmed account state.
Validation: auth logs showed no valid credentials; retry placeholder read failed. Replaced by `#current` after account reconnection and direct provider proof.
Current-Relation: historical
Replacement: `#current`

## Historical packet: `20260805T2305-reclaim-bound-review`

Scope: Sage follow-up fixes for operation timeouts, reclaim bounds, evidence completeness, and stable-readiness review; reclaim cap corrected for both dehydration phases.
Validation: source build and contract checks passed; reclaim capped at 10 paths, each with two 30s phases, under the 660s policy. Replaced by `#current` after provider diagnosis.
Current-Relation: historical
Replacement: `#current`

## Historical packet: `20260805T2250-stability-hardening-review`

Scope: Sage follow-up fixes for operation timeouts, initial 16-path reclaim bound, and evidence completeness.
Validation: source build and contract checks passed; Sage identified the 16-path bound could reach ~960s because dehydration has two 30s phases. Replaced by `#current`.
Current-Relation: historical
Replacement: `#current`

## Historical packet: `20260805T2215-stable-hardening-negative`

Scope: Agent lifecycle hardening, isolated Host negative proof, and initial current evidence rewrite.
Validation: exact final extracted Windows Agent source 154/154; preceding source Core 62/62 and Host 124/124; live provider-denied 423; isolated Host health/status/423 forwarding; contract checks pass.
Current-Relation: historical
Replacement: `#current`

## Historical packet: `20260805T1718-live-diagnostic`

Scope: initial live diagnostic validation before lifecycle hardening.
Validation: Agent status 200; traversal 422 `onedrive_path_blocked`; stale config ETag 409 `onedrive_config_conflict`; known placeholder hydration failed with provider access denied. Tests Core 62/62, Agent 152/152, Host 124/124. Host proxy proof was not recorded.
Current-Relation: historical
Replacement: `#current`
