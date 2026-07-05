# Local Machine Overrides

Machine-specific Windows Operator settings belong on the Windows machine, not in
shared source.

Agent override path:

```text
%LOCALAPPDATA%\WindowsOperator\run\appsettings.Local.json
```

Host override path:

```text
%ProgramData%\WindowsOperator\run\host.appsettings.Local.json
```

`WindowsOperator.Agent` reads the Agent override when
`WINDOWS_OPERATOR_LOCAL_STATE_ROOT` names the state root. `WindowsOperator.Host`
reads the Host override when `WINDOWS_OPERATOR_HOST_STATE_ROOT` names the state
root, falling back to `WINDOWS_OPERATOR_LOCAL_STATE_ROOT`. The registered Host
task uses `%ProgramData%\WindowsOperator` by default.

## Agent Template

Use this shape for desktop-session overrides:

```json
{
  "Operator": {
    "BindAddress": "127.0.0.1",
    "RestPort": 43119,
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
  },
  "Workbench": {
    "ExchangeRoot": "Z:\\operator-exchange",
    "HostExchangeRoot": "/var/lib/windows-server/shared/operator-exchange"
  },
  "DevAutomation": {
    "Enabled": false,
    "AllowRawJs": false,
    "MaxResultBytes": 65536
  }
}
```

## Host Template

Use this shape for headless Host overrides:

```json
{
  "Operator": {
    "BindAddress": "127.0.0.1",
    "RestPort": 43117
  },
  "Workbench": {
    "ExchangeRoot": "Z:\\operator-exchange",
    "HostExchangeRoot": "/var/lib/windows-server/shared/operator-exchange"
  },
  "DesktopAgent": {
    "BaseUrl": "http://127.0.0.1:43119"
  },
  "PowerPointAddIn": {
    "Enabled": false,
    "BaseUrl": "https://localhost:3003",
    "StaticRoot": "",
    "StateRoot": "",
    "MaxArtifactBytes": 15728640
  }
}
```

Keep REST bindings on loopback unless an authenticated relay or SSH tunnel owns
remote access.
