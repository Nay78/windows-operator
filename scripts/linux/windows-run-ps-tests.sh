#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
runner="$repo_root/scripts/linux/windows-run-ps.sh"
syncer="$repo_root/scripts/linux/windows-sync-repo.sh"
profile_syncer="$repo_root/scripts/linux/windows-sync-powershell-profile.sh"
available_syncer="$repo_root/scripts/linux/windows-sync-available.sh"
tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

run_ok() {
  local run_id=$1
  local script_path=$2
  shift 2

  WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
  WINDOWS_OPERATOR_RUN_ID="$run_id" \
  "$runner" --dry-run "$script_path" "$@" >/dev/null
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

run_ok "with-args" "scripts/windows/bootstrap-vm.ps1" "-RepoRoot" "Z:\\windows-operator" "value with spaces"
python3 - "$tmp_root/exchange/runs/with-args/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    request = json.load(handle)
assert request["arguments"] == ["-RepoRoot", r"Z:\windows-operator", "value with spaces"]
PY

run_ok "probe-url" "scripts/windows/probe-url.ps1" "-Url" "https://localhost:3003/taskpane.html" "-RequiredText" "Windows Operator PowerPoint"
python3 - "$tmp_root/exchange/runs/probe-url/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    request = json.load(handle)
assert request["sourcePath"] == "scripts/windows/probe-url.ps1"
assert request["arguments"] == [
    "-Url",
    "https://localhost:3003/taskpane.html",
    "-RequiredText",
    "Windows Operator PowerPoint",
]
PY

WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
WINDOWS_OPERATOR_RUN_ID="ssh-copy" \
WINDOWS_OPERATOR_RUN_TRANSPORT="ssh-copy" \
WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="C:\\src\\windows-operator" \
"$runner" --dry-run "scripts/windows/probe-url.ps1" "-Url" "http://127.0.0.1:43117/v1/health" >/dev/null
python3 - "$tmp_root/exchange/runs/ssh-copy/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    request = json.load(handle)
assert request["runTransport"] == "ssh-copy"
assert request["repoRootWindows"] == r"C:\src\windows-operator"
assert request["exchangeRootWindows"] == r"C:\ProgramData\WindowsOperator\exchange"
assert request["runRootWindows"] == r"C:\ProgramData\WindowsOperator\exchange\runs\ssh-copy"
PY

WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
WINDOWS_OPERATOR_RUN_ID="sync-dry-run" \
WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="C:\\src\\windows-operator" \
"$syncer" --dry-run >/dev/null
python3 - "$tmp_root/exchange/runs/sync-dry-run/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    request = json.load(handle)
assert request["repoRootWindows"] == r"C:\src\windows-operator"
assert request["syncRootWindows"] == r"C:\ProgramData\WindowsOperator\sync\sync-dry-run"
assert request["archivePathWindows"] == r"C:\ProgramData\WindowsOperator\sync\sync-dry-run\repo.tar.gz"
assert request["archiveSha256"]
assert request["archiveBytes"] > 0
PY

WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
WINDOWS_OPERATOR_RUN_ID="profile-sync" \
WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="C:\\src\\windows-operator" \
WINDOWS_OPERATOR_RUN_TRANSPORT="ssh-copy" \
"$profile_syncer" --dry-run >/dev/null
python3 - "$tmp_root/exchange/runs/profile-sync-sync/request.json" "$tmp_root/exchange/runs/profile-sync-install/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    sync_request = json.load(handle)
with open(sys.argv[2], encoding="utf-8") as handle:
    install_request = json.load(handle)
assert sync_request["repoRootWindows"] == r"C:\src\windows-operator"
assert install_request["sourcePath"] == "scripts/windows/configure-powershell-profile.ps1"
assert install_request["runTransport"] == "ssh-copy"
assert install_request["arguments"] == [
    "-RepoRoot",
    r"C:\src\windows-operator",
    "-ProfileTargetsText",
    "WindowsPowerShell;PowerShell",
    "-ProfileSourceRelativePath",
    r"profiles\powershell\profile.ps1",
]
PY

WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
WINDOWS_OPERATOR_SYNC_TARGETS="tester@win-a:2222 win-b" \
WINDOWS_OPERATOR_SSH_USER="fallback" \
WINDOWS_OPERATOR_SSH_PORT="22" \
"$available_syncer" --dry-run >"$tmp_root/available-sync.txt"
grep -q "target=win-a port=2222 user=tester" "$tmp_root/available-sync.txt"
grep -q "target=win-b port=22 user=fallback" "$tmp_root/available-sync.txt"
grep -q "sync repo + profile" "$tmp_root/available-sync.txt"

run_fail "absolute" "$repo_root/scripts/windows/bootstrap-vm.ps1"
run_fail "parent" "scripts/windows/../windows/bootstrap-vm.ps1"
run_fail "wrong-extension" "scripts/windows/bootstrap-vm.txt"
run_fail "outside" "README.md"

printf 'windows-run-ps tests passed\n'
