# Windows Operator Storage Cleanup

## Purpose

Keep VM-local Windows Operator state bounded without sweeping Windows, browser,
Outlook, Downloads, or user OneDrive content.

The cleanup engine is a standalone SYSTEM task. It does not run inside Host or
Agent, does not restart either runtime, and does not expose a REST route.

## Components

- scripts/windows/invoke-storage-cleanup.ps1: audit, execute, and restore engine.
- scripts/windows/register-storage-cleanup.ps1: stages the engine under
  C:\ProgramData\WindowsOperator\maintenance\executor and manages
  WindowsOperator.StorageCleanup.
- scripts/windows/test-storage-cleanup.ps1: synthetic Windows test.
- scripts/linux/storage-cleanup-tests.sh: live Windows test wrapper.

Registration defaults to Audit mode. Execute-mode registration requires an
explicit operator decision because it performs deletion after retention checks.

## Ownership boundary

Eligible paths are hardcoded in the engine:

- C:\ProgramData\WindowsOperator\sync\<run-id> after 72 hours; 24 hours under
  low disk space.
- C:\ProgramData\WindowsOperator\stability-preflight after 7 days; 48 hours
  under low disk space.
- C:\ProgramData\WindowsOperator\onedrive-module-live-test after 7 days; 48
  hours under low disk space.
- ...\WindowsOperator\logs\agent-*.log after 14 days; 3 days under low disk
  space, retaining the newest five logs per user state root.
- Maintenance JSON reports after 30 days; 7 days under low disk space,
  retaining the newest 30 reports.

Protected: published Host/Agent runtimes, launchers, certificates, exchange,
SDK, NuGet, build artifacts, provisioning state, Files-On-Demand state, repo
roots, configured OneDrive roots, and OneDrive rollback backups.

The engine rejects reparse-point candidates and honors
.windows-operator-active markers. Sync writes that marker before archive upload
and removes it on success or failure.

## Disk policy

- Low-space mode: free space below 20 GiB or 15%.
- Recovery target: 30 GiB and 20%.
- Normal deletion/quarantine limit: 10 GiB per run.
- Low-space deletion limit: 25 GiB per run.
- Normal mode moves candidates into same-volume quarantine. A later run purges
  quarantine after 24 hours.
- Low-space mode purges quarantine first and purges newly eligible candidates
  immediately until recovery target or the allowlist is exhausted.
- Failure to reach target returns capacity_unresolved; scope never expands.

Every run writes a plan before mutation and a result with free-space before and
after, candidate disposition, bytes, paths, errors, and script hash. Restore
uses the quarantine manifest:

~~~
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ProgramData\WindowsOperator\maintenance\executor\invoke-storage-cleanup.ps1 -Mode Restore -RestoreRunId <cleanup-run-id>
~~~

## Operations

Audit a live VM through the staged runner:

~~~
scripts/linux/windows-run-ps.sh scripts/windows/invoke-storage-cleanup.ps1 -Mode Audit
~~~

Run synthetic Windows tests:

~~~
scripts/linux/storage-cleanup-tests.sh
~~~

Register the scheduled task in audit-only mode:

~~~
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\register-storage-cleanup.ps1 -RepoRoot C:\src\windows-operator -Mode Audit
~~~

Register execute mode only after reviewing a live audit report:

~~~
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\register-storage-cleanup.ps1 -RepoRoot C:\src\windows-operator -Mode Execute
~~~

Unregister the task without deleting state:

~~~
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\register-storage-cleanup.ps1 -RepoRoot C:\src\windows-operator -Unregister
~~~

## Explicit non-goals

No whole-root OneDrive sweep. Files-On-Demand reclaim remains lease-owned and
must use the existing hydrate/use/release service. No Windows Temp, Windows
Update, Installer, browser profile, Outlook data, Downloads, or user-file
cleanup in this first slice.
