#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/windows-sync-repo.sh [--dry-run]

Environment:
  WINDOWS_OPERATOR_LOCAL_ENV           Optional shell env file. Default: .windows-operator.local.env
  WINDOWS_OPERATOR_EXCHANGE_ROOT       Linux artifact root. Default: /var/lib/windows-server/shared/operator-exchange
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT   Windows repo root. Default: C:\src\windows-operator
  WINDOWS_OPERATOR_WINDOWS_SYNC_ROOT   Windows sync staging root. Default: C:\ProgramData\WindowsOperator\sync\<run-id>
  WINDOWS_OPERATOR_SSH_USER            SSH user. Default: administrator
  WINDOWS_OPERATOR_SSH_HOST            SSH host. Default: 127.0.0.1
  WINDOWS_OPERATOR_SSH_TARGET          Full SSH target override. Default: $WINDOWS_OPERATOR_SSH_USER@$WINDOWS_OPERATOR_SSH_HOST
  WINDOWS_OPERATOR_SSH_PORT            SSH port. Default: 22555
  WINDOWS_OPERATOR_SSH_IDENTITY_FILE   SSH private key. Default: /run/secrets/ssh_automation_key when present
  WINDOWS_OPERATOR_SSH_TIMEOUT         SSH wait timeout seconds. Default: 120
  WINDOWS_OPERATOR_RUN_ID              Optional run id.
USAGE
}

die() {
  printf 'windows-sync-repo: %s\n' "$*" >&2
  exit 1
}

json_quote() {
  python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"
}

ps_quote() {
  local value=$1
  printf "'%s'" "${value//\'/\'\'}"
}

windows_path_for_scp() {
  local path=$1
  printf '%s' "${path//\\/\/}"
}

write_result() {
  local result_path=$1
  local run_id=$2
  local status=$3
  local exit_code=$4
  local message=$5

  python3 - "$result_path" "$run_id" "$status" "$exit_code" "$message" <<'PY'
import datetime
import json
import sys

path, run_id, status, exit_code, message = sys.argv[1:6]
payload = {
    "runId": run_id,
    "status": status,
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

[[ "$#" -eq 0 ]] || die "unexpected arguments: $*"

exchange_root="${WINDOWS_OPERATOR_EXCHANGE_ROOT:-/var/lib/windows-server/shared/operator-exchange}"
windows_repo_root="${WINDOWS_OPERATOR_WINDOWS_REPO_ROOT:-C:\\src\\windows-operator}"
ssh_user="${WINDOWS_OPERATOR_SSH_USER:-administrator}"
ssh_host="${WINDOWS_OPERATOR_SSH_HOST:-127.0.0.1}"
ssh_target="${WINDOWS_OPERATOR_SSH_TARGET:-${ssh_user}@${ssh_host}}"
ssh_port="${WINDOWS_OPERATOR_SSH_PORT:-22555}"
default_identity_file="/run/secrets/ssh_automation_key"
if [[ -n "${WINDOWS_OPERATOR_SSH_IDENTITY_FILE:-}" ]]; then
  ssh_identity_file="$WINDOWS_OPERATOR_SSH_IDENTITY_FILE"
elif [[ -e "$default_identity_file" ]]; then
  ssh_identity_file="$default_identity_file"
else
  ssh_identity_file=""
fi
ssh_timeout="${WINDOWS_OPERATOR_SSH_TIMEOUT:-120}"
run_id="${WINDOWS_OPERATOR_RUN_ID:-sync-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
run_root="$exchange_root/runs/$run_id"
windows_sync_root="${WINDOWS_OPERATOR_WINDOWS_SYNC_ROOT:-C:\\ProgramData\\WindowsOperator\\sync\\$run_id}"
windows_archive_path="${windows_sync_root}\\repo.tar.gz"

ssh_common_opts=(
  -o BatchMode=yes
  -o ConnectTimeout=3
  -o StrictHostKeyChecking=no
  -o UserKnownHostsFile=/dev/null
)
ssh_opts=(-p "$ssh_port" "${ssh_common_opts[@]}")
scp_opts=(-P "$ssh_port" "${ssh_common_opts[@]}")

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
archive_path="$run_root/repo.tar.gz"

tar \
  --exclude='.git' \
  --exclude='.work' \
  --exclude='*/bin' \
  --exclude='*/obj' \
  --exclude='*/node_modules' \
  --exclude='*/dist' \
  -czf "$archive_path" \
  -C "$repo_root" \
  .

archive_sha256="$(sha256sum "$archive_path" | awk '{print $1}')"
archive_bytes="$(wc -c <"$archive_path" | tr -d ' ')"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

cat > "$run_root/request.json" <<EOF
{
  "runId": $(json_quote "$run_id"),
  "createdAtUtc": $(json_quote "$created_at"),
  "repoRootLinux": $(json_quote "$repo_root"),
  "repoRootWindows": $(json_quote "$windows_repo_root"),
  "syncRootWindows": $(json_quote "$windows_sync_root"),
  "archivePathWindows": $(json_quote "$windows_archive_path"),
  "archiveSha256": $(json_quote "$archive_sha256"),
  "archiveBytes": $archive_bytes,
  "sshTarget": $(json_quote "$ssh_target"),
  "sshPort": $(json_quote "$ssh_port")
}
EOF

if [[ "$dry_run" -eq 1 ]]; then
  printf '%s\n' "$run_root/request.json"
  exit 0
fi

deadline=$((SECONDS + ssh_timeout))
until SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "echo ready" >"$run_root/ssh-probe.stdout.txt" 2>"$run_root/ssh-probe.stderr.txt"; do
  if (( SECONDS >= deadline )); then
    write_result "$run_root/result.json" "$run_id" "failed" 255 "Windows SSH unavailable after ${ssh_timeout}s. See ssh-probe.stderr.txt."
    printf '%s\n' "$run_root/result.json"
    exit 255
  fi
  sleep 3
done

setup_remote="powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path $(ps_quote "$windows_sync_root") | Out-Null; New-Item -ItemType Directory -Force -Path $(ps_quote "$windows_repo_root") | Out-Null\""
SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "$setup_remote" >"$run_root/setup.stdout.txt" 2>"$run_root/setup.stderr.txt" || {
  write_result "$run_root/result.json" "$run_id" "failed" 255 "Remote sync setup failed."
  printf '%s\n' "$run_root/result.json"
  exit 255
}

remote_archive_scp="$(windows_path_for_scp "$windows_archive_path")"
SSH_AUTH_SOCK= scp "${scp_opts[@]}" "$archive_path" "$ssh_target:$remote_archive_scp" >"$run_root/scp-upload.stdout.txt" 2>"$run_root/scp-upload.stderr.txt" || {
  write_result "$run_root/result.json" "$run_id" "failed" 255 "Archive upload failed."
  printf '%s\n' "$run_root/result.json"
  exit 255
}

extract_remote="powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"\$ErrorActionPreference = 'Stop'; tar.exe -xzf $(ps_quote "$windows_archive_path") -C $(ps_quote "$windows_repo_root"); if (\$LASTEXITCODE -ne 0) { throw 'tar extract failed.' }; Get-ChildItem -LiteralPath $(ps_quote "$windows_repo_root") -Force | Select-Object -First 5 | Out-String\""
SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "$extract_remote" >"$run_root/extract.stdout.txt" 2>"$run_root/extract.stderr.txt" || {
  write_result "$run_root/result.json" "$run_id" "failed" 1 "Remote archive extract failed."
  printf '%s\n' "$run_root/result.json"
  exit 1
}

write_result "$run_root/result.json" "$run_id" "succeeded" 0 "Repo synced."
printf '%s\n' "$run_root/result.json"
