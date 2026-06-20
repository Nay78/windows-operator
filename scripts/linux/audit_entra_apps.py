#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable


APP_REGISTRATIONS_URL = (
    "https://entra.microsoft.com/#view/"
    "Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps"
)
GRAPH_USER_READ_SCOPE = "https://graph.microsoft.com/User.Read"
GRAPH_MAIL_READ_SCOPE = "https://graph.microsoft.com/Mail.Read"
GRAPH_ME_URL = "https://graph.microsoft.com/v1.0/me?$select=id,userPrincipalName"
GRAPH_MESSAGES_URL = (
    "https://graph.microsoft.com/v1.0/me/messages"
    "?$select=id,subject,receivedDateTime&$top=1"
)
APP_GUID_RE = re.compile(
    r"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"
)
DETAIL_APP_ID_RE = re.compile(
    r"/appId/(?P<appid>[0-9a-fA-F-]{36})/",
    re.IGNORECASE,
)
REDIRECT_COUNTS_RE = re.compile(
    r"(?P<web>\d+)\s+web,\s*(?P<spa>\d+)\s+spa,\s*(?P<public>\d+)\s+public client",
    re.IGNORECASE,
)
SECRET_COUNT_RE = re.compile(r"(?P<count>\d+)\s+secret", re.IGNORECASE)
CERT_COUNT_RE = re.compile(r"(?P<count>\d+)\s+certificate", re.IGNORECASE)
MAIL_KEYWORDS = ("mail", "outlook", "smtp", "imap", "exchange")
AUTH_KEYWORDS = ("auth", "oauth", "login", "entra", "graph", "aad", "microsoft")
APP_ENTRY_ROLES = {
    "link",
    "button",
    "listitem",
    "row",
    "gridcell",
    "dataitem",
    "option",
}
APP_STOP_WORDS = {
    "app registrations",
    "owned applications",
    "all applications",
    "view all applications in the directory",
    "overview",
    "certificates & secrets",
    "api permissions",
    "manifest",
    "authentication",
    "branding",
    "owners",
    "roles and administrators",
    "download",
    "delete",
    "search",
    "filter",
    "refresh",
    "new registration",
    "help",
    "feedback",
    "close",
    "cancel",
    "accept",
    "continue",
    "next",
    "sign in",
}


class AuditError(RuntimeError):
    pass


def utc_now() -> str:
    return dt.datetime.now(dt.UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def json_dump(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def json_load(path: Path, default: Any = None) -> Any:
    if not path.exists():
        return default
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def slugify(value: str, fallback: str) -> str:
    lowered = value.strip().lower()
    lowered = re.sub(r"[^a-z0-9]+", "-", lowered)
    lowered = lowered.strip("-")
    return lowered or fallback


def flatten_text(value: Any) -> list[str]:
    lines: list[str] = []
    seen: set[str] = set()

    def visit(node: Any) -> None:
        if node is None:
            return
        if isinstance(node, str):
            for part in re.split(r"[\r\n]+", node):
                cleaned = " ".join(part.split())
                if cleaned and cleaned not in seen:
                    seen.add(cleaned)
                    lines.append(cleaned)
            return
        if isinstance(node, list):
            for item in node:
                visit(item)
            return
        if isinstance(node, dict):
            for key, item in node.items():
                if key.lower() in {
                    "text",
                    "innertext",
                    "textcontent",
                    "name",
                    "label",
                    "title",
                    "value",
                    "placeholder",
                    "ariaLabel".lower(),
                }:
                    visit(item)
            for key in ("attributes", "props", "dataset", "meta"):
                extra = node.get(key)
                if isinstance(extra, dict):
                    visit(list(extra.values()))
            for key in ("children", "elements", "items", "nodes", "content", "document", "dom"):
                if key in node:
                    visit(node[key])

    visit(value)
    return lines


def extract_guid_near(lines: list[str], anchors: tuple[str, ...]) -> str | None:
    for index, line in enumerate(lines):
        lowered = line.lower()
        if not any(anchor in lowered for anchor in anchors):
            continue
        for look_ahead in lines[index : index + 3]:
            match = APP_GUID_RE.search(look_ahead)
            if match:
                return match.group(0).lower()
    for line in lines:
        match = APP_GUID_RE.search(line)
        if match:
            return match.group(0).lower()
    return None


def derive_display_name(lines: list[str], title: str | None) -> str | None:
    if title:
        cleaned = title.split(" - ")[0].strip()
        if cleaned and cleaned.lower() not in APP_STOP_WORDS:
            return cleaned
    for line in lines[:8]:
        lowered = line.lower()
        if lowered not in APP_STOP_WORDS and not APP_GUID_RE.fullmatch(line):
            return line
    return None


@dataclass
class HostClient:
    base_url: str
    timeout_seconds: int = 30

    def request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
        expected_status: int | tuple[int, ...] = (200, 201, 202),
    ) -> Any:
        if isinstance(expected_status, int):
            expected = {expected_status}
        else:
            expected = set(expected_status)
        url = urllib.parse.urljoin(f"{self.base_url.rstrip('/')}/", path.lstrip("/"))
        body = None
        headers = {"Accept": "application/json"}
        if payload is not None:
            body = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(url, data=body, method=method.upper(), headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                raw = response.read().decode("utf-8")
                if response.status not in expected:
                    raise AuditError(f"{method} {path} returned status {response.status}: {raw}")
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise AuditError(f"{method} {path} returned status {exc.code}: {detail}") from exc
        except urllib.error.URLError as exc:
            raise AuditError(f"{method} {path} failed: {exc}") from exc

    def browser_reset(self) -> Any:
        return self.request("POST", "/v1/browser/edge/reset", {})

    def session_start(self) -> Any:
        return self.request(
            "POST",
            "/v1/browser/edge/session/start",
            {
                "profileMode": "work",
                "startUrl": APP_REGISTRATIONS_URL,
                "pageLoadSeconds": 8,
            },
        )

    def session_state(self, session_id: str) -> Any:
        return self.request("GET", f"/v1/browser/edge/session/{session_id}/state", None, expected_status=200)

    def session_navigate(self, session_id: str, url: str) -> Any:
        return self.request(
            "POST",
            f"/v1/browser/edge/session/{session_id}/navigate",
            {"url": url},
        )

    def dom_click(self, session_id: str, target: dict[str, Any]) -> Any:
        payload = self._dom_target_payload(target)
        return self.request("POST", f"/v1/browser/edge/session/{session_id}/dom/click", payload)

    def dom_fill(
        self,
        session_id: str,
        target: dict[str, Any],
        value: str,
        submit: bool = False,
    ) -> Any:
        payload = self._dom_target_payload(target)
        payload["value"] = value
        return self.request("POST", f"/v1/browser/edge/session/{session_id}/dom/fill", payload)

    def session_close(self, session_id: str) -> Any:
        return self.request("POST", f"/v1/browser/edge/session/{session_id}/close", {})

    def activate_window(self, hwnd: int) -> Any:
        return self.request("POST", f"/v1/windows/{hwnd}/activate", {})

    def uia_query(
        self,
        hwnd: int,
        *,
        name: str | None = None,
        automation_id: str | None = None,
        control_type: str | None = None,
        include_offscreen: bool = False,
        max_results: int = 25,
    ) -> Any:
        payload: dict[str, Any] = {
            "windowHwnd": hwnd,
            "includeOffscreen": include_offscreen,
            "maxResults": max_results,
        }
        if name:
            payload["name"] = name
        if automation_id:
            payload["automationId"] = automation_id
        if control_type:
            payload["controlType"] = control_type
        return self.request("POST", "/v1/uia/query", payload)

    def uia_click(
        self,
        hwnd: int,
        *,
        name: str | None = None,
        automation_id: str | None = None,
        control_type: str | None = None,
        double_click: bool = False,
        include_offscreen: bool = False,
        max_results: int = 25,
    ) -> Any:
        query: dict[str, Any] = {
            "windowHwnd": hwnd,
            "includeOffscreen": include_offscreen,
            "maxResults": max_results,
        }
        if name:
            query["name"] = name
        if automation_id:
            query["automationId"] = automation_id
        if control_type:
            query["controlType"] = control_type
        return self.request(
            "POST",
            "/v1/uia/click",
            {
                "query": query,
                "doubleClick": double_click,
            },
        )

    @staticmethod
    def _dom_target_payload(target: dict[str, Any]) -> dict[str, Any]:
        payload: dict[str, Any] = {}
        selector = target.get("selector") or target.get("cssSelector")
        if not selector and isinstance(target.get("id"), str) and target["id"].strip():
            selector = f"#{target['id'].strip()}"
        text = target.get("text")
        label = target.get("label")
        if isinstance(selector, str) and selector.strip():
            payload["selector"] = selector.strip()
        if isinstance(text, str) and text.strip():
            payload["visibleText"] = " ".join(text.split())[:200]
        if isinstance(label, str) and label.strip():
            payload["labelText"] = " ".join(label.split())[:200]
        return payload


class SessionSnapshot:
    def __init__(self, raw: Any):
        self.raw = raw if isinstance(raw, dict) else {"value": raw}
        self.url = self._first_value("url", "location", "href", "pageUrl")
        self.title = self._first_value("title", "pageTitle", "documentTitle")
        self.hwnd = self._first_int("hwnd")
        self.text_lines = flatten_text(self.raw)
        self.elements = self._extract_elements(self.raw)

    def _first_value(self, *keys: str) -> str | None:
        queue: list[Any] = [self.raw]
        lowered = {key.lower() for key in keys}
        while queue:
            node = queue.pop(0)
            if isinstance(node, dict):
                for key, value in node.items():
                    if key.lower() in lowered and isinstance(value, str) and value.strip():
                        return value.strip()
                    if isinstance(value, (dict, list)):
                        queue.append(value)
            elif isinstance(node, list):
                queue.extend(node)
        return None

    def _first_int(self, *keys: str) -> int | None:
        queue: list[Any] = [self.raw]
        lowered = {key.lower() for key in keys}
        while queue:
            node = queue.pop(0)
            if isinstance(node, dict):
                for key, value in node.items():
                    if key.lower() in lowered and isinstance(value, int):
                        return value
                    if isinstance(value, (dict, list)):
                        queue.append(value)
            elif isinstance(node, list):
                queue.extend(node)
        return None

    def _extract_elements(self, root: Any) -> list[dict[str, Any]]:
        elements: list[dict[str, Any]] = []
        visited: set[int] = set()

        def visit(node: Any) -> None:
            if isinstance(node, dict):
                if id(node) in visited:
                    return
                visited.add(id(node))
                if self._looks_like_element(node):
                    elements.append(node)
                for value in node.values():
                    if isinstance(value, (dict, list)):
                        visit(value)
            elif isinstance(node, list):
                for item in node:
                    visit(item)

        visit(root)
        return elements

    @staticmethod
    def _looks_like_element(node: dict[str, Any]) -> bool:
        keys = {key.lower() for key in node}
        return bool(
            keys.intersection(
                {
                    "elementid",
                    "role",
                    "tagname",
                    "selector",
                    "xpath",
                    "cssselector",
                    "name",
                    "text",
                    "innertext",
                    "label",
                    "href",
                }
            )
        )

    @staticmethod
    def element_text(element: dict[str, Any]) -> str:
        parts: list[str] = []
        for key in ("text", "innerText", "textContent", "name", "label", "title", "value", "placeholder"):
            value = element.get(key)
            if isinstance(value, str) and value.strip():
                parts.append(" ".join(value.split()))
        for container_key in ("attributes", "props", "dataset", "meta"):
            container = element.get(container_key)
            if isinstance(container, dict):
                for value in container.values():
                    if isinstance(value, str) and value.strip():
                        parts.append(" ".join(value.split()))
        deduped: list[str] = []
        seen: set[str] = set()
        for part in parts:
            if part not in seen:
                seen.add(part)
                deduped.append(part)
        return " ".join(deduped)

    @staticmethod
    def element_role(element: dict[str, Any]) -> str:
        for key in ("role", "tagName"):
            value = element.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip().lower()
        return ""

    @staticmethod
    def selector_for(element: dict[str, Any]) -> dict[str, Any]:
        target: dict[str, Any] = {}
        for key in ("elementId", "selector", "cssSelector", "xpath", "id"):
            value = element.get(key)
            if isinstance(value, str) and value.strip():
                target[key] = value
        text = SessionSnapshot.element_text(element)
        if text:
            target["text"] = text[:200]
        role = SessionSnapshot.element_role(element)
        if role:
            target["role"] = role
        href = element.get("href")
        if isinstance(href, str) and href.strip():
            target["href"] = href
        attrs = element.get("attributes")
        if isinstance(attrs, dict) and attrs:
            target["attributes"] = {
                key: value
                for key, value in attrs.items()
                if isinstance(value, (str, int, float, bool))
            }
        return target

    def find_first(self, predicate: Callable[[dict[str, Any]], bool]) -> dict[str, Any] | None:
        for element in self.elements:
            if predicate(element):
                return element
        return None

    def find_many(self, predicate: Callable[[dict[str, Any]], bool]) -> list[dict[str, Any]]:
        return [element for element in self.elements if predicate(element)]


class RunStore:
    def __init__(self, output_root: Path):
        self.output_root = output_root
        self.apps_path = output_root / "apps.jsonl"
        self.run_path = output_root / "run.json"
        self.summary_path = output_root / "summary.json"
        self.artifacts_root = output_root / "artifacts"

    def ensure_ready(self, resume: bool) -> dict[str, Any]:
        self.output_root.mkdir(parents=True, exist_ok=True)
        self.artifacts_root.mkdir(parents=True, exist_ok=True)
        if resume:
            return json_load(self.run_path, default={}) or {}
        if self.run_path.exists() or self.apps_path.exists() or self.summary_path.exists():
            raise AuditError(
                f"output root already contains audit state: {self.output_root}. "
                "Use --resume or choose another --output-root."
            )
        return {}

    def append_jsonl(self, path: Path, payload: dict[str, Any]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(payload, sort_keys=True))
            handle.write("\n")

    def artifact_dir(self, app_key: str) -> Path:
        path = self.artifacts_root / app_key
        path.mkdir(parents=True, exist_ok=True)
        return path

    def write_artifact(self, app_key: str, name: str, payload: Any) -> Path:
        path = self.artifact_dir(app_key) / name
        json_dump(path, payload)
        return path

    def load_latest_apps(self) -> dict[str, dict[str, Any]]:
        apps: dict[str, dict[str, Any]] = {}
        if not self.apps_path.exists():
            return apps
        with self.apps_path.open(encoding="utf-8") as handle:
            for line in handle:
                stripped = line.strip()
                if not stripped:
                    continue
                payload = json.loads(stripped)
                app_key = payload.get("clientId") or payload.get("appKey")
                if not app_key:
                    continue
                existing = apps.get(app_key, {})
                if payload.get("recordType") == "probeResult":
                    existing["lastProbeResult"] = payload
                else:
                    existing.update(payload)
                apps[app_key] = existing
        return apps


class EntraAuditor:
    def __init__(self, args: argparse.Namespace):
        self.args = args
        self.host = HostClient(args.host_base_url)
        self.store = RunStore(args.output_root)
        self.run: dict[str, Any] = self.store.ensure_ready(args.resume)
        self.latest_apps = self.store.load_latest_apps()
        self.run_id = self.run.get("runId") or f"entra-audit-{dt.datetime.now(dt.UTC):%Y%m%dT%H%M%SZ}"
        self.session_id = self.run.get("browserSession", {}).get("sessionId")
        self.processed_order: list[str] = list(self.run.get("processedOrder", []))
        self._max_apps_hit = False
        self.started_new_session = False

        if not self.run:
            self.run = {
                "runId": self.run_id,
                "tenantId": args.tenant_id,
                "hostBaseUrl": args.host_base_url,
                "outputRoot": str(args.output_root),
                "createdAtUtc": utc_now(),
                "updatedAtUtc": utc_now(),
                "mode": {
                    "resume": bool(args.resume),
                    "metadataOnly": bool(args.metadata_only),
                    "probeCandidatesOnly": bool(args.probe_candidates_only),
                },
                "browserSession": {},
                "processedOrder": [],
                "stats": {
                    "metadataInspected": 0,
                    "probeAttempts": 0,
                    "probeSuccesses": 0,
                },
                "errors": [],
            }
            self._save_run()

    def _save_run(self) -> None:
        self.run["updatedAtUtc"] = utc_now()
        json_dump(self.store.run_path, self.run)

    def _record_error(self, phase: str, message: str) -> None:
        self.run.setdefault("errors", []).append(
            {
                "phase": phase,
                "message": message,
                "recordedAtUtc": utc_now(),
            }
        )
        self._save_run()

    def _set_session(self, session_id: str, reset_payload: Any | None = None, start_payload: Any | None = None) -> None:
        self.session_id = session_id
        self.run["browserSession"] = {
            "sessionId": session_id,
            "startedAtUtc": utc_now(),
            "resetPayload": reset_payload,
            "startPayload": start_payload,
        }
        self._save_run()

    def ensure_session(self) -> str:
        if self.session_id:
            try:
                state = self.host.session_state(self.session_id)
                self.run["browserSession"]["lastKnownState"] = {
                    "url": SessionSnapshot(state).url,
                    "title": SessionSnapshot(state).title,
                    "checkedAtUtc": utc_now(),
                }
                self._save_run()
                return self.session_id
            except AuditError:
                self.session_id = None
                self.run["browserSession"]["stale"] = True
                self._save_run()

        reset_payload = self.host.browser_reset()
        start_payload = self.host.session_start()
        session_id = self._extract_session_id(start_payload)
        if not session_id:
            raise AuditError(f"session/start response missing session id: {start_payload}")
        self.started_new_session = True
        self._set_session(session_id, reset_payload=reset_payload, start_payload=start_payload)
        self.wait_for_entra_ready()
        return session_id

    def wait_for_entra_ready(self, timeout_seconds: int = 20) -> SessionSnapshot:
        deadline = time.monotonic() + timeout_seconds
        last_snapshot: SessionSnapshot | None = None
        while time.monotonic() < deadline:
            snapshot = self.capture_state("entra-ready")
            last_snapshot = snapshot
            url = (snapshot.url or "").lower()
            text_blob = " ".join(snapshot.text_lines[:40]).lower()
            title = (snapshot.title or "").lower()
            if "registeredapps" in url and "app registrations" in text_blob:
                return snapshot
            if "entra.microsoft.com" in url and "grupo minero antofagasta minerals" in title:
                return snapshot
            time.sleep(1.5)
        raise AuditError(
            f"entra session not ready. url={getattr(last_snapshot, 'url', None)} "
            f"title={getattr(last_snapshot, 'title', None)}"
        )

    @staticmethod
    def _extract_session_id(payload: Any) -> str | None:
        if isinstance(payload, dict):
            for key in ("sessionId", "id", "browserSessionId"):
                value = payload.get(key)
                if isinstance(value, str) and value.strip():
                    return value.strip()
            for value in payload.values():
                nested = EntraAuditor._extract_session_id(value)
                if nested:
                    return nested
        elif isinstance(payload, list):
            for item in payload:
                nested = EntraAuditor._extract_session_id(item)
                if nested:
                    return nested
        return None

    def capture_state(self, label: str, app_key: str | None = None) -> SessionSnapshot:
        session_id = self.ensure_session()
        raw = self.host.session_state(session_id)
        snapshot = SessionSnapshot(raw)
        state_record = {
            "capturedAtUtc": utc_now(),
            "label": label,
            "sessionId": session_id,
            "url": snapshot.url,
            "title": snapshot.title,
            "textLines": snapshot.text_lines,
            "raw": raw,
        }
        target_key = app_key or "_session"
        safe_label = slugify(label, "state")
        self.store.write_artifact(target_key, f"{safe_label}.state.json", state_record)
        self.run["browserSession"]["lastKnownState"] = {
            "label": label,
            "url": snapshot.url,
            "title": snapshot.title,
            "checkedAtUtc": utc_now(),
        }
        self._save_run()
        return snapshot

    def navigate(self, url: str, label: str, app_key: str | None = None) -> SessionSnapshot:
        session_id = self.ensure_session()
        response = self.host.session_navigate(session_id, url)
        target_key = app_key or "_session"
        self.store.write_artifact(
            target_key,
            f"{slugify(label, 'navigate')}.navigate.json",
            {
                "requestedAtUtc": utc_now(),
                "sessionId": session_id,
                "url": url,
                "response": response,
            },
        )
        time.sleep(1.0)
        return self.capture_state(label, app_key=app_key)

    def click_element(self, element: dict[str, Any], label: str, app_key: str | None = None) -> SessionSnapshot:
        session_id = self.ensure_session()
        target = SessionSnapshot.selector_for(element)
        response = self.host.dom_click(session_id, target)
        target_key = app_key or "_session"
        self.store.write_artifact(
            target_key,
            f"{slugify(label, 'click')}.click.json",
            {
                "requestedAtUtc": utc_now(),
                "sessionId": session_id,
                "target": target,
                "response": response,
            },
        )
        time.sleep(1.0)
        return self.capture_state(label, app_key=app_key)

    def fill_element(
        self,
        element: dict[str, Any],
        value: str,
        label: str,
        app_key: str | None = None,
        submit: bool = False,
    ) -> SessionSnapshot:
        session_id = self.ensure_session()
        target = SessionSnapshot.selector_for(element)
        response = self.host.dom_fill(session_id, target, value, submit=submit)
        target_key = app_key or "_session"
        self.store.write_artifact(
            target_key,
            f"{slugify(label, 'fill')}.fill.json",
            {
                "requestedAtUtc": utc_now(),
                "sessionId": session_id,
                "target": target,
                "valueRedacted": "***",
                "submit": submit,
                "response": response,
            },
        )
        time.sleep(1.0)
        return self.capture_state(label, app_key=app_key)

    def wait_for(
        self,
        description: str,
        condition: Callable[[SessionSnapshot], bool],
        timeout_seconds: int = 30,
        poll_seconds: float = 2.0,
        app_key: str | None = None,
    ) -> SessionSnapshot:
        deadline = time.monotonic() + timeout_seconds
        snapshot = self.capture_state(f"wait-{description}-start", app_key=app_key)
        while time.monotonic() < deadline:
            if condition(snapshot):
                return snapshot
            time.sleep(poll_seconds)
            snapshot = self.capture_state(f"wait-{description}", app_key=app_key)
        raise AuditError(f"timed out waiting for {description}")

    def maybe_click_view_all(self, snapshot: SessionSnapshot) -> SessionSnapshot:
        hwnd = snapshot.hwnd
        if hwnd is None:
            return snapshot
        self.host.activate_window(hwnd)
        matched: list[dict[str, Any]] = []
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            try:
                buttons = self.host.uia_query(
                    hwnd,
                    control_type="Button",
                    include_offscreen=True,
                    max_results=200,
                )
            except AuditError:
                time.sleep(1.0)
                self.host.activate_window(hwnd)
                continue
            matched = [
                button for button in buttons
                if "view all applications in the directory" in button.get("name", "").lower()
            ]
            if matched:
                break
            time.sleep(1.0)
        if not matched:
            return snapshot
        response = self.host.uia_click(
            hwnd,
            name=matched[0].get("name"),
            control_type="Button",
            include_offscreen=True,
            max_results=25,
        )
        self.store.write_artifact(
            "_session",
            "view-all-applications.uia-click.json",
            {
                "requestedAtUtc": utc_now(),
                "hwnd": hwnd,
                "button": matched[0],
                "response": response,
            },
        )
        ready_deadline = time.monotonic() + 20
        while time.monotonic() < ready_deadline:
            try:
                data_items = self.host.uia_query(
                    hwnd,
                    control_type="DataItem",
                    include_offscreen=True,
                    max_results=80,
                )
            except AuditError:
                time.sleep(1.0)
                self.host.activate_window(hwnd)
                continue
            if self._grid_ready(data_items):
                break
            time.sleep(1.5)
        return self.capture_state("view-all-applications")

    def maybe_click_load_more(self, snapshot: SessionSnapshot) -> SessionSnapshot | None:
        hwnd = snapshot.hwnd
        if hwnd is None:
            return None
        self.host.activate_window(hwnd)
        deadline = time.monotonic() + 10
        matched: list[dict[str, Any]] = []
        while time.monotonic() < deadline:
            try:
                buttons = self.host.uia_query(
                    hwnd,
                    control_type="Button",
                    include_offscreen=True,
                    max_results=200,
                )
            except AuditError:
                time.sleep(1.0)
                self.host.activate_window(hwnd)
                continue
            matched = [
                button for button in buttons
                if button.get("name", "").strip().lower() == "load more"
            ]
            if matched:
                break
            time.sleep(0.5)
        if not matched:
            return None
        response = self.host.uia_click(
            hwnd,
            name=matched[0].get("name"),
            control_type="Button",
            include_offscreen=True,
            max_results=25,
        )
        self.run["loadMoreClicks"] = int(self.run.get("loadMoreClicks", 0)) + 1
        self._save_run()
        self.store.write_artifact(
            "_session",
            f"load-more-{self.run['loadMoreClicks']}.uia-click.json",
            {
                "requestedAtUtc": utc_now(),
                "hwnd": hwnd,
                "button": matched[0],
                "response": response,
            },
        )
        time.sleep(2.0)
        return self.capture_state("load-more")

    @staticmethod
    def _grid_ready(data_items: list[dict[str, Any]]) -> bool:
        for item in data_items:
            name = " ".join(str(item.get("name") or "").split()).lower()
            if "display name" in name and "application (client) id" in name:
                return True
            automation_id = str(item.get("automationId") or "")
            if automation_id.startswith("fxc-gc-cell-content_0_") and name.startswith("a "):
                return True
        return False

    def discover_visible_apps(self, snapshot: SessionSnapshot) -> list[dict[str, Any]]:
        hwnd = snapshot.hwnd
        if hwnd is None:
            return []
        self.host.activate_window(hwnd)
        rows: list[dict[str, Any]] = []
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            try:
                rows = self.host.uia_query(
                    hwnd,
                    control_type="DataItem",
                    include_offscreen=True,
                    max_results=200,
                )
            except AuditError:
                time.sleep(1.0)
                self.host.activate_window(hwnd)
                continue
            if any(str(row.get("automationId") or "").startswith("fxc-gc-cell-content_0_") for row in rows):
                break
            time.sleep(1.0)
        candidates: list[dict[str, Any]] = []
        seen: set[str] = set()

        for row in rows:
            automation_id = str(row.get("automationId") or "")
            if "fxc-gc-cell-content_" not in automation_id:
                continue
            name = " ".join(str(row.get("name") or "").split())
            if not name:
                continue
            lowered = name.lower()
            if lowered in APP_STOP_WORDS or lowered == "display name":
                continue
            if not lowered.startswith("a "):
                continue
            if not name.endswith(":"):
                continue
            if lowered in seen:
                continue
            seen.add(lowered)
            display_name = re.sub(r"^[A-Z0-9()]+\s+", "", name).rstrip(" :")
            candidates.append(
                {
                    "displayName": display_name,
                    "row": row,
                    "automationId": automation_id,
                    "hwnd": hwnd,
                }
            )
        return candidates

    def inspect_visible_apps(self) -> None:
        exhausted_load_more = False
        while True:
            if self.args.max_apps and self.run["stats"]["metadataInspected"] >= self.args.max_apps:
                self._max_apps_hit = True
                break
            snapshot = self.navigate(APP_REGISTRATIONS_URL, "app-registrations")
            snapshot = self.maybe_click_view_all(snapshot)
            visible_apps = self.discover_visible_apps(snapshot)
            if not visible_apps:
                raise AuditError("no visible app candidates discovered from Entra app registrations page")
            candidate = next(
                (
                    item for item in visible_apps
                    if item["displayName"] not in self.run.get("inspectedDisplayNames", [])
                ),
                None,
            )
            if candidate is None:
                if exhausted_load_more:
                    break
                load_more_snapshot = self.maybe_click_load_more(snapshot)
                if load_more_snapshot is None:
                    exhausted_load_more = True
                    continue
                exhausted_load_more = False
                continue
            exhausted_load_more = False
            self.inspect_one_app(candidate, source_snapshot=snapshot)

    def inspect_one_app(self, candidate: dict[str, Any], source_snapshot: SessionSnapshot) -> None:
        display_name = candidate["displayName"]
        app_key = slugify(display_name, f"app-{uuid.uuid4().hex[:8]}")
        hwnd = int(candidate["hwnd"])
        automation_id = str(candidate["automationId"])
        self.host.activate_window(hwnd)
        response = self.host.uia_click(
            hwnd,
            automation_id=automation_id,
            control_type="DataItem",
            double_click=True,
            include_offscreen=True,
            max_results=10,
        )
        self.store.write_artifact(
            app_key,
            f"{app_key}-open.uia-click.json",
            {
                "requestedAtUtc": utc_now(),
                "hwnd": hwnd,
                "automationId": automation_id,
                "response": response,
            },
        )
        detail_snapshot = self.wait_for(
            f"{app_key}-detail-open",
            lambda snap: (
                (snap.url or "").lower() != (source_snapshot.url or "").lower()
                and display_name.lower() in " ".join(snap.text_lines[:20]).lower()
            ),
            timeout_seconds=20,
            app_key=app_key,
        )

        metadata = self.parse_app_metadata(detail_snapshot, fallback_name=display_name)
        client_id = metadata.get("clientId")
        if client_id:
            app_key = client_id

        metadata["appKey"] = app_key
        metadata["inspectedAtUtc"] = utc_now()
        metadata["tenantId"] = self.args.tenant_id
        metadata["detailUrl"] = detail_snapshot.url
        metadata["detailTitle"] = detail_snapshot.title
        metadata["candidateSource"] = {
            "displayName": display_name,
            "automationId": automation_id,
            "listPageUrl": source_snapshot.url,
        }
        metadata["signals"] = self.compute_signals(metadata)
        metadata["probePlan"] = self.build_probe_plan(metadata)

        self.store.write_artifact(app_key, "metadata.json", metadata)
        self.store.append_jsonl(self.store.apps_path, metadata)
        self.latest_apps[app_key] = metadata
        self.run["stats"]["metadataInspected"] += 1
        self.run.setdefault("inspectedDisplayNames", []).append(display_name)
        if client_id and client_id not in self.processed_order:
            self.processed_order.append(client_id)
        self.run["processedOrder"] = self.processed_order
        self._save_run()

    def parse_app_metadata(self, snapshot: SessionSnapshot, fallback_name: str) -> dict[str, Any]:
        lines = snapshot.text_lines
        client_id = extract_guid_near(lines, ("application (client) id", "client id"))
        if not client_id and snapshot.url:
            detail_match = DETAIL_APP_ID_RE.search(snapshot.url)
            if detail_match:
                client_id = detail_match.group("appid").lower()
        redirect_counts_match = REDIRECT_COUNTS_RE.search(" | ".join(lines))
        redirect_counts = {
            "web": int(redirect_counts_match.group("web")) if redirect_counts_match else 0,
            "spa": int(redirect_counts_match.group("spa")) if redirect_counts_match else 0,
            "publicClient": int(redirect_counts_match.group("public")) if redirect_counts_match else 0,
        }
        secret_count = 0
        cert_count = 0
        joined = " | ".join(lines)
        secret_match = SECRET_COUNT_RE.search(joined)
        cert_match = CERT_COUNT_RE.search(joined)
        if secret_match:
            secret_count = int(secret_match.group("count"))
        if cert_match:
            cert_count = int(cert_match.group("count"))

        permissions = self.parse_permissions(snapshot)
        display_name = derive_display_name(lines, snapshot.title) or fallback_name
        return {
            "recordType": "appMetadata",
            "displayName": display_name,
            "clientId": client_id,
            "redirects": redirect_counts,
            "clientCredentials": {
                "secretCount": secret_count,
                "certificateCount": cert_count,
            },
            "permissions": permissions,
            "rawTextLines": lines,
        }

    def parse_permissions(self, snapshot: SessionSnapshot) -> list[dict[str, Any]]:
        permissions: list[dict[str, Any]] = []
        seen: set[tuple[str, str, str]] = set()
        for line in snapshot.text_lines:
            match_type = None
            lowered = line.lower()
            if "delegated" in lowered:
                match_type = "Delegated"
            elif "application" in lowered:
                match_type = "Application"
            if not match_type:
                continue
            scope_match = re.search(
                r"([A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_.]+|user_impersonation|openid|profile|offline_access)",
                line,
            )
            if not scope_match:
                continue
            scope = scope_match.group(1)
            resource = "Microsoft Graph" if "graph" in lowered else "Unknown"
            key = (resource, scope, match_type)
            if key in seen:
                continue
            seen.add(key)
            permissions.append(
                {
                    "resource": resource,
                    "scope": scope,
                    "type": match_type,
                    "sourceLine": line,
                }
            )
        return permissions

    def compute_signals(self, metadata: dict[str, Any]) -> dict[str, Any]:
        permissions = metadata.get("permissions", [])
        mail_permission_signal = any(
            "mail." in permission.get("scope", "").lower() or "mail" in permission.get("sourceLine", "").lower()
            for permission in permissions
        )
        delegated_permission_signal = any(
            permission.get("type") == "Delegated" for permission in permissions
        )
        redirects = metadata.get("redirects", {})
        public_client_signal = bool(redirects.get("publicClient", 0))

        lowered_name = metadata.get("displayName", "").lower()
        auth_lines = [
            line.lower()
            for line in metadata.get("rawTextLines", [])
            if any(token in line.lower() for token in ("redirect", "reply url", "public client", "oauth", "openid"))
        ]
        auth_signal = any(keyword in lowered_name for keyword in AUTH_KEYWORDS) or any(
            keyword in " ".join(auth_lines) for keyword in AUTH_KEYWORDS
        )
        name_mail_signal = any(keyword in lowered_name for keyword in MAIL_KEYWORDS)

        score = sum(
            1
            for flag in (
                public_client_signal,
                delegated_permission_signal,
                mail_permission_signal or name_mail_signal,
                auth_signal,
            )
            if flag
        )
        return {
            "publicClient": public_client_signal,
            "delegatedPermissions": delegated_permission_signal,
            "mail": mail_permission_signal or name_mail_signal,
            "auth": auth_signal,
            "score": score,
        }

    def build_probe_plan(self, metadata: dict[str, Any]) -> list[dict[str, Any]]:
        signals = metadata.get("signals", {})
        plan = [
            {
                "name": "User.Read",
                "scope": GRAPH_USER_READ_SCOPE,
                "enabled": bool(signals.get("publicClient") or signals.get("delegatedPermissions") or signals.get("auth")),
            }
        ]
        plan.append(
            {
                "name": "Mail.Read",
                "scope": GRAPH_MAIL_READ_SCOPE,
                "enabled": bool(signals.get("mail")),
                "dependsOn": "User.Read",
            }
        )
        return plan

    def candidate_apps_for_probe(self) -> list[dict[str, Any]]:
        apps = list(self.latest_apps.values())
        apps.sort(
            key=lambda item: (
                -int(item.get("signals", {}).get("score", 0)),
                item.get("displayName", ""),
            )
        )
        if self.args.max_apps:
            apps = apps[: self.args.max_apps]
        return apps

    def run_probes(self) -> None:
        apps = self.candidate_apps_for_probe()
        if not apps:
            raise AuditError("no apps available for probe. Run metadata collection first or supply --resume.")
        for app in apps:
            client_id = app.get("clientId")
            if not client_id:
                continue
            app_key = client_id
            plans = app.get("probePlan", [])
            user_read_success = False
            for plan in plans:
                if not plan.get("enabled"):
                    continue
                if plan.get("name") == "Mail.Read" and not user_read_success:
                    continue
                result = self.probe_scope(app, plan)
                if plan.get("name") == "User.Read":
                    user_read_success = bool(result.get("success"))

    def probe_scope(self, app: dict[str, Any], plan: dict[str, Any]) -> dict[str, Any]:
        client_id = app["clientId"]
        app_key = client_id
        scope_name = plan["name"]
        scope_value = plan["scope"]
        started_at = utc_now()
        artifact_name = f"probe-{scope_name.lower().replace('.', '-')}.json"
        artifact_path = self.store.artifact_dir(app_key) / artifact_name
        if self.args.resume and artifact_path.exists():
            return json_load(artifact_path, default={}) or {}

        self.run["stats"]["probeAttempts"] += 1
        self._save_run()

        scopes = ["openid", "profile", "offline_access", scope_value]
        device_code_payload = self._request_form(
            "POST",
            f"https://login.microsoftonline.com/{self.args.tenant_id}/oauth2/v2.0/devicecode",
            {
                "client_id": client_id,
                "scope": " ".join(scopes),
            },
        )
        verification_uri = device_code_payload.get("verification_uri") or "https://microsoft.com/devicelogin"
        user_code = device_code_payload.get("user_code")
        device_code = device_code_payload.get("device_code")
        interval = int(device_code_payload.get("interval") or 5)
        expires_in = int(device_code_payload.get("expires_in") or 900)
        if not user_code or not device_code:
            raise AuditError(f"device-code response missing user_code/device_code for {client_id}: {device_code_payload}")

        self.navigate(verification_uri, f"{app_key}-{scope_name}-device-login", app_key=app_key)
        before_fill = self.capture_state(f"{app_key}-{scope_name}-before-fill", app_key=app_key)
        code_input = self.find_code_input(before_fill)
        if not code_input:
            raise AuditError(f"device-code input not found for {client_id} {scope_name}")
        after_fill = self.fill_element(
            code_input,
            user_code,
            f"{app_key}-{scope_name}-fill-user-code",
            app_key=app_key,
            submit=True,
        )
        submit_button = self.find_submit_button(after_fill)
        if submit_button:
            self.click_element(submit_button, f"{app_key}-{scope_name}-submit-user-code", app_key=app_key)

        poll_started = time.monotonic()
        deadline = poll_started + expires_in
        token_result: dict[str, Any] | None = None
        poll_errors: list[dict[str, Any]] = []
        while time.monotonic() < deadline:
            response = self._request_form(
                "POST",
                f"https://login.microsoftonline.com/{self.args.tenant_id}/oauth2/v2.0/token",
                {
                    "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                    "client_id": client_id,
                    "device_code": device_code,
                },
                allow_http_errors=True,
            )
            if response["status"] == 200:
                token_result = response["json"]
                break
            error_json = response.get("json") or {}
            poll_errors.append(
                {
                    "polledAtUtc": utc_now(),
                    "status": response["status"],
                    "error": error_json,
                }
            )
            error_code = (error_json.get("error") or "").lower()
            if error_code not in {"authorization_pending", "slow_down"}:
                break
            time.sleep(interval)

        browser_state = self.capture_state(f"{app_key}-{scope_name}-post-poll", app_key=app_key)
        graph_probe: dict[str, Any] | None = None
        success = token_result is not None
        if success:
            access_token = token_result.get("access_token")
            if access_token:
                graph_probe = self.graph_probe(scope_name, access_token)
                self.run["stats"]["probeSuccesses"] += 1
                self._save_run()

        result = {
            "recordType": "probeResult",
            "appKey": app_key,
            "clientId": client_id,
            "displayName": app.get("displayName"),
            "scopeName": scope_name,
            "scope": scope_value,
            "startedAtUtc": started_at,
            "success": success,
            "deviceCode": {
                "verificationUri": verification_uri,
                "expiresIn": expires_in,
                "interval": interval,
            },
            "tokenResult": token_result,
            "pollErrors": poll_errors[-10:],
            "browserState": {
                "url": browser_state.url,
                "title": browser_state.title,
                "textLines": browser_state.text_lines,
            },
            "graphProbe": graph_probe,
        }
        self.store.write_artifact(app_key, artifact_name, result)
        self.store.append_jsonl(self.store.apps_path, result)
        self.latest_apps[app_key] = {**app, "lastProbeResult": result}
        self._save_run()
        return result

    @staticmethod
    def find_code_input(snapshot: SessionSnapshot) -> dict[str, Any] | None:
        def predicate(element: dict[str, Any]) -> bool:
            text = SessionSnapshot.element_text(element).lower()
            attrs = element.get("attributes") if isinstance(element.get("attributes"), dict) else {}
            merged = " ".join([text, " ".join(str(value).lower() for value in attrs.values())])
            return any(token in merged for token in ("code", "enter code", "user code"))

        return snapshot.find_first(predicate)

    @staticmethod
    def find_submit_button(snapshot: SessionSnapshot) -> dict[str, Any] | None:
        choices = ("next", "continue", "sign in", "submit")
        return snapshot.find_first(
            lambda element: SessionSnapshot.element_role(element) in {"button", "input"}
            and any(choice in SessionSnapshot.element_text(element).lower() for choice in choices)
        )

    def graph_probe(self, scope_name: str, access_token: str) -> dict[str, Any]:
        url = GRAPH_ME_URL if scope_name == "User.Read" else GRAPH_MESSAGES_URL
        response = self._request_json(
            "GET",
            url,
            headers={"Authorization": f"Bearer {access_token}"},
            allow_http_errors=True,
        )
        return response

    def _request_json(
        self,
        method: str,
        url: str,
        headers: dict[str, str] | None = None,
        allow_http_errors: bool = False,
    ) -> dict[str, Any]:
        request = urllib.request.Request(url, method=method.upper(), headers=headers or {})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read().decode("utf-8")
                return {
                    "status": response.status,
                    "json": json.loads(raw) if raw else {},
                }
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8", errors="replace")
            payload = json.loads(raw) if raw else {}
            if allow_http_errors:
                return {"status": exc.code, "json": payload}
            raise AuditError(f"{method} {url} failed with {exc.code}: {payload}") from exc

    def _request_form(
        self,
        method: str,
        url: str,
        payload: dict[str, str],
        allow_http_errors: bool = False,
    ) -> dict[str, Any]:
        encoded = urllib.parse.urlencode(payload).encode("utf-8")
        request = urllib.request.Request(
            url,
            data=encoded,
            method=method.upper(),
            headers={"Content-Type": "application/x-www-form-urlencoded"},
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read().decode("utf-8")
                parsed = json.loads(raw) if raw else {}
                if allow_http_errors:
                    return {"status": response.status, "json": parsed}
                return parsed
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8", errors="replace")
            payload_json = json.loads(raw) if raw else {}
            if allow_http_errors:
                return {"status": exc.code, "json": payload_json}
            raise AuditError(f"{method} {url} failed with {exc.code}: {payload_json}") from exc

    def close_session(self) -> None:
        if not self.session_id:
            return
        try:
            response = self.host.session_close(self.session_id)
            self.run["browserSession"]["closedAtUtc"] = utc_now()
            self.run["browserSession"]["closePayload"] = response
            self._save_run()
        except AuditError as exc:
            self._record_error("session-close", str(exc))

    def build_summary(self) -> dict[str, Any]:
        latest_apps = self.store.load_latest_apps()
        app_metadata = [payload for payload in latest_apps.values() if payload.get("recordType") == "appMetadata"]
        probe_results: list[dict[str, Any]] = []
        for app_key in latest_apps:
            for artifact in self.store.artifact_dir(app_key).glob("probe-*.json"):
                probe = json_load(artifact, default=None)
                if isinstance(probe, dict):
                    probe_results.append(probe)
        successful_probe_count = sum(1 for probe in probe_results if probe.get("success"))
        candidate_count = sum(
            1 for app in app_metadata if app.get("signals", {}).get("score", 0) > 0
        )
        summary = {
            "runId": self.run_id,
            "tenantId": self.args.tenant_id,
            "hostBaseUrl": self.args.host_base_url,
            "completedAtUtc": utc_now(),
            "modes": self.run.get("mode", {}),
            "counts": {
                "metadataInspected": len(app_metadata),
                "candidates": candidate_count,
                "probeResults": len(probe_results),
                "successfulProbes": successful_probe_count,
                "maxAppsHit": self._max_apps_hit,
            },
            "artifacts": {
                "appsJsonl": str(self.store.apps_path),
                "runJson": str(self.store.run_path),
                "summaryJson": str(self.store.summary_path),
                "artifactsRoot": str(self.store.artifacts_root),
            },
            "browserSession": self.run.get("browserSession", {}),
            "errors": self.run.get("errors", []),
            "topCandidates": [
                {
                    "displayName": app.get("displayName"),
                    "clientId": app.get("clientId"),
                    "signals": app.get("signals"),
                    "probePlan": app.get("probePlan"),
                }
                for app in sorted(
                    app_metadata,
                    key=lambda item: -int(item.get("signals", {}).get("score", 0)),
                )[:10]
            ],
        }
        return summary

    def run_audit(self) -> int:
        try:
            self.ensure_session()
            if not self.args.probe_candidates_only:
                self.inspect_visible_apps()
            if not self.args.metadata_only:
                self.run_probes()
            summary = self.build_summary()
            json_dump(self.store.summary_path, summary)
            self.run["status"] = "completed"
            self._save_run()
            self.close_session()
            return 0
        except Exception as exc:
            self.run["status"] = "failed"
            self._record_error("run", str(exc))
            summary = self.build_summary()
            summary["status"] = "failed"
            summary["failure"] = str(exc)
            json_dump(self.store.summary_path, summary)
            return 1


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Resumable Linux-side Entra app auditor using Windows Operator Edge session REST."
    )
    parser.add_argument("--tenant-id", required=True, help="Entra tenant id used for device-code and token endpoints.")
    parser.add_argument(
        "--host-base-url",
        default=os.environ.get("WINDOWS_OPERATOR_BASE_URL", "http://127.0.0.1:43117"),
        help="Windows Operator Host REST base URL.",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path("artifacts/entra-audit"),
        help="Run output root. Writes run.json, summary.json, apps.jsonl, artifacts/<client-id>/.",
    )
    parser.add_argument("--resume", action="store_true", help="Resume existing run.json and artifacts under output root.")
    parser.add_argument("--max-apps", type=int, default=0, help="Max apps to inspect and/or probe. 0 means no explicit cap.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--metadata-only", action="store_true", help="Collect metadata only. Skip OAuth probes.")
    mode.add_argument(
        "--probe-candidates-only",
        action="store_true",
        help="Skip portal metadata inspection. Probe candidates already persisted in apps.jsonl.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    auditor = EntraAuditor(args)
    return auditor.run_audit()


if __name__ == "__main__":
    sys.exit(main())
