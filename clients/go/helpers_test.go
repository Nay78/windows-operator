package windowsoperator

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

func TestDecodeOperatorError(t *testing.T) {
	code := "artifact_not_found"
	category := OperatorErrorCategoryNotFound
	retryable := false
	correlationID := "corr-1"
	message := "Requested artifact was not found."
	body, err := json.Marshal(OperatorError{
		Code:          &code,
		Category:      &category,
		Retryable:     &retryable,
		CorrelationId: &correlationID,
		Message:       &message,
	})
	if err != nil {
		t.Fatal(err)
	}

	operatorError, err := DecodeOperatorError(body)
	if err != nil {
		t.Fatal(err)
	}

	if operatorError.Code == nil || *operatorError.Code != code {
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

	var out bytes.Buffer
	err = DownloadArtifact(
		context.Background(),
		client,
		ArtifactRef{ArtifactId: "proof", Href: "/v1/artifacts/proof", MediaType: "text/plain"},
		&out)
	if err != nil {
		t.Fatal(err)
	}
	if out.String() != "artifact proof" {
		t.Fatalf("artifact body = %q", out.String())
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
