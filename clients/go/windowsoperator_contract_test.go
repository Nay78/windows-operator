package windowsoperator

import (
	"encoding/json"
	"testing"
	"time"
)

func TestPowerPointOnlineUpdateRequestFinalProofShape(t *testing.T) {
	deckURL := "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1"
	sessionID := "ppt-mutation-proof-20260704t0950z"
	text := "Windows Operator live edit proof 2026-07-04 09:50 UTC"
	mode := "plain"
	allowMutation := true
	prepareTemplate := true
	cleanupTemplate := true
	verifyReopen := true
	cleanupSession := true
	bindNamedTargets := true
	validateOnly := false
	capture := true
	evidenceSlide := int32(4)
	templateWait := int32(2)
	reopenWait := int32(40)

	request := PowerPointOnlineUpdateRequest{
		DeckUrl:             &deckURL,
		SessionId:           &sessionID,
		EvidenceSlideNumber: &evidenceSlide,
		Capture:             &capture,
		AllowDeckMutation:   &allowMutation,
		PrepareTemplate:     &prepareTemplate,
		CleanupTemplate:     &cleanupTemplate,
		TemplateWaitSeconds: &templateWait,
		VerifyReopen:        &verifyReopen,
		ReopenWaitSeconds:   &reopenWait,
		CleanupSession:      &cleanupSession,
		Job: PowerPointUpdateJobInput{
			JobId:            sessionID,
			DiscoverTargets:  &prepareTemplate,
			BindNamedTargets: &bindNamedTargets,
			ValidateOnly:     &validateOnly,
			RequestedBy:      "codex-live-mutation-proof",
			CreatedAt:        timePtr(time.Date(2026, 7, 4, 9, 50, 0, 0, time.UTC)),
			Operations: slicePtr([]PowerPointUpdateOperationInput{
				{
					Kind:     "replaceText",
					TargetId: "TITLE_MAIN",
					Mode:     &mode,
					Text:     &text,
				},
			}),
		},
	}

	payload, err := json.Marshal(request)
	if err != nil {
		t.Fatal(err)
	}

	var roundTrip map[string]any
	if err := json.Unmarshal(payload, &roundTrip); err != nil {
		t.Fatal(err)
	}

	assertField(t, roundTrip, "deckUrl", deckURL)
	assertField(t, roundTrip, "sessionId", sessionID)
	assertField(t, roundTrip, "evidenceSlideNumber", float64(4))
	assertField(t, roundTrip, "allowDeckMutation", true)
	assertField(t, roundTrip, "prepareTemplate", true)
	assertField(t, roundTrip, "cleanupTemplate", true)
	assertField(t, roundTrip, "verifyReopen", true)
	assertField(t, roundTrip, "cleanupSession", true)

	job, ok := roundTrip["job"].(map[string]any)
	if !ok {
		t.Fatalf("job field missing or wrong type: %T", roundTrip["job"])
	}
	assertField(t, job, "discoverTargets", true)
	assertField(t, job, "bindNamedTargets", true)
	assertField(t, job, "validateOnly", false)

	operations, ok := job["operations"].([]any)
	if !ok || len(operations) != 1 {
		t.Fatalf("operations field = %#v, want one operation", job["operations"])
	}
	operation, ok := operations[0].(map[string]any)
	if !ok {
		t.Fatalf("operation field wrong type: %T", operations[0])
	}
	assertField(t, operation, "kind", "replaceText")
	assertField(t, operation, "targetId", "TITLE_MAIN")
	assertField(t, operation, "text", text)
}

func timePtr(value time.Time) *time.Time {
	return &value
}

func slicePtr[T any](value []T) *[]T {
	return &value
}

func TestPowerPointOnlineUpdateResultProofSessionFieldsCompile(t *testing.T) {
	shapeName := "TARGET_TITLE_MAIN"
	source := "repairedName"
	bound := true
	tagged := true
	result := PowerPointOnlineUpdateResult{
		SaveProofTier: Tier3ReopenVisual,
		Status:        PowerPointOnlineUpdateStatusSucceeded,
		VerificationSession: &PowerPointOnlineSessionResult{
			Status: PowerPointOnlineSessionStatusReady,
		},
		TemplatePreparationSession: &PowerPointOnlineSessionResult{
			Status: PowerPointOnlineSessionStatusReady,
		},
		TemplateCleanupSession: &PowerPointOnlineSessionResult{
			Status: PowerPointOnlineSessionStatusReady,
		},
		SessionCleanupSession: &PowerPointOnlineSessionResult{
			Status: PowerPointOnlineSessionStatusClosed,
		},
		JobRecord: PowerPointJobRecord{
			JobId: "job-named-target",
			Job: PowerPointUpdateJob{
				JobId:            "job-named-target",
				BindNamedTargets: true,
				RequestedBy:      "test",
				Operations:       []PowerPointUpdateOperation{},
			},
			Status:        "succeeded",
			EnqueuedAtUtc: time.Date(2026, 7, 5, 0, 0, 0, 0, time.UTC),
			UpdatedAtUtc:  time.Date(2026, 7, 5, 0, 0, 1, 0, time.UTC),
			Result: &PowerPointUpdateResult{
				JobId:      "job-named-target",
				Status:     "succeeded",
				StartedAt:  time.Date(2026, 7, 5, 0, 0, 0, 0, time.UTC),
				FinishedAt: time.Date(2026, 7, 5, 0, 0, 1, 0, time.UTC),
				Targets: []PowerPointTargetResult{
					{
						TargetId:      "TITLE_MAIN",
						OperationKind: "replaceText",
						Status:        "succeeded",
						ShapeName:     &shapeName,
						Source:        &source,
						Bound:         &bound,
						Tagged:        &tagged,
					},
				},
			},
		},
	}

	if result.SaveProofTier != Tier3ReopenVisual {
		t.Fatalf("save proof tier = %q", result.SaveProofTier)
	}
	if result.VerificationSession == nil || result.TemplateCleanupSession == nil {
		t.Fatal("proof session fields missing")
	}
	if result.JobRecord.Result == nil || result.JobRecord.Result.Targets[0].Source == nil {
		t.Fatal("named target metadata missing")
	}
}

func assertField(t *testing.T, fields map[string]any, name string, expected any) {
	t.Helper()
	actual, ok := fields[name]
	if !ok {
		t.Fatalf("field %q missing", name)
	}
	if actual != expected {
		t.Fatalf("field %q = %#v, want %#v", name, actual, expected)
	}
}
