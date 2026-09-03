#!/usr/bin/env bash
set -euo pipefail

umask 077

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/login-powerbi-xmla.sh \
    --tenant-id <tenant-id-or-domain> \
    [--host-base-url <url>] \
    [--client-id <client-id>] \
    [--scope "<scopes>"] \
    [--timeout-seconds <seconds>] \
    [--verification-wait-seconds <seconds>] \
    [--force] \
    [--dry-run]

Defaults:
  host-base-url: http://127.0.0.1:43117
  client-id: Power BI native public client
  scope: "openid profile offline_access https://analysis.windows.net/powerbi/api/.default"
  timeout-seconds: 900
  verification-wait-seconds: 120
  token cache: $XDG_STATE_HOME/windows-operator/auth/powerbi/<tenant>.json
               or ~/.local/state/windows-operator/auth/powerbi/<tenant>.json

Behavior:
  - Reuses a valid cached token or refreshes it without opening Edge.
  - Otherwise starts Entra device login and sends only the user code to Windows
    Operator over its loopback REST tunnel.
  - Opens an isolated Edge auth profile for account, password, MFA, or consent.
  - Stores the complete token response in Legion-local mode-0600 state.
  - Prints only sanitized status. It never prints access or refresh tokens.
USAGE
}

die() {
  printf 'login-powerbi-xmla: %s\n' "$*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "missing command: $1"
}

is_positive_integer() {
  [[ "$1" =~ ^[0-9]+$ ]] && (( 10#$1 > 0 ))
}

read_handoff_result() {
  if [[ -s "$handoff_json" ]] && jq -e . "$handoff_json" >/dev/null 2>&1; then
    jq 'del(.statusPath)' "$handoff_json"
  else
    printf 'null'
  fi
}

write_cache() {
  local source_path=$1
  local prior_path=${2:-}
  local now_epoch expires_in expires_at tmp_cache

  now_epoch="$(date +%s)"
  expires_in="$(jq -r '.expires_in // 0' "$source_path")"
  [[ "$expires_in" =~ ^[0-9]+$ ]] || expires_in=0
  expires_at=$((now_epoch + expires_in))
  tmp_cache="${cache_path}.tmp.$$"

  if [[ -n "$prior_path" && -s "$prior_path" ]]; then
    jq -s \
      --arg tenantId "$tenant_id" \
      --arg clientId "$client_id" \
      --arg requestedScope "$scope" \
      --arg cachedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
      --argjson expiresAtEpoch "$expires_at" \
      '.[0] as $new | .[1] as $old |
       $new
       | if ((.refresh_token // "") | length) == 0 then .refresh_token = $old.refresh_token else . end
       | . + {
           tenant_id: $tenantId,
           client_id: $clientId,
           requested_scope: $requestedScope,
           cached_at_utc: $cachedAtUtc,
           expires_at_epoch: $expiresAtEpoch
         }' \
      "$source_path" "$prior_path" >"$tmp_cache"
  else
    jq \
      --arg tenantId "$tenant_id" \
      --arg clientId "$client_id" \
      --arg requestedScope "$scope" \
      --arg cachedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
      --argjson expiresAtEpoch "$expires_at" \
      '. + {
         tenant_id: $tenantId,
         client_id: $clientId,
         requested_scope: $requestedScope,
         cached_at_utc: $cachedAtUtc,
         expires_at_epoch: $expiresAtEpoch
       }' \
      "$source_path" >"$tmp_cache"
  fi

  chmod 600 "$tmp_cache"
  mv -f "$tmp_cache" "$cache_path"
}

probe_powerbi_api() {
  local token_path=$1
  local header_path=$tmpdir/authorization-header.txt
  local status

  {
    printf 'Authorization: Bearer '
    jq -r '.access_token' "$token_path"
  } >"$header_path"
  chmod 600 "$header_path"

  if ! status="$(curl -sS \
    -o "$probe_json" \
    -w '%{http_code}' \
    -H @"$header_path" \
    'https://api.powerbi.com/v1.0/myorg/groups')"; then
    status=000
  fi
  printf '%s' "$status"
}

emit_success() {
  local phase=$1
  local token_path=$2
  local api_status handoff_result expires_at granted_scope

  api_status="$(probe_powerbi_api "$token_path")"
  expires_at="$(jq -r '.expires_at_epoch // 0' "$token_path")"
  granted_scope="$(jq -r '.scope // ""' "$token_path")"
  handoff_result="$(read_handoff_result)"

  jq -n \
    --arg phase "$phase" \
    --arg tenantId "$tenant_id" \
    --arg clientId "$client_id" \
    --arg cachePath "$cache_path" \
    --arg grantedScope "$granted_scope" \
    --arg powerBiApiStatus "$api_status" \
    --argjson expiresAtEpoch "$expires_at" \
    --argjson handoffResult "$handoff_result" \
    '{
      success: true,
      phase: $phase,
      tenantId: $tenantId,
      clientId: $clientId,
      cachePath: $cachePath,
      expiresAtEpoch: $expiresAtEpoch,
      grantedScope: $grantedScope,
      powerBiApiStatus: ($powerBiApiStatus | tonumber),
      handoffResult: $handoffResult
    }'
}

tenant_id=""
client_id="871c010f-5e61-4fb1-83ac-98610a7e9110"
scope="openid profile offline_access https://analysis.windows.net/powerbi/api/.default"
host_base_url="http://127.0.0.1:43117"
timeout_seconds=900
verification_wait_seconds=120
force=0
dry_run=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tenant-id)
      tenant_id="${2:-}"
      shift 2
      ;;
    --client-id)
      client_id="${2:-}"
      shift 2
      ;;
    --scope)
      scope="${2:-}"
      shift 2
      ;;
    --host-base-url)
      host_base_url="${2:-}"
      shift 2
      ;;
    --timeout-seconds)
      timeout_seconds="${2:-}"
      shift 2
      ;;
    --verification-wait-seconds)
      verification_wait_seconds="${2:-}"
      shift 2
      ;;
    --force)
      force=1
      shift
      ;;
    --dry-run)
      dry_run=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      printf 'login-powerbi-xmla: unknown arg: %s\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

[[ -n "$tenant_id" ]] || { usage >&2; exit 2; }
[[ "$tenant_id" =~ ^[A-Za-z0-9.-]+$ ]] || die "tenant id/domain contains unsupported characters"
[[ "$client_id" =~ ^[A-Za-z0-9.-]+$ ]] || die "client id contains unsupported characters"
is_positive_integer "$timeout_seconds" || die "timeout-seconds must be a positive integer"
is_positive_integer "$verification_wait_seconds" || die "verification-wait-seconds must be a positive integer"
(( verification_wait_seconds <= 120 )) || die "verification-wait-seconds must not exceed 120"

require_cmd curl
require_cmd jq
require_cmd python3
require_cmd realpath
require_cmd flock

state_home="${XDG_STATE_HOME:-$(python3 -c 'from pathlib import Path; print(Path.home() / ".local" / "state")')}"
[[ "$state_home" == /* ]] || die "XDG_STATE_HOME must be an absolute Linux-local path"
state_home="$(realpath -m -- "$state_home")"
cache_root="$state_home/windows-operator/auth/powerbi"
cache_path="$cache_root/${tenant_id}.json"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
exchange_root="$(realpath -m -- "${WINDOWS_OPERATOR_EXCHANGE_ROOT:-/var/lib/windows-server/shared/operator-exchange}")"

case "$cache_root/" in
  "$repo_root/"*|"$exchange_root/"*)
    die "token cache must stay outside shared source and operator exchange"
    ;;
esac

if [[ "$dry_run" -eq 1 ]]; then
  jq -n \
    --arg tenantId "$tenant_id" \
    --arg clientId "$client_id" \
    --arg scope "$scope" \
    --arg hostBaseUrl "$host_base_url" \
    --arg cachePath "$cache_path" \
    --argjson timeoutSeconds "$timeout_seconds" \
    --argjson verificationWaitSeconds "$verification_wait_seconds" \
    '{
      success: true,
      phase: "dry_run",
      tenantId: $tenantId,
      clientId: $clientId,
      scope: $scope,
      hostBaseUrl: $hostBaseUrl,
      cachePath: $cachePath,
      timeoutSeconds: $timeoutSeconds,
      verificationWaitSeconds: $verificationWaitSeconds
    }'
  exit 0
fi

mkdir -p "$cache_root"
chmod 700 "$cache_root"
lock_path="${cache_path}.lock"
exec 9>"$lock_path"
chmod 600 "$lock_path"
flock -w 1 9 || die "another Power BI login is already running for this tenant"

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

device_json="$tmpdir/device.json"
token_json="$tmpdir/token.json"
probe_json="$tmpdir/powerbi-api.json"
handoff_json="$tmpdir/handoff.json"
refresh_token_file="$tmpdir/refresh-token.txt"
device_code_file="$tmpdir/device-code.txt"
user_code_file="$tmpdir/user-code.txt"
handoff_request="$tmpdir/handoff-request.json"

if [[ "$force" -eq 0 && -s "$cache_path" ]] && \
  jq -e \
    --arg clientId "$client_id" \
    --arg requestedScope "$scope" \
    '.access_token and .expires_at_epoch and .client_id == $clientId and .requested_scope == $requestedScope' \
    "$cache_path" >/dev/null 2>&1; then
  now_epoch="$(date +%s)"
  expires_at="$(jq -r '.expires_at_epoch // 0' "$cache_path")"
  if [[ "$expires_at" =~ ^[0-9]+$ ]] && (( expires_at > now_epoch + 300 )); then
    api_status="$(probe_powerbi_api "$cache_path")"
    if [[ "$api_status" == "200" ]]; then
      emit_success "cache_valid" "$cache_path"
      exit 0
    fi
  fi

  if jq -e '(.refresh_token // "") | length > 0' "$cache_path" >/dev/null 2>&1; then
    jq -r '.refresh_token' "$cache_path" >"$refresh_token_file"
    if ! refresh_status="$(curl -sS \
      -o "$token_json" \
      -w '%{http_code}' \
      -X POST "https://login.microsoftonline.com/${tenant_id}/oauth2/v2.0/token" \
      -H 'Content-Type: application/x-www-form-urlencoded' \
      --data-urlencode 'grant_type=refresh_token' \
      --data-urlencode "client_id=${client_id}" \
      --data-urlencode "scope=${scope}" \
      --data-urlencode "refresh_token@${refresh_token_file}")"; then
      refresh_status=000
    fi

    if [[ "$refresh_status" == "200" ]] && jq -e '.access_token' "$token_json" >/dev/null 2>&1; then
      write_cache "$token_json" "$cache_path"
      emit_success "token_refreshed" "$cache_path"
      exit 0
    fi
  fi
fi

health_json="$(curl -fsS --max-time 5 "${host_base_url%/}/v1/health" 2>/dev/null || true)"
health_status="$(jq -r '.status // empty' <<<"$health_json" 2>/dev/null || true)"
[[ "$health_status" == "ok" ]] || die "Windows Operator Host unavailable at ${host_base_url%/}; establish the Legion-to-Windows REST tunnel first"

if ! device_status="$(curl -sS \
  -o "$device_json" \
  -w '%{http_code}' \
  -X POST "https://login.microsoftonline.com/${tenant_id}/oauth2/v2.0/devicecode" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode "client_id=${client_id}" \
  --data-urlencode "scope=${scope}")"; then
  device_status=000
fi

if [[ "$device_status" != "200" ]] || ! jq -e '.user_code and .device_code' "$device_json" >/dev/null 2>&1; then
  error_code="$(jq -r '.error // "device_code_request_failed"' "$device_json" 2>/dev/null || printf 'device_code_request_failed')"
  error_description="$(jq -r '.error_description // "Entra device-code request failed."' "$device_json" 2>/dev/null || printf 'Entra device-code request failed.')"
  jq -n \
    --arg error "$error_code" \
    --arg errorDescription "$error_description" \
    --arg httpStatus "$device_status" \
    '{success:false,phase:"device_code_failed",httpStatus:($httpStatus | tonumber),error:$error,errorDescription:$errorDescription}'
  exit 1
fi

jq -r '.device_code' "$device_json" >"$device_code_file"
jq -r '.user_code' "$device_json" >"$user_code_file"
verification_uri="$(jq -r '.verification_uri // "https://microsoft.com/devicelogin"' "$device_json")"
interval_seconds="$(jq -r '.interval // 5' "$device_json")"
[[ "$interval_seconds" =~ ^[0-9]+$ ]] || interval_seconds=5

jq -n \
  --rawfile deviceCode "$user_code_file" \
  --arg loginUrl "$verification_uri" \
  --argjson verificationWaitSeconds "$verification_wait_seconds" \
  '{
    deviceCode: ($deviceCode | rtrimstr("\n")),
    loginUrl: $loginUrl,
    verificationWaitSeconds: $verificationWaitSeconds,
    reuseExistingProfile: false
  }' >"$handoff_request"

if ! handoff_status="$(curl -sS \
  -o "$handoff_json" \
  -w '%{http_code}' \
  -X POST "${host_base_url%/}/v1/auth/microsoft/device-login" \
  -H 'Content-Type: application/json' \
  --data-binary @"$handoff_request")"; then
  handoff_status=000
fi

if [[ "$handoff_status" != "200" ]]; then
  jq -n \
    --arg httpStatus "$handoff_status" \
    '{success:false,phase:"browser_handoff_failed",httpStatus:($httpStatus | tonumber)}'
  exit 1
fi

handoff_state="$(jq -r '.status // empty' "$handoff_json")"
if [[ "$handoff_state" == "failed" || "$handoff_state" == "invalidCode" ]]; then
  jq -n \
    --argjson handoffResult "$(read_handoff_result)" \
    '{success:false,phase:"browser_handoff_failed",handoffResult:$handoffResult}'
  exit 1
fi

deadline=$(( $(date +%s) + timeout_seconds ))
poll_count=0
last_error="authorization_pending"
last_error_description="Waiting for Microsoft login completion in Windows Edge."

while (( $(date +%s) <= deadline )); do
  poll_count=$((poll_count + 1))
  if ! token_status="$(curl -sS \
    -o "$token_json" \
    -w '%{http_code}' \
    -X POST "https://login.microsoftonline.com/${tenant_id}/oauth2/v2.0/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode 'grant_type=urn:ietf:params:oauth:grant-type:device_code' \
    --data-urlencode "client_id=${client_id}" \
    --data-urlencode "device_code@${device_code_file}")"; then
    token_status=000
  fi

  if [[ "$token_status" == "200" ]] && jq -e '.access_token' "$token_json" >/dev/null 2>&1; then
    write_cache "$token_json"
    emit_success "token_acquired" "$cache_path"
    exit 0
  fi

  last_error="$(jq -r '.error // "token_request_failed"' "$token_json" 2>/dev/null || printf 'token_request_failed')"
  last_error_description="$(jq -r '.error_description // "Token request failed."' "$token_json" 2>/dev/null || printf 'Token request failed.')"

  case "$last_error" in
    authorization_pending)
      sleep "$interval_seconds"
      ;;
    slow_down)
      interval_seconds=$((interval_seconds + 5))
      sleep "$interval_seconds"
      ;;
    *)
      jq -n \
        --arg error "$last_error" \
        --arg errorDescription "$last_error_description" \
        --argjson pollCount "$poll_count" \
        --argjson handoffResult "$(read_handoff_result)" \
        '{
          success:false,
          phase:"token_failed",
          pollCount:$pollCount,
          error:$error,
          errorDescription:$errorDescription,
          handoffResult:$handoffResult
        }'
      exit 1
      ;;
  esac
done

jq -n \
  --arg error "$last_error" \
  --arg errorDescription "$last_error_description" \
  --argjson pollCount "$poll_count" \
  --argjson handoffResult "$(read_handoff_result)" \
  '{
    success:false,
    phase:"timed_out",
    pollCount:$pollCount,
    error:$error,
    errorDescription:$errorDescription,
    handoffResult:$handoffResult
  }'
exit 1
