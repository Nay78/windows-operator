#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Callable


ROOT = Path(__file__).resolve().parents[2]
OPENAPI_PATH = ROOT / "openapi" / "windows-operator.openapi.json"
POLICY_PATH = ROOT / "openapi" / "windows-operator.operation-policy.json"
JsonCheck = Callable[[Any, bytes], tuple[bool, str]]


def now() -> str:
    return dt.datetime.now(dt.UTC).isoformat().replace("+00:00", "Z")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2, sort_keys=True)
        handle.write("\n")


class Client:
    def __init__(self, base_url: str, timeout: int) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

    def request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        raw: bytes | None = None,
        content_type: str = "application/json",
    ) -> tuple[int | str, dict[str, str], bytes, Any]:
        headers = {"Accept": "*/*"}
        data = raw
        if body is not None:
            data = json.dumps(body).encode()
        if data is not None:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(
            self.base_url + path,
            method=method,
            data=data,
            headers=headers,
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                payload = response.read()
                return response.status, dict(response.headers), payload, parse_json(payload)
        except urllib.error.HTTPError as error:
            payload = error.read()
            return error.code, dict(error.headers), payload, parse_json(payload)
        except Exception as error:
            return "exception", {}, str(error).encode(), None


def parse_json(payload: bytes) -> Any:
    try:
        return json.loads(payload)
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None


def resolve(schema: dict[str, Any], document: dict[str, Any]) -> dict[str, Any]:
    reference = schema.get("$ref")
    if not reference:
        return schema
    value: Any = document
    for segment in reference.removeprefix("#/").split("/"):
        value = value[segment]
    return value


def validate(value: Any, schema: dict[str, Any], document: dict[str, Any], at: str = "$") -> list[str]:
    schema = resolve(schema, document)
    if value is None and schema.get("nullable"):
        return []
    if "allOf" in schema:
        errors: list[str] = []
        for child in schema["allOf"]:
            errors.extend(validate(value, child, document, at))
        return errors
    if "oneOf" in schema:
        candidates = [validate(value, child, document, at) for child in schema["oneOf"]]
        return [] if any(not errors for errors in candidates) else [f"{at}: no oneOf schema matched"]
    expected = schema.get("type")
    if expected == "object":
        if not isinstance(value, dict):
            return [f"{at}: expected object"]
        errors = [
            f"{at}.{name}: required"
            for name in schema.get("required", [])
            if name not in value
        ]
        properties = schema.get("properties", {})
        for name, child in properties.items():
            if name in value:
                errors.extend(validate(value[name], child, document, f"{at}.{name}"))
        if schema.get("additionalProperties") is False:
            errors.extend(f"{at}.{name}: unexpected" for name in value.keys() - properties.keys())
        return errors
    if expected == "array":
        if not isinstance(value, list):
            return [f"{at}: expected array"]
        errors = []
        for index, item in enumerate(value):
            errors.extend(validate(item, schema.get("items", {}), document, f"{at}[{index}]"))
        return errors
    if expected == "string" and not isinstance(value, str):
        return [f"{at}: expected string"]
    if expected == "integer" and (not isinstance(value, int) or isinstance(value, bool)):
        return [f"{at}: expected integer"]
    if expected == "number" and (not isinstance(value, (int, float)) or isinstance(value, bool)):
        return [f"{at}: expected number"]
    if expected == "boolean" and not isinstance(value, bool):
        return [f"{at}: expected boolean"]
    if "enum" in schema and value not in schema["enum"]:
        return [f"{at}: invalid enum {value!r}"]
    return []


class Campaign:
    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.client = Client(args.base_url, args.timeout_seconds)
        self.openapi = json.loads(OPENAPI_PATH.read_text())
        self.policy = json.loads(POLICY_PATH.read_text())
        self.policy_by_id = {item["operationId"]: item for item in self.policy["operations"]}
        self.operations = {
            operation["operationId"]: (path, method.upper(), operation)
            for path, path_item in self.openapi["paths"].items()
            for method, operation in path_item.items()
            if method.lower() in {"get", "post", "put", "patch", "delete"}
        }
        self.records: list[dict[str, Any]] = []
        self.evidence: dict[str, list[dict[str, str | bool]]] = {}
        self.runtime_build = "unavailable"
        self.report_path = Path(args.output)

    def schema_for(self, operation_id: str, status: int) -> tuple[bool, dict[str, Any] | None]:
        operation = self.operations[operation_id][2]
        responses = operation.get("responses", {})
        status_family = f"{status // 100}XX"
        response = responses.get(str(status), responses.get(status_family, responses.get("default")))
        if response is None:
            return False, None
        content = response.get("content", {})
        for media_type in ("application/json", "application/octet-stream"):
            if media_type in content:
                return True, content[media_type].get("schema", {})
        return True, None

    def add_evidence(
        self,
        operation_id: str,
        kind: str,
        endpoint: str,
        fixture: str,
        status: int | str,
        result: str,
        effect: str,
        schema_valid: bool,
    ) -> None:
        if not schema_valid:
            return
        self.evidence.setdefault(operation_id, []).append(
            {
                "kind": kind,
                "endpointOrCommand": endpoint,
                "requestFixture": fixture,
                "observedStatus": str(status),
                "result": result,
                "observedEffect": effect,
                "schemaValid": True,
                "timestamp": now(),
                "runtimeBuild": self.runtime_build,
                "evidenceLocation": str(self.report_path),
            }
        )

    def success(
        self,
        operation_id: str,
        path: str,
        body: dict[str, Any] | None = None,
        check: JsonCheck | None = None,
        fixture: str | None = None,
    ) -> Any:
        _template, method, _operation = self.operations[operation_id]
        status, headers, payload, parsed = self.client.request(method, path, body)
        documented, schema = self.schema_for(operation_id, status) if isinstance(status, int) else (False, None)
        if schema and schema.get("format") == "binary":
            schema_errors = [] if payload else ["empty binary response"]
        elif schema is not None:
            schema_errors = validate(parsed, schema, self.openapi)
        elif documented:
            schema_errors = []
        else:
            schema_errors = [f"undocumented status {status}"]
        check_ok, check_detail = check(parsed, payload) if check else (True, "documented response")
        ok = isinstance(status, int) and 200 <= status < 300 and not schema_errors and check_ok
        detail = check_detail if check_ok else f"effect check failed: {check_detail}"
        if schema_errors:
            detail = "; ".join(schema_errors[:5])
        self.records.append(
            {
                "operationId": operation_id,
                "kind": "success",
                "ok": ok,
                "method": method,
                "path": path,
                "status": status,
                "detail": detail,
                "responseKeys": sorted(parsed) if isinstance(parsed, dict) else None,
                "bytes": len(payload),
                "contentType": headers.get("Content-Type"),
            }
        )
        print(f"{'PASS' if ok else 'FAIL'} success {operation_id}: HTTP {status} {detail}", flush=True)
        if ok:
            policy = self.policy_by_id[operation_id]
            request_fixture = (
                fixture
                if fixture is not None
                else json.dumps(body, sort_keys=True)
                if body is not None
                else "no request body"
            )
            self.add_evidence(
                operation_id,
                "success",
                f"{method} {path}",
                request_fixture,
                status,
                detail,
                policy["observableEffect"],
                True,
            )
        return parsed

    def skipped_success(self, operation_id: str, path: str, reason: str) -> None:
        _template, method, _operation = self.operations[operation_id]
        self.records.append(
            {
                "operationId": operation_id,
                "kind": "success",
                "ok": False,
                "method": method,
                "path": path,
                "status": "blocked",
                "detail": reason,
            }
        )
        print(f"BLOCKED success {operation_id}: {reason}", flush=True)

    def negative(self, operation_id: str, path: str) -> None:
        _template, method, _operation = self.operations[operation_id]
        if method == "GET":
            negative_method, raw = "POST", None
            expected_status = 405
            expected_code = "method_not_allowed"
        else:
            negative_method, raw = ("GET", None) if self.schema_for_body(operation_id) is None else (method, b'{"invalid":')
            expected_status = 405 if negative_method != method else 400
            expected_code = "method_not_allowed" if negative_method != method else "invalid_request"
        status, _headers, _payload, parsed = self.client.request(negative_method, path, raw=raw)
        errors = validate(parsed, {"$ref": "#/components/schemas/OperatorError"}, self.openapi)
        ok = (
            status == expected_status
            and isinstance(parsed, dict)
            and parsed.get("code") == expected_code
            and parsed.get("category") == "validation"
            and not errors
        )
        detail = (
            f"code={parsed.get('code')}; category={parsed.get('category')}"
            if isinstance(parsed, dict)
            else "missing typed error"
        )
        self.records.append(
            {
                "operationId": operation_id,
                "kind": "negative",
                "ok": ok,
                "method": negative_method,
                "path": path,
                "status": status,
                "detail": detail,
            }
        )
        print(f"{'PASS' if ok else 'FAIL'} negative {operation_id}: HTTP {status} {detail}", flush=True)
        if ok:
            self.add_evidence(
                operation_id,
                "negative",
                f"{negative_method} {path}",
                "wrong HTTP method" if negative_method != method else "malformed JSON body",
                status,
                detail,
                "Typed OperatorError observed.",
                True,
            )

    def negative_expected(
        self,
        operation_id: str,
        path: str,
        *,
        body: dict[str, Any] | None = None,
        raw: bytes | None = None,
        expected_status: int,
        expected_code: str,
        expected_category: str,
        fixture: str,
    ) -> None:
        _template, method, _operation = self.operations[operation_id]
        status, _headers, _payload, parsed = self.client.request(method, path, body=body, raw=raw)
        errors = validate(parsed, {"$ref": "#/components/schemas/OperatorError"}, self.openapi)
        ok = (
            status == expected_status
            and isinstance(parsed, dict)
            and parsed.get("category") == expected_category
            and parsed.get("code") == expected_code
            and not errors
        )
        detail = (
            f"code={parsed.get('code')}; category={parsed.get('category')}"
            if isinstance(parsed, dict)
            else "missing typed error"
        )
        self.records.append(
            {
                "operationId": operation_id,
                "kind": "negative",
                "ok": ok,
                "method": method,
                "path": path,
                "status": status,
                "detail": detail,
            }
        )
        print(f"{'PASS' if ok else 'FAIL'} negative {operation_id}: HTTP {status} {detail}", flush=True)
        if ok:
            self.add_evidence(
                operation_id,
                "negative",
                f"{method} {path}",
                fixture,
                status,
                detail,
                f"Typed {expected_code} OperatorError observed.",
                True,
            )

    def negative_not_found(
        self,
        operation_id: str,
        path: str,
        *,
        body: dict[str, Any] | None = None,
        expected_code: str,
        fixture: str = "unknown synthetic resource identifier",
    ) -> None:
        self.negative_expected(
            operation_id,
            path,
            body=body,
            expected_status=404,
            expected_code=expected_code,
            expected_category="notFound",
            fixture=fixture,
        )

    def schema_for_body(self, operation_id: str) -> dict[str, Any] | None:
        operation = self.operations[operation_id][2]
        return (
            operation.get("requestBody", {})
            .get("content", {})
            .get("application/json", {})
            .get("schema")
        )

    def cleanup(self, operation_ids: list[str], endpoint: str, result: str, ok: bool = True) -> None:
        for operation_id in operation_ids:
            self.records.append(
                {
                    "operationId": operation_id,
                    "kind": "cleanup",
                    "ok": ok,
                    "path": endpoint,
                    "status": "observed",
                    "detail": result,
                }
            )
            if ok:
                self.add_evidence(
                    operation_id,
                    "cleanup",
                    endpoint,
                    self.policy_by_id[operation_id]["cleanup"],
                    "observed",
                    result,
                    "Owned fixture removed or no fixture created.",
                    True,
                )

    def complete(self, operation_id: str) -> None:
        path, _method, _operation = self.operations[operation_id]
        self.negative(operation_id, path)
        self.cleanup([operation_id], "none", "No owned state created.")

    def run_windows_fixture(self, script: str, suffix: str, run_id: str) -> bool:
        environment = os.environ.copy()
        environment["WINDOWS_OPERATOR_RUN_ID"] = f"{self.args.run_id}-{suffix}"
        fixture_exchange_root = environment.get("WINDOWS_OPERATOR_CONFORMANCE_WINDOWS_EXCHANGE")
        if not fixture_exchange_root:
            if environment.get("WINDOWS_OPERATOR_RUN_TRANSPORT") == "shared":
                fixture_exchange_root = environment.get(
                    "WINDOWS_OPERATOR_WINDOWS_EXCHANGE",
                    r"Z:\operator-exchange",
                )
            else:
                fixture_exchange_root = r"C:\ProgramData\WindowsOperator\exchange"
        completed = subprocess.run(
            [
                str(ROOT / "scripts/linux/windows-run-ps.sh"),
                script,
                "-RunId",
                run_id,
                "-ExchangeRoot",
                fixture_exchange_root,
            ],
            cwd=ROOT,
            env=environment,
            capture_output=True,
            text=True,
            timeout=120,
            check=False,
        )
        return completed.returncode == 0

    def run(self) -> int:
        capabilities = self.success(
            "getCapabilities",
            "/v1/capabilities",
            check=lambda value, _payload: (
                isinstance(value, dict) and value.get("host", {}).get("desktopAgentStatus") == "ok",
                f"contract={value.get('contractVersion') if isinstance(value, dict) else None}",
            ),
        )
        if isinstance(capabilities, dict):
            build = capabilities.get("build", {})
            self.runtime_build = (
                f"contract={capabilities.get('contractVersion')};"
                f"informational={build.get('informationalVersion')};"
                f"revision={build.get('sourceRevision')}"
            )
        self.negative("getCapabilities", "/v1/capabilities")
        self.cleanup(["getCapabilities"], "none", "No owned state created.")

        self.success("getHealth", "/v1/health", check=key_equals("status", "ok"))
        self.complete("getHealth")
        self.success(
            "getOpenApiDocument",
            "/openapi.json",
            check=lambda value, _payload: (
                isinstance(value, dict) and len(value.get("paths", {})) == 67,
                f"operations={sum(len([m for m in item if m in {'get','post','put','patch','delete'}]) for item in value.get('paths', {}).values()) if isinstance(value, dict) else 0}",
            ),
        )
        self.complete("getOpenApiDocument")
        namespaces = self.success(
            "listOpenApiNamespaces",
            "/openapi/namespaces",
            check=lambda value, _payload: (
                isinstance(value, dict)
                and isinstance(value.get("namespaces"), list)
                and len(value["namespaces"]) > 0,
                f"namespaces={len(value.get('namespaces', [])) if isinstance(value, dict) else 0}",
            ),
        )
        self.complete("listOpenApiNamespaces")
        namespace = "system"
        namespace_items = namespaces.get("namespaces", []) if isinstance(namespaces, dict) else []
        if namespace_items:
            first = namespace_items[0]
            namespace = first.get("name", namespace) if isinstance(first, dict) else namespace
        namespace_path = f"/openapi/namespaces/{urllib.parse.quote(namespace, safe='')}.json"
        self.success("getOpenApiNamespaceDocument", namespace_path)
        self.negative_not_found(
            "getOpenApiNamespaceDocument",
            "/openapi/namespaces/v1-contract-conformance-unknown.json",
            expected_code="openapi_namespace_not_found",
            fixture="unknown synthetic OpenAPI namespace",
        )
        self.cleanup(["getOpenApiNamespaceDocument"], "none", "No owned state created.")

        windows = self.success(
            "listWindows",
            "/v1/windows",
            check=lambda value, _payload: (isinstance(value, list) and len(value) > 0, f"windows={len(value) if isinstance(value, list) else 0}"),
        )
        self.complete("listWindows")
        foreground = self.success(
            "getDesktopForeground",
            "/v1/desktop/foreground",
            check=lambda value, _payload: (isinstance(value, dict) and bool(value.get("hwnd")), f"hwnd={value.get('hwnd') if isinstance(value, dict) else None}"),
        )
        self.complete("getDesktopForeground")

        session_id = f"{self.args.run_id}-edge"
        start_body = {
            "sessionId": session_id,
            "startUrl": "https://example.com",
            "profileMode": "temp",
            "pageLoadSeconds": 5,
        }
        edge = self.success("startEdgeBrowserSession", "/v1/browser/edge/session/start", start_body, key_equals("success", True))
        self.negative("startEdgeBrowserSession", "/v1/browser/edge/session/start")
        hwnd = edge.get("hwnd") if isinstance(edge, dict) else None
        unknown_session = "v1-contract-conformance-unknown"
        self.success("getEdgeBrowserSessionState", f"/v1/browser/edge/session/{session_id}/state", check=key_equals("success", True))
        self.negative_not_found(
            "getEdgeBrowserSessionState",
            f"/v1/browser/edge/session/{unknown_session}/state",
            expected_code="browser_session_not_found",
        )
        self.success(
            "clickEdgeBrowserDom",
            f"/v1/browser/edge/session/{session_id}/dom/click",
            {"selector": "a", "timeoutSeconds": 8},
            key_equals("success", True),
        )
        self.negative_not_found(
            "clickEdgeBrowserDom",
            f"/v1/browser/edge/session/{unknown_session}/dom/click",
            body={"selector": "a", "timeoutSeconds": 1},
            expected_code="browser_session_not_found",
        )
        self.success(
            "navigateEdgeBrowserSession",
            f"/v1/browser/edge/session/{session_id}/navigate",
            {"url": "https://httpbin.org/forms/post", "waitSeconds": 8},
            key_equals("success", True),
        )
        self.negative_not_found(
            "navigateEdgeBrowserSession",
            f"/v1/browser/edge/session/{unknown_session}/navigate",
            body={"url": "https://example.com", "waitSeconds": 1},
            expected_code="browser_session_not_found",
        )
        self.success(
            "fillEdgeBrowserDom",
            f"/v1/browser/edge/session/{session_id}/dom/fill",
            {"selector": "input[name=custname]", "value": "Windows Operator v1 proof", "timeoutSeconds": 8},
            key_equals("success", True),
        )
        self.negative_not_found(
            "fillEdgeBrowserDom",
            f"/v1/browser/edge/session/{unknown_session}/dom/fill",
            body={"selector": "input", "value": "synthetic", "timeoutSeconds": 1},
            expected_code="browser_session_not_found",
        )

        if isinstance(hwnd, int):
            activation = self.success("activateWindow", f"/v1/windows/{hwnd}/activate", check=key_equals("success", True))
            self.negative_not_found(
                "activateWindow",
                "/v1/windows/9223372036854775807/activate",
                expected_code="window_not_found",
                fixture="unknown synthetic window handle",
            )
            self.success("captureWindow", f"/v1/windows/{hwnd}/screenshot?format=png")
            self.negative_not_found(
                "captureWindow",
                "/v1/windows/9223372036854775807/screenshot?format=png",
                expected_code="window_not_found",
                fixture="unknown synthetic window handle",
            )
            elements = self.success(
                "queryUi",
                "/v1/uia/query",
                {"windowHwnd": hwnd, "includeOffscreen": False, "maxResults": 25},
                check=lambda value, _payload: (isinstance(value, list) and len(value) > 0, f"elements={len(value) if isinstance(value, list) else 0}"),
            )
            self.negative("queryUi", "/v1/uia/query")
            if isinstance(activation, dict) and activation.get("success") is True:
                editable = next(
                    (
                        item
                        for item in elements
                        if isinstance(item, dict)
                        and item.get("controlType") in {"Edit", "Document"}
                        and item.get("isEnabled") is True
                        and item.get("isOffscreen") is False
                    ),
                    None,
                ) if isinstance(elements, list) else None
                if editable is None:
                    _status, _headers, _payload, editables = self.client.request(
                        "POST",
                        "/v1/uia/query",
                        {"windowHwnd": hwnd, "controlType": "Edit", "includeOffscreen": False, "maxResults": 10},
                    )
                    editable = next(
                        (
                            item
                            for item in editables
                            if isinstance(item, dict)
                            and item.get("isEnabled") is True
                            and item.get("isOffscreen") is False
                        ),
                        None,
                    ) if isinstance(editables, list) else None
                clickable = next(
                    (
                        item
                        for item in elements
                        if isinstance(item, dict)
                        and item.get("controlType") == "Hyperlink"
                        and item.get("isEnabled") is True
                        and item.get("isOffscreen") is False
                        and item.get("name")
                    ),
                    None,
                ) if isinstance(elements, list) else None
                if editable:
                    query = {
                        "windowHwnd": hwnd,
                        "name": editable.get("name"),
                        "automationId": editable.get("automationId"),
                        "controlType": editable.get("controlType"),
                        "includeOffscreen": False,
                        "maxResults": 1,
                    }
                    self.success(
                        "typeUi",
                        "/v1/uia/type",
                        {"query": query, "text": "https://example.com", "append": False, "submit": False},
                        key_equals("success", True),
                    )
                else:
                    self.skipped_success("typeUi", "/v1/uia/type", "Owned Edge exposed no safe editable UI element.")
                self.negative("typeUi", "/v1/uia/type")
                if clickable:
                    query = {
                        "windowHwnd": hwnd,
                        "name": clickable.get("name"),
                        "automationId": clickable.get("automationId"),
                        "controlType": clickable.get("controlType"),
                        "includeOffscreen": False,
                        "maxResults": 1,
                    }
                    self.success("clickUi", "/v1/uia/click", {"query": query, "doubleClick": False}, key_equals("success", True))
                else:
                    self.skipped_success("clickUi", "/v1/uia/click", "Owned Edge exposed no safe clickable UI element.")
                self.negative("clickUi", "/v1/uia/click")
                _status, _headers, _payload, current_windows = self.client.request("GET", "/v1/windows")
                owned_window = next(
                    (
                        item
                        for item in current_windows
                        if isinstance(item, dict) and item.get("hwnd") == hwnd
                    ),
                    None,
                ) if isinstance(current_windows, list) else None
                bounds = owned_window.get("bounds") if isinstance(owned_window, dict) else None
                if isinstance(bounds, dict):
                    x = int(bounds.get("x", 0)) + max(10, int(bounds.get("width", 0)) // 2)
                    y = int(bounds.get("y", 0)) + max(80, int(bounds.get("height", 0)) // 2)
                    self.success("clickScreen", "/v1/input/click", {"x": x, "y": y, "doubleClick": False}, key_equals("success", True))
                else:
                    self.skipped_success("clickScreen", "/v1/input/click", "Owned Edge returned no screen bounds.")
                self.negative("clickScreen", "/v1/input/click")
            else:
                reason = "Owned Edge could not be activated on the interactive desktop."
                _status, _headers, _payload, editables = self.client.request(
                    "POST",
                    "/v1/uia/query",
                    {"windowHwnd": hwnd, "controlType": "Edit", "includeOffscreen": False, "maxResults": 10},
                )
                editable = next(
                    (
                        item
                        for item in editables
                        if isinstance(item, dict)
                        and item.get("isEnabled") is True
                        and item.get("isOffscreen") is False
                    ),
                    None,
                ) if isinstance(editables, list) else None
                if editable:
                    query = {
                        "windowHwnd": hwnd,
                        "name": editable.get("name"),
                        "automationId": editable.get("automationId"),
                        "controlType": editable.get("controlType"),
                        "includeOffscreen": False,
                        "maxResults": 1,
                    }
                    self.success(
                        "typeUi",
                        "/v1/uia/type",
                        {"query": query, "text": "https://example.com", "append": False, "submit": False},
                        key_equals("success", True),
                    )
                else:
                    self.skipped_success("typeUi", "/v1/uia/type", "Owned Edge exposed no safe editable UI element.")
                self.negative("typeUi", "/v1/uia/type")

                _status, _headers, _payload, hyperlinks = self.client.request(
                    "POST",
                    "/v1/uia/query",
                    {"windowHwnd": hwnd, "controlType": "Hyperlink", "includeOffscreen": False, "maxResults": 10},
                )
                clickable = next(
                    (
                        item
                        for item in hyperlinks
                        if isinstance(item, dict)
                        and item.get("isEnabled") is True
                        and item.get("isOffscreen") is False
                        and item.get("name")
                    ),
                    None,
                ) if isinstance(hyperlinks, list) else None
                if clickable:
                    query = {
                        "windowHwnd": hwnd,
                        "name": clickable.get("name"),
                        "automationId": clickable.get("automationId"),
                        "controlType": clickable.get("controlType"),
                        "includeOffscreen": False,
                        "maxResults": 1,
                    }
                    self.success("clickUi", "/v1/uia/click", {"query": query, "doubleClick": False}, key_equals("success", True))
                else:
                    self.skipped_success("clickUi", "/v1/uia/click", "Owned Edge exposed no safe clickable UI element.")
                self.negative("clickUi", "/v1/uia/click")
                self.skipped_success("clickScreen", "/v1/input/click", reason)
                self.negative("clickScreen", "/v1/input/click")
        self.success(
            "captureEdgeBrowserSessionScreenshot",
            f"/v1/browser/edge/session/{session_id}/screenshot",
            {"runId": self.args.run_id, "label": "edge"},
            artifact_check,
        )
        self.negative_not_found(
            "captureEdgeBrowserSessionScreenshot",
            f"/v1/browser/edge/session/{unknown_session}/screenshot",
            body={"runId": self.args.run_id, "label": "unknown-edge"},
            expected_code="browser_session_not_found",
        )
        self.success(
            "captureDesktopScreenshot",
            "/v1/desktop/screenshot",
            {"target": "foreground", "runId": self.args.run_id, "label": "foreground"},
            artifact_check,
        )
        self.negative("captureDesktopScreenshot", "/v1/desktop/screenshot")
        self.success(
            "sendHotkey",
            "/v1/input/hotkey",
            {"keys": ["shift"]},
            key_equals("success", True),
        )
        self.negative("sendHotkey", "/v1/input/hotkey")

        close = self.success(
            "closeEdgeBrowserSession",
            f"/v1/browser/edge/session/{session_id}/close",
            check=lambda value, _payload: (
                isinstance(value, dict) and value.get("isAlive") is False,
                f"isAlive={value.get('isAlive') if isinstance(value, dict) else None}",
            ),
        )
        self.negative_not_found(
            "closeEdgeBrowserSession",
            f"/v1/browser/edge/session/{unknown_session}/close",
            expected_code="browser_session_not_found",
        )
        cleanup = self.success(
            "cleanupEdgeBrowserSession",
            f"/v1/browser/edge/session/{session_id}/cleanup",
            check=key_equals("success", True),
        )
        self.negative_not_found(
            "cleanupEdgeBrowserSession",
            f"/v1/browser/edge/session/{unknown_session}/cleanup",
            expected_code="browser_session_not_found",
        )
        cleanup_ok = isinstance(cleanup, dict) and cleanup.get("success") is True
        self.cleanup(
            [
                "startEdgeBrowserSession",
                "getEdgeBrowserSessionState",
                "navigateEdgeBrowserSession",
                "clickEdgeBrowserDom",
                "fillEdgeBrowserDom",
                "captureEdgeBrowserSessionScreenshot",
                "closeEdgeBrowserSession",
                "cleanupEdgeBrowserSession",
                "activateWindow",
                "captureWindow",
                "queryUi",
                "clickUi",
                "typeUi",
                "clickScreen",
                "captureDesktopScreenshot",
                "sendHotkey",
            ],
            f"POST /v1/browser/edge/session/{session_id}/cleanup",
            f"success={cleanup_ok}; closeAlive={close.get('isAlive') if isinstance(close, dict) else None}",
            cleanup_ok,
        )

        open_id = f"{self.args.run_id}-open"
        self.success(
            "openEdgeUrl",
            "/v1/browser/edge/open-url",
            {"url": "https://example.com", "sessionId": open_id, "profileMode": "temp", "waitSeconds": 5},
            key_equals("success", True),
        )
        self.negative("openEdgeUrl", "/v1/browser/edge/open-url")
        self.success("getWorkbenchSession", f"/v1/sessions/{open_id}", check=key_equals("success", True))
        self.negative_not_found(
            "getWorkbenchSession",
            f"/v1/sessions/{unknown_session}",
            expected_code="workbench_session_not_found",
        )
        self.success(
            "captureWorkbenchSessionScreenshot",
            f"/v1/sessions/{open_id}/screenshot",
            {"runId": self.args.run_id, "label": "workbench"},
            artifact_check,
        )
        self.negative_not_found(
            "captureWorkbenchSessionScreenshot",
            f"/v1/sessions/{unknown_session}/screenshot",
            body={"runId": self.args.run_id, "label": "unknown-workbench"},
            expected_code="workbench_session_not_found",
        )
        workbench_cleanup = self.success(
            "cleanupWorkbenchSession",
            f"/v1/sessions/{open_id}/cleanup",
            check=key_equals("success", True),
        )
        self.negative_not_found(
            "cleanupWorkbenchSession",
            f"/v1/sessions/{unknown_session}/cleanup",
            expected_code="workbench_session_not_found",
        )
        workbench_ok = isinstance(workbench_cleanup, dict) and workbench_cleanup.get("success") is True
        self.cleanup(
            ["openEdgeUrl", "getWorkbenchSession", "captureWorkbenchSessionScreenshot", "cleanupWorkbenchSession"],
            f"POST /v1/sessions/{open_id}/cleanup",
            f"success={workbench_ok}",
            workbench_ok,
        )

        self.success("getPowerAutomateMcpStatus", "/v1/power-automate/mcp/status")
        self.complete("getPowerAutomateMcpStatus")
        self.success("cleanupPowerAutomateMcpEdge", "/v1/power-automate/mcp/edge/cleanup", check=key_equals("success", True))
        self.negative("cleanupPowerAutomateMcpEdge", "/v1/power-automate/mcp/edge/cleanup")
        self.cleanup(["cleanupPowerAutomateMcpEdge"], "POST /v1/power-automate/mcp/edge/cleanup", "Owned bridge Edge state absent or removed.")
        self.success(
            "cleanupMicrosoftAuthWindows",
            "/v1/auth/microsoft/cleanup",
            {"preserveRecentSeconds": 0},
            key_equals("success", True),
        )
        self.negative("cleanupMicrosoftAuthWindows", "/v1/auth/microsoft/cleanup")
        self.cleanup(["cleanupMicrosoftAuthWindows"], "POST /v1/auth/microsoft/cleanup", "Owned authentication windows removed.")

        self.run_powerpoint_jobs()
        self.run_artifact_fixture()
        self.success("getMailStatus", "/v1/mail/status")
        self.complete("getMailStatus")
        return self.finish()

    def run_auth_status_reads(self) -> int:
        status, _headers, _payload, capabilities = self.client.request("GET", "/v1/capabilities")
        if status != 200 or not isinstance(capabilities, dict):
            print(f"FAIL prerequisite getCapabilities: HTTP {status}", flush=True)
            return 1
        build = capabilities.get("build", {})
        self.runtime_build = (
            f"contract={capabilities.get('contractVersion')};"
            f"informational={build.get('informationalVersion')};"
            f"revision={build.get('sourceRevision')}"
        )

        families = (
            (
                "authorize-probe",
                "getLatestMicrosoftAuthorizeProbeStatus",
                "getMicrosoftAuthorizeProbeStatus",
            ),
            (
                "device-login",
                "getLatestMicrosoftDeviceLoginStatus",
                "getMicrosoftDeviceLoginStatus",
            ),
        )
        for family, latest_operation, by_id_operation in families:
            latest_path = f"/v1/auth/microsoft/{family}/status/latest"
            latest = self.success(
                latest_operation,
                latest_path,
                check=lambda value, _payload: (
                    isinstance(value, dict)
                    and value.get("success") is True
                    and bool(value.get("runId")),
                    f"success={value.get('success') if isinstance(value, dict) else None};"
                    f"status={value.get('status') if isinstance(value, dict) else None};"
                    f"runIdPresent={bool(value.get('runId')) if isinstance(value, dict) else False}",
                ),
                fixture="latest existing authentication handoff state",
            )
            self.negative(latest_operation, latest_path)
            self.cleanup([latest_operation], "none", "Read-only status lookup created no state.")

            run_id = latest.get("runId") if isinstance(latest, dict) else None
            if not run_id:
                self.skipped_success(by_id_operation, f"/v1/auth/microsoft/{family}/status/missing", "Latest state returned no runId.")
                continue
            by_id_path = f"/v1/auth/microsoft/{family}/status/{urllib.parse.quote(str(run_id), safe='')}"
            self.success(
                by_id_operation,
                by_id_path,
                check=lambda value, _payload, expected=run_id: (
                    isinstance(value, dict)
                    and value.get("success") is True
                    and value.get("runId") == expected,
                    f"success={value.get('success') if isinstance(value, dict) else None};"
                    f"status={value.get('status') if isinstance(value, dict) else None};"
                    f"runIdMatches={value.get('runId') == expected if isinstance(value, dict) else False}",
                ),
                fixture="existing authentication handoff state selected by explicit runId",
            )
            self.negative_not_found(
                by_id_operation,
                f"/v1/auth/microsoft/{family}/status/v1-contract-conformance-unknown",
                expected_code="auth_run_not_found",
            )
            self.cleanup([by_id_operation], "none", "Read-only status lookup created no state.")

        return self.finish()

    def run_gated_preflight(self) -> int:
        status, _headers, _payload, capabilities = self.client.request("GET", "/v1/capabilities")
        if status != 200 or not isinstance(capabilities, dict):
            print(f"FAIL prerequisite getCapabilities: HTTP {status}", flush=True)
            return 1
        build = capabilities.get("build", {})
        self.runtime_build = (
            f"contract={capabilities.get('contractVersion')};"
            f"informational={build.get('informationalVersion')};"
            f"revision={build.get('sourceRevision')}"
        )

        malformed_operations = (
            "resetEdgeBrowser",
            "startPowerAutomateMcpBridge",
            "openPowerAutomateMcpEdge",
            "readPowerAutomateMcpFlow",
            "updatePowerAutomateMcpFlow",
            "startPowerPointOnlineSession",
            "updatePowerPointOnlinePresentation",
            "startMicrosoftAuthorizeProbe",
            "startMicrosoftDeviceLogin",
            "listMailFolders",
            "searchMailMessages",
            "downloadMailAttachments",
        )
        for operation_id in malformed_operations:
            path, _method, _operation = self.operations[operation_id]
            self.negative_expected(
                operation_id,
                path,
                raw=b'{"invalid":',
                expected_status=400,
                expected_code="invalid_request",
                expected_category="validation",
                fixture="malformed JSON body; handler not invoked",
            )

        session_id = "v1-contract-conformance-unknown"
        powerpoint_not_found = (
            ("getPowerPointOnlineSession", None),
            ("selectPowerPointOnlineSlide", {"slideNumber": 1, "capture": False}),
            (
                "probePowerPointOnlineAddIn",
                {"addInBaseUrl": "https://localhost:3003", "capture": False, "activateIfNeeded": False},
            ),
            ("waitPowerPointOnlineSave", {"timeoutSeconds": 1, "pollSeconds": 1, "capture": False}),
            ("preparePowerPointOnlineTemplate", {"allowDeckMutation": True, "capture": False}),
            ("cleanupPowerPointOnlineTemplate", {"allowDeckMutation": True, "capture": False}),
            ("runPowerPointOnlinePendingJob", {"capture": False, "waitSeconds": 0}),
            ("capturePowerPointOnlineSessionScreenshot", {"label": "unknown-session", "format": "png"}),
            ("cleanupPowerPointOnlineSession", None),
        )
        for operation_id, body in powerpoint_not_found:
            path_template, _method, _operation = self.operations[operation_id]
            path = path_template.replace("{sessionId}", session_id)
            self.negative_not_found(
                operation_id,
                path,
                body=body,
                expected_code="powerpoint_session_not_found",
                fixture="unknown synthetic PowerPoint Online session identifier",
            )

        dev_path = self.operations["runPowerPointOnlineDevScript"][0].replace("{sessionId}", session_id)
        self.negative_expected(
            "runPowerPointOnlineDevScript",
            dev_path,
            body={"scriptId": "ppt.dom.snapshot", "captureScreenshot": False},
            expected_status=422,
            expected_code="dev_automation_disabled",
            expected_category="permission",
            fixture="valid allowlisted script request while development automation is disabled",
        )

        mail_path = self.operations["getMailRun"][0].replace("{runId}", "v1-contract-conformance-unknown")
        self.negative_not_found(
            "getMailRun",
            mail_path,
            expected_code="mail_run_not_found",
            fixture="unknown synthetic Outlook mail run identifier",
        )

        blocked_ids = [
            item["operationId"]
            for item in self.policy["operations"]
            if item.get("proofState") == "blocked"
        ]
        exercised_ids = {
            item["operationId"]
            for item in self.records
            if item["kind"] == "negative"
        }
        missing = sorted(set(blocked_ids) - exercised_ids)
        unexpected = sorted(exercised_ids - set(blocked_ids))
        coverage_ok = len(blocked_ids) == 23 and not missing and not unexpected
        self.records.append(
            {
                "operationId": "_gatedPreflightCoverage",
                "kind": "coverage",
                "ok": coverage_ok,
                "status": "observed",
                "detail": (
                    f"blocked={len(blocked_ids)}; exercised={len(exercised_ids)};"
                    f" missing={missing}; unexpected={unexpected}"
                ),
            }
        )
        print(
            f"{'PASS' if coverage_ok else 'FAIL'} gated preflight coverage: "
            f"blocked={len(blocked_ids)} exercised={len(exercised_ids)}",
            flush=True,
        )
        self.cleanup(
            sorted(exercised_ids),
            "none",
            "Negative-only preflight invoked no operation handler or targeted an absent synthetic resource.",
        )
        return self.finish()

    def run_powerpoint_jobs(self) -> None:
        stamp = now()
        complete_id = f"{self.args.run_id}-complete"
        fail_id = f"{self.args.run_id}-fail"
        document_complete = f"https://example.invalid/{complete_id}.pptx"
        document_fail = f"https://example.invalid/{fail_id}.pptx"
        complete_job = {
            "jobId": complete_id,
            "expectedDocumentUrl": document_complete,
            "discoverTargets": True,
            "requestedBy": "v1-contract-conformance",
            "createdAt": stamp,
            "operations": [],
        }
        self.success("enqueuePowerPointJob", "/v1/powerpoint/jobs", complete_job, status_equals("queued"))
        self.negative("enqueuePowerPointJob", "/v1/powerpoint/jobs")
        self.success(
            "claimPowerPointJob",
            "/v1/powerpoint/jobs/claim",
            {"workerId": "v1-contract-conformance", "documentUrl": document_complete},
            check=lambda value, _payload: (isinstance(value, dict) and value.get("jobId") == complete_id, f"jobId={value.get('jobId') if isinstance(value, dict) else None}"),
        )
        self.negative("claimPowerPointJob", "/v1/powerpoint/jobs/claim")
        self.success(
            "completePowerPointJob",
            f"/v1/powerpoint/jobs/{complete_id}/complete",
            {"jobId": complete_id, "status": "succeeded", "startedAt": stamp, "finishedAt": now(), "targets": []},
            status_equals("succeeded"),
        )
        unknown_job = "v1-contract-conformance-unknown"
        self.negative_not_found(
            "completePowerPointJob",
            f"/v1/powerpoint/jobs/{unknown_job}/complete",
            body={"jobId": unknown_job, "status": "succeeded", "startedAt": stamp, "finishedAt": now(), "targets": []},
            expected_code="powerpoint_job_not_found",
            fixture="unknown synthetic PowerPoint job identifier",
        )
        self.success("getPowerPointJob", f"/v1/powerpoint/jobs/{complete_id}", check=status_equals("succeeded"))
        self.negative_not_found(
            "getPowerPointJob",
            f"/v1/powerpoint/jobs/{unknown_job}",
            expected_code="powerpoint_job_not_found",
            fixture="unknown synthetic PowerPoint job identifier",
        )

        fail_job = {
            "jobId": fail_id,
            "expectedDocumentUrl": document_fail,
            "requestedBy": "v1-contract-conformance",
            "createdAt": now(),
            "operations": [
                {
                    "kind": "replaceImage",
                    "targetId": "fixture",
                    "artifact": {
                        "artifactId": "proof",
                        "url": "data:image/png;base64,AQID",
                        "mediaType": "image/png",
                    },
                }
            ],
        }
        self.success("enqueuePowerPointJob", "/v1/powerpoint/jobs", fail_job, status_equals("queued"))
        self.success(
            "getPowerPointJobArtifact",
            f"/v1/powerpoint/jobs/{fail_id}/artifacts/proof",
            check=lambda _value, payload: (payload == b"\x01\x02\x03", f"bytes={len(payload)}"),
        )
        self.negative_not_found(
            "getPowerPointJobArtifact",
            f"/v1/powerpoint/jobs/{unknown_job}/artifacts/missing",
            expected_code="powerpoint_job_not_found",
            fixture="unknown synthetic PowerPoint job identifier",
        )
        self.success(
            "claimPowerPointJob",
            "/v1/powerpoint/jobs/claim",
            {"workerId": "v1-contract-conformance", "documentUrl": document_fail},
            check=lambda value, _payload: (isinstance(value, dict) and value.get("jobId") == fail_id, f"jobId={value.get('jobId') if isinstance(value, dict) else None}"),
        )
        self.success(
            "failPowerPointJob",
            f"/v1/powerpoint/jobs/{fail_id}/fail",
            {"code": "CONTRACT_FIXTURE", "retryable": False, "operatorMessage": "Synthetic conformance failure."},
            status_equals("failed"),
        )
        self.negative_not_found(
            "failPowerPointJob",
            f"/v1/powerpoint/jobs/{unknown_job}/fail",
            body={"code": "CONTRACT_FIXTURE", "retryable": False, "operatorMessage": "Synthetic conformance failure."},
            expected_code="powerpoint_job_not_found",
            fixture="unknown synthetic PowerPoint job identifier",
        )
        self.cleanup(
            [
                "enqueuePowerPointJob",
                "claimPowerPointJob",
                "completePowerPointJob",
                "failPowerPointJob",
                "getPowerPointJob",
                "getPowerPointJobArtifact",
            ],
            "terminal synthetic PowerPoint job records",
            f"{complete_id}=completed; {fail_id}=failed",
        )

    def run_artifact_fixture(self) -> None:
        fixture_id = f"{self.args.run_id}-artifact"
        created = self.run_windows_fixture(
            "scripts/windows/new-contract-artifact-fixture.ps1",
            "artifact-create",
            fixture_id,
        )
        artifacts = self.success(
            "listRunArtifacts",
            f"/v1/runs/{fixture_id}/artifacts",
            check=lambda value, _payload: (
                created
                and isinstance(value, dict)
                and isinstance(value.get("artifacts"), list)
                and len(value["artifacts"]) == 1,
                f"artifacts={len(value.get('artifacts', [])) if isinstance(value, dict) else 0}",
            ),
        )
        self.negative_not_found(
            "listRunArtifacts",
            "/v1/runs/v1-contract-conformance-unknown/artifacts",
            expected_code="artifact_not_found",
            fixture="unknown synthetic artifact run identifier",
        )
        artifact_items = artifacts.get("artifacts", []) if isinstance(artifacts, dict) else []
        artifact_id = artifact_items[0].get("artifactId") if artifact_items else "missing"
        self.success(
            "getArtifact",
            f"/v1/artifacts/{urllib.parse.quote(str(artifact_id), safe='')}",
            check=lambda _value, payload: (b"Windows Operator v1 contract fixture" in payload, f"bytes={len(payload)}"),
        )
        self.negative_not_found(
            "getArtifact",
            "/v1/artifacts/invalid",
            expected_code="artifact_not_found",
            fixture="unknown synthetic artifact identifier",
        )
        removed = self.run_windows_fixture(
            "scripts/windows/remove-contract-artifact-fixture.ps1",
            "artifact-remove",
            fixture_id,
        )
        self.cleanup(
            ["listRunArtifacts", "getArtifact"],
            "scripts/windows/remove-contract-artifact-fixture.ps1",
            f"runId={fixture_id}; removed={removed}",
            removed,
        )

    def finish(self) -> int:
        verified: list[str] = []
        for operation_id, entries in self.evidence.items():
            kinds = {entry["kind"] for entry in entries}
            if kinds == {"success", "negative", "cleanup"}:
                verified.append(operation_id)
                if self.args.update_policy:
                    policy = self.policy_by_id[operation_id]
                    policy["proofState"] = "verified"
                    policy["evidence"] = entries
        report = {
            "runId": self.args.run_id,
            "baseUrl": self.args.base_url,
            "runtimeBuild": self.runtime_build,
            "completedAtUtc": now(),
            "verifiedOperations": sorted(verified),
            "verifiedCount": len(verified),
            "coveredOperations": sorted(self.evidence),
            "coveredCount": len(self.evidence),
            "negativeVerifiedOperations": sorted(
                operation_id
                for operation_id, entries in self.evidence.items()
                if any(entry["kind"] == "negative" for entry in entries)
            ),
            "evidenceByOperation": self.evidence,
            "failedChecks": sum(not item["ok"] for item in self.records),
            "records": self.records,
        }
        write_json(self.report_path, report)
        if self.args.update_policy:
            write_json(POLICY_PATH, self.policy)
        print(
            f"REPORT {self.report_path} verified={len(verified)} failedChecks={report['failedChecks']}",
            flush=True,
        )
        return 0 if report["failedChecks"] == 0 else 1


def key_equals(key: str, expected: Any) -> JsonCheck:
    return lambda value, _payload: (
        isinstance(value, dict) and value.get(key) == expected,
        f"{key}={value.get(key) if isinstance(value, dict) else None}",
    )


def status_equals(expected: str) -> JsonCheck:
    return key_equals("status", expected)


def artifact_check(value: Any, _payload: bytes) -> tuple[bool, str]:
    artifact = value.get("artifact", {}).get("artifact") if isinstance(value, dict) else None
    return (
        isinstance(artifact, dict) and bool(artifact.get("artifactId")) and bool(artifact.get("href")),
        f"artifactId={artifact.get('artifactId') if isinstance(artifact, dict) else None}",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run safe live v1 operation conformance.")
    parser.add_argument("--base-url", default="http://127.0.0.1:43117")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--mode", choices=("safe", "auth-status", "gated-preflight"), default="safe")
    parser.add_argument("--timeout-seconds", type=int, default=90)
    parser.add_argument("--update-policy", action="store_true")
    parser.add_argument("--output")
    args = parser.parse_args()
    args.run_id = args.run_id.lower()
    if args.output is None:
        exchange = Path(os.environ.get("WINDOWS_OPERATOR_EXCHANGE_ROOT", "/var/lib/windows-server/shared/operator-exchange"))
        args.output = str(exchange / "runs" / args.run_id / "v1-contract-conformance.json")
    return args


def main() -> int:
    args = parse_args()
    campaign = Campaign(args)
    if args.mode == "auth-status":
        return campaign.run_auth_status_reads()
    if args.mode == "gated-preflight":
        return campaign.run_gated_preflight()
    return campaign.run()


if __name__ == "__main__":
    sys.exit(main())
