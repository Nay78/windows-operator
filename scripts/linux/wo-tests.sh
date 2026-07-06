#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
script="$repo_root/scripts/linux/wo"
tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

stub_ppt="$tmp_root/ppt_stub.py"
stub_smoke="$tmp_root/smoke_stub.py"
stub_out="$tmp_root/calls.jsonl"
rest_stub="$tmp_root/rest_stub.py"
rest_port_file="$tmp_root/rest_port"

cat >"$stub_ppt" <<'PY'
#!/usr/bin/env python3
import json
import os
import sys

with open(os.environ["WO_STUB_OUT"], "a", encoding="utf-8") as handle:
    handle.write(json.dumps({"kind": "ppt", "argv": sys.argv[1:]}) + "\n")
print("/tmp/ppt-summary.json")
PY

cat >"$stub_smoke" <<'PY'
#!/usr/bin/env python3
import argparse
import json
import os
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--base-url")
parser.add_argument("--codex-url")
parser.add_argument("--exchange-root")
parser.add_argument("--run-id")
parser.add_argument("--output")
parser.add_argument("--fail-report", action="store_true")
parser.add_argument("--fail-exit", action="store_true")
parser.add_argument("--include-notepad", action="store_true")
args, rest = parser.parse_known_args()

with open(os.environ["WO_STUB_OUT"], "a", encoding="utf-8") as handle:
    handle.write(json.dumps({"kind": "smoke", "argv": sys.argv[1:]}) + "\n")

if "--help" in sys.argv[1:] or "-h" in sys.argv[1:]:
    print("usage: smoke stub")
    sys.exit(0)

report = {
    "ok": not args.fail_report,
    "runId": args.run_id,
    "results": [{"name": "stub", "ok": not args.fail_report}],
}
output = Path(args.output)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report), encoding="utf-8")
print(f"REPORT {output}")
print("stub stderr marker", file=sys.stderr)
sys.exit(1 if args.fail_exit else 0)
PY

cat >"$rest_stub" <<'PY'
#!/usr/bin/env python3
import json
import os
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

port_file = sys.argv[1]
out_path = os.environ["WO_REST_STUB_OUT"]

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/v1/health":
            body = {
                "status": "ok",
                "runtimeMode": "headless-host",
                "checkedAtUtc": "2026-07-06T00:00:00Z",
            }
            self.send_response(200)
        elif self.path == "/v1/windows":
            body = [
                {"id": 101, "title": "Window A"},
                {"id": 102, "title": "Window B"},
            ]
            self.send_response(200)
        elif self.path == "/v1/mail/status":
            body = {
                "workerAvailable": True,
                "outlookAvailable": False,
            }
            self.send_response(200)
        else:
            body = {"error": "missing"}
            self.send_response(404)
        with open(out_path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps({"method": "GET", "path": self.path}) + "\n")
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(body).encode("utf-8"))

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", "0")) or 0)
        payload = json.loads(raw.decode("utf-8")) if raw else None
        if self.path == "/v1/mail/folders":
            body = {
                "success": payload.get("freshness") != "cached",
                "recovered": False,
                "folders": [
                    {"path": "Inbox"},
                    {"path": "Inbox/Subfolder"},
                ],
            }
            self.send_response(200)
        elif self.path == "/v1/mail/messages/search":
            body = {
                "success": payload.get("subjectContains") != "__wo_mail_search_no_match__test-mail-search-fail",
                "recovered": False,
                "messages": [],
            }
            self.send_response(200)
        elif self.path == "/v1/mail/attachments/download":
            body = {
                "success": payload.get("runId") != "test-mail-download-fail",
                "runId": payload.get("runId"),
                "attachmentsSaved": 0,
                "attachmentsSkipped": 1,
                "saved": [],
                "skipped": [{"reason": "stub"}],
            }
            self.send_response(200)
        elif self.path == "/v1/auth/microsoft/cleanup":
            body = {
                "success": payload.get("preserveRecentSeconds") != 999,
                "status": "dryRun" if payload.get("dryRun") else "cleaned",
            }
            self.send_response(200)
        elif self.path == "/v1/auth/microsoft/device-login":
            body = {
                "success": payload.get("runId") != "test-auth-device-fail",
                "runId": payload.get("runId"),
                "status": "dryRun" if payload.get("dryRun") else "submitted",
            }
            self.send_response(200)
        elif self.path == "/v1/auth/microsoft/authorize-probe":
            body = {
                "success": payload.get("runId") != "test-auth-authorize-fail",
                "runId": payload.get("runId"),
                "status": "dryRun" if payload.get("dryRun") else "started",
            }
            self.send_response(200)
        else:
            body = {"error": "missing"}
            self.send_response(404)
        with open(out_path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps({"method": "POST", "path": self.path, "payload": payload}) + "\n")
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(body).encode("utf-8"))

    def log_message(self, format, *args):
        return

server = HTTPServer(("127.0.0.1", 0), Handler)
with open(port_file, "w", encoding="utf-8") as handle:
    handle.write(str(server.server_address[1]))
    handle.flush()
server.serve_forever()
PY

chmod +x "$stub_ppt" "$stub_smoke" "$rest_stub"
WO_REST_STUB_OUT="$stub_out" "$rest_stub" "$rest_port_file" &
rest_pid=$!
trap 'kill "$rest_pid" >/dev/null 2>&1 || true; rm -rf "$tmp_root"' EXIT
for _ in {1..50}; do
    [[ -f "$rest_port_file" ]] && break
    sleep 0.1
done
rest_port="$(<"$rest_port_file")"
base_url="http://127.0.0.1:$rest_port"

help_text="$("$script" --help)"
grep -q "health" <<<"$help_text"
grep -q "windows" <<<"$help_text"
grep -q "ppt" <<<"$help_text"
grep -q "smoke" <<<"$help_text"

hot_help="$("$script" ppt hot run --help)"
grep -q "validate-only hot iteration" <<<"$hot_help"

health_json="$("$script" health --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-health)"
windows_json="$("$script" windows list --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-windows)"
mail_status_json="$("$script" mail status --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-status)"
mail_folders_json="$("$script" mail folders --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-folders --freshness fresh)"
mail_search_json="$("$script" mail search --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-search --folder Inbox/Subfolder)"
mail_download_json="$("$script" mail download --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-download --folder Inbox/Subfolder --subject Invoice)"
auth_cleanup_json="$("$script" auth microsoft cleanup --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-cleanup)"
auth_device_json="$("$script" auth microsoft device-login --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-device --device-code ABCD-EFGH --dry-run)"
auth_authorize_json="$("$script" auth microsoft authorize-probe --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-authorize --authorize-url https://login.microsoftonline.com/common/oauth2/v2.0/authorize --dry-run)"
smoke_json="$(
  WO_SMOKE_SCRIPT="$stub_smoke" \
  WO_STUB_OUT="$stub_out" \
  "$script" smoke --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-smoke -- --output /tmp/ignored.json --run-id ignored --include-notepad
)"

smoke_help_text="$(
  WO_SMOKE_SCRIPT="$stub_smoke" \
  WO_STUB_OUT="$stub_out" \
  "$script" smoke -- --help
)"
grep -q "usage: smoke_stub.py" <<<"$smoke_help_text"

set +e
unsafe_download_json="$("$script" mail download --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-download-unsafe 2>/dev/null)"
unsafe_download_status=$?
mail_folders_fail_json="$("$script" mail folders --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-folders-fail --freshness cached 2>/dev/null)"
mail_folders_fail_status=$?
mail_search_fail_json="$("$script" mail search --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-search-fail 2>/dev/null)"
mail_search_fail_status=$?
mail_download_fail_json="$("$script" mail download --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-mail-download-fail --subject Invoice 2>/dev/null)"
mail_download_fail_status=$?
auth_cleanup_fail_json="$("$script" auth microsoft cleanup --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-cleanup-fail --preserve-recent-seconds 999 2>/dev/null)"
auth_cleanup_fail_status=$?
auth_device_fail_json="$("$script" auth microsoft device-login --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-device-fail --device-code ABCD-EFGH --dry-run 2>/dev/null)"
auth_device_fail_status=$?
auth_authorize_fail_json="$("$script" auth microsoft authorize-probe --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-auth-authorize-fail --authorize-url https://login.microsoftonline.com/common/oauth2/v2.0/authorize --dry-run 2>/dev/null)"
auth_authorize_fail_status=$?
smoke_fail_json="$(
  WO_SMOKE_SCRIPT="$stub_smoke" \
  WO_STUB_OUT="$stub_out" \
  "$script" smoke --json --base-url "$base_url" --exchange-root "$tmp_root/exchange" --run-id test-smoke-fail -- --fail-report 2>/dev/null
)"
smoke_fail_status=$?
set -e
[[ "$unsafe_download_status" -eq 2 ]]
[[ "$mail_folders_fail_status" -eq 1 ]]
[[ "$mail_search_fail_status" -eq 1 ]]
[[ "$mail_download_fail_status" -eq 1 ]]
[[ "$auth_cleanup_fail_status" -eq 1 ]]
[[ "$auth_device_fail_status" -eq 1 ]]
[[ "$auth_authorize_fail_status" -eq 1 ]]
[[ "$smoke_fail_status" -eq 1 ]]

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt profile -- --help >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt profile-fast >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt warm >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt hot start >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt hot run >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt hot status >/dev/null

WO_POWERPOINT_SCRIPT="$stub_ppt" \
WO_SMOKE_SCRIPT="$stub_smoke" \
WO_STUB_OUT="$stub_out" \
"$script" ppt hot cleanup >/dev/null

python3 - "$stub_out" "$tmp_root/exchange" \
  "$health_json" "$windows_json" "$mail_status_json" "$mail_folders_json" "$mail_search_json" "$mail_download_json" \
  "$auth_cleanup_json" "$auth_device_json" "$auth_authorize_json" "$smoke_json" "$unsafe_download_json" \
  "$mail_folders_fail_json" "$mail_search_fail_json" "$mail_download_fail_json" "$auth_cleanup_fail_json" \
  "$auth_device_fail_json" "$auth_authorize_fail_json" "$smoke_fail_json" <<'PY'
import json
import sys
from pathlib import Path

path = sys.argv[1]
exchange_root = Path(sys.argv[2])
payloads = {
    "wo health": json.loads(sys.argv[3]),
    "wo windows list": json.loads(sys.argv[4]),
    "wo mail status": json.loads(sys.argv[5]),
    "wo mail folders": json.loads(sys.argv[6]),
    "wo mail search": json.loads(sys.argv[7]),
    "wo mail download": json.loads(sys.argv[8]),
    "wo auth microsoft cleanup": json.loads(sys.argv[9]),
    "wo auth microsoft device-login": json.loads(sys.argv[10]),
    "wo auth microsoft authorize-probe": json.loads(sys.argv[11]),
    "wo smoke": json.loads(sys.argv[12]),
}
unsafe_download_json = json.loads(sys.argv[13])
failure_payloads = {
    "wo mail folders": json.loads(sys.argv[14]),
    "wo mail search": json.loads(sys.argv[15]),
    "wo mail download": json.loads(sys.argv[16]),
    "wo auth microsoft cleanup": json.loads(sys.argv[17]),
    "wo auth microsoft device-login": json.loads(sys.argv[18]),
    "wo auth microsoft authorize-probe": json.loads(sys.argv[19]),
    "wo smoke": json.loads(sys.argv[20]),
}
with open(path, "r", encoding="utf-8") as handle:
    rows = [json.loads(line) for line in handle if line.strip()]

assert len(rows) == 24, rows
rest_rows = [row for row in rows if row.get("method")]
ppt_rows = [row for row in rows if row.get("kind") == "ppt"]
smoke_rows = [row for row in rows if row.get("kind") == "smoke"]

assert rest_rows[0] == {"method": "GET", "path": "/v1/health"}, rest_rows[0]
assert rest_rows[1] == {"method": "GET", "path": "/v1/windows"}, rest_rows[1]
assert rest_rows[2] == {"method": "GET", "path": "/v1/mail/status"}, rest_rows[2]

assert rest_rows[3]["path"] == "/v1/mail/folders", rest_rows[3]
assert rest_rows[3]["payload"] == {"freshness": "fresh"}, rest_rows[3]

assert rest_rows[4]["path"] == "/v1/mail/messages/search", rest_rows[4]
assert rest_rows[4]["payload"]["folderPath"] == "Inbox/Subfolder", rest_rows[4]
assert rest_rows[4]["payload"]["freshness"] == "cached", rest_rows[4]
assert rest_rows[4]["payload"]["includeAttachmentDetails"] is True, rest_rows[4]
assert rest_rows[4]["payload"]["maxResults"] == 25, rest_rows[4]
assert rest_rows[4]["payload"]["subjectContains"].startswith("__wo_mail_search_no_match__"), rest_rows[4]

assert rest_rows[5]["path"] == "/v1/mail/attachments/download", rest_rows[5]
assert rest_rows[5]["payload"] == {
    "runId": "test-mail-download",
    "freshness": "cached",
    "dryRun": False,
    "folderPath": "Inbox/Subfolder",
    "subjectContains": "Invoice",
}, rest_rows[5]

assert rest_rows[6]["path"] == "/v1/auth/microsoft/cleanup", rest_rows[6]
assert rest_rows[6]["payload"] == {"dryRun": True, "preserveRecentSeconds": 0}, rest_rows[6]

assert rest_rows[7]["path"] == "/v1/auth/microsoft/device-login", rest_rows[7]
assert rest_rows[7]["payload"] == {
    "runId": "test-auth-device",
    "deviceCode": "ABCD-EFGH",
    "dryRun": True,
}, rest_rows[7]

assert rest_rows[8]["path"] == "/v1/auth/microsoft/authorize-probe", rest_rows[8]
authorize_row = rest_rows[8]
assert authorize_row["payload"] == {
    "runId": "test-auth-authorize",
    "authorizeUrl": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
    "dryRun": True,
}, authorize_row

assert len(ppt_rows) == 7, ppt_rows
profile, profile_fast, warm, hot_start, hot_run, hot_status, hot_cleanup = ppt_rows

assert "--verify-readiness" in profile["argv"], profile
assert "--deck-url" in profile["argv"], profile
assert "--help" in profile["argv"], profile

assert profile_fast["kind"] == "ppt"
assert "--verify-readiness-fast" in profile_fast["argv"], profile_fast

assert warm["kind"] == "ppt"
assert "--profile-warm" in warm["argv"], warm
assert "--warm-iterations" in warm["argv"], warm

assert hot_start["kind"] == "ppt"
assert "--hot-start" in hot_start["argv"], hot_start
assert "--hot-session-id" in hot_start["argv"], hot_start
assert "ppt-hot-sem27" in hot_start["argv"], hot_start

assert hot_run["kind"] == "ppt"
assert "--hot-run" in hot_run["argv"], hot_run

assert hot_status["kind"] == "ppt"
assert "--hot-status" in hot_status["argv"], hot_status

assert hot_cleanup["kind"] == "ppt"
assert "--hot-cleanup" in hot_cleanup["argv"], hot_cleanup

assert len(smoke_rows) == 2, smoke_rows
smoke_success, smoke_failure = smoke_rows
assert smoke_success["argv"].count("--run-id") == 2, smoke_success
assert smoke_success["argv"].count("--output") == 2, smoke_success
assert smoke_success["argv"][smoke_success["argv"].index("--run-id") + 1] == "ignored", smoke_success
assert smoke_success["argv"][smoke_success["argv"].index("--output") + 1] == "/tmp/ignored.json", smoke_success
assert smoke_success["argv"][len(smoke_success["argv"]) - 6 : len(smoke_success["argv"]) - 2] == [
    "--run-id",
    "test-smoke",
    "--output",
    str(exchange_root / "runs" / "test-smoke" / "live-smoke-report.json"),
], smoke_success
assert "--include-notepad" in smoke_success["argv"], smoke_success
assert "--output" in smoke_success["argv"], smoke_success
assert "--fail-report" in smoke_failure["argv"], smoke_failure

for payload, command, run_id, artifact_name in (
    (payloads["wo health"], "wo health", "test-health", "health.json"),
    (payloads["wo windows list"], "wo windows list", "test-windows", "windows.json"),
    (payloads["wo mail status"], "wo mail status", "test-mail-status", "mail-status.json"),
    (payloads["wo mail folders"], "wo mail folders", "test-mail-folders", "mail-folders.json"),
    (payloads["wo mail search"], "wo mail search", "test-mail-search", "mail-search.json"),
    (payloads["wo mail download"], "wo mail download", "test-mail-download", "mail-download.json"),
    (payloads["wo auth microsoft cleanup"], "wo auth microsoft cleanup", "test-auth-cleanup", "auth-microsoft-cleanup.json"),
    (payloads["wo auth microsoft device-login"], "wo auth microsoft device-login", "test-auth-device", "auth-microsoft-device-login.json"),
    (payloads["wo auth microsoft authorize-probe"], "wo auth microsoft authorize-probe", "test-auth-authorize", "auth-microsoft-authorize-probe.json"),
):
    assert sorted(payload.keys()) == ["summaryPath"], payload
    summary_path = Path(payload["summaryPath"])
    summary = json.loads(summary_path.read_text(encoding="utf-8"))
    assert summary["success"] is True, summary
    assert summary["status"] == "ok", summary
    assert summary["command"] == command, summary
    assert summary["runId"] == run_id, summary
    assert summary["exchangeRoot"] == str(exchange_root), summary
    assert summary["summaryPath"] == str(summary_path), summary
    assert summary["startedAtUtc"].endswith("Z"), summary
    assert summary["observedAtUtc"].endswith("Z"), summary
    assert isinstance(summary["elapsedSeconds"], (int, float)), summary
    assert summary["inputs"]["runId"] == run_id, summary
    assert summary["artifacts"]["responsePath"] == str(summary_path.parent / artifact_name), summary
    assert isinstance(summary["gates"], list) and len(summary["gates"]) == 1, summary
    assert summary["gates"][0]["status"] == "passed", summary
    assert summary["error"] is None, summary
    assert summary["cleanup"] is None, summary

smoke_summary_path = Path(payloads["wo smoke"]["summaryPath"])
smoke_summary = json.loads(smoke_summary_path.read_text(encoding="utf-8"))
assert smoke_summary["success"] is True, smoke_summary
assert smoke_summary["status"] == "ok", smoke_summary
assert smoke_summary["command"] == "wo smoke", smoke_summary
assert smoke_summary["runId"] == "test-smoke", smoke_summary
assert smoke_summary["exchangeRoot"] == str(exchange_root), smoke_summary
assert smoke_summary["summaryPath"] == str(smoke_summary_path), smoke_summary
assert smoke_summary["startedAtUtc"].endswith("Z"), smoke_summary
assert smoke_summary["observedAtUtc"].endswith("Z"), smoke_summary
assert isinstance(smoke_summary["elapsedSeconds"], (int, float)), smoke_summary
assert smoke_summary["inputs"]["runId"] == "test-smoke", smoke_summary
assert isinstance(smoke_summary["gates"], list) and len(smoke_summary["gates"]) == 1, smoke_summary
assert smoke_summary["gates"][0]["status"] == "passed", smoke_summary
assert smoke_summary["error"] is None, smoke_summary
assert smoke_summary["cleanup"] is None, smoke_summary

assert smoke_summary["artifacts"] == {
    "reportPath": str(smoke_summary_path.parent / "live-smoke-report.json"),
    "stdoutPath": str(smoke_summary_path.parent / "live-smoke.stdout.log"),
    "stderrPath": str(smoke_summary_path.parent / "live-smoke.stderr.log"),
}, smoke_summary
assert smoke_summary["reportOk"] is True, smoke_summary
assert Path(smoke_summary["artifacts"]["stdoutPath"]).read_text(encoding="utf-8").startswith("REPORT "), smoke_summary
assert "stub stderr marker" in Path(smoke_summary["artifacts"]["stderrPath"]).read_text(encoding="utf-8"), smoke_summary

download_summary = json.loads(Path(payloads["wo mail download"]["summaryPath"]).read_text(encoding="utf-8"))
assert download_summary["mailRunId"] == "test-mail-download", download_summary
assert download_summary["attachmentsSaved"] == 0, download_summary
assert download_summary["attachmentsSkipped"] == 1, download_summary
assert download_summary["savedCount"] == 0, download_summary
assert download_summary["skippedCount"] == 1, download_summary

unsafe_summary_path = Path(unsafe_download_json["summaryPath"])
unsafe_summary = json.loads(unsafe_summary_path.read_text(encoding="utf-8"))
assert unsafe_summary["success"] is False, unsafe_summary
assert unsafe_summary["status"] == "usage_error", unsafe_summary
assert unsafe_summary["command"] == "wo mail download", unsafe_summary
assert unsafe_summary["runId"] == "test-mail-download-unsafe", unsafe_summary
assert unsafe_summary["error"]["code"] == "mail_download_scope_required", unsafe_summary
assert unsafe_summary["gates"][0]["status"] == "failed", unsafe_summary

for command, run_id, failure_payload in (
    ("wo mail folders", "test-mail-folders-fail", failure_payloads["wo mail folders"]),
    ("wo mail search", "test-mail-search-fail", failure_payloads["wo mail search"]),
    ("wo mail download", "test-mail-download-fail", failure_payloads["wo mail download"]),
    ("wo auth microsoft cleanup", "test-auth-cleanup-fail", failure_payloads["wo auth microsoft cleanup"]),
    ("wo auth microsoft device-login", "test-auth-device-fail", failure_payloads["wo auth microsoft device-login"]),
    ("wo auth microsoft authorize-probe", "test-auth-authorize-fail", failure_payloads["wo auth microsoft authorize-probe"]),
):
    summary = json.loads(Path(failure_payload["summaryPath"]).read_text(encoding="utf-8"))
    assert summary["success"] is False, summary
    assert summary["command"] == command, summary
    assert summary["runId"] == run_id, summary
    assert summary["gates"][0]["status"] == "failed", summary
    assert summary["error"] is not None, summary

smoke_fail_summary = json.loads(Path(failure_payloads["wo smoke"]["summaryPath"]).read_text(encoding="utf-8"))
assert smoke_fail_summary["success"] is False, smoke_fail_summary
assert smoke_fail_summary["status"] == "live-smoke-failed", smoke_fail_summary
assert smoke_fail_summary["command"] == "wo smoke", smoke_fail_summary
assert smoke_fail_summary["runId"] == "test-smoke-fail", smoke_fail_summary
assert smoke_fail_summary["reportOk"] is False, smoke_fail_summary
assert smoke_fail_summary["gates"][0]["status"] == "failed", smoke_fail_summary
assert smoke_fail_summary["error"]["code"] == "live_smoke_failed", smoke_fail_summary
PY
