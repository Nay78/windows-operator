#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/windows-run-ps.sh [--dry-run] scripts/windows/script.ps1 [args...]

Environment:
  WINDOWS_OPERATOR_LOCAL_ENV           Optional shell env file. Default: .windows-operator.local.env
  WINDOWS_OPERATOR_EXCHANGE_ROOT       Linux exchange root. Default: /var/lib/windows-server/shared/operator-exchange
  WINDOWS_OPERATOR_WINDOWS_EXCHANGE    Windows exchange root. Default: Z:\operator-exchange
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT   Windows repo root. Default: Z:\windows-operator
  WINDOWS_OPERATOR_RUN_TRANSPORT       shared or ssh-copy. Default: shared
  WINDOWS_OPERATOR_SSH_USER            SSH user. Default: administrator
  WINDOWS_OPERATOR_SSH_HOST            SSH host. Default: 127.0.0.1
  WINDOWS_OPERATOR_SSH_TARGET          Full SSH target override. Default: $WINDOWS_OPERATOR_SSH_USER@$WINDOWS_OPERATOR_SSH_HOST
  WINDOWS_OPERATOR_SSH_PORT            SSH port override. Default: SSH config for an explicit target; otherwise 22555
  WINDOWS_OPERATOR_SSH_IDENTITY_FILE   SSH private key. Default: /run/secrets/ssh_automation_key when present
  WINDOWS_OPERATOR_SSH_TIMEOUT         SSH wait timeout seconds. Default: 120
  WINDOWS_OPERATOR_RUN_ID              Optional run id.
USAGE
}

die() {
  printf 'windows-run-ps: %s\n' "$*" >&2
  exit 1
}

json_quote() {
  python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"
}

json_array() {
  python3 - "$@" <<'PY'
import json
import sys
print(json.dumps(sys.argv[1:]))
PY
}

ps_quote() {
  local value=$1
  printf "'%s'" "${value//\'/\'\'}"
}

windows_path_for_scp() {
  local path=$1
  printf '%s' "${path//\\/\/}"
}

write_failure_result() {
  local result_path=$1
  local run_id=$2
  local exit_code=$3
  local message=$4

  python3 - "$result_path" "$run_id" "$exit_code" "$message" <<'PY'
import datetime
import json
import sys

path, run_id, exit_code, message = sys.argv[1:5]
payload = {
    "runId": run_id,
    "status": "failed",
    "exitCode": int(exit_code),
    "message": message,
    "completedAtUtc": datetime.datetime.now(datetime.UTC).isoformat().replace("+00:00", "Z"),
}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, indent=2)
    handle.write("\n")
PY
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

script_rel="${1:-}"
[[ -n "$script_rel" ]] || { usage >&2; exit 2; }
shift

[[ "$script_rel" != /* ]] || die "script path must be repo-relative"
[[ "$script_rel" == *.ps1 ]] || die "script path must end with .ps1"
[[ "$script_rel" == scripts/windows/* ]] || die "script path must be under scripts/windows"
[[ "$script_rel" != *..* ]] || die "script path must not contain .."

script_abs="$repo_root/$script_rel"
[[ -f "$script_abs" ]] || die "script missing: $script_rel"

script_real="$(realpath "$script_abs")"
allowed_root="$(realpath "$repo_root/scripts/windows")"
case "$script_real" in
  "$allowed_root"/*) ;;
  *) die "script resolves outside scripts/windows: $script_rel" ;;
esac

exchange_root="${WINDOWS_OPERATOR_EXCHANGE_ROOT:-/var/lib/windows-server/shared/operator-exchange}"
windows_repo_root="${WINDOWS_OPERATOR_WINDOWS_REPO_ROOT:-Z:\\windows-operator}"
run_transport="${WINDOWS_OPERATOR_RUN_TRANSPORT:-shared}"
case "$run_transport" in
  shared)
    windows_exchange_root="${WINDOWS_OPERATOR_WINDOWS_EXCHANGE:-Z:\\operator-exchange}"
    ;;
  ssh-copy)
    windows_exchange_root="${WINDOWS_OPERATOR_WINDOWS_EXCHANGE:-C:\\ProgramData\\WindowsOperator\\exchange}"
    ;;
  *)
    die "WINDOWS_OPERATOR_RUN_TRANSPORT must be shared or ssh-copy"
    ;;
esac
ssh_user="${WINDOWS_OPERATOR_SSH_USER:-administrator}"
ssh_host="${WINDOWS_OPERATOR_SSH_HOST:-127.0.0.1}"
ssh_target="${WINDOWS_OPERATOR_SSH_TARGET:-${ssh_user}@${ssh_host}}"
if [[ -n "${WINDOWS_OPERATOR_SSH_TARGET:-}" ]]; then
  ssh_port="${WINDOWS_OPERATOR_SSH_PORT:-}"
else
  ssh_port="${WINDOWS_OPERATOR_SSH_PORT:-22555}"
fi
default_identity_file="/run/secrets/ssh_automation_key"
if [[ -n "${WINDOWS_OPERATOR_SSH_IDENTITY_FILE:-}" ]]; then
  ssh_identity_file="$WINDOWS_OPERATOR_SSH_IDENTITY_FILE"
elif [[ -e "$default_identity_file" ]]; then
  ssh_identity_file="$default_identity_file"
else
  ssh_identity_file=""
fi
ssh_timeout="${WINDOWS_OPERATOR_SSH_TIMEOUT:-120}"
run_id="${WINDOWS_OPERATOR_RUN_ID:-run-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
run_root="$exchange_root/runs/$run_id"

ssh_common_opts=(
  -o BatchMode=yes
  -o ConnectTimeout=3
  -o StrictHostKeyChecking=no
  -o UserKnownHostsFile=/dev/null
)
ssh_opts=("${ssh_common_opts[@]}")
scp_opts=("${ssh_common_opts[@]}")
if [[ -n "$ssh_port" ]]; then
  ssh_opts=(-p "$ssh_port" "${ssh_opts[@]}")
  scp_opts=(-P "$ssh_port" "${scp_opts[@]}")
fi

if [[ -n "$ssh_identity_file" ]]; then
  [[ "$dry_run" -eq 1 || -r "$ssh_identity_file" ]] || die "SSH identity file unreadable: $ssh_identity_file"
  ssh_opts+=(
    -o IdentitiesOnly=yes
    -i "$ssh_identity_file"
  )
  scp_opts+=(
    -o IdentitiesOnly=yes
    -i "$ssh_identity_file"
  )
fi

mkdir -p "$run_root"
cp "$script_real" "$run_root/command.ps1"

script_sha256="$(sha256sum "$script_real" | awk '{print $1}')"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
windows_run_root="${windows_exchange_root}\\runs\\${run_id}"
source_windows="${windows_repo_root}\\${script_rel//\//\\}"

cat > "$run_root/request.json" <<EOF
{
  "runId": $(json_quote "$run_id"),
  "createdAtUtc": $(json_quote "$created_at"),
  "repoRootLinux": $(json_quote "$repo_root"),
  "repoRootWindows": $(json_quote "$windows_repo_root"),
  "exchangeRootLinux": $(json_quote "$exchange_root"),
  "exchangeRootWindows": $(json_quote "$windows_exchange_root"),
  "runRootWindows": $(json_quote "$windows_run_root"),
  "runTransport": $(json_quote "$run_transport"),
  "sourcePath": $(json_quote "$script_rel"),
  "sourcePathWindows": $(json_quote "$source_windows"),
  "scriptSha256": $(json_quote "$script_sha256"),
  "arguments": $(json_array "$@")
}
EOF

if [[ "$dry_run" -eq 1 ]]; then
  printf '%s\n' "$run_root/request.json"
  exit 0
fi

executor="${windows_repo_root}\\scripts\\windows\\run-staged-repo-script.ps1"
remote_command="powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"$executor\" -RunRoot \"$windows_run_root\" -RepoRoot \"$windows_repo_root\""

deadline=$((SECONDS + ssh_timeout))
until SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "echo ready" >"$run_root/ssh-probe.stdout.txt" 2>"$run_root/ssh-probe.stderr.txt"; do
  if (( SECONDS >= deadline )); then
    write_failure_result "$run_root/result.json" "$run_id" 255 "Windows SSH unavailable after ${ssh_timeout}s. See ssh-probe.stderr.txt."
    printf '%s\n' "$run_root/result.json"
    exit 255
  fi
  sleep 3
done

if [[ "$run_transport" == "ssh-copy" ]]; then
  remote_run_root_scp="$(windows_path_for_scp "$windows_run_root")"
  create_remote_run_root="powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path $(ps_quote "$windows_run_root") | Out-Null\""
  SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "$create_remote_run_root" >"$run_root/ssh-copy-setup.stdout.txt" 2>"$run_root/ssh-copy-setup.stderr.txt" || {
    write_failure_result "$run_root/result.json" "$run_id" 255 "Remote run root setup failed."
    printf '%s\n' "$run_root/result.json"
    exit 255
  }

  SSH_AUTH_SOCK= scp "${scp_opts[@]}" "$run_root/request.json" "$run_root/command.ps1" "$ssh_target:$remote_run_root_scp/" >"$run_root/scp-upload.stdout.txt" 2>"$run_root/scp-upload.stderr.txt" || {
    write_failure_result "$run_root/result.json" "$run_id" 255 "Remote run upload failed."
    printf '%s\n' "$run_root/result.json"
    exit 255
  }
fi

set +e
SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "$remote_command" >"$run_root/stdout.txt" 2>"$run_root/stderr.txt"
exit_code=$?
set -e

if [[ "$run_transport" == "ssh-copy" ]]; then
  remote_run_root_scp="$(windows_path_for_scp "$windows_run_root")"
  SSH_AUTH_SOCK= scp "${scp_opts[@]}" "$ssh_target:$remote_run_root_scp/result.json" "$run_root/result.json.remote" >"$run_root/scp-result.stdout.txt" 2>"$run_root/scp-result.stderr.txt" || true
  if [[ -f "$run_root/result.json.remote" ]]; then
    mv "$run_root/result.json.remote" "$run_root/result.json"
  fi

  SSH_AUTH_SOCK= scp "${scp_opts[@]}" "$ssh_target:$remote_run_root_scp/transcript.txt" "$run_root/transcript.txt" >"$run_root/scp-transcript.stdout.txt" 2>"$run_root/scp-transcript.stderr.txt" || true
fi

if [[ "$exit_code" -ne 0 && ! -f "$run_root/result.json" ]]; then
  write_failure_result "$run_root/result.json" "$run_id" "$exit_code" "SSH or remote executor failed before writing result.json."
fi

printf '%s\n' "$run_root/result.json"
exit "$exit_code"
