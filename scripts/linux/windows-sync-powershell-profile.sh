#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/windows-sync-powershell-profile.sh [--dry-run]

Environment:
  WINDOWS_OPERATOR_LOCAL_ENV                       Optional shell env file. Default: .windows-operator.local.env
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT               Windows repo root. Default: C:\src\windows-operator
  WINDOWS_OPERATOR_POWERSHELL_PROFILE_TARGETS      Semicolon-separated targets. Default: WindowsPowerShell;PowerShell
  WINDOWS_OPERATOR_POWERSHELL_PROFILE_SOURCE       Repo-relative profile source. Default: profiles\powershell\profile.ps1

The script syncs this repo to the configured Windows computer, then installs a
managed block in the Windows user PowerShell profile that dot-sources the
repo-owned profile.
USAGE
}

die() {
  printf 'windows-sync-powershell-profile: %s\n' "$*" >&2
  exit 1
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
dry_run=0

local_env="${WINDOWS_OPERATOR_LOCAL_ENV:-$repo_root/.windows-operator.local.env}"
if [[ -f "$local_env" ]]; then
  # shellcheck source=/dev/null
  source "$local_env"
fi

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

if [[ "${1:-}" == "--dry-run" ]]; then
  dry_run=1
  shift
fi

[[ "$#" -eq 0 ]] || die "unexpected arguments: $*"

windows_repo_root="${WINDOWS_OPERATOR_WINDOWS_REPO_ROOT:-C:\\src\\windows-operator}"
profile_targets="${WINDOWS_OPERATOR_POWERSHELL_PROFILE_TARGETS:-WindowsPowerShell;PowerShell}"
profile_source="${WINDOWS_OPERATOR_POWERSHELL_PROFILE_SOURCE:-profiles\\powershell\\profile.ps1}"
run_id_base="${WINDOWS_OPERATOR_RUN_ID:-ps-profile-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
sync_run_id="${run_id_base}-sync"
install_run_id="${run_id_base}-install"

sync_cmd=("$repo_root/scripts/linux/windows-sync-repo.sh")
install_cmd=(
  "$repo_root/scripts/linux/windows-run-ps.sh"
  "scripts/windows/configure-powershell-profile.ps1"
  "-RepoRoot"
  "$windows_repo_root"
  "-ProfileTargetsText"
  "$profile_targets"
  "-ProfileSourceRelativePath"
  "$profile_source"
)

if [[ "$dry_run" -eq 1 ]]; then
  sync_cmd+=("--dry-run")
  install_cmd=("${install_cmd[@]:0:1}" "--dry-run" "${install_cmd[@]:1}")
fi

sync_result="$(
  WINDOWS_OPERATOR_RUN_ID="$sync_run_id" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${sync_cmd[@]}"
)"

install_result="$(
  WINDOWS_OPERATOR_RUN_ID="$install_run_id" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${install_cmd[@]}"
)"

printf 'syncResult=%s\n' "$sync_result"
printf 'profileResult=%s\n' "$install_result"
