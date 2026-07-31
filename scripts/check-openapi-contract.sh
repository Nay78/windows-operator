#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

contract_version="$(scripts/read-contract-version.sh)"
spec_path="$repo_root/openapi/windows-operator.openapi.json"
tmpdir="$(mktemp -d)"
tmp_spec="$tmpdir/windows-operator.openapi.json"
tmp_stable_spec="$tmpdir/windows-operator.stable.openapi.json"
tmp_go_root="$tmpdir/clients/go"
tmp_go_file="$tmp_go_root/windowsoperator.gen.go"
tmp_go_config="$tmpdir/go-client.oapi-codegen.yaml"

cleanup() {
  rm -rf "$tmpdir"
}
trap cleanup EXIT

fail() {
  echo "contract check failed: $*" >&2
  exit 1
}

run_optional_hook() {
  local label="$1"
  local command_text="$2"
  if [[ -z "$command_text" ]]; then
    echo "skip $label hook. set WINDOWS_OPERATOR_${label^^}_CMD to enable."
    return 0
  fi

  echo "run $label hook"
  WINDOWS_OPERATOR_OPENAPI_SPEC="$spec_path" \
  WINDOWS_OPERATOR_CONTRACT_VERSION="$contract_version" \
  bash -lc "$command_text"
}

dotnet run --project src/WindowsOperator.OpenApi/WindowsOperator.OpenApi.csproj -- "$tmp_spec" >/dev/null

grep -q "\"version\":\"$contract_version\"" "$tmp_spec" \
  || fail "generated openapi info.version != $contract_version"
grep -q "\"version\":\"$contract_version\"" "$spec_path" \
  || fail "committed openapi info.version != $contract_version"
grep -q "const SupportedContractVersion = \"$contract_version\"" clients/go/helpers.go \
  || fail "clients/go SupportedContractVersion != $contract_version"
cmp -s "$tmp_spec" "$spec_path" || fail "openapi/windows-operator.openapi.json stale. run scripts/generate-openapi.sh"

mkdir -p "$tmp_go_root"
python3 scripts/filter-openapi-surface.py "$tmp_spec" "$tmp_stable_spec" --surface stable
cp clients/go/*.go "$tmp_go_root/"
cp clients/go/go.mod clients/go/go.sum "$tmp_go_root/"
cat >"$tmp_go_config" <<EOF
package: windowsoperator
output: $tmp_go_file
generate:
  models: true
  client: true
output-options:
  skip-prune: true
EOF

go run github.com/oapi-codegen/oapi-codegen/v2/cmd/oapi-codegen@v2.5.0 \
  -config "$tmp_go_config" \
  "$tmp_stable_spec"
gofmt -w "$tmp_go_file" "$tmp_go_root/generate.go" "$tmp_go_root/windowsoperator_contract_test.go"
(cd "$tmp_go_root" && go mod tidy -go=1.22)

cmp -s "$tmp_go_file" clients/go/windowsoperator.gen.go \
  || fail "clients/go/windowsoperator.gen.go stale. run scripts/generate-go-client.sh"
cmp -s "$tmp_go_root/go.mod" clients/go/go.mod \
  || fail "clients/go/go.mod stale. run scripts/generate-go-client.sh"
cmp -s "$tmp_go_root/go.sum" clients/go/go.sum \
  || fail "clients/go/go.sum stale. run scripts/generate-go-client.sh"

python3 scripts/lint-openapi.py "$spec_path"
run_optional_hook "lint" "${WINDOWS_OPERATOR_LINT_CMD:-}"

latest_tag="$(git describe --tags --abbrev=0 2>/dev/null || true)"
baseline_spec="$repo_root/openapi/windows-operator.v1-baseline.json"
if [[ -f "$baseline_spec" ]]; then
  python3 scripts/check-openapi-breaking.py "$baseline_spec" "$spec_path"
elif [[ -n "${WINDOWS_OPERATOR_BREAKING_CMD:-}" ]]; then
  WINDOWS_OPERATOR_PREVIOUS_TAG="$latest_tag" run_optional_hook "breaking" "$WINDOWS_OPERATOR_BREAKING_CMD"
elif [[ -n "$latest_tag" ]]; then
  echo "skip breaking hook. latest tag: $latest_tag. set WINDOWS_OPERATOR_BREAKING_CMD to enable."
else
  echo "skip breaking hook. no release tag yet."
fi

echo "contract check passed. version $contract_version"
