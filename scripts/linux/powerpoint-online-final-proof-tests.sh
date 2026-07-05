#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
script="$repo_root/scripts/linux/powerpoint-online-final-proof.py"
tmp_root="$(mktemp -d)"
trap 'rm -rf "$tmp_root"' EXIT

python3 - "$script" <<'PY'
import importlib.util
import os
import tempfile
import sys
from types import SimpleNamespace

script = sys.argv[1]
spec = importlib.util.spec_from_file_location("ppt_final_proof", script)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)

old_argv = sys.argv[:]
try:
    sys.argv = ["ppt-final-proof", "--deck-url", "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1"]
    parsed = module.parse_args()
    assert parsed.http_timeout_seconds == 420
finally:
    sys.argv = old_argv

args = SimpleNamespace(
    deck_url="https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
    slide_number=4,
    allow_deck_mutation=True,
    template_wait_seconds=2,
    reopen_wait_seconds=40,
    open_wait_seconds=40,
    job_timeout_seconds=60,
    save_timeout_seconds=30,
    save_poll_seconds=1,
    base_url="http://127.0.0.1:43117",
    hot_lease_ttl_seconds=1800,
)
request = module.build_update_request(args, "proof-run", "proof text")
assert request["deckUrl"] == args.deck_url
assert request["sessionId"] == "proof-run"
assert request["evidenceSlideNumber"] == 4
assert request["allowDeckMutation"] is True
assert request["prepareTemplate"] is True
assert request["cleanupTemplate"] is True
assert request["verifyReopen"] is True
assert request["cleanupSession"] is True
assert request["job"]["jobId"] == "proof-run"
assert request["job"]["discoverTargets"] is True
assert request["job"]["validateOnly"] is False
operation = request["job"]["operations"][0]
assert operation["kind"] == "replaceText"
assert operation["targetId"] == "TITLE_MAIN"
assert operation["text"] == "proof text"

readiness_request = module.build_readiness_request(args, "readiness-run")
assert readiness_request["deckUrl"] == args.deck_url
assert readiness_request["sessionId"] == "readiness-run"
assert readiness_request["evidenceSlideNumber"] == 4
assert readiness_request["allowDeckMutation"] is False
assert readiness_request["prepareTemplate"] is False
assert readiness_request["cleanupTemplate"] is False
assert readiness_request["cleanupTemplateOnFailure"] is False
assert readiness_request["capture"] is True
assert readiness_request["verifyReopen"] is True
assert readiness_request["cleanupSession"] is True
assert readiness_request["job"]["jobId"] == "readiness-run"
assert readiness_request["job"]["discoverTargets"] is True
assert readiness_request["job"]["validateOnly"] is True
assert readiness_request["job"]["operations"] == []

fast_readiness_request = module.build_readiness_request(args, "readiness-fast-run", verify_reopen=False)
assert fast_readiness_request["allowDeckMutation"] is False
assert fast_readiness_request["verifyReopen"] is False
assert fast_readiness_request["cleanupSession"] is True
assert fast_readiness_request["job"]["validateOnly"] is True
assert fast_readiness_request["job"]["requestedBy"] == "codex-readiness-proof-fast"

warm_start_request = module.build_warm_session_start_request(args, "warm-run")
assert warm_start_request["deckUrl"] == args.deck_url
assert warm_start_request["sessionId"] == "warm-run"
assert warm_start_request["runId"] == "warm-run"
assert warm_start_request["capture"] is False
assert warm_start_request["waitSeconds"] == 40
assert "job" not in warm_start_request

warm_iteration_request = module.build_warm_iteration_request(args, "warm-run", "warm-run-warm-01")
assert "deckUrl" not in warm_iteration_request
assert warm_iteration_request["sessionId"] == "warm-run"
assert warm_iteration_request["capture"] is False
assert warm_iteration_request["allowDeckMutation"] is False
assert warm_iteration_request["verifyReopen"] is False
assert warm_iteration_request["cleanupSession"] is False
assert warm_iteration_request["job"]["jobId"] == "warm-run-warm-01"
assert warm_iteration_request["job"]["validateOnly"] is True
assert warm_iteration_request["job"]["operations"] == []

summary = {
    "httpStatus": 200,
    "success": True,
    "status": "succeeded",
    "saveProofTier": "tier3ReopenVisual",
    "jobStatus": "succeeded",
    "claimedBy": "officejs-taskpane",
    "targetCount": 1,
    "titleMainTargetSucceeded": True,
    "titleMainDiscovered": True,
    "evidenceCount": 3,
    "successfulEvidenceCount": 3,
    "verifiedEvidenceCount": 3,
    "distinctVerifiedEvidenceCount": 3,
    "templatePreparationStatus": "ready",
    "verificationStatus": "ready",
    "templateCleanupStatus": "ready",
    "sessionCleanupStatus": "closed",
    "edgeLikeWindowCount": 0,
}
assert module.proof_passed(summary)
without_discovery = dict(summary)
without_discovery["titleMainDiscovered"] = False
assert not module.proof_passed(without_discovery)
without_cleanup = dict(summary)
without_cleanup["sessionCleanupStatus"] = "ready"
assert not module.proof_passed(without_cleanup)
without_http_200 = dict(summary)
without_http_200["httpStatus"] = 202
assert not module.proof_passed(without_http_200)
without_successful_evidence = dict(summary)
without_successful_evidence["successfulEvidenceCount"] = 2
assert not module.proof_passed(without_successful_evidence)
without_successful_evidence_key = dict(summary)
del without_successful_evidence_key["successfulEvidenceCount"]
assert not module.proof_passed(without_successful_evidence_key)
without_verified_evidence = dict(summary)
without_verified_evidence["verifiedEvidenceCount"] = 2
assert not module.proof_passed(without_verified_evidence)
without_verified_evidence_key = dict(summary)
del without_verified_evidence_key["verifiedEvidenceCount"]
assert not module.proof_passed(without_verified_evidence_key)
without_distinct_verified_evidence = dict(summary)
without_distinct_verified_evidence["distinctVerifiedEvidenceCount"] = 2
assert not module.proof_passed(without_distinct_verified_evidence)
without_distinct_verified_evidence_key = dict(summary)
del without_distinct_verified_evidence_key["distinctVerifiedEvidenceCount"]
assert not module.proof_passed(without_distinct_verified_evidence_key)

readiness_summary = {
    "httpStatus": 200,
    "success": True,
    "status": "succeeded",
    "saveProofTier": "tier3ReopenVisual",
    "jobStatus": "succeeded",
    "claimedBy": "officejs-taskpane",
    "evidenceCount": 2,
    "successfulEvidenceCount": 2,
    "verifiedEvidenceCount": 2,
    "distinctVerifiedEvidenceCount": 2,
    "verificationStatus": "ready",
    "sessionCleanupStatus": "closed",
    "edgeLikeWindowCount": 0,
}
assert module.readiness_passed(readiness_summary)
readiness_http_not_200 = dict(readiness_summary)
readiness_http_not_200["httpStatus"] = 202
assert not module.readiness_passed(readiness_http_not_200)
readiness_without_cleanup = dict(readiness_summary)
del readiness_without_cleanup["sessionCleanupStatus"]
assert not module.readiness_passed(readiness_without_cleanup)
readiness_cleanup_not_closed = dict(readiness_summary)
readiness_cleanup_not_closed["sessionCleanupStatus"] = "ready"
assert not module.readiness_passed(readiness_cleanup_not_closed)
readiness_without_verification = dict(readiness_summary)
del readiness_without_verification["verificationStatus"]
assert not module.readiness_passed(readiness_without_verification)
readiness_verification_not_ready = dict(readiness_summary)
readiness_verification_not_ready["verificationStatus"] = "pending"
assert not module.readiness_passed(readiness_verification_not_ready)
readiness_wrong_claim = dict(readiness_summary)
readiness_wrong_claim["claimedBy"] = "other-runner"
assert not module.readiness_passed(readiness_wrong_claim)
readiness_one_evidence = dict(readiness_summary)
readiness_one_evidence["evidenceCount"] = 1
assert not module.readiness_passed(readiness_one_evidence)
readiness_low_successful_evidence = dict(readiness_summary)
readiness_low_successful_evidence["successfulEvidenceCount"] = 1
assert not module.readiness_passed(readiness_low_successful_evidence)
readiness_missing_successful_evidence = dict(readiness_summary)
del readiness_missing_successful_evidence["successfulEvidenceCount"]
assert not module.readiness_passed(readiness_missing_successful_evidence)
readiness_low_verified_evidence = dict(readiness_summary)
readiness_low_verified_evidence["verifiedEvidenceCount"] = 1
assert not module.readiness_passed(readiness_low_verified_evidence)
readiness_missing_verified_evidence = dict(readiness_summary)
del readiness_missing_verified_evidence["verifiedEvidenceCount"]
assert not module.readiness_passed(readiness_missing_verified_evidence)
readiness_low_distinct_verified_evidence = dict(readiness_summary)
readiness_low_distinct_verified_evidence["distinctVerifiedEvidenceCount"] = 1
assert not module.readiness_passed(readiness_low_distinct_verified_evidence)
readiness_missing_distinct_verified_evidence = dict(readiness_summary)
del readiness_missing_distinct_verified_evidence["distinctVerifiedEvidenceCount"]
assert not module.readiness_passed(readiness_missing_distinct_verified_evidence)
readiness_edge_left = dict(readiness_summary)
readiness_edge_left["edgeLikeWindowCount"] = 1
assert not module.readiness_passed(readiness_edge_left)

fast_readiness_summary = {
    "httpStatus": 200,
    "success": True,
    "status": "succeeded",
    "jobStatus": "succeeded",
    "claimedBy": "officejs-taskpane",
    "evidenceCount": 1,
    "successfulEvidenceCount": 1,
    "verifiedEvidenceCount": 1,
    "distinctVerifiedEvidenceCount": 1,
    "sessionCleanupStatus": "closed",
    "edgeLikeWindowCount": 0,
}
assert module.fast_readiness_passed(fast_readiness_summary)
fast_readiness_missing_cleanup = dict(fast_readiness_summary)
fast_readiness_missing_cleanup["sessionCleanupStatus"] = "ready"
assert not module.fast_readiness_passed(fast_readiness_missing_cleanup)
fast_readiness_requires_verified_image = dict(fast_readiness_summary)
fast_readiness_requires_verified_image["verifiedEvidenceCount"] = 0
assert not module.fast_readiness_passed(fast_readiness_requires_verified_image)
fast_readiness_no_tier3_needed = dict(fast_readiness_summary)
fast_readiness_no_tier3_needed["verificationStatus"] = None
assert module.fast_readiness_passed(fast_readiness_no_tier3_needed)

warm_iteration_summary = {
    "httpStatus": 200,
    "success": True,
    "status": "succeeded",
    "jobStatus": "succeeded",
    "claimedBy": "officejs-taskpane",
}
assert module.warm_iteration_passed(warm_iteration_summary)
warm_iteration_summary["claimedBy"] = "other"
assert not module.warm_iteration_passed(warm_iteration_summary)
assert module.warm_session_started(200, {"success": True, "status": "ready"})
assert not module.warm_session_started(200, {"success": True, "status": "succeeded"})
assert not module.warm_session_started(500, {"success": True, "status": "ready"})
assert module.default_hot_lease_path(module.Path("/exchange")) == module.Path("/exchange/state/ppt-hot-lease.json")
hot_lease = module.make_hot_lease(args, "hot-session", "hot-run")
assert hot_lease["kind"] == "powerpoint-online-hot-lease"
assert hot_lease["sessionId"] == "hot-session"
assert hot_lease["deckUrl"] == args.deck_url
assert hot_lease["ttlSeconds"] == 1800
assert hot_lease["lastRunId"] == "hot-run"
assert not module.lease_expired(hot_lease)
expired_hot_lease = dict(hot_lease)
expired_hot_lease["expiresAtUtc"] = "2000-01-01T00:00:00Z"
assert module.lease_expired(expired_hot_lease)
refreshed_hot_lease = module.refresh_hot_lease(hot_lease, args, "hot-run-2")
assert refreshed_hot_lease["lastRunId"] == "hot-run-2"
assert not module.lease_expired(refreshed_hot_lease)

with tempfile.TemporaryDirectory() as summary_root:
    exchange_root = module.Path(summary_root) / "exchange"
    exchange_root.mkdir()
    relative_file = exchange_root / "runs" / "example" / "shot-1.png"
    relative_file.parent.mkdir(parents=True, exist_ok=True)
    relative_file.write_bytes(module.PNG_SIGNATURE + b"png1")
    host_file = module.Path(summary_root) / "shot-2.jpg"
    host_file.write_bytes(module.JPEG_SIGNATURE + b"j")
    host_alias = module.Path(summary_root) / "." / "shot-2.jpg"
    missing_relative = "runs/example/missing.png"
    wrong_size_file = module.Path(summary_root) / "wrong-size.png"
    wrong_size_file.write_bytes(module.PNG_SIGNATURE[:5])
    zero_file = module.Path(summary_root) / "zero-size.png"
    zero_file.write_bytes(b"")
    bad_header_file = module.Path(summary_root) / "bad-header.png"
    bad_header_file.write_bytes(b"not-a-png!!12")
    unsupported_image_file = module.Path(summary_root) / "unsupported.webp"
    unsupported_image_file.write_bytes(b"RIFFwebp")

    evidence_summary = module.summarize_response(
        {
            "success": True,
            "status": "succeeded",
            "saveProofTier": "tier3ReopenVisual",
            "phaseTimings": {"openSessionMs": 11, "jobMs": 22},
            "jobRecord": {
                "status": "succeeded",
                "claimedBy": "officejs-taskpane",
                "result": {
                    "targets": [
                        {
                            "targetId": "TITLE_MAIN",
                            "operationKind": "replaceText",
                            "status": "succeeded",
                        }
                    ],
                    "discoveredTargets": [{"targetId": "TITLE_MAIN"}],
                },
            },
            "evidence": [
                {
                    "success": True,
                    "artifact": {
                        "bytes": 12,
                        "mediaType": "image/png",
                        "relativePath": "runs/example/shot-1.png",
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 4,
                        "mediaType": "image/jpeg",
                        "hostPath": str(host_alias),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 4,
                        "mediaType": "image/jpeg",
                        "hostPath": str(host_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 9,
                        "mediaType": "image/png",
                        "relativePath": missing_relative,
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 9,
                        "mediaType": "application/json",
                        "path": str(wrong_size_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 7,
                        "mediaType": "image/png",
                        "path": str(wrong_size_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 12,
                        "mediaType": "image/png",
                        "path": str(bad_header_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 8,
                        "mediaType": "image/webp",
                        "path": str(unsupported_image_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 1,
                        "mediaType": "image/png",
                        "path": str(zero_file),
                    },
                },
                {
                    "success": True,
                    "artifact": {
                        "bytes": 11,
                        "mediaType": "image/png",
                        "hostPath": "Z:\\operator-exchange\\runs\\example\\win-only.png",
                    },
                },
            ],
            "verificationSession": {"status": "ready"},
            "sessionCleanupSession": {"status": "closed"},
        },
        0,
        exchange_root,
    )
    assert evidence_summary["evidenceCount"] == 10
    assert evidence_summary["successfulEvidenceCount"] == 9
    assert evidence_summary["verifiedEvidenceCount"] == 3
    assert evidence_summary["distinctVerifiedEvidenceCount"] == 2
    assert evidence_summary["phaseTimings"] == {"openSessionMs": 11, "jobMs": 22}

gate = {
    "status": "hostGateVerified",
    "httpStatus": 422,
    "errorCode": "powerpoint_validation_failed",
    "jobLookupHttpStatus": 404,
    "edgeLikeWindowCount": 0,
    "edgeLikeWindowCountBefore": 0,
}
assert module.gate_verification_passed(gate)
queued_gate = dict(gate)
queued_gate["jobLookupHttpStatus"] = 200
assert not module.gate_verification_passed(queued_gate)
edge_gate = dict(gate)
edge_gate["edgeLikeWindowCount"] = 1
assert not module.gate_verification_passed(edge_gate)

assert module.is_sem27_url("https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1")
assert not module.is_sem27_url("https://host/Documents/Other.pptx?web=1")

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    exchange_path = module.Path(exchange_root)
    initial_file = exchange_path / "runs" / "readiness-sem27-ok" / "initial.png"
    initial_file.parent.mkdir(parents=True, exist_ok=True)
    initial_file.write_bytes(module.PNG_SIGNATURE + b"abc")
    reopen_file = exchange_path / "runs" / "readiness-sem27-ok" / "reopen.png"
    reopen_file.write_bytes(module.PNG_SIGNATURE + b"abcde")

    def fake_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/updates":
            assert payload["allowDeckMutation"] is False
            assert payload["job"]["validateOnly"] is True
            assert payload["job"]["operations"] == []
            assert payload["prepareTemplate"] is False
            assert payload["cleanupTemplate"] is False
            return 200, b"{}", {
                "success": True,
                "status": "succeeded",
                "saveProofTier": "tier3ReopenVisual",
                "jobRecord": {
                    "status": "succeeded",
                    "claimedBy": "officejs-taskpane",
                },
                "evidence": [
                    {
                        "stage": "initial",
                        "success": True,
                        "artifact": {
                            "bytes": 11,
                            "mediaType": "image/png",
                            "relativePath": "runs/readiness-sem27-ok/initial.png",
                        },
                    },
                    {
                        "stage": "reopen",
                        "success": True,
                        "artifact": {
                            "bytes": 13,
                            "mediaType": "image/png",
                            "relativePath": "runs/readiness-sem27-ok/reopen.png",
                        },
                    },
                ],
                "verificationSession": {"status": "ready"},
                "sessionCleanupSession": {"status": "closed"},
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "readiness-sem27-ok",
            "--verify-readiness",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "readiness-sem27-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is True
    assert summary["verifyReadiness"] is True
    assert summary["execute"] is False
    assert summary["sem27"] is True
    assert summary["evidenceCount"] == 2
    assert summary["successfulEvidenceCount"] == 2
    assert summary["verifiedEvidenceCount"] == 2
    assert summary["distinctVerifiedEvidenceCount"] == 2
    assert calls[1][1] == "/v1/powerpoint/online/updates"

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    exchange_path = module.Path(exchange_root)
    initial_file = exchange_path / "runs" / "readiness-sem27-fast-ok" / "initial.png"
    initial_file.parent.mkdir(parents=True, exist_ok=True)
    initial_file.write_bytes(module.PNG_SIGNATURE + b"abc")

    def fake_fast_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/updates":
            assert payload["allowDeckMutation"] is False
            assert payload["verifyReopen"] is False
            assert payload["cleanupSession"] is True
            return 200, b"{}", {
                "success": True,
                "status": "succeeded",
                "saveProofTier": "tier2SavedIndicator",
                "jobRecord": {
                    "status": "succeeded",
                    "claimedBy": "officejs-taskpane",
                },
                "evidence": [
                    {
                        "success": True,
                        "artifact": {
                            "bytes": 11,
                            "mediaType": "image/png",
                            "relativePath": "runs/readiness-sem27-fast-ok/initial.png",
                        },
                    }
                ],
                "sessionCleanupSession": {"status": "closed"},
                "phaseTimings": {"openSessionMs": 10, "jobMs": 20, "sessionCleanupMs": 5},
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_fast_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "readiness-sem27-fast-ok",
            "--verify-readiness-fast",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "readiness-sem27-fast-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is True
    assert summary["verifyReadiness"] is False
    assert summary["verifyReadinessFast"] is True
    assert summary["phaseTimings"]["jobMs"] == 20
    assert calls[1][2]["verifyReopen"] is False

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []

    def fake_warm_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/sessions":
            assert payload == {
                "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
                "sessionId": "warm-sem27-ok",
                "runId": "warm-sem27-ok",
                "capture": False,
                "waitSeconds": 40,
            }
            return 200, b"{}", {
                "success": True,
                "status": "ready",
            }
        if method == "POST" and path == "/v1/powerpoint/online/updates":
            assert payload["allowDeckMutation"] is False
            assert payload["job"]["validateOnly"] is True
            assert payload["verifyReopen"] is False
            assert payload["cleanupSession"] is False
            assert "deckUrl" not in payload
            return 200, b"{}", {
                "success": True,
                "status": "succeeded",
                "jobRecord": {
                    "status": "succeeded",
                    "claimedBy": "officejs-taskpane",
                },
                "phaseTimings": {"jobMs": 25},
            }
        if method == "POST" and path == "/v1/powerpoint/online/sessions/warm-sem27-ok/cleanup":
            assert payload == {}
            return 200, b"{}", {"status": "closed", "phaseTimings": {"cleanupMs": 18}}
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_warm_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "warm-sem27-ok",
            "--profile-warm",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "warm-sem27-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is True
    assert summary["profileWarm"] is True
    assert summary["requestedIterations"] == 2
    assert summary["successfulIterations"] == 2
    assert isinstance(summary["openSessionMs"], (int, float))
    assert summary["openSessionMs"] >= 0
    assert summary["sessionStart"]["elapsedMs"] == summary["openSessionMs"]
    assert summary["sessionStart"]["phaseTimings"] is None
    assert summary["cleanup"]["status"] == "closed"
    assert summary["edgeLikeWindowCountBefore"] == 0
    assert summary["edgeLikeWindowCount"] == 0
    assert len(summary["iterations"]) == 2
    assert summary["iterations"][0]["jobId"] == "warm-sem27-ok-warm-01"
    assert summary["iterations"][1]["jobId"] == "warm-sem27-ok-warm-02"
    assert summary["iterations"][0]["phaseTimings"]["jobMs"] == 25
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions",
        "/v1/powerpoint/online/updates",
        "/v1/powerpoint/online/updates",
        "/v1/powerpoint/online/sessions/warm-sem27-ok/cleanup",
        "/v1/windows",
    ]
    assert calls[2][2]["job"]["jobId"] == "warm-sem27-ok-warm-01"
    assert calls[3][2]["job"]["jobId"] == "warm-sem27-ok-warm-02"

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []

    def fake_warm_failure_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/sessions":
            return 200, b"{}", {
                "success": True,
                "status": "ready",
                "phaseTimings": {"openSessionMs": 111},
            }
        if method == "POST" and path == "/v1/powerpoint/online/updates":
            if payload["job"]["jobId"].endswith("warm-01"):
                return 200, b"{}", {
                    "success": True,
                    "status": "succeeded",
                    "jobRecord": {
                        "status": "succeeded",
                        "claimedBy": "officejs-taskpane",
                    },
                    "phaseTimings": {"jobMs": 19},
                }
            return 500, b"boom", {
                "success": False,
                "status": "failed",
                "jobRecord": {
                    "status": "failed",
                    "claimedBy": "officejs-taskpane",
                },
                "phaseTimings": {"jobMs": 29},
            }
        if method == "POST" and path == "/v1/powerpoint/online/sessions/warm-sem27-fail/cleanup":
            return 200, b"{}", {"status": "closed"}
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_warm_failure_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "warm-sem27-fail",
            "--profile-warm",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 1
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "warm-sem27-fail" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is False
    assert summary["cleanup"]["attempted"] is True
    assert summary["cleanup"]["status"] == "closed"
    assert summary["successfulIterations"] == 1
    assert len(summary["iterations"]) == 2
    assert summary["iterations"][1]["success"] is False
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions",
        "/v1/powerpoint/online/updates",
        "/v1/powerpoint/online/updates",
        "/v1/powerpoint/online/sessions/warm-sem27-fail/cleanup",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []

    def fake_warm_start_failure_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/sessions":
            return 200, b"{}", {
                "success": False,
                "status": "failed",
            }
        if method == "POST" and path == "/v1/powerpoint/online/sessions/warm-sem27-start-fail/cleanup":
            return 200, b"{}", {"status": "closed"}
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_warm_start_failure_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "warm-sem27-start-fail",
            "--profile-warm",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 1
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "warm-sem27-start-fail" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is False
    assert summary["sessionStart"]["success"] is False
    assert summary["cleanup"]["attempted"] is True
    assert summary["cleanup"]["status"] == "closed"
    assert summary["successfulIterations"] == 0
    assert summary["iterations"] == []
    assert isinstance(summary["openSessionMs"], (int, float))
    assert summary["openSessionMs"] >= 0
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions",
        "/v1/powerpoint/online/sessions/warm-sem27-start-fail/cleanup",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []

    def fake_hot_start_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/sessions":
            assert payload == {
                "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
                "sessionId": "ppt-hot-sem27",
                "runId": "ppt-hot-sem27",
                "capture": False,
                "waitSeconds": 40,
            }
            return 200, b"{}", {
                "success": True,
                "status": "ready",
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_hot_start_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "hot-start-ok",
            "--hot-session-id",
            "ppt-hot-sem27",
            "--hot-start",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "hot-start-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)
    lease_path = module.Path(exchange_root) / "state" / "ppt-hot-lease.json"
    with lease_path.open(encoding="utf-8") as handle:
        lease = module.json.load(handle)

    assert summary["success"] is True
    assert summary["status"] == "hotLeaseStarted"
    assert summary["hotStart"] is True
    assert summary["leasePath"] == str(lease_path)
    assert summary["sessionStart"]["status"] == "ready"
    assert lease["sessionId"] == "ppt-hot-sem27"
    assert lease["lastRunId"] == "hot-start-ok"
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    lease_path = module.Path(exchange_root) / "state" / "ppt-hot-lease.json"
    future = module.iso_utc(module.dt.datetime.now(module.dt.UTC) + module.dt.timedelta(minutes=10))
    module.write_json(
        lease_path,
        {
            "kind": "powerpoint-online-hot-lease",
            "version": 1,
            "sessionId": "ppt-hot-sem27",
            "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "baseUrl": "http://127.0.0.1:43117",
            "createdAtUtc": future,
            "updatedAtUtc": future,
            "expiresAtUtc": future,
            "ttlSeconds": 1800,
            "lastRunId": "hot-start-ok",
        },
    )

    def fake_hot_start_reuse_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "GET" and path == "/v1/powerpoint/online/sessions/ppt-hot-sem27":
            return 200, b"{}", {
                "success": True,
                "status": "ready",
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_hot_start_reuse_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "hot-start-reuse-ok",
            "--hot-session-id",
            "ppt-hot-sem27",
            "--hot-start",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "hot-start-reuse-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)
    with lease_path.open(encoding="utf-8") as handle:
        lease = module.json.load(handle)

    assert summary["success"] is True
    assert summary["status"] == "hotLeaseAlreadyReady"
    assert summary["hotStart"] is True
    assert lease["lastRunId"] == "hot-start-reuse-ok"
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions/ppt-hot-sem27",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    lease_path = module.Path(exchange_root) / "state" / "ppt-hot-lease.json"
    future = module.iso_utc(module.dt.datetime.now(module.dt.UTC) + module.dt.timedelta(minutes=10))
    module.write_json(
        lease_path,
        {
            "kind": "powerpoint-online-hot-lease",
            "version": 1,
            "sessionId": "ppt-hot-sem27",
            "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "baseUrl": "http://127.0.0.1:43117",
            "createdAtUtc": future,
            "updatedAtUtc": future,
            "expiresAtUtc": future,
            "ttlSeconds": 1800,
            "lastRunId": "hot-start-ok",
        },
    )

    def fake_hot_run_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "GET" and path == "/v1/powerpoint/online/sessions/ppt-hot-sem27":
            return 200, b"{}", {
                "success": True,
                "status": "ready",
            }
        if method == "POST" and path == "/v1/powerpoint/online/updates":
            assert "deckUrl" not in payload
            assert payload["sessionId"] == "ppt-hot-sem27"
            assert payload["allowDeckMutation"] is False
            assert payload["job"]["validateOnly"] is True
            assert payload["job"]["operations"] == []
            assert payload["verifyReopen"] is False
            assert payload["cleanupSession"] is False
            assert payload["job"]["jobId"] == "hot-run-ok-hot"
            return 200, b"{}", {
                "success": True,
                "status": "succeeded",
                "jobRecord": {
                    "status": "succeeded",
                    "claimedBy": "officejs-taskpane",
                },
                "phaseTimings": {"jobMs": 21},
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_hot_run_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "hot-run-ok",
            "--hot-run",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "hot-run-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)
    with lease_path.open(encoding="utf-8") as handle:
        lease = module.json.load(handle)

    assert summary["success"] is True
    assert summary["status"] == "hotRunSucceeded"
    assert summary["hotRun"] is True
    assert summary["phaseTimings"]["jobMs"] == 21
    assert lease["lastRunId"] == "hot-run-ok"
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions/ppt-hot-sem27",
        "/v1/powerpoint/online/updates",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    lease_path = module.Path(exchange_root) / "state" / "ppt-hot-lease.json"
    future = module.iso_utc(module.dt.datetime.now(module.dt.UTC) + module.dt.timedelta(minutes=10))
    module.write_json(
        lease_path,
        {
            "kind": "powerpoint-online-hot-lease",
            "version": 1,
            "sessionId": "ppt-hot-sem27",
            "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "baseUrl": "http://127.0.0.1:43117",
            "createdAtUtc": future,
            "updatedAtUtc": future,
            "expiresAtUtc": future,
            "ttlSeconds": 1800,
            "lastRunId": "hot-run-ok",
        },
    )

    def fake_hot_status_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            return 200, b"[]", []
        if method == "GET" and path == "/v1/powerpoint/online/sessions/ppt-hot-sem27":
            return 200, b"{}", {
                "success": True,
                "status": "ready",
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_hot_status_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "hot-status-ok",
            "--hot-status",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "hot-status-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is True
    assert summary["status"] == "hotLeaseReady"
    assert summary["hotStatus"] is True
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions/ppt-hot-sem27",
        "/v1/windows",
    ]

with tempfile.TemporaryDirectory() as exchange_root:
    calls = []
    lease_path = module.Path(exchange_root) / "state" / "ppt-hot-lease.json"
    future = module.iso_utc(module.dt.datetime.now(module.dt.UTC) + module.dt.timedelta(minutes=10))
    module.write_json(
        lease_path,
        {
            "kind": "powerpoint-online-hot-lease",
            "version": 1,
            "sessionId": "ppt-hot-sem27",
            "deckUrl": "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "baseUrl": "http://127.0.0.1:43117",
            "createdAtUtc": future,
            "updatedAtUtc": future,
            "expiresAtUtc": future,
            "ttlSeconds": 1800,
            "lastRunId": "hot-run-ok",
        },
    )

    def fake_hot_cleanup_request_json(base_url, method, path, payload, timeout_seconds):
        calls.append((method, path, payload))
        if method == "GET" and path == "/v1/windows":
            if len([call for call in calls if call[1] == "/v1/windows"]) == 1:
                return 200, b"[]", [{"className": "Chrome_WidgetWin_1", "title": "PowerPoint"}]
            return 200, b"[]", []
        if method == "POST" and path == "/v1/powerpoint/online/sessions/ppt-hot-sem27/cleanup":
            assert payload == {}
            return 200, b"{}", {
                "success": True,
                "status": "closed",
            }
        raise AssertionError(f"unexpected call: {method} {path}")

    module.request_json = fake_hot_cleanup_request_json
    old_argv = sys.argv
    try:
        sys.argv = [
            script,
            "--deck-url",
            "https://host/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1",
            "--run-id",
            "hot-cleanup-ok",
            "--hot-cleanup",
            "--exchange-root",
            exchange_root,
        ]
        exit_code = module.main()
        assert exit_code == 0
    finally:
        sys.argv = old_argv

    summary_path = module.Path(exchange_root) / "runs" / "hot-cleanup-ok" / "summary.json"
    with summary_path.open(encoding="utf-8") as handle:
        summary = module.json.load(handle)

    assert summary["success"] is True
    assert summary["status"] == "hotLeaseClosed"
    assert summary["hotCleanup"] is True
    assert summary["cleanup"]["status"] == "closed"
    assert not lease_path.exists()
    assert [call[1] for call in calls] == [
        "/v1/windows",
        "/v1/powerpoint/online/sessions/ppt-hot-sem27/cleanup",
        "/v1/windows",
    ]
PY

exchange_root="$tmp_root/exchange"
summary_path="$(
  WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
    "$script" \
    --deck-url 'https://tenant.sharepoint.com/sites/team/deck.pptx?web=1' \
    --run-id proof-runner-prepared-test \
    --allow-deck-mutation
)"
python3 - "$summary_path" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is True
assert summary["status"] == "prepared"
assert summary["execute"] is False
assert summary["verifyHostGate"] is False
PY

set +e
WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
  "$script" \
  --deck-url 'https://tenant.sharepoint.com/sites/team/deck.pptx?web=1' \
  --run-id proof-runner-mutual-exclusion-test \
  --execute \
  --verify-host-gate \
  --allow-deck-mutation >"$tmp_root/mutual-exclusion.out"
status=$?
set -e
[[ "$status" -eq 2 ]]
python3 - "$exchange_root/runs/proof-runner-mutual-exclusion-test/summary.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is False
assert summary["status"] == "gateFailed"
assert "--execute, --verify-host-gate, --verify-readiness, --verify-readiness-fast, --profile-warm, and hot lease modes are mutually exclusive" in summary["errors"]
PY

set +e
WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
  "$script" \
  --deck-url 'https://tenant.sharepoint.com/sites/team/deck.pptx?web=1' \
  --run-id proof-runner-readiness-prepared-test \
  --verify-readiness \
  --execute >"$tmp_root/readiness-mutual-exclusion.out"
status=$?
set -e
[[ "$status" -eq 2 ]]
python3 - "$exchange_root/runs/proof-runner-readiness-prepared-test/summary.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is False
assert summary["status"] == "gateFailed"
assert summary["verifyReadiness"] is True
assert "--execute, --verify-host-gate, --verify-readiness, --verify-readiness-fast, --profile-warm, and hot lease modes are mutually exclusive" in summary["errors"]
PY

set +e
WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
  "$script" \
  --deck-url 'https://tenant.sharepoint.com/sites/team/deck.pptx?web=1' \
  --run-id proof-runner-warm-mutual-exclusion-test \
  --profile-warm \
  --verify-readiness >"$tmp_root/warm-mutual-exclusion.out"
status=$?
set -e
[[ "$status" -eq 2 ]]
python3 - "$exchange_root/runs/proof-runner-warm-mutual-exclusion-test/summary.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is False
assert summary["status"] == "gateFailed"
assert summary["profileWarm"] is True
assert "--execute, --verify-host-gate, --verify-readiness, --verify-readiness-fast, --profile-warm, and hot lease modes are mutually exclusive" in summary["errors"]
PY

set +e
WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
  "$script" \
  --deck-url 'https://tenant.sharepoint.com/sites/team/deck.pptx?web=1' \
  --run-id proof-runner-hot-mutual-exclusion-test \
  --hot-start \
  --hot-run >"$tmp_root/hot-mutual-exclusion.out"
status=$?
set -e
[[ "$status" -eq 2 ]]
python3 - "$exchange_root/runs/proof-runner-hot-mutual-exclusion-test/summary.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is False
assert summary["status"] == "gateFailed"
assert summary["hotStart"] is True
assert summary["hotRun"] is True
assert "--execute, --verify-host-gate, --verify-readiness, --verify-readiness-fast, --profile-warm, and hot lease modes are mutually exclusive" in summary["errors"]
PY

set +e
WINDOWS_OPERATOR_EXCHANGE_ROOT="$exchange_root" \
  "$script" \
  --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
  --run-id proof-runner-sem27-gate-test \
  --execute \
  --allow-deck-mutation >"$tmp_root/sem27-gate.out"
status=$?
set -e
[[ "$status" -eq 2 ]]
python3 - "$exchange_root/runs/proof-runner-sem27-gate-test/summary.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    summary = json.load(handle)

assert summary["success"] is False
assert summary["status"] == "gateFailed"
assert summary["sem27"] is True
assert "--execute against SEM27 requires --allow-sem27" in summary["errors"]
PY

printf 'powerpoint-online-final-proof-tests: ok\n'
