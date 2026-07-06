# Windows Operator Go Client

Generated Go client for the Windows Operator Host REST contract.

Use this package from external projects instead of `scripts/linux/wo`, `Justfile`,
SSH runner scripts, staged PowerShell, or Windows-local paths.

## Install

After a release tag is pushed:

```bash
go get github.com/alejg/windows-operator/clients/go@<tag>
```

During local development:

```text
replace github.com/alejg/windows-operator/clients/go => /path/to/windows-operator/clients/go
```

## Client

```go
client, err := windowsoperator.NewClientWithResponses("http://127.0.0.1:43117")
if err != nil {
    return err
}
```

## Health And Capabilities

```go
health, err := client.GetHealthWithResponse(ctx)
if err != nil {
    return err
}
if health.StatusCode() != 200 || health.JSON200 == nil {
    return fmt.Errorf("health failed: %d", health.StatusCode())
}

capabilities, err := windowsoperator.CheckContractVersion(
    ctx,
    client,
    windowsoperator.SupportedContractVersion)
if err != nil {
    return err
}
if !capabilities.Features["powerpoint.online.update"].Available {
    return fmt.Errorf("powerpoint unavailable")
}
```

## Operator Errors

Branch on `code`; treat `message` as operator text.

```go
run, err := client.GetMailRunWithResponse(ctx, "missing-run")
if err != nil {
    return err
}
if run.StatusCode() >= 400 && run.JSON4XX != nil && run.JSON4XX.Code != nil {
    switch *run.JSON4XX.Code {
    case "mail_run_not_found":
        return nil
    default:
        return run.JSON4XX
    }
}
```

## PowerPoint Update

```go
mode := "plain"
text := "Updated by external consumer"
request := windowsoperator.PowerPointOnlineUpdateRequest{
    DeckUrl:   &deckURL,
    SessionId: &sessionID,
    Job: &windowsoperator.PowerPointUpdateJob{
        JobId:       jobID,
        RequestedBy: "external-consumer",
        Operations: []windowsoperator.PowerPointUpdateOperation{
            {
                Kind:     "replaceText",
                TargetId: "TITLE_MAIN",
                Mode:     &mode,
                Text:     &text,
            },
        },
    },
}

result, err := client.UpdatePowerPointOnlinePresentationWithResponse(ctx, request)
if err != nil {
    return err
}
if result.StatusCode() >= 400 {
    return fmt.Errorf("powerpoint update failed: %d", result.StatusCode())
}
```

## Mail Download Dry Run

```go
dryRun := true
request := windowsoperator.MailDownloadRequest{
    RunId:  &runID,
    DryRun: &dryRun,
}
result, err := client.DownloadMailAttachmentsWithResponse(ctx, request)
if err != nil {
    return err
}
if result.StatusCode() != 200 {
    return fmt.Errorf("mail dry run failed: %d", result.StatusCode())
}
```

## Artifact Download

Use `artifact` refs from results or `ListRunArtifactsWithResponse`. Do not read
`path`, `hostPath`, or `absolutePath` fields from external projects.

```go
artifacts, err := client.ListRunArtifactsWithResponse(ctx, runID)
if err != nil {
    return err
}
var body bytes.Buffer
err = windowsoperator.DownloadArtifact(ctx, client, artifacts.JSON200.Artifacts[0], &body)
```

## Contract Drift

Release gates:

```bash
scripts/check-openapi-contract.sh
cd clients/go && go test ./...
```

Consumer runtime gate:

```go
_, err := windowsoperator.CheckContractVersion(
    ctx,
    client,
    windowsoperator.SupportedContractVersion)
```
