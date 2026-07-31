package windowsoperator

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"io"
	"mime"
	"net/http"
	"strings"
	"time"
)

const SupportedContractVersion = "0.1.0"

type RemoteError struct {
	StatusCode int
	Operator   *OperatorError
	Body       []byte
}

func (e *RemoteError) Error() string {
	if e.Operator != nil && e.Operator.Code != "" {
		if e.Operator.Message != "" {
			return fmt.Sprintf("windows operator error: status=%d code=%s message=%s", e.StatusCode, e.Operator.Code, e.Operator.Message)
		}

		return fmt.Sprintf("windows operator error: status=%d code=%s", e.StatusCode, e.Operator.Code)
	}

	return fmt.Sprintf("windows operator error: status=%d", e.StatusCode)
}

func DecodeOperatorError(body []byte) (*OperatorError, error) {
	var operatorError OperatorError
	if err := json.Unmarshal(body, &operatorError); err != nil {
		return nil, err
	}

	if operatorError.Code == "" {
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

// CheckContractCompatibility verifies that the live contract can satisfy a
// consumer compiled against minimumSupported. Pre-release versions require an
// exact core and pre-release match; stable consumers accept an equal or newer
// runtime in the same major line.
func CheckContractCompatibility(
	ctx context.Context,
	client *ClientWithResponses,
	minimumSupported string,
	reqEditors ...RequestEditorFn,
) (*CapabilitiesResult, error) {
	response, err := client.GetCapabilitiesWithResponse(ctx, reqEditors...)
	if err != nil {
		return nil, err
	}
	if response.StatusCode() != http.StatusOK || response.JSON200 == nil {
		return nil, remoteError(response.StatusCode(), response.Body, response.JSON4XX, response.JSON5XX)
	}
	if !compatibleContractVersion(response.JSON200.ContractVersion, minimumSupported) {
		return nil, fmt.Errorf(
			"windows operator contract incompatible: live=%s minimum=%s",
			response.JSON200.ContractVersion,
			minimumSupported)
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

	if artifact.Bytes != nil && int64(len(response.Body)) != *artifact.Bytes {
		return fmt.Errorf(
			"windows operator artifact size mismatch: received=%d expected=%d",
			len(response.Body),
			*artifact.Bytes)
	}
	if artifact.Sha256 != nil {
		actual := fmt.Sprintf("%x", sha256.Sum256(response.Body))
		if !strings.EqualFold(actual, *artifact.Sha256) {
			return fmt.Errorf(
				"windows operator artifact sha256 mismatch: received=%s expected=%s",
				actual,
				*artifact.Sha256)
		}
	}
	if artifact.MediaType != "" {
		actual := ""
		if response.HTTPResponse != nil {
			actual, _, err = mime.ParseMediaType(response.HTTPResponse.Header.Get("Content-Type"))
		}
		expected, _, expectedErr := mime.ParseMediaType(artifact.MediaType)
		if err != nil || expectedErr != nil || !strings.EqualFold(actual, expected) {
			return fmt.Errorf(
				"windows operator artifact media type mismatch: received=%s expected=%s",
				actual,
				artifact.MediaType)
		}
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
	case "succeeded", "failed", "partial":
		return true
	default:
		return false
	}
}

func compatibleContractVersion(live string, minimum string) bool {
	liveVersion, liveOK := parseContractVersion(live)
	minimumVersion, minimumOK := parseContractVersion(minimum)
	if !liveOK || !minimumOK {
		return false
	}
	if len(liveVersion.prerelease) > 0 || len(minimumVersion.prerelease) > 0 {
		return compareNumericIdentifier(liveVersion.major, minimumVersion.major) == 0 &&
			compareNumericIdentifier(liveVersion.minor, minimumVersion.minor) == 0 &&
			compareNumericIdentifier(liveVersion.patch, minimumVersion.patch) == 0 &&
			equalIdentifiers(liveVersion.prerelease, minimumVersion.prerelease)
	}
	if compareNumericIdentifier(liveVersion.major, minimumVersion.major) != 0 {
		return false
	}
	if minor := compareNumericIdentifier(liveVersion.minor, minimumVersion.minor); minor != 0 {
		return minor > 0
	}
	return compareNumericIdentifier(liveVersion.patch, minimumVersion.patch) >= 0
}

type contractVersion struct {
	major      string
	minor      string
	patch      string
	prerelease []string
}

func parseContractVersion(value string) (contractVersion, bool) {
	var version contractVersion
	if value == "" || strings.Count(value, "+") > 1 {
		return version, false
	}

	coreAndMetadata := strings.SplitN(value, "+", 2)
	if len(coreAndMetadata) == 2 && !validIdentifiers(coreAndMetadata[1], false) {
		return version, false
	}

	coreAndPrerelease := strings.SplitN(coreAndMetadata[0], "-", 2)
	core := strings.Split(coreAndPrerelease[0], ".")
	if len(core) != 3 {
		return version, false
	}
	for _, identifier := range core {
		if !validNumericIdentifier(identifier) {
			return version, false
		}
	}

	if len(coreAndPrerelease) == 2 {
		if !validIdentifiers(coreAndPrerelease[1], true) {
			return version, false
		}
		version.prerelease = strings.Split(coreAndPrerelease[1], ".")
	}

	version.major = core[0]
	version.minor = core[1]
	version.patch = core[2]
	return version, true
}

func validNumericIdentifier(value string) bool {
	if value == "" || (len(value) > 1 && value[0] == '0') {
		return false
	}
	for _, char := range value {
		if char < '0' || char > '9' {
			return false
		}
	}
	return true
}

func validIdentifiers(value string, enforceNumericLeadingZeros bool) bool {
	for _, identifier := range strings.Split(value, ".") {
		if identifier == "" {
			return false
		}
		numeric := true
		for _, char := range identifier {
			if (char < '0' || char > '9') &&
				(char < 'A' || char > 'Z') &&
				(char < 'a' || char > 'z') &&
				char != '-' {
				return false
			}
			numeric = numeric && char >= '0' && char <= '9'
		}
		if enforceNumericLeadingZeros && numeric && len(identifier) > 1 && identifier[0] == '0' {
			return false
		}
	}
	return true
}

func compareNumericIdentifier(left string, right string) int {
	if len(left) < len(right) {
		return -1
	}
	if len(left) > len(right) {
		return 1
	}
	return strings.Compare(left, right)
}

func equalIdentifiers(left []string, right []string) bool {
	if len(left) != len(right) {
		return false
	}
	for index := range left {
		if left[index] != right[index] {
			return false
		}
	}
	return true
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
