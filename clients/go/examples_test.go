package windowsoperator_test

import (
	"bytes"
	"context"
	"errors"
	"fmt"

	windowsoperator "github.com/Nay78/windows-operator/clients/go"
)

func Example_healthAndCapabilities() {
	ctx := context.Background()
	client, err := windowsoperator.NewClientWithResponses("http://127.0.0.1:43117")
	if err != nil {
		panic(err)
	}

	health, err := client.GetHealthWithResponse(ctx)
	if err != nil {
		panic(err)
	}
	if health.StatusCode() != 200 || health.JSON200 == nil {
		panic(fmt.Errorf("health failed: status=%d", health.StatusCode()))
	}

	capabilities, err := windowsoperator.CheckContractCompatibility(
		ctx,
		client,
		windowsoperator.SupportedContractVersion)
	if err != nil {
		panic(err)
	}
	if feature := capabilities.Features["powerpoint.online.update"]; !feature.Available {
		panic(fmt.Errorf("PowerPoint unavailable: %s", valueOrEmpty(feature.Reason)))
	}
}

func Example_typedOperatorError() {
	ctx := context.Background()
	client, err := windowsoperator.NewClientWithResponses("http://127.0.0.1:43117")
	if err != nil {
		panic(err)
	}

	_, err = windowsoperator.CheckContractVersion(
		ctx,
		client,
		windowsoperator.SupportedContractVersion)
	var remote *windowsoperator.RemoteError
	if errors.As(err, &remote) && remote.Operator != nil {
		fmt.Printf(
			"status=%d code=%s remediation=%s\n",
			remote.StatusCode,
			remote.Operator.Code,
			remote.Operator.Remediation)
	}
}

func Example_powerPointUpdate() {
	ctx := context.Background()
	client, err := windowsoperator.NewClientWithResponses("http://127.0.0.1:43117")
	if err != nil {
		panic(err)
	}

	deckURL := "https://contoso.sharepoint.com/presentation.pptx"
	text := "Updated by external consumer"
	mode := "plain"
	request := windowsoperator.PowerPointOnlineUpdateRequest{
		DeckUrl: &deckURL,
		Job: windowsoperator.PowerPointUpdateJobInput{
			JobId:       "consumer-job-1",
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

	response, err := client.UpdatePowerPointOnlinePresentationWithResponse(ctx, request)
	if err != nil {
		panic(err)
	}
	if response.StatusCode() != 200 || response.JSON200 == nil {
		panic(fmt.Errorf("PowerPoint update failed: status=%d", response.StatusCode()))
	}
}

func Example_artifactDownload() {
	ctx := context.Background()
	client, err := windowsoperator.NewClientWithResponses("http://127.0.0.1:43117")
	if err != nil {
		panic(err)
	}

	runID := "consumer-run-1"
	artifacts, err := client.ListRunArtifactsWithResponse(ctx, runID)
	if err != nil {
		panic(err)
	}
	if artifacts.StatusCode() != 200 ||
		artifacts.JSON200 == nil ||
		len(artifacts.JSON200.Artifacts) == 0 {
		panic(fmt.Errorf("artifact listing failed: status=%d", artifacts.StatusCode()))
	}

	var body bytes.Buffer
	if err := windowsoperator.DownloadArtifact(
		ctx,
		client,
		artifacts.JSON200.Artifacts[0],
		&body); err != nil {
		panic(err)
	}
}

func valueOrEmpty(value *string) string {
	if value == nil {
		return ""
	}
	return *value
}
