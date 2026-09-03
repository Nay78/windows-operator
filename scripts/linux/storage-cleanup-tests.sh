#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
runner="$repo_root/scripts/linux/windows-run-ps.sh"
run_id="storage-cleanup-test-$(date -u +%Y%m%dT%H%M%SZ)"
windows_repo_root="${WINDOWS_OPERATOR_WINDOWS_REPO_ROOT:-C:\\src\\windows-operator}"

result_path="$(WINDOWS_OPERATOR_RUN_ID="$run_id" "$runner" "scripts/windows/test-storage-cleanup.ps1" \
  -InvokeScriptPath "${windows_repo_root}\\scripts\\windows\\invoke-storage-cleanup.ps1")"

[[ -f "$result_path" ]] || {
  printf 'storage-cleanup-tests: result missing: %s\n' "$result_path" >&2
  exit 1
}

printf '%s\n' "$result_path"
