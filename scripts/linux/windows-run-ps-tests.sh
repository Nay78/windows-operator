#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
runner="$repo_root/scripts/linux/windows-run-ps.sh"
syncer="$repo_root/scripts/linux/windows-sync-repo.sh"
profile_syncer="$repo_root/scripts/linux/windows-sync-powershell-profile.sh"
codex_syncer="$repo_root/scripts/linux/windows-sync-codex-profile.sh"
available_syncer="$repo_root/scripts/linux/windows-sync-available.sh"
tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

"$available_syncer" --help >"$tmp_root/available-help.txt"
grep -q "WINDOWS_OPERATOR_SSH_TARGET" "$tmp_root/available-help.txt"
grep -q "WINDOWS_OPERATOR_SSH_IDENTITY_FILE" "$tmp_root/available-help.txt"

"$codex_syncer" --help >"$tmp_root/codex-help.txt"
grep -q "WINDOWS_OPERATOR_SSH_TIMEOUT" "$tmp_root/codex-help.txt"

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

capture_fail() {
  set +e
  local output
  output="$("$@" 2>&1)"
  local code=$?
  set -e
  [[ "$code" -ne 0 ]] || {
    printf 'expected failure: %s\n' "$*" >&2
    return 1
  }
  printf '%s' "$output"
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

codex_home="$tmp_root/codex-home"
mkdir -p "$codex_home/skills/example"
printf 'model = "gpt-5"\n' >"$codex_home/config.toml"
printf '%s\n' '# Example Skill' >"$codex_home/skills/example/SKILL.md"

WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
WINDOWS_OPERATOR_CODEX_HOME="$codex_home" \
WINDOWS_OPERATOR_EXCHANGE_ROOT="$tmp_root/exchange" \
WINDOWS_OPERATOR_RUN_ID="codex-profile" \
WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="C:\\src\\windows-operator" \
WINDOWS_OPERATOR_WINDOWS_CODEX_HOME="C:\\Users\\tester\\.codex" \
WINDOWS_OPERATOR_RUN_TRANSPORT="ssh-copy" \
"$codex_syncer" --dry-run >"$tmp_root/codex-sync.txt"
grep -q "\\[codex-sync\\] sourceCodexHome=$codex_home" "$tmp_root/codex-sync.txt"
grep -q "repoResult=$tmp_root/exchange/runs/codex-profile-repo/request.json" "$tmp_root/codex-sync.txt"
grep -q "staticProfileResult=$tmp_root/exchange/runs/codex-profile-static/request.json" "$tmp_root/codex-sync.txt"
grep -q "configResult=$tmp_root/exchange/runs/codex-profile-config/request.json" "$tmp_root/codex-sync.txt"
grep -q "verifyResult=$tmp_root/exchange/runs/codex-profile-verify/request.json" "$tmp_root/codex-sync.txt"
grep -q "\\[codex-sync\\] dry-run: would upload skills archive through ssh-copy transport" "$tmp_root/codex-sync.txt"
python3 - \
  "$tmp_root/exchange/runs/codex-profile-repo/request.json" \
  "$tmp_root/exchange/runs/codex-profile-static/request.json" \
  "$tmp_root/exchange/runs/codex-profile-config/request.json" \
  "$tmp_root/exchange/runs/codex-profile-verify/request.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    repo_request = json.load(handle)
with open(sys.argv[2], encoding="utf-8") as handle:
    static_request = json.load(handle)
with open(sys.argv[3], encoding="utf-8") as handle:
    config_request = json.load(handle)
with open(sys.argv[4], encoding="utf-8") as handle:
    verify_request = json.load(handle)

assert repo_request["repoRootWindows"] == r"C:\src\windows-operator"
assert static_request["sourcePath"] == "scripts/windows/configure-codex-profile.ps1"
assert static_request["arguments"] == [
    "-CodexHome",
    r"C:\Users\tester\.codex",
    "-ForceStaticProfile",
]
assert config_request["sourcePath"] == "scripts/windows/sync-codex-config.ps1"
assert config_request["arguments"] == ["-CodexHome", r"C:\Users\tester\.codex"]
assert verify_request["sourcePath"] == "scripts/windows/verify-codex-profile.ps1"
assert verify_request["arguments"] == ["-CodexHome", r"C:\Users\tester\.codex"]
PY

mkdir -p "$tmp_root/codex-home-no-skills"
printf 'model = "gpt-5"\n' >"$tmp_root/codex-home-no-skills/config.toml"
codex_missing_home_output="$(
  capture_fail env \
    WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
    WINDOWS_OPERATOR_CODEX_HOME="$tmp_root/codex-home-missing" \
    "$codex_syncer" --dry-run
)"
grep -q "source Codex home missing" <<<"$codex_missing_home_output"

codex_missing_skills_output="$(
  capture_fail env \
    WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
    WINDOWS_OPERATOR_CODEX_HOME="$tmp_root/codex-home-no-skills" \
    "$codex_syncer" --dry-run
)"
grep -q "source Codex skills missing" <<<"$codex_missing_skills_output"

WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
WINDOWS_OPERATOR_SYNC_TARGETS="tester@win-a:2222 win-b" \
WINDOWS_OPERATOR_SSH_USER="fallback" \
WINDOWS_OPERATOR_SSH_PORT="22" \
"$available_syncer" --dry-run >"$tmp_root/available-sync.txt"
grep -q "target=win-a port=2222 user=tester" "$tmp_root/available-sync.txt"
grep -q "target=win-b port=22 user=fallback" "$tmp_root/available-sync.txt"
grep -q "sync repo + profile + codex" "$tmp_root/available-sync.txt"

WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
WINDOWS_OPERATOR_SYNC_TARGETS="tester@win-a:2222" \
WINDOWS_OPERATOR_RUN_ID="sync-minimal" \
WINDOWS_OPERATOR_SYNC_PROFILE="0" \
WINDOWS_OPERATOR_SYNC_CODEX="0" \
"$available_syncer" --dry-run >"$tmp_root/available-sync-minimal.txt"
grep -q "sync repo run-id-base=sync-minimal target-label=tester-win-a-2222" "$tmp_root/available-sync-minimal.txt"
! grep -q "+ profile" "$tmp_root/available-sync-minimal.txt"
! grep -q "+ codex" "$tmp_root/available-sync-minimal.txt"

WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
WINDOWS_OPERATOR_SYNC_TARGETS="alice@win-a:2222 bob@win-a:2223" \
WINDOWS_OPERATOR_RUN_ID="sync-label-test" \
WINDOWS_OPERATOR_SYNC_PROFILE="0" \
WINDOWS_OPERATOR_SYNC_CODEX="1" \
"$available_syncer" --dry-run >"$tmp_root/available-sync-labels.txt"
grep -q "run-id-base=sync-label-test target-label=alice-win-a-2222" "$tmp_root/available-sync-labels.txt"
grep -q "run-id-base=sync-label-test target-label=bob-win-a-2223" "$tmp_root/available-sync-labels.txt"

invalid_port_output="$(
  capture_fail env \
    WINDOWS_OPERATOR_LOCAL_ENV="/dev/null" \
    WINDOWS_OPERATOR_SYNC_TARGETS="tester@win-a:notaport" \
    "$available_syncer" --dry-run
)"
grep -q "target port invalid for spec: tester@win-a:notaport" <<<"$invalid_port_output"

run_fail "absolute" "$repo_root/scripts/windows/bootstrap-vm.ps1"
run_fail "parent" "scripts/windows/../windows/bootstrap-vm.ps1"
run_fail "wrong-extension" "scripts/windows/bootstrap-vm.txt"
run_fail "outside" "README.md"

printf 'windows-run-ps tests passed\n'
