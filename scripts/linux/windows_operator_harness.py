from __future__ import annotations

import datetime as dt
import json
import os
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


DEFAULT_HOST_URL = os.environ.get("WINDOWS_OPERATOR_HOST_URL", "http://127.0.0.1:43117")
DEFAULT_EXCHANGE_ROOT = Path(
    os.environ.get("WINDOWS_OPERATOR_EXCHANGE_ROOT", "/var/lib/windows-server/shared/operator-exchange")
)


def utc_stamp() -> str:
    return dt.datetime.now(dt.UTC).strftime("%Y%m%dT%H%M%SZ").lower()


def utc_now() -> str:
    return dt.datetime.now(dt.UTC).isoformat().replace("+00:00", "Z")


def default_exchange_root() -> Path:
    return DEFAULT_EXCHANGE_ROOT


def next_run_id(prefix: str) -> str:
    return f"{prefix}-{utc_stamp()}"


def run_root(exchange_root: Path, run_id: str) -> Path:
    return exchange_root / "runs" / run_id


def summary_path(exchange_root: Path, run_id: str) -> Path:
    return run_root(exchange_root, run_id) / "summary.json"


def write_json(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def read_json(path: Path) -> Any:
    try:
        with path.open("r", encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, json.JSONDecodeError):
        return None


def parse_json(body: bytes) -> Any:
    if not body:
        return None
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None


def request_json(
    base_url: str,
    method: str,
    path: str,
    payload: dict[str, Any] | None = None,
    timeout_seconds: int = 30,
) -> tuple[int | str, bytes, Any]:
    data = None
    headers = {"Accept": "application/json"}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}",
        data=data,
        headers=headers,
        method=method,
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            body = response.read()
            return response.status, body, parse_json(body)
    except urllib.error.HTTPError as exc:
        body = exc.read()
        return exc.code, body, parse_json(body)
    except Exception as exc:
        return "exception", str(exc).encode("utf-8", "replace"), None


def emit_contract_summary(
    *,
    exchange_root: Path,
    run_id: str,
    command: str,
    base_url: str,
    response_name: str,
    response_payload: Any,
    success: bool,
    status: str,
    http_status: int | str,
    started_at_utc: str,
    elapsed_seconds: float,
    inputs: dict[str, Any],
    gates: list[dict[str, Any]],
    error: dict[str, Any] | None,
    cleanup: dict[str, Any] | None,
    extra: dict[str, Any] | None = None,
    json_stdout: bool = False,
) -> int:
    root = run_root(exchange_root, run_id)
    response_path = root / response_name
    write_json(response_path, response_payload)
    final_summary_path = summary_path(exchange_root, run_id)
    summary = {
        "success": success,
        "status": status,
        "command": command,
        "runId": run_id,
        "baseUrl": base_url,
        "exchangeRoot": str(exchange_root),
        "summaryPath": str(final_summary_path),
        "startedAtUtc": started_at_utc,
        "elapsedSeconds": round(elapsed_seconds, 3),
        "inputs": inputs,
        "artifacts": {
            "responsePath": str(response_path),
        },
        "gates": gates,
        "error": error,
        "cleanup": cleanup,
        "httpStatus": http_status,
        "observedAtUtc": utc_now(),
    }
    if extra:
        summary.update(extra)
    write_json(final_summary_path, summary)
    if json_stdout:
        print(json.dumps({"summaryPath": str(final_summary_path)}))
    else:
        print(final_summary_path)
    return 0 if success else 1
