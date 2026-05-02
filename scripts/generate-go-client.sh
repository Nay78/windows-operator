#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

scripts/generate-openapi.sh >/dev/null
go run github.com/oapi-codegen/oapi-codegen/v2/cmd/oapi-codegen@v2.5.0 \
  -config openapi/go-client.oapi-codegen.yaml \
  openapi/windows-operator.openapi.json
gofmt -w clients/go/windowsoperator.gen.go clients/go/generate.go
(cd clients/go && go mod tidy -go=1.22)
