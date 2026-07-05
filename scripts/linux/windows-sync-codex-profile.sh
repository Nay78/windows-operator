#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/windows-sync-codex-profile.sh [--dry-run]

Environment:
  WINDOWS_OPERATOR_LOCAL_ENV             Optional shell env file. Default: .windows-operator.local.env
  WINDOWS_OPERATOR_CODEX_HOME            Source Codex home. Default: $CODEX_HOME or ~/.config/codex
  WINDOWS_OPERATOR_WINDOWS_CODEX_HOME    Target Windows Codex home. Default: Windows user's ~/.codex
  WINDOWS_OPERATOR_RUN_TRANSPORT         shared or ssh-copy. Default: shared
  WINDOWS_OPERATOR_EXCHANGE_ROOT         Linux exchange root. Default: /var/lib/windows-server/shared/operator-exchange
  WINDOWS_OPERATOR_WINDOWS_EXCHANGE      Windows exchange root. Default depends on transport
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT     Windows repo root. Default: C:\src\windows-operator
  WINDOWS_OPERATOR_SSH_USER              SSH user. Default: administrator
  WINDOWS_OPERATOR_SSH_HOST              SSH host. Default: 127.0.0.1
  WINDOWS_OPERATOR_SSH_TARGET            Full SSH target override. Default: $WINDOWS_OPERATOR_SSH_USER@$WINDOWS_OPERATOR_SSH_HOST
  WINDOWS_OPERATOR_SSH_PORT              SSH port. Default: 22555
  WINDOWS_OPERATOR_SSH_IDENTITY_FILE     SSH private key. Default: /run/secrets/ssh_automation_key when present
  WINDOWS_OPERATOR_SSH_TIMEOUT           SSH wait timeout seconds passed to helper scripts. Default: 120
  WINDOWS_OPERATOR_RUN_ID                Optional run id.

This syncs durable Codex config, AGENTS/rules/subagents, and skill files. It
does not copy auth.json, history, sessions, caches, plugin runtimes, or other
mutable Codex state.
USAGE
}

die() {
  printf 'windows-sync-codex-profile: %s\n' "$*" >&2
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

source_codex_home="${WINDOWS_OPERATOR_CODEX_HOME:-${CODEX_HOME:-$HOME/.config/codex}}"
source_codex_home="$(realpath -m "$source_codex_home")"
[[ -d "$source_codex_home" ]] || die "source Codex home missing: $source_codex_home"
[[ -d "$source_codex_home/skills" ]] || die "source Codex skills missing: $source_codex_home/skills"
[[ -f "$source_codex_home/config.toml" ]] || die "source Codex config missing: $source_codex_home/config.toml"

exchange_root="${WINDOWS_OPERATOR_EXCHANGE_ROOT:-/var/lib/windows-server/shared/operator-exchange}"
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
run_id_base="${WINDOWS_OPERATOR_RUN_ID:-codex-profile-$(date -u +%Y%m%dT%H%M%SZ)-$$}"

codex_home_args=()
if [[ -n "${WINDOWS_OPERATOR_WINDOWS_CODEX_HOME:-}" ]]; then
  codex_home_args=("-CodexHome" "$WINDOWS_OPERATOR_WINDOWS_CODEX_HOME")
fi

sync_repo_cmd=("$repo_root/scripts/linux/windows-sync-repo.sh")
profile_cmd=(
  "$repo_root/scripts/linux/windows-run-ps.sh"
  "scripts/windows/configure-codex-profile.ps1"
  "${codex_home_args[@]}"
  "-ForceStaticProfile"
)
config_cmd=(
  "$repo_root/scripts/linux/windows-run-ps.sh"
  "scripts/windows/sync-codex-config.ps1"
  "${codex_home_args[@]}"
)
verify_cmd=(
  "$repo_root/scripts/linux/windows-run-ps.sh"
  "scripts/windows/verify-codex-profile.ps1"
  "${codex_home_args[@]}"
)

if [[ "$dry_run" -eq 1 ]]; then
  printf '[codex-sync] sourceCodexHome=%s\n' "$source_codex_home"
  printf '[codex-sync] would archive %s/skills\n' "$source_codex_home"

  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-repo" \
    WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
    "${sync_repo_cmd[@]}" --dry-run | sed 's/^/repoResult=/'

  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-static" \
    WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
    "${profile_cmd[@]:0:1}" --dry-run "${profile_cmd[@]:1}" | sed 's/^/staticProfileResult=/'

  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-config" \
    WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
    "${config_cmd[@]:0:1}" --dry-run "${config_cmd[@]:1}" | sed 's/^/configResult=/'

  printf '[codex-sync] dry-run: would upload skills archive through %s transport\n' "$run_transport"

  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-verify" \
    WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
    "${verify_cmd[@]:0:1}" --dry-run "${verify_cmd[@]:1}" | sed 's/^/verifyResult=/'
  exit 0
fi

sync_result="$(
  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-repo" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${sync_repo_cmd[@]}"
)"
printf 'repoResult=%s\n' "$sync_result"

static_profile_result="$(
  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-static" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${profile_cmd[@]}"
)"
printf 'staticProfileResult=%s\n' "$static_profile_result"

config_result="$(
  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-config" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${config_cmd[@]}"
)"
printf 'configResult=%s\n' "$config_result"

skills_run_id="${run_id_base}-skills"
run_root="$exchange_root/runs/$skills_run_id"
windows_run_root="${windows_exchange_root}\\runs\\${skills_run_id}"
windows_archive_path="${windows_run_root}\\codex-skills.tar.gz"
archive_path="$run_root/codex-skills.tar.gz"
mkdir -p "$run_root"

tar \
  --exclude='*/__pycache__' \
  --exclude='*.pyc' \
  -czf "$archive_path" \
  -C "$source_codex_home" \
  skills

archive_sha256="$(sha256sum "$archive_path" | awk '{print $1}')"
archive_bytes="$(wc -c <"$archive_path" | tr -d ' ')"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$run_root/request.json" <<EOF
{
  "runId": $(json_quote "$skills_run_id"),
  "createdAtUtc": $(json_quote "$created_at"),
  "sourceCodexHome": $(json_quote "$source_codex_home"),
  "archiveSha256": $(json_quote "$archive_sha256"),
  "archiveBytes": $archive_bytes,
  "runTransport": $(json_quote "$run_transport"),
  "archivePathWindows": $(json_quote "$windows_archive_path")
}
EOF

ssh_common_opts=(
  -o BatchMode=yes
  -o ConnectTimeout=3
  -o StrictHostKeyChecking=no
  -o UserKnownHostsFile=/dev/null
)
ssh_opts=(-p "$ssh_port" "${ssh_common_opts[@]}")
scp_opts=(-P "$ssh_port" "${ssh_common_opts[@]}")
if [[ -n "$ssh_identity_file" ]]; then
  [[ -r "$ssh_identity_file" ]] || die "SSH identity file unreadable: $ssh_identity_file"
  ssh_opts+=(
    -o IdentitiesOnly=yes
    -i "$ssh_identity_file"
  )
  scp_opts+=(
    -o IdentitiesOnly=yes
    -i "$ssh_identity_file"
  )
fi

if [[ "$run_transport" == "ssh-copy" ]]; then
  setup_remote="powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path $(ps_quote "$windows_run_root") | Out-Null\""
  SSH_AUTH_SOCK= ssh "${ssh_opts[@]}" "$ssh_target" "$setup_remote" >"$run_root/setup.stdout.txt" 2>"$run_root/setup.stderr.txt" || {
    die "remote skills archive setup failed; see $run_root/setup.stderr.txt"
  }

  remote_archive_scp="$(windows_path_for_scp "$windows_archive_path")"
  SSH_AUTH_SOCK= scp "${scp_opts[@]}" "$archive_path" "$ssh_target:$remote_archive_scp" >"$run_root/scp-upload.stdout.txt" 2>"$run_root/scp-upload.stderr.txt" || {
    die "skills archive upload failed; see $run_root/scp-upload.stderr.txt"
  }
fi

skills_cmd=(
  "$repo_root/scripts/linux/windows-run-ps.sh"
  "scripts/windows/sync-codex-skills.ps1"
  "${codex_home_args[@]}"
  "-SkillsArchivePath"
  "$windows_archive_path"
)
skills_result="$(
  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-skills-install" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${skills_cmd[@]}"
)"
printf 'skillsResult=%s\n' "$skills_result"

verify_result="$(
  WINDOWS_OPERATOR_RUN_ID="${run_id_base}-verify" \
  WINDOWS_OPERATOR_WINDOWS_REPO_ROOT="$windows_repo_root" \
  "${verify_cmd[@]}"
)"
printf 'verifyResult=%s\n' "$verify_result"
