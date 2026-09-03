#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
subject="$repo_root/scripts/linux/login-powerbi-xmla.sh"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT

mkdir -p "$test_root/bin" "$test_root/state"

cat >"$test_root/bin/curl" <<'FAKE_CURL'
#!/usr/bin/env bash
set -euo pipefail

output=""
url=""
handoff_request=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o)
      output=$2
      shift 2
      ;;
    -w|--max-time|-H|--data-urlencode)
      shift 2
      ;;
    --data-binary)
      handoff_request=${2#@}
      shift 2
      ;;
    -X|-fsS|-sS)
      if [[ "$1" == "-X" ]]; then shift 2; else shift; fi
      ;;
    http://*|https://*)
      url=$1
      shift
      ;;
    *)
      shift
      ;;
  esac
done

printf '%s\n' "$url" >>"$FAKE_CURL_LOG"

case "$url" in
  */v1/health)
    printf '{"status":"ok","runtimeMode":"headless-host"}'
    ;;
  */oauth2/v2.0/devicecode)
    printf '%s' '{"user_code":"TEST-CODE","device_code":"test-device-secret","verification_uri":"https://microsoft.com/devicelogin","interval":1}' >"$output"
    printf '200'
    ;;
  */v1/auth/microsoft/device-login)
    cp "$handoff_request" "$FAKE_HANDOFF_CAPTURE"
    printf '%s' '{"success":false,"loginUrl":"https://microsoft.com/devicelogin","inPrivate":false,"actions":["isolated_auth_profile"],"errors":[],"completedAtUtc":"2026-09-03T12:00:00Z","runId":"test-run","status":"needsUserAction","browserState":"browser_needs_password"}' >"$output"
    printf '200'
    ;;
  */oauth2/v2.0/token)
    printf '%s' '{"token_type":"Bearer","scope":"https://analysis.windows.net/powerbi/api/.default","expires_in":3600,"access_token":"test-access-secret","refresh_token":"test-refresh-secret"}' >"$output"
    printf '200'
    ;;
  */v1.0/myorg/groups)
    printf '%s' '{"value":[]}' >"$output"
    printf '200'
    ;;
  *)
    printf 'unexpected fake curl URL: %s\n' "$url" >&2
    exit 1
    ;;
esac
FAKE_CURL
chmod +x "$test_root/bin/curl"

export PATH="$test_root/bin:$PATH"
export XDG_STATE_HOME="$test_root/state"
export FAKE_CURL_LOG="$test_root/curl.log"
export FAKE_HANDOFF_CAPTURE="$test_root/handoff.json"

tenant_id="00000000-0000-0000-0000-000000000000"
first_result="$test_root/first.json"
second_result="$test_root/second.json"
third_result="$test_root/third.json"

"$subject" \
  --tenant-id "$tenant_id" \
  --timeout-seconds 10 \
  --verification-wait-seconds 10 >"$first_result"

jq -e '.success == true and .phase == "token_acquired" and .powerBiApiStatus == 200' "$first_result" >/dev/null
jq -e '.reuseExistingProfile == false and .deviceCode == "TEST-CODE"' "$FAKE_HANDOFF_CAPTURE" >/dev/null

cache_path="$test_root/state/windows-operator/auth/powerbi/${tenant_id}.json"
[[ -f "$cache_path" ]]
[[ "$(stat -c '%a' "$cache_path")" == "600" ]]
[[ "$(stat -c '%a' "${cache_path}.lock")" == "600" ]]
jq -e '.access_token == "test-access-secret" and .refresh_token == "test-refresh-secret"' "$cache_path" >/dev/null
! rg -q 'test-access-secret|test-refresh-secret|test-device-secret' "$first_result"

: >"$FAKE_CURL_LOG"
"$subject" \
  --tenant-id "$tenant_id" \
  --timeout-seconds 10 \
  --verification-wait-seconds 10 >"$second_result"

jq -e '.success == true and .phase == "cache_valid" and .powerBiApiStatus == 200' "$second_result" >/dev/null
! rg -q 'devicecode|/token|device-login' "$FAKE_CURL_LOG"
! rg -q 'test-access-secret|test-refresh-secret|test-device-secret' "$second_result"

jq '.expires_at_epoch = 0' "$cache_path" >"$test_root/expired-cache.json"
mv "$test_root/expired-cache.json" "$cache_path"
chmod 600 "$cache_path"
: >"$FAKE_CURL_LOG"
"$subject" \
  --tenant-id "$tenant_id" \
  --timeout-seconds 10 \
  --verification-wait-seconds 10 >"$third_result"

jq -e '.success == true and .phase == "token_refreshed" and .powerBiApiStatus == 200' "$third_result" >/dev/null
rg -q '/token' "$FAKE_CURL_LOG"
! rg -q 'devicecode|device-login' "$FAKE_CURL_LOG"
! rg -q 'test-access-secret|test-refresh-secret|test-device-secret' "$third_result"

printf 'login-powerbi-xmla tests passed\n'
