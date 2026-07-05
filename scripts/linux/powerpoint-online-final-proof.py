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
from pathlib import Path
from typing import Any


SEM27_MARKER = "sem27 - plan semanal servicios mina.pptx"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
JPEG_SIGNATURE = b"\xff\xd8\xff"


def utc_stamp() -> str:
    return dt.datetime.now(dt.UTC).strftime("%Y%m%dT%H%M%SZ").lower()


def utc_now() -> str:
    return dt.datetime.now(dt.UTC).isoformat().replace("+00:00", "Z")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def default_exchange_root() -> Path:
    return Path(os.environ.get("WINDOWS_OPERATOR_EXCHANGE_ROOT", "/var/lib/windows-server/shared/operator-exchange"))


def default_hot_lease_path(exchange_root: Path) -> Path:
    return Path(os.environ.get("POWERPOINT_ONLINE_HOT_LEASE_PATH", exchange_root / "state" / "ppt-hot-lease.json"))


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
    payload: dict[str, Any] | None,
    timeout_seconds: int,
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


def is_sem27_url(url: str) -> bool:
    decoded = urllib.parse.unquote(url).lower()
    return SEM27_MARKER in decoded


def build_update_request(args: argparse.Namespace, run_id: str, proof_text: str) -> dict[str, Any]:
    now = utc_now()
    return {
        "deckUrl": args.deck_url,
        "sessionId": run_id,
        "job": {
            "jobId": run_id,
            "discoverTargets": True,
            "validateOnly": False,
            "operations": [
                {
                    "kind": "replaceText",
                    "targetId": "TITLE_MAIN",
                    "mode": "plain",
                    "text": proof_text,
                }
            ],
            "requestedBy": "codex-live-mutation-proof",
            "createdAt": now,
        },
        "evidenceSlideNumber": args.slide_number,
        "capture": True,
        "allowDeckMutation": bool(args.allow_deck_mutation),
        "prepareTemplate": True,
        "cleanupTemplate": True,
        "cleanupTemplateOnFailure": True,
        "templateWaitSeconds": args.template_wait_seconds,
        "verifyReopen": True,
        "reopenWaitSeconds": args.reopen_wait_seconds,
        "cleanupSession": True,
        "openWaitSeconds": args.open_wait_seconds,
        "jobTimeoutSeconds": args.job_timeout_seconds,
        "saveTimeoutSeconds": args.save_timeout_seconds,
        "savePollSeconds": args.save_poll_seconds,
    }


def build_readiness_request(args: argparse.Namespace, run_id: str, verify_reopen: bool = True) -> dict[str, Any]:
    now = utc_now()
    return {
        "deckUrl": args.deck_url,
        "sessionId": run_id,
        "job": {
            "jobId": run_id,
            "discoverTargets": True,
            "validateOnly": True,
            "operations": [],
            "requestedBy": "codex-readiness-proof-fast" if not verify_reopen else "codex-readiness-proof",
            "createdAt": now,
        },
        "evidenceSlideNumber": args.slide_number,
        "capture": True,
        "allowDeckMutation": False,
        "prepareTemplate": False,
        "cleanupTemplate": False,
        "cleanupTemplateOnFailure": False,
        "verifyReopen": verify_reopen,
        "cleanupSession": True,
        "openWaitSeconds": args.open_wait_seconds,
        "jobTimeoutSeconds": args.job_timeout_seconds,
        "saveTimeoutSeconds": args.save_timeout_seconds,
        "savePollSeconds": args.save_poll_seconds,
        "reopenWaitSeconds": args.reopen_wait_seconds,
    }


def build_warm_session_start_request(args: argparse.Namespace, session_id: str) -> dict[str, Any]:
    return {
        "deckUrl": args.deck_url,
        "sessionId": session_id,
        "runId": session_id,
        "capture": False,
        "waitSeconds": args.open_wait_seconds,
    }


def build_warm_iteration_request(args: argparse.Namespace, session_id: str, job_id: str) -> dict[str, Any]:
    now = utc_now()
    return {
        "sessionId": session_id,
        "job": {
            "jobId": job_id,
            "discoverTargets": True,
            "validateOnly": True,
            "operations": [],
            "requestedBy": "codex-warm-profile-iteration",
            "createdAt": now,
        },
        "evidenceSlideNumber": args.slide_number,
        "capture": False,
        "allowDeckMutation": False,
        "prepareTemplate": False,
        "cleanupTemplate": False,
        "cleanupTemplateOnFailure": False,
        "verifyReopen": False,
        "cleanupSession": False,
        "openWaitSeconds": args.open_wait_seconds,
        "jobTimeoutSeconds": args.job_timeout_seconds,
        "saveTimeoutSeconds": args.save_timeout_seconds,
        "savePollSeconds": args.save_poll_seconds,
        "reopenWaitSeconds": args.reopen_wait_seconds,
    }


def edge_like_windows(windows: Any) -> list[dict[str, Any]]:
    if not isinstance(windows, list):
        return []
    matches: list[dict[str, Any]] = []
    for item in windows:
        if not isinstance(item, dict):
            continue
        title = str(item.get("title", "")).lower()
        class_name = str(item.get("className", "")).lower()
        if "chrome_widgetwin" in class_name or "edge" in title or "chrome" in title or "powerpoint" in title:
            matches.append(item)
    return matches


def successful_evidence_count(evidence: Any) -> int:
    if not isinstance(evidence, list):
        return 0
    count = 0
    for item in evidence:
        if not isinstance(item, dict) or item.get("success") is not True:
            continue
        artifact = item.get("artifact")
        if not isinstance(artifact, dict):
            continue
        media_type = artifact.get("mediaType")
        if not isinstance(media_type, str) or not media_type.startswith("image/"):
            continue
        artifact_bytes = artifact.get("bytes")
        if not isinstance(artifact_bytes, int) or artifact_bytes <= 0:
            continue
        if not any(str(artifact.get(key, "")).strip() for key in ("hostPath", "relativePath", "path")):
            continue
        count += 1
    return count


def is_windows_path(value: str) -> bool:
    path = value.strip()
    if not path:
        return False
    return bool(re.match(r"^[a-zA-Z]:[\\/]", path)) or path.startswith("\\\\")


def local_artifact_path(artifact: dict[str, Any], exchange_root: Path | None) -> Path | None:
    host_path = artifact.get("hostPath")
    if isinstance(host_path, str):
        candidate = host_path.strip()
        if candidate and not is_windows_path(candidate):
            candidate_path = Path(candidate)
            if candidate_path.is_absolute():
                return candidate_path

    relative_path = artifact.get("relativePath")
    if isinstance(relative_path, str) and exchange_root is not None:
        candidate = relative_path.strip()
        if candidate:
            return exchange_root / Path(candidate)

    artifact_path = artifact.get("path")
    if isinstance(artifact_path, str):
        candidate = artifact_path.strip()
        if candidate and not is_windows_path(candidate):
            candidate_path = Path(candidate)
            if candidate_path.is_absolute():
                return candidate_path

    return None


def verified_artifact_path(item: Any, exchange_root: Path | None) -> Path | None:
    if not isinstance(item, dict) or item.get("success") is not True:
        return None
    artifact = item.get("artifact")
    if not isinstance(artifact, dict):
        return None
    media_type = artifact.get("mediaType")
    if not isinstance(media_type, str) or not media_type.startswith("image/"):
        return None
    artifact_bytes = artifact.get("bytes")
    if not isinstance(artifact_bytes, int) or artifact_bytes <= 0:
        return None
    if not any(str(artifact.get(key, "")).strip() for key in ("hostPath", "relativePath", "path")):
        return None

    candidate = local_artifact_path(artifact, exchange_root)
    if candidate is None:
        return None
    try:
        stat_result = candidate.stat()
    except OSError:
        return None
    if not candidate.is_file() or stat_result.st_size <= 0:
        return None
    if stat_result.st_size != artifact_bytes:
        return None
    try:
        with candidate.open("rb") as handle:
            header = handle.read(max(len(PNG_SIGNATURE), len(JPEG_SIGNATURE)))
    except OSError:
        return None
    if media_type == "image/png":
        if not header.startswith(PNG_SIGNATURE):
            return None
    elif media_type == "image/jpeg":
        if not header.startswith(JPEG_SIGNATURE):
            return None
    else:
        return None
    return candidate


def verified_evidence_paths(evidence: Any, exchange_root: Path | None) -> list[Path]:
    if not isinstance(evidence, list):
        return []
    paths: list[Path] = []
    for item in evidence:
        candidate = verified_artifact_path(item, exchange_root)
        if candidate is not None:
            paths.append(candidate)
    return paths


def summarize_response(response: Any, edge_count: int | None, exchange_root: Path | None = None) -> dict[str, Any]:
    if not isinstance(response, dict):
        return {
            "success": False,
            "status": "nonJsonResponse",
            "saveProofTier": None,
            "edgeLikeWindowCount": edge_count,
        }

    job_record = response.get("jobRecord") if isinstance(response.get("jobRecord"), dict) else {}
    result = job_record.get("result") if isinstance(job_record.get("result"), dict) else {}
    targets = result.get("targets") if isinstance(result.get("targets"), list) else []
    discovered = result.get("discoveredTargets") if isinstance(result.get("discoveredTargets"), list) else []
    evidence = response.get("evidence") if isinstance(response.get("evidence"), list) else []
    verified_paths = verified_evidence_paths(evidence, exchange_root)
    session_cleanup = response.get("sessionCleanupSession")
    cleanup_status = session_cleanup.get("status") if isinstance(session_cleanup, dict) else None
    title_main_target_succeeded = any(
        isinstance(target, dict)
        and target.get("targetId") == "TITLE_MAIN"
        and target.get("operationKind") == "replaceText"
        and target.get("status") == "succeeded"
        for target in targets
    )
    title_main_discovered = any(
        isinstance(target, dict) and target.get("targetId") == "TITLE_MAIN"
        for target in discovered
    )

    return {
        "success": response.get("success") is True,
        "status": response.get("status"),
        "saveProofTier": response.get("saveProofTier"),
        "jobStatus": job_record.get("status"),
        "claimedBy": job_record.get("claimedBy"),
        "targetCount": len(targets),
        "discoveredTargetCount": len(discovered),
        "titleMainTargetSucceeded": title_main_target_succeeded,
        "titleMainDiscovered": title_main_discovered,
        "evidenceCount": len(evidence),
        "successfulEvidenceCount": successful_evidence_count(evidence),
        "verifiedEvidenceCount": len(verified_paths),
        "distinctVerifiedEvidenceCount": len(
            {
                str(path.resolve(strict=False) if hasattr(path, "resolve") else path.absolute())
                for path in verified_paths
            }
        ),
        "templatePreparationStatus": status_of(response.get("templatePreparationSession")),
        "verificationStatus": status_of(response.get("verificationSession")),
        "templateCleanupStatus": status_of(response.get("templateCleanupSession")),
        "sessionCleanupStatus": cleanup_status,
        "edgeLikeWindowCount": edge_count,
        "phaseTimings": response.get("phaseTimings") if isinstance(response.get("phaseTimings"), dict) else None,
    }


def status_of(value: Any) -> str | None:
    if isinstance(value, dict):
        return value.get("status")
    return None


def proof_passed(summary: dict[str, Any]) -> bool:
    return (
        summary.get("httpStatus") == 200
        and summary.get("success") is True
        and summary.get("status") == "succeeded"
        and summary.get("saveProofTier") == "tier3ReopenVisual"
        and summary.get("jobStatus") == "succeeded"
        and summary.get("claimedBy") == "officejs-taskpane"
        and summary.get("targetCount", 0) >= 1
        and summary.get("titleMainTargetSucceeded") is True
        and summary.get("titleMainDiscovered") is True
        and summary.get("evidenceCount", 0) >= 3
        and summary.get("successfulEvidenceCount", 0) >= 3
        and summary.get("verifiedEvidenceCount", 0) >= 3
        and summary.get("distinctVerifiedEvidenceCount", 0) >= 3
        and summary.get("templatePreparationStatus") == "ready"
        and summary.get("verificationStatus") == "ready"
        and summary.get("templateCleanupStatus") == "ready"
        and summary.get("sessionCleanupStatus") == "closed"
        and summary.get("edgeLikeWindowCount") == 0
    )


def readiness_passed(summary: dict[str, Any]) -> bool:
    return (
        summary.get("httpStatus") == 200
        and summary.get("success") is True
        and summary.get("status") == "succeeded"
        and summary.get("saveProofTier") == "tier3ReopenVisual"
        and summary.get("jobStatus") == "succeeded"
        and summary.get("claimedBy") == "officejs-taskpane"
        and summary.get("evidenceCount", 0) >= 2
        and summary.get("successfulEvidenceCount", 0) >= 2
        and summary.get("verifiedEvidenceCount", 0) >= 2
        and summary.get("distinctVerifiedEvidenceCount", 0) >= 2
        and summary.get("verificationStatus") == "ready"
        and summary.get("sessionCleanupStatus") == "closed"
        and summary.get("edgeLikeWindowCount") == 0
    )


def fast_readiness_passed(summary: dict[str, Any]) -> bool:
    return (
        summary.get("httpStatus") == 200
        and summary.get("success") is True
        and summary.get("status") == "succeeded"
        and summary.get("jobStatus") == "succeeded"
        and summary.get("claimedBy") == "officejs-taskpane"
        and summary.get("evidenceCount", 0) >= 1
        and summary.get("successfulEvidenceCount", 0) >= 1
        and summary.get("verifiedEvidenceCount", 0) >= 1
        and summary.get("distinctVerifiedEvidenceCount", 0) >= 1
        and summary.get("sessionCleanupStatus") == "closed"
        and summary.get("edgeLikeWindowCount") == 0
    )


def gate_verification_passed(summary: dict[str, Any]) -> bool:
    return (
        summary.get("status") == "hostGateVerified"
        and summary.get("httpStatus") == 422
        and summary.get("errorCode") == "powerpoint_validation_failed"
        and summary.get("jobLookupHttpStatus") == 404
        and summary.get("edgeLikeWindowCount") == 0
        and summary.get("edgeLikeWindowCountBefore") == 0
    )


def warm_iteration_passed(summary: dict[str, Any]) -> bool:
    return (
        summary.get("httpStatus") == 200
        and summary.get("success") is True
        and summary.get("status") == "succeeded"
        and summary.get("jobStatus") == "succeeded"
        and summary.get("claimedBy") == "officejs-taskpane"
    )


def warm_session_started(http_status: int | str, response: Any) -> bool:
    if http_status != 200 or not isinstance(response, dict):
        return False
    return response.get("success") is True and response.get("status") == "ready"


def parse_utc(value: Any) -> dt.datetime | None:
    if not isinstance(value, str) or not value.strip():
        return None
    text = value.strip()
    if text.endswith("Z"):
        text = f"{text[:-1]}+00:00"
    try:
        parsed = dt.datetime.fromisoformat(text)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=dt.UTC)
    return parsed.astimezone(dt.UTC)


def iso_utc(value: dt.datetime) -> str:
    return value.astimezone(dt.UTC).isoformat().replace("+00:00", "Z")


def lease_expired(lease: Any, now: dt.datetime | None = None) -> bool:
    if not isinstance(lease, dict):
        return True
    expires_at = parse_utc(lease.get("expiresAtUtc"))
    if expires_at is None:
        return True
    return expires_at <= (now or dt.datetime.now(dt.UTC))


def session_ready(http_status: int | str, response: Any) -> bool:
    return http_status == 200 and isinstance(response, dict) and response.get("success") is True and response.get("status") == "ready"


def make_hot_lease(args: argparse.Namespace, session_id: str, run_id: str) -> dict[str, Any]:
    now = dt.datetime.now(dt.UTC)
    return {
        "kind": "powerpoint-online-hot-lease",
        "version": 1,
        "sessionId": session_id,
        "deckUrl": args.deck_url,
        "baseUrl": args.base_url,
        "createdAtUtc": iso_utc(now),
        "updatedAtUtc": iso_utc(now),
        "expiresAtUtc": iso_utc(now + dt.timedelta(seconds=args.hot_lease_ttl_seconds)),
        "ttlSeconds": args.hot_lease_ttl_seconds,
        "lastRunId": run_id,
    }


def refresh_hot_lease(lease: dict[str, Any], args: argparse.Namespace, run_id: str) -> dict[str, Any]:
    now = dt.datetime.now(dt.UTC)
    refreshed = dict(lease)
    refreshed["updatedAtUtc"] = iso_utc(now)
    refreshed["expiresAtUtc"] = iso_utc(now + dt.timedelta(seconds=args.hot_lease_ttl_seconds))
    refreshed["ttlSeconds"] = args.hot_lease_ttl_seconds
    refreshed["lastRunId"] = run_id
    return refreshed


def remove_file(path: Path) -> None:
    try:
        path.unlink()
    except FileNotFoundError:
        return


def cleanup_session(
    base_url: str,
    session_id: str,
    timeout_seconds: int,
) -> tuple[int | str, bytes, Any]:
    return request_json(
        base_url,
        "POST",
        f"/v1/powerpoint/online/sessions/{urllib.parse.quote(session_id, safe='')}/cleanup",
        {},
        timeout_seconds,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run or prepare the gated PowerPoint Online final mutation proof.",
    )
    parser.add_argument("--deck-url", required=True)
    parser.add_argument("--base-url", default=os.environ.get("WINDOWS_OPERATOR_HOST_URL", "http://127.0.0.1:43117"))
    parser.add_argument("--run-id", default=f"ppt-mutation-proof-{utc_stamp()}")
    parser.add_argument("--exchange-root", type=Path, default=default_exchange_root())
    parser.add_argument("--slide-number", type=int, default=4)
    parser.add_argument("--proof-text", default=None)
    parser.add_argument("--execute", action="store_true", help="POST to the Host endpoint. Without this, only writes request artifacts.")
    parser.add_argument("--verify-host-gate", action="store_true", help="POST with allowDeckMutation=false and require Host to reject before opening Edge.")
    parser.add_argument("--verify-readiness", action="store_true", help="POST bounded non-mutating readiness proof before mutation approval.")
    parser.add_argument("--verify-readiness-fast", action="store_true", help="POST bounded non-mutating readiness proof without tier3 reopen verification.")
    parser.add_argument("--profile-warm", action="store_true", help="Open one session, run validate-only warm iterations, then cleanup.")
    parser.add_argument("--hot-start", action="store_true", help="Start or reuse a persistent hot PowerPoint Online lease.")
    parser.add_argument("--hot-run", action="store_true", help="Run one validate-only iteration against the persistent hot lease.")
    parser.add_argument("--hot-status", action="store_true", help="Read persistent hot lease and live session status.")
    parser.add_argument("--hot-cleanup", action="store_true", help="Close the persistent hot lease session and remove the lease file.")
    parser.add_argument("--allow-deck-mutation", action="store_true", help="Required with --execute because this writes to SharePoint.")
    parser.add_argument("--allow-sem27", action="store_true", help="Required to execute against the SEM27 production deck.")
    parser.add_argument("--open-wait-seconds", type=int, default=40)
    parser.add_argument("--job-timeout-seconds", type=int, default=60)
    parser.add_argument("--save-timeout-seconds", type=int, default=30)
    parser.add_argument("--save-poll-seconds", type=int, default=1)
    parser.add_argument("--reopen-wait-seconds", type=int, default=40)
    parser.add_argument("--template-wait-seconds", type=int, default=2)
    parser.add_argument("--http-timeout-seconds", type=int, default=420)
    parser.add_argument("--warm-iterations", type=int, default=2)
    parser.add_argument("--hot-session-id", default=None)
    parser.add_argument("--hot-lease-path", type=Path, default=None)
    parser.add_argument("--hot-lease-ttl-seconds", type=int, default=1800)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    run_id = args.run_id
    run_root = args.exchange_root / "runs" / run_id
    proof_text = args.proof_text or f"Windows Operator live edit proof {utc_now()}"
    sem27 = is_sem27_url(args.deck_url)
    hot_lease_path = args.hot_lease_path or default_hot_lease_path(args.exchange_root)
    hot_mode = args.hot_start or args.hot_run or args.hot_status or args.hot_cleanup

    gate_errors: list[str] = []
    selected_modes = (
        int(bool(args.execute))
        + int(bool(args.verify_host_gate))
        + int(bool(args.verify_readiness))
        + int(bool(args.verify_readiness_fast))
        + int(bool(args.profile_warm))
        + int(bool(args.hot_start))
        + int(bool(args.hot_run))
        + int(bool(args.hot_status))
        + int(bool(args.hot_cleanup))
    )
    if selected_modes > 1:
        gate_errors.append("--execute, --verify-host-gate, --verify-readiness, --verify-readiness-fast, --profile-warm, and hot lease modes are mutually exclusive")
    if args.execute and not args.allow_deck_mutation:
        gate_errors.append("--execute requires --allow-deck-mutation")
    if args.execute and sem27 and not args.allow_sem27:
        gate_errors.append("--execute against SEM27 requires --allow-sem27")
    if args.profile_warm and args.warm_iterations < 2:
        gate_errors.append("--profile-warm requires --warm-iterations >= 2")
    if args.hot_lease_ttl_seconds <= 0:
        gate_errors.append("--hot-lease-ttl-seconds must be positive")

    if args.hot_start:
        update_request = build_warm_session_start_request(args, args.hot_session_id or run_id)
    elif args.hot_run:
        update_request = {
            "mode": "hot-run",
            "leasePath": str(hot_lease_path),
            "runId": run_id,
        }
    elif args.hot_status:
        update_request = {
            "mode": "hot-status",
            "leasePath": str(hot_lease_path),
            "runId": run_id,
        }
    elif args.hot_cleanup:
        update_request = {
            "mode": "hot-cleanup",
            "leasePath": str(hot_lease_path),
            "runId": run_id,
        }
    elif args.profile_warm:
        update_request = build_warm_session_start_request(args, run_id)
    elif args.verify_readiness or args.verify_readiness_fast:
        update_request = build_readiness_request(args, run_id, verify_reopen=not args.verify_readiness_fast)
    else:
        update_request = build_update_request(args, run_id, proof_text)
    if args.verify_host_gate:
        update_request["allowDeckMutation"] = False

    write_json(run_root / "request.json", update_request)

    if gate_errors:
        summary = {
            "runId": run_id,
            "success": False,
            "status": "gateFailed",
            "execute": args.execute,
            "verifyHostGate": args.verify_host_gate,
            "verifyReadiness": args.verify_readiness,
            "verifyReadinessFast": args.verify_readiness_fast,
            "profileWarm": args.profile_warm,
            "hotStart": args.hot_start,
            "hotRun": args.hot_run,
            "hotStatus": args.hot_status,
            "hotCleanup": args.hot_cleanup,
            "hotLeasePath": str(hot_lease_path),
            "sem27": sem27,
            "errors": gate_errors,
            "requestPath": str(run_root / "request.json"),
            "observedAtUtc": utc_now(),
        }
        write_json(run_root / "summary.json", summary)
        print(run_root / "summary.json")
        return 2

    if not args.execute and not args.verify_host_gate and not args.verify_readiness and not args.verify_readiness_fast and not args.profile_warm and not hot_mode:
        summary = {
            "runId": run_id,
            "success": True,
            "status": "prepared",
            "execute": False,
            "verifyHostGate": False,
            "verifyReadiness": False,
            "verifyReadinessFast": False,
            "profileWarm": False,
            "hotStart": False,
            "hotRun": False,
            "hotStatus": False,
            "hotCleanup": False,
            "sem27": sem27,
            "requestPath": str(run_root / "request.json"),
            "nextStep": "rerun with --execute --allow-deck-mutation; add --allow-sem27 only with explicit SEM27 approval",
            "observedAtUtc": utc_now(),
        }
        write_json(run_root / "summary.json", summary)
        print(run_root / "summary.json")
        return 0

    started = time.time()
    windows_before_status, _windows_before_body, windows_before = request_json(args.base_url, "GET", "/v1/windows", None, 30)
    write_json(run_root / "windows-before.json", windows_before if windows_before is not None else {"status": windows_before_status})
    edge_matches_before = edge_like_windows(windows_before)

    if hot_mode:
        lease = read_json(hot_lease_path)
        lease_valid = isinstance(lease, dict) and isinstance(lease.get("sessionId"), str) and bool(lease.get("sessionId"))
        lease_session_id = str(lease.get("sessionId")) if lease_valid else None
        expired = lease_expired(lease)
        status_response: Any = None
        status_http: int | str | None = None
        status_body: bytes = b""
        if lease_session_id is not None and not args.hot_cleanup:
            status_http, status_body, status_response = request_json(
                args.base_url,
                "GET",
                f"/v1/powerpoint/online/sessions/{urllib.parse.quote(lease_session_id, safe='')}",
                None,
                30,
            )
            write_json(run_root / "hot-session-status.json", status_response if status_response is not None else {"raw": status_body.decode("utf-8", "replace")})

        if args.hot_status:
            live_ready = session_ready(status_http, status_response)
            windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
            write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
            edge_matches = edge_like_windows(windows)
            if not lease_valid:
                status = "hotLeaseMissing"
            elif expired:
                status = "hotLeaseExpired"
            elif live_ready:
                status = "hotLeaseReady"
            else:
                status = "hotLeaseNotReady"
            summary = {
                "runId": run_id,
                "success": lease_valid and live_ready and not expired,
                "status": status,
                "httpStatus": status_http,
                "windowsHttpStatus": windows_status,
                "windowsBeforeHttpStatus": windows_before_status,
                "execute": False,
                "verifyHostGate": False,
                "verifyReadiness": False,
                "verifyReadinessFast": False,
                "profileWarm": False,
                "hotStart": False,
                "hotRun": False,
                "hotStatus": True,
                "hotCleanup": False,
                "sem27": sem27,
                "leasePath": str(hot_lease_path),
                "lease": lease if lease_valid else None,
                "leaseExpired": expired,
                "sessionStatusPath": str(run_root / "hot-session-status.json") if lease_session_id is not None else None,
                "windowsBeforePath": str(run_root / "windows-before.json"),
                "windowsAfterPath": str(run_root / "windows-after.json"),
                "edgeLikeWindowCountBefore": len(edge_matches_before),
                "edgeLikeWindowCount": len(edge_matches),
                "elapsedSeconds": round(time.time() - started, 3),
                "observedAtUtc": utc_now(),
            }
            write_json(run_root / "summary.json", summary)
            print(run_root / "summary.json")
            return 0 if summary["success"] else 1

        if args.hot_start:
            cleanup_before_summary: dict[str, Any] | None = None
            deck_mismatch = lease_valid and lease.get("deckUrl") != args.deck_url
            live_ready = session_ready(status_http, status_response)
            if lease_valid and live_ready and not expired and not deck_mismatch:
                refreshed = refresh_hot_lease(lease, args, run_id)
                write_json(hot_lease_path, refreshed)
                windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
                write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
                edge_matches = edge_like_windows(windows)
                summary = {
                    "runId": run_id,
                    "success": True,
                    "status": "hotLeaseAlreadyReady",
                    "httpStatus": status_http,
                    "windowsHttpStatus": windows_status,
                    "windowsBeforeHttpStatus": windows_before_status,
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": True,
                    "hotRun": False,
                    "hotStatus": False,
                    "hotCleanup": False,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "lease": refreshed,
                    "sessionStatusPath": str(run_root / "hot-session-status.json"),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "windowsAfterPath": str(run_root / "windows-after.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "edgeLikeWindowCount": len(edge_matches),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
                write_json(run_root / "summary.json", summary)
                print(run_root / "summary.json")
                return 0

            if lease_session_id is not None:
                cleanup_status, cleanup_body, cleanup_response = cleanup_session(args.base_url, lease_session_id, args.http_timeout_seconds)
                write_json(run_root / "cleanup-before-start-response.json", cleanup_response if cleanup_response is not None else {"raw": cleanup_body.decode("utf-8", "replace")})
                cleanup_before_summary = {
                    "attempted": True,
                    "httpStatus": cleanup_status,
                    "status": status_of(cleanup_response),
                    "responsePath": str(run_root / "cleanup-before-start-response.json"),
                    "reason": "expired" if expired else "deckMismatch" if deck_mismatch else "notReady",
                }
                if live_ready and cleanup_before_summary.get("status") != "closed":
                    windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
                    write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
                    summary = {
                        "runId": run_id,
                        "success": False,
                        "status": "hotLeaseCleanupBeforeStartFailed",
                        "httpStatus": cleanup_status,
                        "windowsHttpStatus": windows_status,
                        "windowsBeforeHttpStatus": windows_before_status,
                        "execute": False,
                        "verifyHostGate": False,
                        "verifyReadiness": False,
                        "verifyReadinessFast": False,
                        "profileWarm": False,
                        "hotStart": True,
                        "hotRun": False,
                        "hotStatus": False,
                        "hotCleanup": False,
                        "sem27": sem27,
                        "leasePath": str(hot_lease_path),
                        "lease": lease,
                        "cleanupBeforeStart": cleanup_before_summary,
                        "elapsedSeconds": round(time.time() - started, 3),
                        "observedAtUtc": utc_now(),
                    }
                    write_json(run_root / "summary.json", summary)
                    print(run_root / "summary.json")
                    return 1
                remove_file(hot_lease_path)

            session_id = args.hot_session_id or run_id
            start_request = build_warm_session_start_request(args, session_id)
            write_json(run_root / "request.json", start_request)
            start_started = time.time()
            start_status, start_body, start_response = request_json(
                args.base_url,
                "POST",
                "/v1/powerpoint/online/sessions",
                start_request,
                args.http_timeout_seconds,
            )
            start_elapsed_ms = round((time.time() - start_started) * 1000, 3)
            write_json(run_root / "session-start-response.json", start_response if start_response is not None else {"raw": start_body.decode("utf-8", "replace")})
            session_started = warm_session_started(start_status, start_response)
            lease_payload = make_hot_lease(args, session_id, run_id) if session_started else None
            if lease_payload is not None:
                write_json(hot_lease_path, lease_payload)
            windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
            write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
            edge_matches = edge_like_windows(windows)
            summary = {
                "runId": run_id,
                "success": session_started,
                "status": "hotLeaseStarted" if session_started else "hotLeaseStartFailed",
                "httpStatus": start_status,
                "windowsHttpStatus": windows_status,
                "windowsBeforeHttpStatus": windows_before_status,
                "execute": False,
                "verifyHostGate": False,
                "verifyReadiness": False,
                "verifyReadinessFast": False,
                "profileWarm": False,
                "hotStart": True,
                "hotRun": False,
                "hotStatus": False,
                "hotCleanup": False,
                "sem27": sem27,
                "leasePath": str(hot_lease_path),
                "lease": lease_payload,
                "cleanupBeforeStart": cleanup_before_summary,
                "requestPath": str(run_root / "request.json"),
                "sessionStartResponsePath": str(run_root / "session-start-response.json"),
                "windowsBeforePath": str(run_root / "windows-before.json"),
                "windowsAfterPath": str(run_root / "windows-after.json"),
                "edgeLikeWindowCountBefore": len(edge_matches_before),
                "edgeLikeWindowCount": len(edge_matches),
                "sessionStart": {
                    "httpStatus": start_status,
                    "requestPath": str(run_root / "request.json"),
                    "responsePath": str(run_root / "session-start-response.json"),
                    "elapsedMs": start_elapsed_ms,
                    "success": session_started,
                    "status": start_response.get("status") if isinstance(start_response, dict) else None,
                },
                "openSessionMs": start_elapsed_ms,
                "elapsedSeconds": round(time.time() - started, 3),
                "observedAtUtc": utc_now(),
            }
            write_json(run_root / "summary.json", summary)
            print(run_root / "summary.json")
            return 0 if summary["success"] else 1

        if args.hot_run:
            if not lease_valid:
                summary = {
                    "runId": run_id,
                    "success": False,
                    "status": "hotLeaseMissing",
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": False,
                    "hotRun": True,
                    "hotStatus": False,
                    "hotCleanup": False,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
                write_json(run_root / "summary.json", summary)
                print(run_root / "summary.json")
                return 1
            if expired:
                summary = {
                    "runId": run_id,
                    "success": False,
                    "status": "hotLeaseExpired",
                    "httpStatus": status_http,
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": False,
                    "hotRun": True,
                    "hotStatus": False,
                    "hotCleanup": False,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "lease": lease,
                    "leaseExpired": True,
                    "sessionStatusPath": str(run_root / "hot-session-status.json"),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
                write_json(run_root / "summary.json", summary)
                print(run_root / "summary.json")
                return 1
            if not session_ready(status_http, status_response):
                summary = {
                    "runId": run_id,
                    "success": False,
                    "status": "hotLeaseNotReady",
                    "httpStatus": status_http,
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": False,
                    "hotRun": True,
                    "hotStatus": False,
                    "hotCleanup": False,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "lease": lease,
                    "sessionStatusPath": str(run_root / "hot-session-status.json"),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
                write_json(run_root / "summary.json", summary)
                print(run_root / "summary.json")
                return 1
            job_id = f"{run_id}-hot"
            iteration_request = build_warm_iteration_request(args, lease_session_id, job_id)
            write_json(run_root / "request.json", iteration_request)
            iteration_status, iteration_body, iteration_response = request_json(
                args.base_url,
                "POST",
                "/v1/powerpoint/online/updates",
                iteration_request,
                args.http_timeout_seconds,
            )
            write_json(run_root / "response.json", iteration_response if iteration_response is not None else {"raw": iteration_body.decode("utf-8", "replace")})
            iteration_summary = summarize_response(iteration_response, None, args.exchange_root)
            iteration_summary.update(
                {
                    "runId": run_id,
                    "jobId": job_id,
                    "httpStatus": iteration_status,
                    "requestPath": str(run_root / "request.json"),
                    "responsePath": str(run_root / "response.json"),
                }
            )
            iteration_success = warm_iteration_passed(iteration_summary)
            if iteration_success:
                refreshed = refresh_hot_lease(lease, args, run_id)
                write_json(hot_lease_path, refreshed)
            else:
                refreshed = lease
            windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
            write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
            edge_matches = edge_like_windows(windows)
            summary = dict(iteration_summary)
            summary.update(
                {
                    "success": iteration_success,
                    "status": "hotRunSucceeded" if iteration_success else "hotRunFailed",
                    "windowsHttpStatus": windows_status,
                    "windowsBeforeHttpStatus": windows_before_status,
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": False,
                    "hotRun": True,
                    "hotStatus": False,
                    "hotCleanup": False,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "lease": refreshed,
                    "sessionStatusPath": str(run_root / "hot-session-status.json"),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "windowsAfterPath": str(run_root / "windows-after.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "edgeLikeWindowCount": len(edge_matches),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
            )
            write_json(run_root / "summary.json", summary)
            print(run_root / "summary.json")
            return 0 if summary["success"] else 1

        if args.hot_cleanup:
            if not lease_valid:
                windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
                write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
                edge_matches = edge_like_windows(windows)
                summary = {
                    "runId": run_id,
                    "success": True,
                    "status": "hotLeaseMissing",
                    "windowsHttpStatus": windows_status,
                    "windowsBeforeHttpStatus": windows_before_status,
                    "execute": False,
                    "verifyHostGate": False,
                    "verifyReadiness": False,
                    "verifyReadinessFast": False,
                    "profileWarm": False,
                    "hotStart": False,
                    "hotRun": False,
                    "hotStatus": False,
                    "hotCleanup": True,
                    "sem27": sem27,
                    "leasePath": str(hot_lease_path),
                    "windowsBeforePath": str(run_root / "windows-before.json"),
                    "windowsAfterPath": str(run_root / "windows-after.json"),
                    "edgeLikeWindowCountBefore": len(edge_matches_before),
                    "edgeLikeWindowCount": len(edge_matches),
                    "elapsedSeconds": round(time.time() - started, 3),
                    "observedAtUtc": utc_now(),
                }
                write_json(run_root / "summary.json", summary)
                print(run_root / "summary.json")
                return 0
            cleanup_status, cleanup_body, cleanup_response = cleanup_session(args.base_url, lease_session_id, args.http_timeout_seconds)
            write_json(run_root / "cleanup-response.json", cleanup_response if cleanup_response is not None else {"raw": cleanup_body.decode("utf-8", "replace")})
            windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
            write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
            edge_matches = edge_like_windows(windows)
            cleanup_summary = {
                "attempted": True,
                "httpStatus": cleanup_status,
                "status": status_of(cleanup_response),
                "responsePath": str(run_root / "cleanup-response.json"),
            }
            cleanup_succeeded = cleanup_summary.get("status") == "closed" or cleanup_status == 404
            if cleanup_succeeded:
                remove_file(hot_lease_path)
            summary = {
                "runId": run_id,
                "success": cleanup_succeeded and len(edge_matches) <= len(edge_matches_before),
                "status": "hotLeaseClosed" if cleanup_succeeded else "hotLeaseCleanupFailed",
                "httpStatus": cleanup_status,
                "windowsHttpStatus": windows_status,
                "windowsBeforeHttpStatus": windows_before_status,
                "execute": False,
                "verifyHostGate": False,
                "verifyReadiness": False,
                "verifyReadinessFast": False,
                "profileWarm": False,
                "hotStart": False,
                "hotRun": False,
                "hotStatus": False,
                "hotCleanup": True,
                "sem27": sem27,
                "leasePath": str(hot_lease_path),
                "lease": lease,
                "cleanup": cleanup_summary,
                "windowsBeforePath": str(run_root / "windows-before.json"),
                "windowsAfterPath": str(run_root / "windows-after.json"),
                "edgeLikeWindowCountBefore": len(edge_matches_before),
                "edgeLikeWindowCount": len(edge_matches),
                "elapsedSeconds": round(time.time() - started, 3),
                "observedAtUtc": utc_now(),
            }
            write_json(run_root / "summary.json", summary)
            print(run_root / "summary.json")
            return 0 if summary["success"] else 1

    if args.profile_warm:
        start_started = time.time()
        start_status, start_body, start_response = request_json(
            args.base_url,
            "POST",
            "/v1/powerpoint/online/sessions",
            update_request,
            args.http_timeout_seconds,
        )
        start_elapsed_ms = round((time.time() - start_started) * 1000, 3)
        write_json(run_root / "session-start-response.json", start_response if start_response is not None else {"raw": start_body.decode("utf-8", "replace")})
        start_phase_timings = start_response.get("phaseTimings") if isinstance(start_response, dict) and isinstance(start_response.get("phaseTimings"), dict) else None
        session_started = warm_session_started(start_status, start_response)
        iterations: list[dict[str, Any]] = []
        cleanup_status: int | str | None = None
        cleanup_body: bytes = b""
        cleanup_response: Any = None
        cleanup_attempted = start_status != "exception"
        if session_started:
            for iteration_index in range(1, args.warm_iterations + 1):
                job_id = f"{run_id}-warm-{iteration_index:02d}"
                iteration_request = build_warm_iteration_request(args, run_id, job_id)
                iteration_request_path = run_root / "iterations" / f"iteration-{iteration_index:02d}-request.json"
                iteration_response_path = run_root / "iterations" / f"iteration-{iteration_index:02d}-response.json"
                write_json(iteration_request_path, iteration_request)
                iteration_status, iteration_body, iteration_response = request_json(
                    args.base_url,
                    "POST",
                    "/v1/powerpoint/online/updates",
                    iteration_request,
                    args.http_timeout_seconds,
                )
                write_json(iteration_response_path, iteration_response if iteration_response is not None else {"raw": iteration_body.decode("utf-8", "replace")})
                iteration_summary = summarize_response(iteration_response, None, args.exchange_root)
                iteration_summary.update(
                    {
                        "iteration": iteration_index,
                        "jobId": job_id,
                        "httpStatus": iteration_status,
                        "requestPath": str(iteration_request_path),
                        "responsePath": str(iteration_response_path),
                    }
                )
                iteration_summary["success"] = warm_iteration_passed(iteration_summary)
                iterations.append(iteration_summary)
        if cleanup_attempted:
            cleanup_status, cleanup_body, cleanup_response = request_json(
                args.base_url,
                "POST",
                f"/v1/powerpoint/online/sessions/{urllib.parse.quote(run_id, safe='')}/cleanup",
                {},
                args.http_timeout_seconds,
            )
            write_json(run_root / "cleanup-response.json", cleanup_response if cleanup_response is not None else {"raw": cleanup_body.decode("utf-8", "replace")})
        windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
        write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
        edge_matches = edge_like_windows(windows)
        cleanup_summary = {
            "attempted": cleanup_attempted,
            "httpStatus": cleanup_status,
            "status": status_of(cleanup_response),
            "responsePath": str(run_root / "cleanup-response.json") if cleanup_attempted else None,
        }
        success_count = sum(1 for item in iterations if item.get("success") is True)
        summary = {
            "runId": run_id,
            "success": False,
            "status": "warmProfileSucceeded" if session_started else "warmProfileSessionStartFailed",
            "httpStatus": start_status,
            "windowsHttpStatus": windows_status,
            "windowsBeforeHttpStatus": windows_before_status,
            "execute": False,
            "verifyHostGate": False,
            "verifyReadiness": False,
            "verifyReadinessFast": False,
            "profileWarm": True,
            "sem27": sem27,
            "requestPath": str(run_root / "request.json"),
            "sessionStartResponsePath": str(run_root / "session-start-response.json"),
            "windowsBeforePath": str(run_root / "windows-before.json"),
            "windowsAfterPath": str(run_root / "windows-after.json"),
            "edgeLikeWindowCountBefore": len(edge_matches_before),
            "edgeLikeWindowCount": len(edge_matches),
            "sessionStart": {
                "httpStatus": start_status,
                "requestPath": str(run_root / "request.json"),
                "responsePath": str(run_root / "session-start-response.json"),
                "elapsedMs": start_elapsed_ms,
                "success": session_started,
                "status": start_response.get("status") if isinstance(start_response, dict) else None,
                "phaseTimings": start_phase_timings,
            },
            "iterations": iterations,
            "successfulIterations": success_count,
            "requestedIterations": args.warm_iterations,
            "cleanup": cleanup_summary,
            "elapsedSeconds": round(time.time() - started, 3),
            "observedAtUtc": utc_now(),
        }
        summary["openSessionMs"] = start_elapsed_ms
        summary["success"] = (
            session_started
            and success_count >= 2
            and cleanup_summary.get("status") == "closed"
            and len(edge_matches) <= len(edge_matches_before)
        )
        if not summary["success"]:
            summary["status"] = "warmProfileFailed"
        write_json(run_root / "summary.json", summary)
        print(run_root / "summary.json")
        return 0 if summary["success"] else 1

    http_status, body, response = request_json(
        args.base_url,
        "POST",
        "/v1/powerpoint/online/updates",
        update_request,
        args.http_timeout_seconds,
    )
    write_json(run_root / "response.json", response if response is not None else {"raw": body.decode("utf-8", "replace")})

    windows_status, _windows_body, windows = request_json(args.base_url, "GET", "/v1/windows", None, 30)
    write_json(run_root / "windows-after.json", windows if windows is not None else {"status": windows_status})
    edge_matches = edge_like_windows(windows)
    if args.verify_host_gate:
        job_status, _job_body, job_lookup = request_json(
            args.base_url,
            "GET",
            f"/v1/powerpoint/jobs/{urllib.parse.quote(run_id, safe='')}",
            None,
            30,
        )
        write_json(run_root / "job-lookup.json", job_lookup if job_lookup is not None else {"status": job_status})
        error_code = response.get("code") if isinstance(response, dict) else None
        summary = {
            "runId": run_id,
            "success": False,
            "status": "hostGateVerified",
            "httpStatus": http_status,
            "errorCode": error_code,
            "jobLookupHttpStatus": job_status,
            "edgeLikeWindowCount": len(edge_matches),
            "edgeLikeWindowCountBefore": len(edge_matches_before),
            "windowsHttpStatus": windows_status,
            "windowsBeforeHttpStatus": windows_before_status,
            "execute": False,
            "verifyHostGate": True,
            "verifyReadiness": False,
            "verifyReadinessFast": False,
            "profileWarm": False,
            "sem27": sem27,
            "requestPath": str(run_root / "request.json"),
            "responsePath": str(run_root / "response.json"),
            "jobLookupPath": str(run_root / "job-lookup.json"),
            "windowsBeforePath": str(run_root / "windows-before.json"),
            "windowsAfterPath": str(run_root / "windows-after.json"),
            "elapsedSeconds": round(time.time() - started, 3),
            "observedAtUtc": utc_now(),
        }
        summary["success"] = gate_verification_passed(summary)
        if not summary["success"]:
            summary["status"] = "hostGateFailed"
        write_json(run_root / "summary.json", summary)
        print(run_root / "summary.json")
        return 0 if summary["success"] else 1

    summary = summarize_response(response, len(edge_matches), args.exchange_root)
    summary.update(
        {
            "runId": run_id,
            "httpStatus": http_status,
            "windowsHttpStatus": windows_status,
            "windowsBeforeHttpStatus": windows_before_status,
            "execute": args.execute,
            "verifyHostGate": False,
            "verifyReadiness": args.verify_readiness,
            "verifyReadinessFast": args.verify_readiness_fast,
            "profileWarm": False,
            "sem27": sem27,
            "requestPath": str(run_root / "request.json"),
            "responsePath": str(run_root / "response.json"),
            "windowsBeforePath": str(run_root / "windows-before.json"),
            "windowsAfterPath": str(run_root / "windows-after.json"),
            "edgeLikeWindowCountBefore": len(edge_matches_before),
            "elapsedSeconds": round(time.time() - started, 3),
            "observedAtUtc": utc_now(),
        }
    )
    summary["success"] = (
        fast_readiness_passed(summary)
        if args.verify_readiness_fast
        else readiness_passed(summary)
        if args.verify_readiness
        else proof_passed(summary)
    )
    write_json(run_root / "summary.json", summary)
    print(run_root / "summary.json")
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
