#!/usr/bin/env python3
"""Validate operation policy coverage and drift against generated OpenAPI."""

import argparse
from collections import Counter
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
HTTP_METHODS = {"delete", "get", "head", "options", "patch", "post", "put", "trace"}
SURFACES = {"stable", "diagnostic", "development"}
LOCAL_EXPOSURES = {"loopback", "operatorLocal"}
REMOTE_EXPOSURES = {"authenticatedRelay", "denied"}
HANDLER_OWNERS = {"host", "desktopAgentViaHost"}
EXPECTED_CALLERS = {"externalApplication", "monitoringSystem", "officeAddIn", "operatorTool"}
MUTATION_GATES = {"none", "ownedResource", "consequentialApproval"}
PROOF_STATES = {"pending", "blocked", "verified"}
LIFECYCLES = {"synchronous", "session", "polling", "artifact"}
IDEMPOTENCY_POLICIES = {"safeRead", "idempotentCleanup", "callerKeyed", "nonIdempotent"}
RETRY_POLICIES = {"safeWithSameRequest", "typedRetryableOnly", "neverWithoutObservedStatus"}
CANCELLATION_POLICIES = {"requestCancellation", "pollByExplicitId"}
CONCURRENCY_POLICIES = {
    "parallelRead",
    "atomicClaim",
    "serializePerSession",
    "serializePerDesktop",
    "independentRequests",
}
SENSITIVITY_CLASSES = {
    "runtimeMetadata",
    "desktopContent",
    "privateArtifact",
    "credentialMetadata",
    "mailContent",
    "documentContent",
}
REQUIRED_FIELDS = {
    "operationId",
    "method",
    "path",
    "proposedSurface",
    "consumerJob",
    "expectedCaller",
    "localExposure",
    "remoteExposure",
    "handlerOwner",
    "fixture",
    "observableEffect",
    "cleanup",
    "credentialGate",
    "mutationGate",
    "proofState",
    "evidence",
    "namespace",
    "requestSchemas",
    "successSchemas",
    "errorSchemas",
    "lifecycle",
    "idempotency",
    "retryPolicy",
    "timeoutPolicy",
    "cancellationPolicy",
    "concurrencyPolicy",
    "sensitivity",
    "successCase",
    "negativeCase",
    "prerequisites",
    "proofEnvironment",
    "dispositionRationale",
    "unresolvedDecision",
    "decisionOwner",
}
HOST_ONLY_NAMESPACES = {"artifacts", "powerpoint.jobs"}


def schema_names(schema: object) -> list[str]:
    names: set[str] = set()

    def walk(value: object) -> None:
        if isinstance(value, dict):
            reference = value.get("$ref")
            if isinstance(reference, str):
                names.add(reference.rsplit("/", 1)[-1])
            for child in value.values():
                walk(child)
        elif isinstance(value, list):
            for child in value:
                walk(child)

    walk(schema)
    if names:
        return sorted(names)
    if isinstance(schema, dict) and isinstance(schema.get("type"), str):
        return [f"inline:{schema['type']}"]
    return []


def collect_openapi_operations(document: dict, errors: list[str]) -> dict[str, dict]:
    operations: dict[str, dict] = {}
    for path, path_item in document.get("paths", {}).items():
        for method, operation in path_item.items():
            if method not in HTTP_METHODS:
                continue
            operation_id = operation.get("operationId")
            if not isinstance(operation_id, str) or not operation_id:
                errors.append(f"{method.upper()} {path}: missing operationId")
                continue
            if operation_id in operations:
                prior = operations[operation_id]
                errors.append(
                    f"OpenAPI duplicate operationId {operation_id}: "
                    f"{prior['method']} {prior['path']} and {method.upper()} {path}"
                )
                continue
            operations[operation_id] = {
                "method": method.upper(),
                "path": path,
                "surface": operation.get("x-windows-operator-surface"),
                "namespace": operation.get("x-windows-operator-namespace"),
                "requestSchemas": schema_names(
                    operation.get("requestBody", {})
                    .get("content", {})
                    .get("application/json", {})
                    .get("schema", {})
                ),
                "successSchemas": schema_names(
                    operation.get("responses", {})
                    .get("200", {})
                    .get("content", {})
                    .get("application/json", {})
                    .get("schema", {})
                ),
            }
    return operations


def nonempty_string(entry: dict, field: str, label: str, errors: list[str]) -> None:
    if not isinstance(entry.get(field), str) or not entry[field].strip():
        errors.append(f"{label}: {field} must be a non-empty string")


def string_array(entry: dict, field: str, label: str, errors: list[str]) -> None:
    value = entry.get(field)
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        errors.append(f"{label}: {field} must be an array of non-empty strings")


def validate_evidence(entry: dict, label: str, errors: list[str]) -> None:
    evidence = entry.get("evidence")
    if not isinstance(evidence, list):
        errors.append(f"{label}: evidence must be an array")
        return
    required = {
        "kind",
        "endpointOrCommand",
        "requestFixture",
        "observedStatus",
        "result",
        "observedEffect",
        "schemaValid",
        "timestamp",
        "runtimeBuild",
        "evidenceLocation",
    }
    kinds: set[str] = set()
    for index, item in enumerate(evidence):
        evidence_label = f"{label}: evidence[{index}]"
        if not isinstance(item, dict):
            errors.append(f"{evidence_label} must be an object")
            continue
        missing = sorted(required - set(item))
        if missing:
            errors.append(f"{evidence_label} missing fields: {', '.join(missing)}")
        for field in required & set(item):
            if field == "schemaValid":
                if item[field] is not True:
                    errors.append(f"{evidence_label}: schemaValid must be true")
            else:
                nonempty_string(item, field, evidence_label, errors)
        kind = item.get("kind")
        if kind not in {"success", "negative", "cleanup"}:
            errors.append(f"{evidence_label}: invalid kind {kind!r}")
        else:
            kinds.add(kind)
    if entry.get("proofState") == "verified":
        missing_kinds = sorted({"success", "negative", "cleanup"} - kinds)
        if missing_kinds:
            errors.append(
                f"{label}: verified proofState missing evidence kinds: {', '.join(missing_kinds)}"
            )


def validate_policy(
    document: dict,
    policy: dict,
    openapi_operations: dict[str, dict],
    errors: list[str],
) -> None:
    if policy.get("schemaVersion") != 1:
        errors.append("policy schemaVersion must be 1")

    jobs = policy.get("consumerJobs")
    if not isinstance(jobs, dict) or not jobs:
        errors.append("consumerJobs must be a non-empty object")
        jobs = {}
    for name, job in jobs.items():
        label = f"consumerJobs.{name}"
        if not isinstance(job, dict):
            errors.append(f"{label} must be an object")
            continue
        nonempty_string(job, "description", label, errors)
        if job.get("defaultCaller") not in EXPECTED_CALLERS:
            errors.append(f"{label}: invalid defaultCaller {job.get('defaultCaller')!r}")

    entries = policy.get("operations")
    if not isinstance(entries, list):
        errors.append("operations must be an array")
        entries = []

    policy_operations: dict[str, dict] = {}
    for index, entry in enumerate(entries):
        label = f"operations[{index}]"
        if not isinstance(entry, dict):
            errors.append(f"{label} must be an object")
            continue
        missing_fields = sorted(REQUIRED_FIELDS - set(entry))
        extra_fields = sorted(set(entry) - REQUIRED_FIELDS)
        if missing_fields:
            errors.append(f"{label} missing fields: {', '.join(missing_fields)}")
        if extra_fields:
            errors.append(f"{label} unknown fields: {', '.join(extra_fields)}")
        operation_id = entry.get("operationId")
        if not isinstance(operation_id, str) or not operation_id:
            errors.append(f"{label}: operationId must be a non-empty string")
            continue
        label = operation_id
        if operation_id in policy_operations:
            errors.append(f"policy duplicate operationId {operation_id}")
            continue
        policy_operations[operation_id] = entry

        for field in (
            "method",
            "path",
            "consumerJob",
            "fixture",
            "observableEffect",
            "cleanup",
            "credentialGate",
            "namespace",
            "timeoutPolicy",
            "successCase",
            "negativeCase",
            "proofEnvironment",
            "dispositionRationale",
            "decisionOwner",
        ):
            nonempty_string(entry, field, label, errors)
        for field in ("requestSchemas", "successSchemas", "errorSchemas", "prerequisites"):
            string_array(entry, field, label, errors)
        if entry.get("errorSchemas") != ["OperatorError"]:
            errors.append(f"{label}: errorSchemas must contain only OperatorError")
        if entry.get("proposedSurface") not in SURFACES:
            errors.append(f"{label}: invalid proposedSurface {entry.get('proposedSurface')!r}")
        if entry.get("expectedCaller") not in EXPECTED_CALLERS:
            errors.append(f"{label}: invalid expectedCaller {entry.get('expectedCaller')!r}")
        if entry.get("localExposure") not in LOCAL_EXPOSURES:
            errors.append(f"{label}: invalid localExposure {entry.get('localExposure')!r}")
        if entry.get("remoteExposure") not in REMOTE_EXPOSURES:
            errors.append(f"{label}: invalid remoteExposure {entry.get('remoteExposure')!r}")
        if entry.get("handlerOwner") not in HANDLER_OWNERS:
            errors.append(f"{label}: invalid handlerOwner {entry.get('handlerOwner')!r}")
        if entry.get("mutationGate") not in MUTATION_GATES:
            errors.append(f"{label}: invalid mutationGate {entry.get('mutationGate')!r}")
        if entry.get("proofState") not in PROOF_STATES:
            errors.append(f"{label}: invalid proofState {entry.get('proofState')!r}")
        if entry.get("lifecycle") not in LIFECYCLES:
            errors.append(f"{label}: invalid lifecycle {entry.get('lifecycle')!r}")
        if entry.get("idempotency") not in IDEMPOTENCY_POLICIES:
            errors.append(f"{label}: invalid idempotency {entry.get('idempotency')!r}")
        if entry.get("retryPolicy") not in RETRY_POLICIES:
            errors.append(f"{label}: invalid retryPolicy {entry.get('retryPolicy')!r}")
        if entry.get("cancellationPolicy") not in CANCELLATION_POLICIES:
            errors.append(f"{label}: invalid cancellationPolicy {entry.get('cancellationPolicy')!r}")
        if entry.get("concurrencyPolicy") not in CONCURRENCY_POLICIES:
            errors.append(f"{label}: invalid concurrencyPolicy {entry.get('concurrencyPolicy')!r}")
        if entry.get("sensitivity") not in SENSITIVITY_CLASSES:
            errors.append(f"{label}: invalid sensitivity {entry.get('sensitivity')!r}")
        if entry.get("unresolvedDecision") is not None and (
            not isinstance(entry.get("unresolvedDecision"), str)
            or not entry["unresolvedDecision"].strip()
        ):
            errors.append(f"{label}: unresolvedDecision must be null or a non-empty string")
        validate_evidence(entry, label, errors)

        job = entry.get("consumerJob")
        if job not in jobs:
            errors.append(f"{label}: unknown consumerJob {job!r}")
        if entry.get("proposedSurface") == "stable" and not job:
            errors.append(f"{label}: stable operation requires a consumerJob")
        if (
            entry.get("proposedSurface") in {"diagnostic", "development"}
            and entry.get("remoteExposure") != "denied"
        ):
            errors.append(f"{label}: diagnostic/development operation must be remote denied")
        if entry.get("credentialGate") != "none" and entry.get("proofState") == "pending":
            errors.append(f"{label}: credential-gated proof must be blocked or verified")
        if (
            entry.get("mutationGate") == "consequentialApproval"
            and entry.get("proofState") == "pending"
        ):
            errors.append(f"{label}: consequential proof must be blocked or verified")

    missing = sorted(set(openapi_operations) - set(policy_operations))
    extra = sorted(set(policy_operations) - set(openapi_operations))
    if missing:
        errors.append(f"policy missing operationIds: {', '.join(missing)}")
    if extra:
        errors.append(f"policy has stale operationIds: {', '.join(extra)}")

    for operation_id in sorted(set(openapi_operations) & set(policy_operations)):
        source = openapi_operations[operation_id]
        entry = policy_operations[operation_id]
        for policy_field, source_field in (
            ("method", "method"),
            ("path", "path"),
            ("proposedSurface", "surface"),
            ("namespace", "namespace"),
            ("requestSchemas", "requestSchemas"),
            ("successSchemas", "successSchemas"),
        ):
            if entry.get(policy_field) != source[source_field]:
                errors.append(
                    f"{operation_id}: {policy_field} drift; "
                    f"policy={entry.get(policy_field)!r} OpenAPI={source[source_field]!r}"
                )
        if (
            source["namespace"] in HOST_ONLY_NAMESPACES
            and entry.get("handlerOwner") != "host"
        ):
            errors.append(
                f"{operation_id}: {source['namespace']} operation must be Host-owned"
            )

    reset = policy_operations.get("resetEdgeBrowser")
    if reset and (
        reset.get("proposedSurface") != "stable"
        or reset.get("remoteExposure") != "denied"
        or reset.get("mutationGate") != "consequentialApproval"
    ):
        errors.append(
            "resetEdgeBrowser must remain stable, remote denied, and consequentialApproval-gated"
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "spec",
        nargs="?",
        type=Path,
        default=ROOT / "openapi/windows-operator.openapi.json",
    )
    parser.add_argument(
        "policy",
        nargs="?",
        type=Path,
        default=ROOT / "openapi/windows-operator.operation-policy.json",
    )
    args = parser.parse_args()

    errors: list[str] = []
    document = json.loads(args.spec.read_text())
    policy = json.loads(args.policy.read_text())
    openapi_operations = collect_openapi_operations(document, errors)
    validate_policy(document, policy, openapi_operations, errors)
    if errors:
        raise SystemExit("Operation policy check failed:\n- " + "\n- ".join(errors))

    surfaces = Counter(
        entry["proposedSurface"] for entry in policy["operations"]
    )
    proofs = Counter(entry["proofState"] for entry in policy["operations"])
    surface_summary = ", ".join(f"{name}={surfaces[name]}" for name in sorted(surfaces))
    proof_summary = ", ".join(f"{name}={proofs[name]}" for name in sorted(proofs))
    print(
        f"Operation policy passed: {len(openapi_operations)} operations "
        f"({surface_summary}); proof states {proof_summary}."
    )


if __name__ == "__main__":
    main()
