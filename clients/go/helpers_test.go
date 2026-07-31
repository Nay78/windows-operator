package windowsoperator

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestDecodeOperatorError(t *testing.T) {
	category := OperatorErrorCategoryNotFound
	retryable := false
	correlationID := "corr-1"
	code := "artifact_not_found"
	body, err := json.Marshal(OperatorError{
		Code:          code,
		Category:      &category,
		Retryable:     &retryable,
		CorrelationId: &correlationID,
		Message:       "Requested artifact was not found.",
		Remediation:   "Request an existing artifact.",
	})
	if err != nil {
		t.Fatal(err)
	}

	operatorError, err := DecodeOperatorError(body)
	if err != nil {
		t.Fatal(err)
	}

	if operatorError.Code != code {
		t.Fatalf("code = %#v, want %q", operatorError.Code, code)
	}
	if operatorError.Category == nil || *operatorError.Category != category {
		t.Fatalf("category = %#v, want %q", operatorError.Category, category)
	}
	if operatorError.Retryable == nil || *operatorError.Retryable {
		t.Fatalf("retryable = %#v, want false", operatorError.Retryable)
	}
}

func TestContractVersionAndArtifactHelpers(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/v1/capabilities":
			writeJSON(t, w, CapabilitiesResult{
				ContractVersion: SupportedContractVersion,
				Host: CapabilityHost{
					Status:             "ok",
					RuntimeMode:        "headless-host",
					RestBaseUrl:        serverBaseURL(r),
					DesktopAgentStatus: ptr("ok"),
				},
				Features: map[string]CapabilityFeature{
					"powerpoint.online.update": {Available: true, Surface: "stable"},
				},
				CheckedAtUtc: time.Date(2026, 7, 6, 12, 0, 0, 0, time.UTC),
			})
		case "/v1/artifacts/proof":
			w.Header().Set("Content-Type", "text/plain")
			_, _ = w.Write([]byte("artifact proof"))
		default:
			http.NotFound(w, r)
		}
	}))
	defer server.Close()

	client, err := NewClientWithResponses(server.URL)
	if err != nil {
		t.Fatal(err)
	}

	capabilities, err := CheckContractVersion(context.Background(), client, SupportedContractVersion)
	if err != nil {
		t.Fatal(err)
	}
	if !capabilities.Features["powerpoint.online.update"].Available {
		t.Fatal("powerpoint.online.update unavailable")
	}
	if _, err := CheckContractCompatibility(
		context.Background(),
		client,
		SupportedContractVersion); err != nil {
		t.Fatal(err)
	}

	var out bytes.Buffer
	artifactBody := []byte("artifact proof")
	artifactBytes := int64(len(artifactBody))
	artifactSHA256 := fmt.Sprintf("%x", sha256.Sum256(artifactBody))
	err = DownloadArtifact(
		context.Background(),
		client,
		ArtifactRef{
			ArtifactId: "proof",
			Href:       "/v1/artifacts/proof",
			MediaType:  "text/plain",
			Bytes:      &artifactBytes,
			Sha256:     &artifactSHA256,
		},
		&out)
	if err != nil {
		t.Fatal(err)
	}
	if out.String() != "artifact proof" {
		t.Fatalf("artifact body = %q", out.String())
	}
}

func TestDownloadArtifactRejectsInvalidProofBeforeWriting(t *testing.T) {
	body := []byte("artifact proof")
	size := int64(len(body))
	sum := fmt.Sprintf("%x", sha256.Sum256(body))
	wrongSize := size + 1
	wrongSum := strings.Repeat("0", 64)

	tests := []struct {
		name        string
		artifact    ArtifactRef
		mediaType   string
		wantErrPart string
	}{
		{
			name: "size",
			artifact: ArtifactRef{
				ArtifactId: "proof", Href: "/v1/artifacts/proof",
				MediaType: "text/plain", Bytes: &wrongSize, Sha256: &sum,
			},
			mediaType:   "text/plain",
			wantErrPart: "size mismatch",
		},
		{
			name: "sha256",
			artifact: ArtifactRef{
				ArtifactId: "proof", Href: "/v1/artifacts/proof",
				MediaType: "text/plain", Bytes: &size, Sha256: &wrongSum,
			},
			mediaType:   "text/plain",
			wantErrPart: "sha256 mismatch",
		},
		{
			name: "media type",
			artifact: ArtifactRef{
				ArtifactId: "proof", Href: "/v1/artifacts/proof",
				MediaType: "application/json", Bytes: &size, Sha256: &sum,
			},
			mediaType:   "text/plain; charset=utf-8",
			wantErrPart: "media type mismatch",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				w.Header().Set("Content-Type", test.mediaType)
				_, _ = w.Write(body)
			}))
			defer server.Close()

			client, err := NewClientWithResponses(server.URL)
			if err != nil {
				t.Fatal(err)
			}
			output := bytes.NewBufferString("existing")
			err = DownloadArtifact(context.Background(), client, test.artifact, output)
			if err == nil || !strings.Contains(err.Error(), test.wantErrPart) {
				t.Fatalf("error = %v, want %q", err, test.wantErrPart)
			}
			if output.String() != "existing" {
				t.Fatalf("writer changed on validation failure: %q", output.String())
			}
		})
	}
}

func TestCheckContractVersionReturnsTypedRemoteError(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusServiceUnavailable)
		if err := json.NewEncoder(w).Encode(OperatorError{
			Code:        "desktop_agent_unavailable",
			Message:     "Desktop agent unavailable.",
			Remediation: "Start desktop agent.",
		}); err != nil {
			t.Fatal(err)
		}
	}))
	defer server.Close()

	client, err := NewClientWithResponses(server.URL)
	if err != nil {
		t.Fatal(err)
	}
	_, err = CheckContractVersion(context.Background(), client, SupportedContractVersion)
	var remote *RemoteError
	if !errors.As(err, &remote) {
		t.Fatalf("error = %T %v, want *RemoteError", err, err)
	}
	if remote.StatusCode != http.StatusServiceUnavailable ||
		remote.Operator == nil ||
		remote.Operator.Code != "desktop_agent_unavailable" {
		t.Fatalf("remote error = %#v", remote)
	}
}

func TestCompatibleContractVersion(t *testing.T) {
	tests := []struct {
		live, minimum string
		want          bool
	}{
		{"1.0.0", "1.0.0", true},
		{"1.0.1", "1.0.0", true},
		{"1.2.0", "1.1.9", true},
		{"1.0.0", "1.0.1", false},
		{"2.0.0", "1.0.0", false},
		{"0.2.0", "0.1.0", true},
		{"1.0.0-rc.1", "1.0.0-rc.1", true},
		{"1.0.0-rc.1+host.2", "1.0.0-rc.1+client.7", true},
		{"1.0.0-rc.2", "1.0.0-rc.1", false},
		{"1.0.0+host.2", "1.0.0+client.7", true},
		{"999999999999999999999.0.0", "999999999999999999999.0.0", true},
		{"1.0", "1.0.0", false},
		{"01.0.0", "1.0.0", false},
		{"1.0.0-rc.01", "1.0.0-rc.01", false},
		{"1.0.0+", "1.0.0", false},
		{"1.0.0-rc!", "1.0.0-rc!", false},
	}
	for _, test := range tests {
		t.Run(test.live+"_"+test.minimum, func(t *testing.T) {
			if got := compatibleContractVersion(test.live, test.minimum); got != test.want {
				t.Fatalf(
					"compatibleContractVersion(%q, %q) = %v, want %v",
					test.live,
					test.minimum,
					got,
					test.want)
			}
		})
	}
}

func TestPowerPointJobTerminalStatuses(t *testing.T) {
	tests := map[string]bool{
		"queued":    false,
		"running":   false,
		"succeeded": true,
		"failed":    true,
		"partial":   true,
		"skipped":   false,
		"":          false,
		"unknown":   false,
	}
	for status, want := range tests {
		t.Run(status, func(t *testing.T) {
			if got := isTerminalPowerPointJobStatus(status); got != want {
				t.Fatalf("isTerminalPowerPointJobStatus(%q) = %v, want %v", status, got, want)
			}
		})
	}
}

func TestWaitForPowerPointJob(t *testing.T) {
	calls := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/v1/powerpoint/jobs/job-1" {
			http.NotFound(w, r)
			return
		}

		calls++
		status := "queued"
		if calls > 1 {
			status = "succeeded"
		}

		writeJSON(t, w, PowerPointJobRecord{
			JobId:  "job-1",
			Status: status,
			Job: PowerPointUpdateJob{
				JobId:       "job-1",
				RequestedBy: "test",
				Operations:  []PowerPointUpdateOperation{},
			},
			EnqueuedAtUtc: time.Date(2026, 7, 6, 12, 0, 0, 0, time.UTC),
			UpdatedAtUtc:  time.Date(2026, 7, 6, 12, 0, 1, 0, time.UTC),
		})
	}))
	defer server.Close()

	client, err := NewClientWithResponses(server.URL)
	if err != nil {
		t.Fatal(err)
	}

	record, err := WaitForPowerPointJob(
		context.Background(),
		client,
		"job-1",
		WaitOptions{Interval: time.Millisecond, Timeout: time.Second})
	if err != nil {
		t.Fatal(err)
	}
	if record == nil || record.Status != "succeeded" {
		t.Fatalf("record = %#v, want succeeded", record)
	}
	if calls != 2 {
		t.Fatalf("calls = %d, want 2", calls)
	}
}

func writeJSON(t *testing.T, w http.ResponseWriter, value any) {
	t.Helper()
	w.Header().Set("Content-Type", "application/json")
	if err := json.NewEncoder(w).Encode(value); err != nil {
		t.Fatal(err)
	}
}

func ptr[T any](value T) *T {
	return &value
}

func serverBaseURL(r *http.Request) string {
	return "http://" + r.Host
}
