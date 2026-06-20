#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/linux/test-microsoft-graph-mail-read.sh \
    --tenant-id <tenant-id> \
    --client-id <client-id> \
    [--scope "<scopes>"] \
    [--timeout-seconds <seconds>] \
    [--interval-seconds <seconds>] \
    [--handoff windows-script|rest|none] \
    [--host-base-url <url>] \
    [--verification-wait-seconds <seconds>] \
    [--in-private] \
    [--run-id <id>] \
    [--dry-run]

Defaults:
  scope: "openid profile offline_access https://graph.microsoft.com/User.Read https://graph.microsoft.com/Mail.Read"
  timeout-seconds: 240
  handoff: windows-script
  host-base-url: http://127.0.0.1:43117
  verification-wait-seconds: 20

Notes:
  - windows-script handoff uses scripts/windows/login-microsoft-device-code.ps1 through windows-run-ps.sh.
  - rest handoff uses POST /v1/auth/microsoft/device-login.
  - none skips browser handoff and only returns the device-code payload.
USAGE
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'test-microsoft-graph-mail-read: missing command: %s\n' "$1" >&2
    exit 1
  }
}

json_quote() {
  python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"
}

join_json_array() {
  python3 - "$@" <<'PY'
import json
import sys
print(json.dumps(sys.argv[1:]))
PY
}

handoff_result_json() {
  if [[ ! -s "$handoff_json" ]]; then
    printf 'null'
    return
  fi

  local raw
  raw="$(cat "$handoff_json")"
  if jq -e . >/dev/null 2>&1 <<<"$raw"; then
    printf '%s' "$raw"
    return
  fi

  if [[ -f "$raw" ]] && jq -e . "$raw" >/dev/null 2>&1; then
    cat "$raw"
    return
  fi

  printf 'null'
}

tenant_id=""
client_id=""
scope="openid profile offline_access https://graph.microsoft.com/User.Read https://graph.microsoft.com/Mail.Read"
timeout_seconds=240
interval_override=""
handoff="windows-script"
host_base_url="http://127.0.0.1:43117"
verification_wait_seconds=20
in_private=0
run_id=""
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
    --timeout-seconds)
      timeout_seconds="${2:-}"
      shift 2
      ;;
    --interval-seconds)
      interval_override="${2:-}"
      shift 2
      ;;
    --handoff)
      handoff="${2:-}"
      shift 2
      ;;
    --host-base-url)
      host_base_url="${2:-}"
      shift 2
      ;;
    --verification-wait-seconds)
      verification_wait_seconds="${2:-}"
      shift 2
      ;;
    --in-private)
      in_private=1
      shift
      ;;
    --run-id)
      run_id="${2:-}"
      shift 2
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
      printf 'test-microsoft-graph-mail-read: unknown arg: %s\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

[[ -n "$tenant_id" ]] || { usage >&2; exit 2; }
[[ -n "$client_id" ]] || { usage >&2; exit 2; }
[[ "$handoff" == "windows-script" || "$handoff" == "rest" || "$handoff" == "none" ]] || {
  printf 'test-microsoft-graph-mail-read: unsupported handoff: %s\n' "$handoff" >&2
  exit 2
}

require_cmd curl
require_cmd jq
require_cmd python3

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

device_json="$tmpdir/device.json"
token_json="$tmpdir/token.json"
graph_json="$tmpdir/graph.json"
handoff_json="$tmpdir/handoff.json"

device_url="https://login.microsoftonline.com/${tenant_id}/oauth2/v2.0/devicecode"
token_url="https://login.microsoftonline.com/${tenant_id}/oauth2/v2.0/token"

curl -fsS \
  -X POST "$device_url" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode "client_id=${client_id}" \
  --data-urlencode "scope=${scope}" \
  -o "$device_json"

user_code="$(jq -r '.user_code // empty' "$device_json")"
device_code="$(jq -r '.device_code // empty' "$device_json")"
verification_uri="$(jq -r '.verification_uri // "https://microsoft.com/devicelogin"' "$device_json")"
expires_in="$(jq -r '.expires_in // 0' "$device_json")"
interval_seconds="$(jq -r '.interval // 5' "$device_json")"
message="$(jq -r '.message // empty' "$device_json")"

[[ -n "$user_code" && -n "$device_code" ]] || {
  printf 'test-microsoft-graph-mail-read: invalid device-code response\n' >&2
  cat "$device_json" >&2
  exit 1
}

if [[ -n "$interval_override" ]]; then
  interval_seconds="$interval_override"
fi

if [[ "$dry_run" -eq 1 || "$handoff" == "none" ]]; then
  jq -n \
    --arg tenantId "$tenant_id" \
    --arg clientId "$client_id" \
    --arg handoff "$handoff" \
    --arg verificationUri "$verification_uri" \
    --arg userCode "$user_code" \
    --arg message "$message" \
    --arg scope "$scope" \
    --argjson expiresIn "$expires_in" \
    --argjson intervalSeconds "$interval_seconds" \
    '{
      success: true,
      phase: "device_code_ready",
      tenantId: $tenantId,
      clientId: $clientId,
      handoff: $handoff,
      verificationUri: $verificationUri,
      userCode: $userCode,
      scope: $scope,
      expiresIn: $expiresIn,
      intervalSeconds: $intervalSeconds,
      message: $message
    }'
  exit 0
fi

case "$handoff" in
  windows-script)
    scripts/linux/windows-run-ps.sh \
      scripts/windows/login-microsoft-device-code.ps1 \
      -DeviceCode "$user_code" \
      -LoginUrl "$verification_uri" \
      $([[ "$in_private" -eq 1 ]] && printf '%s' '-InPrivate') \
      >"$handoff_json"
    ;;
  rest)
    in_private_json=false
    if [[ "$in_private" -eq 1 ]]; then
      in_private_json=true
    fi
    curl -fsS \
      -X POST "${host_base_url%/}/v1/auth/microsoft/device-login" \
      -H 'Content-Type: application/json' \
      -d "$(cat <<EOF
{"deviceCode":$(json_quote "$user_code"),"loginUrl":$(json_quote "$verification_uri"),"verificationWaitSeconds":${verification_wait_seconds},"inPrivate":${in_private_json}$( [[ -n "$run_id" ]] && printf ',\"runId\":%s' "$(json_quote "$run_id")" )}
EOF
)" \
      -o "$handoff_json"
    ;;
esac

deadline=$(( $(date +%s) + timeout_seconds ))
poll_count=0
last_error=""
last_error_description=""

while (( $(date +%s) <= deadline )); do
  poll_count=$((poll_count + 1))

  http_code="$(curl -sS \
    -o "$token_json" \
    -w '%{http_code}' \
    -X POST "$token_url" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
    --data-urlencode "client_id=${client_id}" \
    --data-urlencode "device_code=${device_code}")"

  if [[ "$http_code" == "200" ]]; then
    access_token="$(jq -r '.access_token // empty' "$token_json")"
    granted_scope="$(jq -r '.scope // empty' "$token_json")"
    graph_status="$(curl -sS \
      -o "$graph_json" \
      -w '%{http_code}' \
      -H "Authorization: Bearer ${access_token}" \
      'https://graph.microsoft.com/v1.0/me/messages?$top=1&$select=id,subject,receivedDateTime')"
    jq -n \
      --arg tenantId "$tenant_id" \
      --arg clientId "$client_id" \
      --arg handoff "$handoff" \
      --arg verificationUri "$verification_uri" \
      --arg userCode "$user_code" \
      --arg grantedScope "$granted_scope" \
      --arg graphStatus "$graph_status" \
      --argjson token "$(cat "$token_json")" \
      --argjson graph "$(cat "$graph_json")" \
      --argjson handoffResult "$(handoff_result_json)" \
      --argjson pollCount "$poll_count" \
      '{
        success: true,
        phase: "token_acquired",
        tenantId: $tenantId,
        clientId: $clientId,
        handoff: $handoff,
        verificationUri: $verificationUri,
        userCode: $userCode,
        grantedScope: $grantedScope,
        graphProbeStatus: ($graphStatus | tonumber),
        pollCount: $pollCount,
        handoffResult: $handoffResult,
        token: {
          token_type: $token.token_type,
          scope: $token.scope,
          expires_in: $token.expires_in,
          ext_expires_in: $token.ext_expires_in
        },
        graph: $graph
      }'
    exit 0
  fi

  last_error="$(jq -r '.error // empty' "$token_json")"
  last_error_description="$(jq -r '.error_description // empty' "$token_json")"

  case "$last_error" in
    authorization_pending)
      sleep "$interval_seconds"
      ;;
    slow_down)
      interval_seconds=$(( interval_seconds + 5 ))
      sleep "$interval_seconds"
      ;;
    *)
      jq -n \
        --arg tenantId "$tenant_id" \
        --arg clientId "$client_id" \
        --arg handoff "$handoff" \
        --arg verificationUri "$verification_uri" \
        --arg userCode "$user_code" \
        --arg error "$last_error" \
        --arg errorDescription "$last_error_description" \
        --argjson handoffResult "$(handoff_result_json)" \
        --argjson pollCount "$poll_count" \
        --argjson tokenError "$(cat "$token_json")" \
        '{
          success: false,
          phase: "token_failed",
          tenantId: $tenantId,
          clientId: $clientId,
          handoff: $handoff,
          verificationUri: $verificationUri,
          userCode: $userCode,
          pollCount: $pollCount,
          error: $error,
          errorDescription: $errorDescription,
          handoffResult: $handoffResult,
          tokenError: $tokenError
        }'
      exit 1
      ;;
  esac
done

jq -n \
  --arg tenantId "$tenant_id" \
  --arg clientId "$client_id" \
  --arg handoff "$handoff" \
  --arg verificationUri "$verification_uri" \
  --arg userCode "$user_code" \
  --arg lastError "$last_error" \
  --arg lastErrorDescription "$last_error_description" \
  --argjson handoffResult "$(handoff_result_json)" \
  --argjson pollCount "$poll_count" \
  '{
    success: false,
    phase: "timed_out",
    tenantId: $tenantId,
    clientId: $clientId,
    handoff: $handoff,
    verificationUri: $verificationUri,
    userCode: $userCode,
    pollCount: $pollCount,
    lastError: $lastError,
    lastErrorDescription: $lastErrorDescription,
    handoffResult: $handoffResult
  }'
exit 1
