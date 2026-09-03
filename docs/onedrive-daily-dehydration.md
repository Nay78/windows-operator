# Daily OneDrive Dehydration

## Decision

Use `cen_vuelos` as the owner of the daily recovery cadence for its own failed
OneDrive operations. Windows Operator remains the local dehydration mechanism
and safety boundary. Storage Sense remains the temporary broad fallback for
stale content until a fresh zero-allocation reclaim proof gates its rollback.
It changes local Files-On-Demand residency only; it does not delete, rename,
or overwrite files in OneDrive or SharePoint.

Microsoft policy values:

- `AllowStorageSenseGlobal = 1`: enable Storage Sense.
- `ConfigStorageSenseGlobalCadence = 1`: run daily.
- `ConfigStorageSenseCloudContentDehydrationThreshold = 1`: dehydrate content
  unopened for at least one day. Threshold `0` means never dehydrate.

The policy is machine-scoped under:

```text
HKLM\SOFTWARE\Policies\Microsoft\Windows\StorageSense
```

The script does not enable unrelated temporary-file, Downloads, or Recycle Bin
cleanup settings.

## Audit and apply

Audit is read-only:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\configure-onedrive-daily-dehydration.ps1 -Action Audit
```

Apply requires an elevated PowerShell process and writes a rollback snapshot:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\configure-onedrive-daily-dehydration.ps1 -Action Apply
```

Use `-ThresholdDays N` to retain locally available content opened within the
last `N` days. Run the reported policy refresh command after apply:

```powershell
gpupdate /target:computer /force
```

`cen_vuelos` owns execution cadence. No Windows Operator scheduled task is
needed. The existing lease/reclaim scheduler remains limited to recovery
records. After cutover, rollback this temporary Storage Sense policy using its
snapshot; do not run two dehydration owners.

## Rollback

Rollback restores only values captured by the selected snapshot:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\configure-onedrive-daily-dehydration.ps1 -Action Rollback -SnapshotPath <snapshot-path>
```

## Boundary and proof

This policy can make unopened OneDrive/SharePoint content online-only. Opening
an online-only file rehydrates it and can consume local disk again. User or
provider restrictions may prevent immediate zero-allocation reclamation; the
policy does not claim per-file completion.

For a selected file that the Agent hydrated, use the existing identity-bound
`CfSetPinState(CF_PIN_STATE_UNPINNED)` release path and its allocation proof.
Do not replace this policy with `attrib +U -P /S` on a root: that broad command
can remove user pins and has no identity-bound proof boundary.

## Microsoft basis

- [Storage Policy CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-storage)
- [Configure Storage Sense in Windows](https://learn.microsoft.com/en-us/windows/configuration/storage/storage-sense)
- [Set Files On-Demand states in Windows](https://learn.microsoft.com/en-us/sharepoint/files-on-demand-windows)
- [CF_PIN_STATE](https://learn.microsoft.com/en-us/windows/win32/api/cfapi/ne-cfapi-cf_pin_state)
