#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
base_url="${WINDOWS_OPERATOR_BASE_URL:-http://127.0.0.1:43117}"
run_id="${WINDOWS_OPERATOR_SMOKE_RUN_ID:-}"
tmpdir="$(mktemp -d)"

cleanup() {
  rm -rf "$tmpdir"
}
trap cleanup EXIT

cat >"$tmpdir/go.mod" <<EOF
module windows-operator-external-smoke

go 1.22

require github.com/alejg/windows-operator/clients/go v0.0.0

replace github.com/alejg/windows-operator/clients/go => $repo_root/clients/go
EOF

cat >"$tmpdir/main.go" <<'EOF'
package main

import (
	"bytes"
	"context"
	"fmt"
	"net/http"
	"os"
	"time"

	wo "github.com/alejg/windows-operator/clients/go"
)

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	baseURL := os.Getenv("WINDOWS_OPERATOR_BASE_URL")
	if baseURL == "" {
		baseURL = "http://127.0.0.1:43117"
	}

	client, err := wo.NewClientWithResponses(baseURL)
	must(err)

	health, err := client.GetHealthWithResponse(ctx)
	must(err)
	if health.StatusCode() != http.StatusOK || health.JSON200 == nil {
		fail("health failed: status=%d", health.StatusCode())
	}
	fmt.Printf("health status=%s runtime=%s\n", health.JSON200.Status, health.JSON200.RuntimeMode)

	capabilities, err := wo.CheckContractVersion(ctx, client, wo.SupportedContractVersion)
	must(err)
	fmt.Printf("capabilities contract=%s features=%d\n", capabilities.ContractVersion, len(capabilities.Features))

	missingRun, err := client.GetMailRunWithResponse(ctx, "__missing_external_smoke__")
	must(err)
	if missingRun.StatusCode() < 400 || missingRun.JSON4XX == nil || missingRun.JSON4XX.Code == nil {
		fail("negative error missing OperatorError: status=%d", missingRun.StatusCode())
	}
	category := ""
	if missingRun.JSON4XX.Category != nil {
		category = string(*missingRun.JSON4XX.Category)
	}
	retryable := false
	if missingRun.JSON4XX.Retryable != nil {
		retryable = *missingRun.JSON4XX.Retryable
	}
	fmt.Printf("negative code=%s category=%s retryable=%v\n", *missingRun.JSON4XX.Code, category, retryable)

	runID := os.Getenv("WINDOWS_OPERATOR_SMOKE_RUN_ID")
	if runID == "" {
		fmt.Println("artifact skipped: WINDOWS_OPERATOR_SMOKE_RUN_ID unset")
		return
	}

	artifacts, err := client.ListRunArtifactsWithResponse(ctx, runID)
	must(err)
	if artifacts.StatusCode() != http.StatusOK || artifacts.JSON200 == nil || len(artifacts.JSON200.Artifacts) == 0 {
		fail("artifact list failed: status=%d", artifacts.StatusCode())
	}

	var out bytes.Buffer
	artifact := artifacts.JSON200.Artifacts[0]
	must(wo.DownloadArtifact(ctx, client, artifact, &out))
	if out.Len() == 0 {
		fail("artifact download returned zero bytes")
	}
	fmt.Printf("artifact id=%s media=%s bytes=%d\n", artifact.ArtifactId, artifact.MediaType, out.Len())
}

func must(err error) {
	if err != nil {
		fail("%v", err)
	}
}

func fail(format string, args ...any) {
	fmt.Fprintf(os.Stderr, format+"\n", args...)
	os.Exit(1)
}
EOF

(
  cd "$tmpdir"
  go mod tidy
  WINDOWS_OPERATOR_BASE_URL="$base_url" \
  WINDOWS_OPERATOR_SMOKE_RUN_ID="$run_id" \
  go run .
)
