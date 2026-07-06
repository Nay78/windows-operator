#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
tmp_root="$(mktemp -d)"
stub="$tmp_root/external_consumer_stub.py"
port_file="$tmp_root/port"

cleanup() {
  if [[ -n "${stub_pid:-}" ]]; then
    kill "$stub_pid" 2>/dev/null || true
    wait "$stub_pid" 2>/dev/null || true
  fi
  rm -rf "$tmp_root"
}
trap cleanup EXIT

cat >"$stub" <<'PY'
#!/usr/bin/env python3
import json
import os
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse

MODE = os.environ.get("WO_EXTERNAL_SMOKE_STUB_MODE", "success")
ARTIFACT_BODY = b"external smoke proof"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, _format, *_args):
        return

    def do_GET(self):
        path = urlparse(self.path).path
        if path == "/v1/health":
            self.write_json(200, {"status": "ok", "runtimeMode": "headless-host"})
            return

        if path == "/v1/capabilities":
            if MODE == "missing-capabilities":
                self.operator_error(404, "route_not_found", "notFound", "capabilities route missing")
                return
            self.write_json(
                200,
                {
                    "contractVersion": "0.1.0",
                    "host": {
                        "status": "ok",
                        "runtimeMode": "headless-host",
                        "restBaseUrl": f"http://{self.headers['Host']}",
                        "desktopAgentStatus": "ok",
                    },
                    "features": {
                        "powerpoint.online.update": {"available": True, "surface": "stable"}
                    },
                    "checkedAtUtc": "2026-07-06T12:00:00Z",
                },
            )
            return

        if path == "/v1/mail/runs/__missing_external_smoke__":
            self.operator_error(404, "mail_run_not_found", "notFound", "mail run not found")
            return

        if path == "/v1/runs/proof-run/artifacts":
            self.write_json(
                200,
                {
                    "runId": "proof-run",
                    "checkedAtUtc": "2026-07-06T12:00:00Z",
                    "artifacts": [
                        {
                            "artifactId": "proof.txt",
                            "href": "/v1/artifacts/proof.txt",
                            "mediaType": "text/plain",
                            "bytes": len(ARTIFACT_BODY),
                        }
                    ],
                },
            )
            return

        if path == "/v1/artifacts/proof.txt":
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(ARTIFACT_BODY)))
            self.end_headers()
            self.wfile.write(ARTIFACT_BODY)
            return

        self.operator_error(404, "route_not_found", "notFound", "route not found")

    def write_json(self, status, value):
        payload = json.dumps(value).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def operator_error(self, status, code, category, message):
        self.write_json(
            status,
            {
                "code": code,
                "category": category,
                "retryable": False,
                "correlationId": "external-smoke-test",
                "message": message,
            },
        )


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: external_consumer_stub.py PORT_FILE")
    server = HTTPServer(("127.0.0.1", 0), Handler)
    with open(sys.argv[1], "w", encoding="utf-8") as handle:
        handle.write(str(server.server_port))
    server.serve_forever()


if __name__ == "__main__":
    main()
PY

WO_EXTERNAL_SMOKE_STUB_MODE=success python3 "$stub" "$port_file" &
stub_pid=$!

for _ in {1..100}; do
  [[ -s "$port_file" ]] && break
  sleep 0.05
done
[[ -s "$port_file" ]]

base_url="http://127.0.0.1:$(cat "$port_file")"

success_output="$(
  WINDOWS_OPERATOR_BASE_URL="$base_url" \
  WINDOWS_OPERATOR_SMOKE_RUN_ID=proof-run \
  "$repo_root/scripts/external-consumer-smoke.sh"
)"

grep -q "health status=ok runtime=headless-host" <<<"$success_output"
grep -q "capabilities contract=0.1.0" <<<"$success_output"
grep -q "negative code=mail_run_not_found category=notFound retryable=false" <<<"$success_output"
grep -q "artifact id=proof.txt media=text/plain bytes=20" <<<"$success_output"

kill "$stub_pid" 2>/dev/null || true
wait "$stub_pid" 2>/dev/null || true
unset stub_pid
rm -f "$port_file"

WO_EXTERNAL_SMOKE_STUB_MODE=missing-capabilities python3 "$stub" "$port_file" &
stub_pid=$!

for _ in {1..100}; do
  [[ -s "$port_file" ]] && break
  sleep 0.05
done
[[ -s "$port_file" ]]

base_url="http://127.0.0.1:$(cat "$port_file")"

set +e
failure_output="$(
  WINDOWS_OPERATOR_BASE_URL="$base_url" \
  "$repo_root/scripts/external-consumer-smoke.sh" 2>&1
)"
failure_status=$?
set -e

[[ "$failure_status" -ne 0 ]]
grep -q "capabilities contract check failed for GET /v1/capabilities" <<<"$failure_output"
grep -q "compare live /openapi.json" <<<"$failure_output"

echo "external consumer smoke tests passed"
