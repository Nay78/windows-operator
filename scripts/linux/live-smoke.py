#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Callable


Json = dict[str, Any] | list[Any]
Expectation = Callable[[Any, bytes, dict[str, str]], tuple[bool, str]]


def utc_stamp() -> str:
    return dt.datetime.now(dt.UTC).strftime("%Y%m%dT%H%M%SZ")


def utc_now() -> str:
    return dt.datetime.now(dt.UTC).isoformat().replace("+00:00", "Z")


def json_dump(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def resolve_repo_path(path: str) -> Path:
    candidate = Path(path)
    if candidate.is_absolute():
        return candidate

    return repo_root() / candidate


class SmokeClient:
    def __init__(self, base_url: str, timeout_seconds: int) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
    ) -> tuple[int | str, dict[str, str], bytes, Any]:
        data = None
        headers = {"Accept": "application/json"}
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"

        request = urllib.request.Request(
            f"{self.base_url}{path}",
            data=data,
            method=method,
            headers=headers,
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                body = response.read()
                return response.status, dict(response.headers), body, parse_json(body)
        except urllib.error.HTTPError as exc:
            body = exc.read()
            return exc.code, dict(exc.headers), body, parse_json(body)
        except Exception as exc:
            return "exception", {}, str(exc).encode("utf-8", "replace"), None


class Recorder:
    def __init__(self) -> None:
        self.results: list[dict[str, Any]] = []

    def add(
        self,
        name: str,
        ok: bool,
        status: int | str,
        detail: str,
        **extra: Any,
    ) -> None:
        item = {
            "name": name,
            "ok": bool(ok),
            "status": status,
            "detail": detail,
        }
        item.update(extra)
        self.results.append(item)
        print(f"{'PASS' if ok else 'FAIL'} {name}: {detail}", flush=True)


def parse_json(body: bytes) -> Any:
    if not body:
        return None
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None


def expect_dict_key(key: str, value: Any) -> Expectation:
    def check(parsed: Any, _body: bytes, _headers: dict[str, str]) -> tuple[bool, str]:
        if not isinstance(parsed, dict):
            return False, "non-json-object"
        return parsed.get(key) == value, f"{key}={parsed.get(key)}"

    return check


def call(
    recorder: Recorder,
    client: SmokeClient,
    name: str,
    method: str,
    path: str,
    payload: dict[str, Any] | None = None,
    expected_status: set[int] | None = None,
    expect: Expectation | None = None,
    keep: Callable[[Any, bytes, dict[str, str]], dict[str, Any]] | None = None,
) -> tuple[Any, bytes]:
    expected = expected_status or set(range(200, 300))
    started = time.time()
    status, headers, body, parsed = client.request(method, path, payload)
    ok = isinstance(status, int) and status in expected
    detail = f"HTTP {status}"
    if status == "exception":
        detail = body.decode("utf-8", "replace")[:240]
        ok = False
    elif expect is not None:
        try:
            expectation_ok, expectation_detail = expect(parsed, body, headers)
            ok = ok and expectation_ok
            detail = expectation_detail or detail
        except Exception as exc:
            ok = False
            detail = f"expectation failed: {exc}"

    extra: dict[str, Any] = {
        "method": method,
        "path": path,
        "elapsedMs": round((time.time() - started) * 1000),
    }
    if keep is not None:
        extra.update(keep(parsed, body, headers))
    elif isinstance(parsed, list):
        extra["count"] = len(parsed)
    elif isinstance(parsed, dict):
        extra["keys"] = sorted(parsed.keys())

    recorder.add(name, ok, status, detail, **extra)
    return parsed, body


def host_path_exists(parsed: Any) -> tuple[bool, str]:
    artifact = parsed.get("artifact") if isinstance(parsed, dict) else None
    host_path = artifact.get("hostPath") if isinstance(artifact, dict) else None
    return bool(host_path and Path(host_path).exists()), f"artifact={host_path}"


def plain_http(url: str, timeout_seconds: int) -> tuple[int | str, bytes]:
    request = urllib.request.Request(url, method="GET")
    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read()
    except Exception as exc:
        return "exception", str(exc).encode("utf-8", "replace")


def fetch_url(
    url: str,
    timeout_seconds: int,
    *,
    insecure_tls: bool = False,
) -> tuple[int | str, dict[str, str], bytes]:
    request = urllib.request.Request(url, method="GET", headers={"Accept": "*/*"})
    context = ssl._create_unverified_context() if insecure_tls and url.startswith("https://") else None
    kwargs: dict[str, Any] = {}
    if context is not None:
        kwargs["context"] = context

    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds, **kwargs) as response:
            return response.status, dict(response.headers), response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, dict(exc.headers), exc.read()
    except Exception as exc:
        return "exception", {}, str(exc).encode("utf-8", "replace")


def load_json_file(path: Path) -> Any:
    try:
        with path.open("r", encoding="utf-8-sig") as handle:
            return json.load(handle)
    except (OSError, json.JSONDecodeError):
        return None


def parse_first_json_value(text: str) -> Any:
    decoder = json.JSONDecoder()
    stripped = text.strip()
    for index, char in enumerate(stripped):
        if char not in "[{":
            continue
        try:
            parsed, _ = decoder.raw_decode(stripped[index:])
            return parsed
        except json.JSONDecodeError:
            continue

    return None


def read_script_stdout_json(result_path: Path | None) -> Any:
    if result_path is None:
        return None

    stdout_path = result_path.parent / "stdout.txt"
    try:
        text = stdout_path.read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return None

    return parse_first_json_value(text)


def run_windows_script(
    recorder: Recorder,
    args: argparse.Namespace,
    name: str,
    script_rel: str,
    script_args: list[str],
    run_suffix: str,
) -> tuple[Path | None, Any]:
    runner = resolve_repo_path(args.windows_runner)
    script = resolve_repo_path(script_rel)
    if not runner.exists():
        recorder.add(name, False, "missing", f"runner missing: {runner}")
        return None, None
    if not script.exists():
        recorder.add(name, False, "missing", f"script missing: {script}")
        return None, None

    script_run_id = f"{args.run_id}-{run_suffix}".lower()
    env = os.environ.copy()
    env["WINDOWS_OPERATOR_RUN_ID"] = script_run_id
    started = time.time()

    try:
        completed = subprocess.run(
            [str(runner), script_rel, *script_args],
            cwd=repo_root(),
            env=env,
            capture_output=True,
            text=True,
            timeout=args.windows_runner_timeout_seconds,
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        recorder.add(
            name,
            False,
            "timeout",
            f"timed out after {args.windows_runner_timeout_seconds}s",
            elapsedMs=round((time.time() - started) * 1000),
            stdout=(exc.stdout or "")[-500:] if isinstance(exc.stdout, str) else None,
            stderr=(exc.stderr or "")[-500:] if isinstance(exc.stderr, str) else None,
        )
        return None, None

    stdout_lines = [line for line in completed.stdout.splitlines() if line.strip()]
    result_path = Path(stdout_lines[-1]) if stdout_lines else Path(args.exchange_root) / "runs" / script_run_id / "result.json"
    result = load_json_file(result_path)
    ok = completed.returncode == 0 and isinstance(result, dict) and result.get("status") == "succeeded"
    detail = (
        f"status={result.get('status')} exit={result.get('exitCode')}"
        if isinstance(result, dict)
        else f"exit={completed.returncode} result=missing"
    )
    extra: dict[str, Any] = {
        "elapsedMs": round((time.time() - started) * 1000),
        "resultPath": str(result_path),
        "scriptRunId": script_run_id,
    }
    if not ok:
        extra["stdout"] = completed.stdout[-500:]
        extra["stderr"] = completed.stderr[-500:]
        if isinstance(result, dict):
            extra["message"] = result.get("message")

    recorder.add(name, ok, completed.returncode, detail, **extra)
    return result_path, read_script_stdout_json(result_path)


def poll_window_by_process(
    client: SmokeClient,
    process_id: int,
    timeout_seconds: int,
) -> tuple[dict[str, Any] | None, int | str]:
    deadline = time.time() + timeout_seconds
    last_status: int | str = "not-started"
    while time.time() < deadline:
        status, _headers, _body, parsed = client.request("GET", "/v1/windows")
        last_status = status
        if isinstance(parsed, list):
            for item in parsed:
                if isinstance(item, dict) and item.get("processId") == process_id:
                    return item, status

        time.sleep(0.5)

    return None, last_status


def find_notepad_text_query(
    recorder: Recorder,
    client: SmokeClient,
    hwnd: int,
) -> dict[str, Any] | None:
    candidates = [
        {"windowHwnd": hwnd, "controlType": "Document", "includeOffscreen": False, "maxResults": 5},
        {"windowHwnd": hwnd, "controlType": "Edit", "includeOffscreen": False, "maxResults": 5},
        {"windowHwnd": hwnd, "name": "Text Editor", "includeOffscreen": False, "maxResults": 5},
    ]
    attempts: list[dict[str, Any]] = []
    for query in candidates:
        status, _headers, _body, parsed = client.request("POST", "/v1/uia/query", query)
        count = len(parsed) if isinstance(parsed, list) else 0
        attempts.append(
            {
                "status": status,
                "count": count,
                "query": query,
                "first": parsed[0] if isinstance(parsed, list) and parsed else None,
            }
        )
        if isinstance(status, int) and 200 <= status < 300 and count > 0:
            recorder.add(
                "notepad_uia_text_query",
                True,
                status,
                f"elements={count} query={query}",
                attempts=attempts,
            )
            return query

    recorder.add(
        "notepad_uia_text_query",
        False,
        attempts[-1]["status"] if attempts else "skipped",
        "no editable Notepad element found",
        attempts=attempts,
    )
    return None


def run_notepad_checks(recorder: Recorder, client: SmokeClient, args: argparse.Namespace, run_id: str) -> None:
    result_path, launch = run_windows_script(
        recorder,
        args,
        "notepad_launch_script",
        args.notepad_launch_script,
        ["-RunId", run_id],
        "notepad-launch",
    )
    process_id = launch.get("processId") if isinstance(launch, dict) else None
    if not isinstance(process_id, int) or process_id <= 0:
        recorder.add(
            "notepad_window_found",
            False,
            "skipped",
            "launch script did not return processId",
            launch=launch,
            resultPath=str(result_path) if result_path else None,
        )
        recorder.add("notepad_cleanup_script", False, "skipped", "no processId")
        return

    try:
        window, status = poll_window_by_process(client, process_id, args.notepad_wait_seconds)
        recorder.add(
            "notepad_window_found",
            window is not None,
            status,
            (
                f"hwnd={window.get('hwnd')} title={window.get('title')}"
                if window is not None
                else f"not found pid={process_id}"
            ),
            processId=process_id,
            window=window,
        )
        if window is None:
            return

        hwnd = window.get("hwnd")
        if not isinstance(hwnd, int):
            recorder.add("notepad_activate", False, "skipped", "window hwnd missing", window=window)
            return

        call(
            recorder,
            client,
            "notepad_activate",
            "POST",
            f"/v1/windows/{hwnd}/activate",
            expect=expect_dict_key("success", True),
        )

        query = find_notepad_text_query(recorder, client, hwnd)
        if query is not None:
            call(
                recorder,
                client,
                "notepad_uia_type_text",
                "POST",
                "/v1/uia/type",
                {
                    "query": query,
                    "text": f"Windows Operator live Notepad smoke {run_id}",
                    "append": False,
                    "submit": False,
                },
                expect=expect_dict_key("success", True),
            )
        else:
            recorder.add("notepad_uia_type_text", False, "skipped", "no text query")

        call(
            recorder,
            client,
            "notepad_screenshot_foreground",
            "POST",
            "/v1/desktop/screenshot",
            {"target": "foreground", "runId": run_id, "label": "notepad"},
            expect=lambda parsed, _body, _headers: host_path_exists(parsed),
            keep=lambda parsed, _body, _headers: {
                "artifactHostPath": (parsed.get("artifact") or {}).get("hostPath") if isinstance(parsed, dict) else None,
                "backend": parsed.get("backend") if isinstance(parsed, dict) else None,
            },
        )
    finally:
        run_windows_script(
            recorder,
            args,
            "notepad_cleanup_script",
            args.notepad_cleanup_script,
            ["-ProcessId", str(process_id)],
            "notepad-cleanup",
        )


def run_smoke(args: argparse.Namespace) -> dict[str, Any]:
    recorder = Recorder()
    client = SmokeClient(args.base_url, args.timeout_seconds)
    run_id = args.run_id
    started_at = utc_now()

    health, _ = call(
        recorder,
        client,
        "host_health",
        "GET",
        "/v1/health",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict)
            and parsed.get("status") == "ok"
            and parsed.get("runtimeMode") == "headless-host",
            f"{parsed.get('status')} {parsed.get('runtimeMode')}" if isinstance(parsed, dict) else "bad health",
        ),
        keep=lambda parsed, _body, _headers: {
            "runtimeMode": parsed.get("runtimeMode") if isinstance(parsed, dict) else None,
            "platform": parsed.get("platform") if isinstance(parsed, dict) else None,
        },
    )

    openapi, _ = call(
        recorder,
        client,
        "openapi",
        "GET",
        "/openapi.json",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and len(parsed.get("paths", {})) >= args.min_openapi_paths,
            f"paths={len(parsed.get('paths', {}))}" if isinstance(parsed, dict) else "bad openapi",
        ),
        keep=lambda parsed, _body, _headers: {
            "pathCount": len(parsed.get("paths", {})) if isinstance(parsed, dict) else None,
        },
    )

    required_paths = {
        "/v1/health",
        "/v1/windows",
        "/v1/uia/query",
        "/v1/browser/edge/session/start",
        "/v1/mail/status",
        "/v1/powerpoint/jobs",
    }
    paths = set(openapi.get("paths", {}).keys()) if isinstance(openapi, dict) else set()
    missing = sorted(required_paths - paths)
    recorder.add(
        "openapi_required_paths",
        not missing,
        "check",
        "all required paths present" if not missing else f"missing={','.join(missing)}",
        missing=missing,
    )

    codex_status, codex_body = plain_http(args.codex_url, args.timeout_seconds)
    recorder.add(
        "codex_app_server_tunnel",
        codex_status == 400 and b"upgrade" in codex_body.lower(),
        codex_status,
        codex_body.decode("utf-8", "replace")[:160],
    )

    windows, _ = call(
        recorder,
        client,
        "windows_list",
        "GET",
        "/v1/windows",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, list) and len(parsed) > 0,
            f"windows={len(parsed) if isinstance(parsed, list) else 'n/a'}",
        ),
    )

    foreground, _ = call(
        recorder,
        client,
        "desktop_foreground",
        "GET",
        "/v1/desktop/foreground",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and bool(parsed.get("hwnd")),
            f"hwnd={parsed.get('hwnd')} title={parsed.get('title')}" if isinstance(parsed, dict) else "no hwnd",
        ),
        keep=lambda parsed, _body, _headers: {
            "hwnd": parsed.get("hwnd") if isinstance(parsed, dict) else None,
            "title": parsed.get("title") if isinstance(parsed, dict) else None,
        },
    )
    hwnd = foreground.get("hwnd") if isinstance(foreground, dict) else None

    call(
        recorder,
        client,
        "desktop_screenshot_foreground",
        "POST",
        "/v1/desktop/screenshot",
        {"target": "foreground", "runId": run_id, "label": "foreground"},
        expect=lambda parsed, _body, _headers: host_path_exists(parsed),
        keep=lambda parsed, _body, _headers: {
            "artifactHostPath": (parsed.get("artifact") or {}).get("hostPath") if isinstance(parsed, dict) else None,
            "backend": parsed.get("backend") if isinstance(parsed, dict) else None,
        },
    )

    if hwnd is not None:
        call(
            recorder,
            client,
            "window_screenshot_png",
            "GET",
            f"/v1/windows/{hwnd}/screenshot?format=png",
            expect=lambda parsed, _body, _headers: (
                isinstance(parsed, dict)
                and str(parsed.get("imageBase64", "")).startswith("iVBOR")
                and parsed.get("pixelWidth", 0) > 0,
                f"{parsed.get('pixelWidth')}x{parsed.get('pixelHeight')}" if isinstance(parsed, dict) else "bad png",
            ),
            keep=lambda parsed, _body, _headers: {
                "pixelWidth": parsed.get("pixelWidth") if isinstance(parsed, dict) else None,
                "pixelHeight": parsed.get("pixelHeight") if isinstance(parsed, dict) else None,
            },
        )
    else:
        recorder.add("window_screenshot_png", False, "skipped", "no foreground hwnd")

    call(
        recorder,
        client,
        "uia_query_windows",
        "POST",
        "/v1/uia/query",
        {"controlType": "Window", "includeOffscreen": False, "maxResults": 5},
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, list) and len(parsed) > 0,
            f"elements={len(parsed) if isinstance(parsed, list) else 'n/a'}",
        ),
    )

    call(
        recorder,
        client,
        "input_hotkey_shift",
        "POST",
        "/v1/input/hotkey",
        {"keys": ["shift"]},
        expect=expect_dict_key("success", True),
    )

    if args.include_notepad:
        run_notepad_checks(recorder, client, args, run_id)

    run_edge_checks(recorder, client, run_id)
    run_auth_dry_run_checks(recorder, client, run_id)
    run_mail_checks(recorder, client, run_id)
    run_powerpoint_checks(recorder, client, args, run_id)

    completed_at = utc_now()
    return {
        "baseUrl": args.base_url,
        "codexUrl": args.codex_url,
        "completedAtUtc": completed_at,
        "failed": sum(1 for item in recorder.results if not item["ok"]),
        "ok": all(item["ok"] for item in recorder.results),
        "passed": sum(1 for item in recorder.results if item["ok"]),
        "notepadIncluded": bool(args.include_notepad),
        "results": recorder.results,
        "runId": run_id,
        "startedAtUtc": started_at,
    }


def run_edge_checks(recorder: Recorder, client: SmokeClient, run_id: str) -> None:
    edge_id = f"{run_id}-edge"
    edge, _ = call(
        recorder,
        client,
        "edge_session_start_example",
        "POST",
        "/v1/browser/edge/session/start",
        {
            "sessionId": edge_id,
            "startUrl": "https://example.com",
            "profileMode": "temp",
            "pageLoadSeconds": 5,
        },
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict)
            and parsed.get("success") is True
            and "example.com" in (parsed.get("url") or ""),
            f"title={parsed.get('title')} state={parsed.get('browserState')}" if isinstance(parsed, dict) else "bad edge state",
        ),
    )
    if isinstance(edge, dict) and edge.get("success") is True:
        try:
            call(
                recorder,
                client,
                "edge_dom_click_link",
                "POST",
                f"/v1/browser/edge/session/{edge_id}/dom/click",
                {"selector": "a", "timeoutSeconds": 8},
                expect=lambda parsed, _body, _headers: (
                    isinstance(parsed, dict) and parsed.get("success") is True,
                    f"matched={parsed.get('matchedBy')} tag={parsed.get('tagName')}" if isinstance(parsed, dict) else "click failed",
                ),
            )
            call(
                recorder,
                client,
                "edge_session_screenshot",
                "POST",
                f"/v1/browser/edge/session/{edge_id}/screenshot",
                {"target": "foreground", "runId": run_id, "label": "edge-session"},
                expect=lambda parsed, _body, _headers: host_path_exists(parsed),
            )
        finally:
            call(
                recorder,
                client,
                "edge_session_cleanup",
                "POST",
                f"/v1/browser/edge/session/{edge_id}/cleanup",
                expect=lambda parsed, _body, _headers: (
                    isinstance(parsed, dict) and parsed.get("success") is True and parsed.get("isAlive") is False,
                    f"state={parsed.get('browserState')} alive={parsed.get('isAlive')}" if isinstance(parsed, dict) else "cleanup failed",
                ),
            )
    else:
        recorder.add("edge_dom_click_link", False, "skipped", "edge session did not start")
        recorder.add("edge_session_screenshot", False, "skipped", "edge session did not start")
        recorder.add("edge_session_cleanup", False, "skipped", "edge session did not start")

    form_id = f"{run_id}-form"
    form, _ = call(
        recorder,
        client,
        "edge_session_start_form",
        "POST",
        "/v1/browser/edge/session/start",
        {
            "sessionId": form_id,
            "startUrl": "https://httpbin.org/forms/post",
            "profileMode": "temp",
            "pageLoadSeconds": 5,
        },
        expect=expect_dict_key("success", True),
    )
    if isinstance(form, dict) and form.get("success") is True:
        try:
            call(
                recorder,
                client,
                "edge_dom_fill_form",
                "POST",
                f"/v1/browser/edge/session/{form_id}/dom/fill",
                {"selector": "input[name=custname]", "value": "Windows Operator live smoke", "timeoutSeconds": 8},
                expect=lambda parsed, _body, _headers: (
                    isinstance(parsed, dict) and parsed.get("success") is True,
                    f"matched={parsed.get('matchedBy')} tag={parsed.get('tagName')}" if isinstance(parsed, dict) else "fill failed",
                ),
            )
        finally:
            call(
                recorder,
                client,
                "edge_form_cleanup",
                "POST",
                f"/v1/browser/edge/session/{form_id}/cleanup",
                expect=expect_dict_key("success", True),
            )
    else:
        recorder.add("edge_dom_fill_form", False, "skipped", "form session did not start")
        recorder.add("edge_form_cleanup", False, "skipped", "form session did not start")

    call(
        recorder,
        client,
        "edge_reset_dry_run",
        "POST",
        "/v1/browser/edge/reset",
        {"dryRun": True},
        expect=expect_dict_key("success", True),
    )


def run_auth_dry_run_checks(recorder: Recorder, client: SmokeClient, run_id: str) -> None:
    device_id = f"{run_id}-device"
    call(
        recorder,
        client,
        "auth_device_login_dry_run",
        "POST",
        "/v1/auth/microsoft/device-login",
        {
            "runId": device_id,
            "deviceCode": "ABCD-EFGH",
            "dryRun": True,
            "pageLoadSeconds": 2,
            "verificationWaitSeconds": 2,
        },
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("success") is True and parsed.get("status") == "dryRun",
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "device dry-run failed",
        ),
    )
    call(
        recorder,
        client,
        "auth_device_login_status",
        "GET",
        f"/v1/auth/microsoft/device-login/status/{device_id}",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("runId") == device_id,
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "device status failed",
        ),
    )

    auth_id = f"{run_id}-authorize"
    authorize_url = (
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize"
        "?client_id=00000000-0000-0000-0000-000000000000"
        "&response_type=code"
        "&redirect_uri=http%3A%2F%2Flocalhost%2Fcallback"
        "&scope=openid"
    )
    call(
        recorder,
        client,
        "auth_authorize_probe_dry_run",
        "POST",
        "/v1/auth/microsoft/authorize-probe",
        {
            "runId": auth_id,
            "authorizeUrl": authorize_url,
            "dryRun": True,
            "pageLoadSeconds": 2,
            "observationTimeoutSeconds": 2,
        },
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("success") is True and parsed.get("status") == "dryRun",
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "authorize dry-run failed",
        ),
    )
    call(
        recorder,
        client,
        "auth_authorize_probe_status",
        "GET",
        f"/v1/auth/microsoft/authorize-probe/status/{auth_id}",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("runId") == auth_id,
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "authorize status failed",
        ),
    )
    call(
        recorder,
        client,
        "auth_cleanup_dry_run",
        "POST",
        "/v1/auth/microsoft/cleanup",
        {"dryRun": True, "preserveRecentSeconds": 0},
        expect=expect_dict_key("success", True),
    )


def run_mail_checks(recorder: Recorder, client: SmokeClient, run_id: str) -> None:
    call(
        recorder,
        client,
        "mail_status",
        "GET",
        "/v1/mail/status",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and "workerAvailable" in parsed,
            f"workerAvailable={parsed.get('workerAvailable')}" if isinstance(parsed, dict) else "mail status failed",
        ),
    )
    call(
        recorder,
        client,
        "mail_search_cached_negative",
        "POST",
        "/v1/mail/messages/search",
        {
            "subjectContains": f"__windows_operator_live_smoke_no_match__{run_id}",
            "maxResults": 1,
            "freshness": "cached",
        },
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and "messages" in parsed,
            f"messages={len(parsed.get('messages') or [])}" if isinstance(parsed, dict) else "mail search failed",
        ),
    )
    call(
        recorder,
        client,
        "mail_missing_run_negative",
        "GET",
        "/v1/mail/runs/windows-operator-live-smoke-missing-run",
        expected_status={404},
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("code") == "mail_run_not_found",
            f"code={parsed.get('code')}" if isinstance(parsed, dict) else "mail missing run failed",
        ),
    )


def run_powerpoint_checks(recorder: Recorder, client: SmokeClient, args: argparse.Namespace, run_id: str) -> None:
    if not args.skip_powerpoint_addin:
        status, headers, body = fetch_url(args.powerpoint_addin_url, args.timeout_seconds, insecure_tls=True)
        recorder.add(
            "powerpoint_addin_taskpane",
            status == 200 and b"Windows Operator PowerPoint" in body,
            status,
            (
                f"bytes={len(body)} type={headers.get('Content-Type')}"
                if isinstance(status, int)
                else body.decode("utf-8", "replace")[:160]
            ),
            url=args.powerpoint_addin_url,
            contentType=headers.get("Content-Type"),
        )

    job_id = f"{run_id}-ppt"
    artifact_id = "live-pixel"
    job = {
        "jobId": job_id,
        "requestedBy": "live-smoke",
        "createdAt": utc_now(),
        "operations": [
            {
                "kind": "replaceImage",
                "targetId": "live-image-target",
                "artifact": {
                    "artifactId": artifact_id,
                    "url": "data:image/png;base64,AQID",
                    "mediaType": "image/png",
                },
                "altText": "live smoke pixel",
                "fit": "contain",
            }
        ],
    }
    call(
        recorder,
        client,
        "powerpoint_job_enqueue",
        "POST",
        "/v1/powerpoint/jobs",
        job,
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("status") == "queued" and parsed.get("jobId") == job_id,
            f"status={parsed.get('status')} job={parsed.get('jobId')}" if isinstance(parsed, dict) else "enqueue failed",
        ),
    )
    call(
        recorder,
        client,
        "powerpoint_job_get_queued",
        "GET",
        f"/v1/powerpoint/jobs/{job_id}",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("status") == "queued",
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "get queued failed",
        ),
    )
    _artifact, artifact_body = call(
        recorder,
        client,
        "powerpoint_artifact_get",
        "GET",
        f"/v1/powerpoint/jobs/{job_id}/artifacts/{artifact_id}",
        expect=lambda _parsed, body, headers: (
            body == b"\x01\x02\x03" and headers.get("Content-Type", "").startswith("image/png"),
            f"bytes={len(body)} type={headers.get('Content-Type')}",
        ),
    )
    call(
        recorder,
        client,
        "powerpoint_job_claim",
        "POST",
        "/v1/powerpoint/jobs/claim",
        {"workerId": "live-smoke"},
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("jobId") == job_id,
            f"claimed={parsed.get('jobId')}" if isinstance(parsed, dict) else "claim failed",
        ),
    )
    call(
        recorder,
        client,
        "powerpoint_job_fail",
        "POST",
        f"/v1/powerpoint/jobs/{job_id}/fail",
        {
            "code": "LIVE_SMOKE",
            "retryable": False,
            "operatorMessage": "Live smoke marked job failed after claim.",
            "technicalMessage": "synthetic",
        },
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("status") == "failed",
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "fail failed",
        ),
    )
    call(
        recorder,
        client,
        "powerpoint_job_get_failed",
        "GET",
        f"/v1/powerpoint/jobs/{job_id}",
        expect=lambda parsed, _body, _headers: (
            isinstance(parsed, dict) and parsed.get("status") == "failed",
            f"status={parsed.get('status')}" if isinstance(parsed, dict) else "get failed failed",
        ),
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a live Windows Operator smoke test through Host REST.")
    parser.add_argument(
        "--base-url",
        default=os.environ.get("WINDOWS_OPERATOR_BASE_URL", "http://127.0.0.1:43117"),
        help="Windows Operator Host base URL.",
    )
    parser.add_argument(
        "--codex-url",
        default=os.environ.get("WINDOWS_OPERATOR_CODEX_URL", "http://127.0.0.1:43118/"),
        help="Codex app-server tunnel URL for plain HTTP upgrade probe.",
    )
    parser.add_argument(
        "--exchange-root",
        default=os.environ.get(
            "WINDOWS_OPERATOR_EXCHANGE_ROOT",
            "/var/lib/windows-server/shared/operator-exchange",
        ),
        help="Linux exchange root used for default report path.",
    )
    parser.add_argument("--run-id", default=f"live-smoke-{utc_stamp().lower()}")
    parser.add_argument("--output", help="Report JSON path. Default: <exchange-root>/runs/<run-id>/live-smoke-report.json")
    parser.add_argument("--timeout-seconds", type=int, default=90)
    parser.add_argument("--min-openapi-paths", type=int, default=39)
    parser.add_argument("--include-notepad", action="store_true", help="Launch Notepad, type text through UIA, screenshot, then close it.")
    parser.add_argument(
        "--windows-runner",
        default=os.environ.get("WINDOWS_OPERATOR_WINDOWS_RUNNER", "scripts/linux/windows-run-ps.sh"),
        help="Linux runner used for Windows PowerShell helper scripts.",
    )
    parser.add_argument("--windows-runner-timeout-seconds", type=int, default=120)
    parser.add_argument("--notepad-wait-seconds", type=int, default=20)
    parser.add_argument("--notepad-launch-script", default="scripts/windows/start-notepad-smoke.ps1")
    parser.add_argument("--notepad-cleanup-script", default="scripts/windows/stop-notepad-smoke.ps1")
    parser.add_argument(
        "--powerpoint-addin-url",
        default=os.environ.get("WINDOWS_OPERATOR_POWERPOINT_ADDIN_URL", "https://127.0.0.1:3003/taskpane.html"),
        help="PowerPoint add-in taskpane URL to verify.",
    )
    parser.add_argument("--skip-powerpoint-addin", action="store_true", help="Skip PowerPoint taskpane HTTPS check.")
    args = parser.parse_args()
    args.run_id = args.run_id.strip().lower()
    return args


def main() -> int:
    args = parse_args()
    output = (
        Path(args.output)
        if args.output
        else Path(args.exchange_root) / "runs" / args.run_id / "live-smoke-report.json"
    )
    report = run_smoke(args)
    json_dump(output, report)
    print(f"REPORT {output}", flush=True)
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
