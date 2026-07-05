#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/windows-sync-available.sh [--dry-run]

Environment:
  WINDOWS_OPERATOR_LOCAL_ENV        Optional shell env file. Default: .windows-operator.local.env
  WINDOWS_OPERATOR_SYNC_TARGETS     Optional whitespace-separated targets: host, user@host, or user@host:port.
  WINDOWS_OPERATOR_SSH_USER         Default SSH user when a target omits user. Default: administrator.
  WINDOWS_OPERATOR_SSH_HOST         Default SSH host when no targets are configured. Default: 127.0.0.1.
  WINDOWS_OPERATOR_SSH_TARGET       Full SSH target fallback when WINDOWS_OPERATOR_SYNC_TARGETS is empty.
  WINDOWS_OPERATOR_SSH_PORT         Default SSH port when a target omits port. Default: 22555.
  WINDOWS_OPERATOR_SSH_IDENTITY_FILE SSH private key. Default: /run/secrets/ssh_automation_key when present.
  WINDOWS_OPERATOR_SSH_TIMEOUT      Per-target probe timeout seconds. Default: 5.
  WINDOWS_OPERATOR_RUN_ID           Optional run id base.
  WINDOWS_OPERATOR_SYNC_PROFILE     Sync PowerShell profile after repo sync. Default: 1.
  WINDOWS_OPERATOR_SYNC_CODEX       Sync Codex config, agents, rules, and skills. Default: 1.

The script probes configured SSH targets. Offline targets are skipped. Reachable
targets receive the repo sync and, by default, the repo-owned PowerShell profile
plus durable Codex config and skills.
USAGE
}

die() {
  printf 'windows-sync-available: %s\n' "$*" >&2
  exit 1
}

sanitize_run_id_part() {
  local value=$1
  value="${value//@/-}"
  value="${value//:/-}"
  value="${value//[^A-Za-z0-9_.-]/-}"
  printf '%s' "$value"
}

resolve_target() {
  local spec=$1
  local default_user=$2
  local default_port=$3
  local target_user=$default_user
  local host_port=$spec
  local target_host
  local target_port=$default_port

  if [[ "$host_port" == *@* ]]; then
    target_user="${host_port%@*}"
    host_port="${host_port#*@}"
  fi

  if [[ "$host_port" == *:* ]]; then
    target_host="${host_port%:*}"
    target_port="${host_port##*:}"
  else
    target_host="$host_port"
  fi

  [[ -n "$target_user" ]] || die "target user missing for spec: $spec"
  [[ -n "$target_host" ]] || die "target host missing for spec: $spec"
  [[ -n "$target_port" ]] || die "target port missing for spec: $spec"
  [[ "$target_port" =~ ^[0-9]+$ ]] || die "target port invalid for spec: $spec"

  RESOLVED_TARGET_USER=$target_user
  RESOLVED_TARGET_HOST=$target_host
  RESOLVED_TARGET_PORT=$target_port
  RESOLVED_TARGET="${target_user}@${target_host}"
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

ssh_user="${WINDOWS_OPERATOR_SSH_USER:-administrator}"
ssh_host="${WINDOWS_OPERATOR_SSH_HOST:-127.0.0.1}"
ssh_port="${WINDOWS_OPERATOR_SSH_PORT:-22555}"
default_identity_file="/run/secrets/ssh_automation_key"
if [[ -n "${WINDOWS_OPERATOR_SSH_IDENTITY_FILE:-}" ]]; then
  ssh_identity_file="$WINDOWS_OPERATOR_SSH_IDENTITY_FILE"
elif [[ -e "$default_identity_file" ]]; then
  ssh_identity_file="$default_identity_file"
else
  ssh_identity_file=""
fi
ssh_timeout="${WINDOWS_OPERATOR_SSH_TIMEOUT:-5}"
sync_profile="${WINDOWS_OPERATOR_SYNC_PROFILE:-1}"
sync_codex="${WINDOWS_OPERATOR_SYNC_CODEX:-1}"
run_id_base="${WINDOWS_OPERATOR_RUN_ID:-sync-available-$(date -u +%Y%m%dT%H%M%SZ)-$$}"

if [[ -n "${WINDOWS_OPERATOR_SYNC_TARGETS:-}" ]]; then
  read -r -a target_specs <<<"$WINDOWS_OPERATOR_SYNC_TARGETS"
elif [[ -n "${WINDOWS_OPERATOR_SSH_TARGET:-}" ]]; then
  target_specs=("$WINDOWS_OPERATOR_SSH_TARGET")
else
  target_specs=("${ssh_user}@${ssh_host}:${ssh_port}")
fi

ssh_common_opts=(
  -o BatchMode=yes
  -o ConnectTimeout=3
  -o StrictHostKeyChecking=no
  -o UserKnownHostsFile=/dev/null
)
if [[ -n "$ssh_identity_file" ]]; then
  [[ "$dry_run" -eq 1 || -r "$ssh_identity_file" ]] || die "SSH identity file unreadable: $ssh_identity_file"
  ssh_common_opts+=(
    -o IdentitiesOnly=yes
    -i "$ssh_identity_file"
  )
fi

available=0
failed=0

for spec in "${target_specs[@]}"; do
  resolve_target "$spec" "$ssh_user" "$ssh_port"
  target_user=$RESOLVED_TARGET_USER
  target_host=$RESOLVED_TARGET_HOST
  target_port=$RESOLVED_TARGET_PORT
  target=$RESOLVED_TARGET
  target_label="$(sanitize_run_id_part "${target_user}@${target_host}:${target_port}")"
  printf '[sync] target=%s port=%s user=%s\n' "$target_host" "$target_port" "$target_user"

  if [[ "$dry_run" -eq 1 ]]; then
    printf '[sync] dry-run: would probe %s:%s and sync repo%s%s run-id-base=%s target-label=%s\n' \
      "$target_host" \
      "$target_port" \
      "$(if [[ "$sync_profile" != "0" ]]; then printf ' + profile'; fi)" \
      "$(if [[ "$sync_codex" != "0" ]]; then printf ' + codex'; fi)" \
      "$run_id_base" \
      "$target_label"
    continue
  fi

  if ! SSH_AUTH_SOCK= timeout "$ssh_timeout" ssh -p "$target_port" "${ssh_common_opts[@]}" "$target" "echo ready" >/dev/null 2>&1; then
    printf '[sync] skipped offline target=%s\n' "$target_host" >&2
    continue
  fi

  available=$((available + 1))
  target_failed=0

  if [[ "$sync_profile" != "0" ]]; then
    if ! WINDOWS_OPERATOR_RUN_ID="${run_id_base}-${target_label}" \
      WINDOWS_OPERATOR_SSH_USER="$target_user" \
      WINDOWS_OPERATOR_SSH_HOST="$target_host" \
      WINDOWS_OPERATOR_SSH_TARGET="$target" \
      WINDOWS_OPERATOR_SSH_PORT="$target_port" \
      "$repo_root/scripts/linux/windows-sync-powershell-profile.sh"; then
      printf '[sync] failed target=%s\n' "$target_host" >&2
      failed=$((failed + 1))
      target_failed=1
    fi
  else
    if ! WINDOWS_OPERATOR_RUN_ID="${run_id_base}-${target_label}-repo" \
      WINDOWS_OPERATOR_SSH_USER="$target_user" \
      WINDOWS_OPERATOR_SSH_HOST="$target_host" \
      WINDOWS_OPERATOR_SSH_TARGET="$target" \
      WINDOWS_OPERATOR_SSH_PORT="$target_port" \
      "$repo_root/scripts/linux/windows-sync-repo.sh"; then
      printf '[sync] failed target=%s\n' "$target_host" >&2
      failed=$((failed + 1))
      target_failed=1
    fi
  fi

  if [[ "$sync_codex" != "0" && "$target_failed" -eq 0 ]]; then
    if ! WINDOWS_OPERATOR_RUN_ID="${run_id_base}-${target_label}-codex" \
      WINDOWS_OPERATOR_SSH_USER="$target_user" \
      WINDOWS_OPERATOR_SSH_HOST="$target_host" \
      WINDOWS_OPERATOR_SSH_TARGET="$target" \
      WINDOWS_OPERATOR_SSH_PORT="$target_port" \
      "$repo_root/scripts/linux/windows-sync-codex-profile.sh"; then
      printf '[sync] codex failed target=%s\n' "$target_host" >&2
      failed=$((failed + 1))
    fi
  fi
done

if [[ "$dry_run" -eq 1 ]]; then
  exit 0
fi

if [[ "$available" -eq 0 ]]; then
  die "no configured targets were reachable"
fi

if [[ "$failed" -ne 0 ]]; then
  die "$failed reachable target(s) failed sync"
fi

printf '[sync] complete reachable=%s failed=%s\n' "$available" "$failed"
