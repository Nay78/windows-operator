package windowsoperator

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"time"
)

const SupportedContractVersion = "0.1.0"

type RemoteError struct {
	StatusCode int
	Operator   *OperatorError
	Body       []byte
}

func (e *RemoteError) Error() string {
	if e.Operator != nil && e.Operator.Code != nil {
		if e.Operator.Message != nil {
			return fmt.Sprintf("windows operator error: status=%d code=%s message=%s", e.StatusCode, *e.Operator.Code, *e.Operator.Message)
		}

		return fmt.Sprintf("windows operator error: status=%d code=%s", e.StatusCode, *e.Operator.Code)
	}

	return fmt.Sprintf("windows operator error: status=%d", e.StatusCode)
}

func DecodeOperatorError(body []byte) (*OperatorError, error) {
	var operatorError OperatorError
	if err := json.Unmarshal(body, &operatorError); err != nil {
		return nil, err
	}

	if operatorError.Code == nil {
		return nil, fmt.Errorf("operator error body missing code")
	}

	return &operatorError, nil
}

func CheckContractVersion(
	ctx context.Context,
	client *ClientWithResponses,
	expected string,
	reqEditors ...RequestEditorFn,
) (*CapabilitiesResult, error) {
	response, err := client.GetCapabilitiesWithResponse(ctx, reqEditors...)
	if err != nil {
		return nil, err
	}

	if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
		return nil, remoteError(response.StatusCode(), response.Body, response.JSON4XX, response.JSON5XX)
	}

	if response.JSON200.ContractVersion != expected {
		return nil, fmt.Errorf("windows operator contract version mismatch: live=%s expected=%s", response.JSON200.ContractVersion, expected)
	}

	return response.JSON200, nil
}

func DownloadArtifact(
	ctx context.Context,
	client *ClientWithResponses,
	artifact ArtifactRef,
	writer io.Writer,
	reqEditors ...RequestEditorFn,
) error {
	response, err := client.GetArtifactWithResponse(ctx, artifact.ArtifactId, reqEditors...)
	if err != nil {
		return err
	}

	if response.StatusCode() != http.StatusOK {
		return remoteError(response.StatusCode(), response.Body, response.JSON4XX, response.JSON5XX)
	}

	_, err = io.Copy(writer, bytes.NewReader(response.Body))
	return err
}

type WaitOptions struct {
	Interval time.Duration
	Timeout  time.Duration
}

func WaitForRun(
	ctx context.Context,
	poll func(context.Context) (status string, terminal bool, err error),
	options WaitOptions,
) (string, error) {
	interval := options.Interval
	if interval <= 0 {
		interval = time.Second
	}

	if options.Timeout > 0 {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, options.Timeout)
		defer cancel()
	}

	timer := time.NewTimer(0)
	defer timer.Stop()

	for {
		select {
		case <-ctx.Done():
			return "", ctx.Err()
		case <-timer.C:
			status, terminal, err := poll(ctx)
			if err != nil {
				return status, err
			}

			if terminal {
				return status, nil
			}

			timer.Reset(interval)
		}
	}
}

func WaitForPowerPointJob(
	ctx context.Context,
	client *ClientWithResponses,
	jobID string,
	options WaitOptions,
	reqEditors ...RequestEditorFn,
) (*PowerPointJobRecord, error) {
	var last *PowerPointJobRecord
	_, err := WaitForRun(
		ctx,
		func(ctx context.Context) (string, bool, error) {
			response, err := client.GetPowerPointJobWithResponse(ctx, jobID, reqEditors...)
			if err != nil {
				return "", false, err
			}

			if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
				return "", false, remoteError(response.StatusCode(), response.Body, response.JSON4XX, response.JSON5XX)
			}

			last = response.JSON200
			return last.Status, isTerminalPowerPointJobStatus(last.Status), nil
		},
		options)
	if err != nil {
		return last, err
	}

	return last, nil
}

func isTerminalPowerPointJobStatus(status string) bool {
	switch status {
	case "succeeded", "failed", "partial", "skipped":
		return true
	default:
		return false
	}
}

func remoteError(statusCode int, body []byte, error4xx *OperatorError, error5xx *OperatorError) error {
	if error4xx != nil {
		return &RemoteError{StatusCode: statusCode, Operator: error4xx, Body: body}
	}

	if error5xx != nil {
		return &RemoteError{StatusCode: statusCode, Operator: error5xx, Body: body}
	}

	operatorError, err := DecodeOperatorError(body)
	if err == nil {
		return &RemoteError{StatusCode: statusCode, Operator: operatorError, Body: body}
	}

	return &RemoteError{StatusCode: statusCode, Body: body}
}
