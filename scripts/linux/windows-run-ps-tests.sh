#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
runner="$repo_root/scripts/linux/windows-run-ps.sh"
tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

run_ok() {
  WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
  WINDOWS_OPERATOR_RUN_ID="$1" \
  "$runner" --dry-run "$2" >/dev/null
}

run_fail() {
  local output
  set +e
  output="$(
    WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
    WINDOWS_OPERATOR_RUN_ID="$1" \
    "$runner" --dry-run "$2" 2>&1
  )"
  local code=$?
  set -e
  [[ "$code" -ne 0 ]] || {
    printf 'expected failure for %s\n' "$2" >&2
    return 1
  }
  [[ -n "$output" ]]
}

run_ok "valid" "scripts/windows/bootstrap-vm.ps1"
[[ -f "$tmp_root/exchange/runs/valid/command.ps1" ]]
[[ -f "$tmp_root/exchange/runs/valid/request.json" ]]
python3 - "$tmp_root/exchange/runs/valid/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    request = json.load(handle)
assert request["sourcePath"] == "scripts/windows/bootstrap-vm.ps1"
assert len(request["scriptSha256"]) == 64
assert request["sourcePathWindows"].endswith(r"scripts\windows\bootstrap-vm.ps1")
PY

run_fail "absolute" "$repo_root/scripts/windows/bootstrap-vm.ps1"
run_fail "parent" "scripts/windows/../windows/bootstrap-vm.ps1"
run_fail "wrong-extension" "scripts/windows/bootstrap-vm.txt"
run_fail "outside" "README.md"

printf 'windows-run-ps tests passed\n'
