# Windows Operator Go Client

Generated Go client for the Windows Operator Host REST contract.

Use this package from external projects instead of `scripts/linux/wo`, `Justfile`,
SSH runner scripts, staged PowerShell, or Windows-local paths.

## Install

After a release tag is pushed:

```bash
go get github.com/Nay78/windows-operator/clients/go@<tag>
```

During local development:

```text
replace github.com/Nay78/windows-operator/clients/go => /path/to/windows-operator/clients/go
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

For bounded discovery, call `ListOpenApiNamespacesWithResponse`, then fetch a
namespace spec such as `mail.outlook` with `GetOpenApiNamespaceDocument`.
Namespace specs default to stable operations; set the generated `Surface` query
parameter to `all` or a comma-separated surface list when diagnostic/development
operations are needed.

## Operator Errors

Branch on `code`; treat `message` as operator text.

```go
run, err := client.GetMailRunWithResponse(ctx, "missing-run")
if err != nil {
    return err
}
if run.StatusCode() >= 400 && run.JSON4XX != nil {
    switch run.JSON4XX.Code {
    case "mail_run_not_found":
        return nil
    default:
        return fmt.Errorf(
            "mail run failed: code=%s message=%s",
            run.JSON4XX.Code,
            run.JSON4XX.Message)
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
    Job: windowsoperator.PowerPointUpdateJobInput{
        JobId:       jobID,
        RequestedBy: "external-consumer",
        Operations: &[]windowsoperator.PowerPointUpdateOperationInput{
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
if result.StatusCode() != 200 || result.JSON200 == nil {
    return fmt.Errorf("powerpoint update failed: %d", result.StatusCode())
}
```

For queued jobs, poll through the typed client. `succeeded`, `failed`, and
`partial` are terminal job states.

```go
record, err := windowsoperator.WaitForPowerPointJob(
    ctx,
    client,
    jobID,
    windowsoperator.WaitOptions{
        Interval: time.Second,
        Timeout:  5 * time.Minute,
    })
if err != nil {
    return err
}
if record.Status != "succeeded" {
    return fmt.Errorf("powerpoint job ended: %s", record.Status)
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
if artifacts.StatusCode() != 200 || artifacts.JSON200 == nil ||
    len(artifacts.JSON200.Artifacts) == 0 {
    return fmt.Errorf("artifact listing failed or empty")
}
err = windowsoperator.DownloadArtifact(
    ctx,
    client,
    artifacts.JSON200.Artifacts[0],
    &body)
```

`DownloadArtifact` validates declared byte length, SHA-256, and media type
before writing. Validation failure leaves the destination untouched.

## Contract Drift

Release gates:

```bash
scripts/check-openapi-contract.sh
cd clients/go && go test ./...
```

Consumer runtime gate:

```go
_, err := windowsoperator.CheckContractCompatibility(
    ctx,
    client,
    windowsoperator.SupportedContractVersion)
```

Compatibility requires valid SemVer. Stable runtimes must share the consumer's
major version and be equal or newer. Pre-release versions require the same core
version and pre-release identifiers; build metadata does not affect matching.

`examples_test.go` compile-checks health/capabilities, typed errors, PowerPoint
updates, and artifact downloads from an external-package consumer.
