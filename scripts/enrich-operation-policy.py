#!/usr/bin/env python3
"""Project OpenAPI ownership and normative v1 semantics into operation policy."""

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SPEC = ROOT / "openapi/windows-operator.openapi.json"
POLICY = ROOT / "openapi/windows-operator.operation-policy.json"
HTTP_METHODS = {"delete", "get", "head", "options", "patch", "post", "put", "trace"}

SESSION_OPERATIONS = {
    "getWorkbenchSession",
    "captureWorkbenchSessionScreenshot",
    "cleanupWorkbenchSession",
    "startEdgeBrowserSession",
    "openEdgeUrl",
    "getEdgeBrowserSessionState",
    "navigateEdgeBrowserSession",
    "clickEdgeBrowserDom",
    "fillEdgeBrowserDom",
    "closeEdgeBrowserSession",
    "captureEdgeBrowserSessionScreenshot",
    "cleanupEdgeBrowserSession",
    "evaluateEdgeBrowserDevScript",
    "startPowerAutomateMcpBridge",
    "openPowerAutomateMcpEdge",
    "cleanupPowerAutomateMcpEdge",
    "startPowerPointOnlineSession",
    "getPowerPointOnlineSession",
    "selectPowerPointOnlineSlide",
    "probePowerPointOnlineAddIn",
    "waitPowerPointOnlineSave",
    "preparePowerPointOnlineTemplate",
    "cleanupPowerPointOnlineTemplate",
    "runPowerPointOnlinePendingJob",
    "capturePowerPointOnlineSessionScreenshot",
    "cleanupPowerPointOnlineSession",
    "runPowerPointOnlineDevScript",
    "updatePowerPointOnlinePresentation",
}
POLLING_OPERATIONS = {
    "startMicrosoftAuthorizeProbe",
    "getLatestMicrosoftAuthorizeProbeStatus",
    "getMicrosoftAuthorizeProbeStatus",
    "startMicrosoftDeviceLogin",
    "getLatestMicrosoftDeviceLoginStatus",
    "getMicrosoftDeviceLoginStatus",
    "enqueuePowerPointJob",
    "claimPowerPointJob",
    "completePowerPointJob",
    "failPowerPointJob",
    "getPowerPointJob",
    "getMailRun",
}
ARTIFACT_OPERATIONS = {
    "captureDesktopScreenshot",
    "captureWindow",
    "captureWorkbenchSessionScreenshot",
    "captureEdgeBrowserSessionScreenshot",
    "capturePowerPointOnlineSessionScreenshot",
    "getPowerPointJobArtifact",
    "getArtifact",
    "listRunArtifacts",
    "downloadMailAttachments",
}
IDEMPOTENT_CLEANUPS = {
    "cleanupWorkbenchSession",
    "closeEdgeBrowserSession",
    "cleanupEdgeBrowserSession",
    "cleanupPowerAutomateMcpEdge",
    "cleanupPowerPointOnlineTemplate",
    "cleanupPowerPointOnlineSession",
    "cleanupMicrosoftAuthWindows",
}
CALLER_KEYED = {
    "startEdgeBrowserSession",
    "openEdgeUrl",
    "startPowerPointOnlineSession",
    "startMicrosoftAuthorizeProbe",
    "startMicrosoftDeviceLogin",
    "enqueuePowerPointJob",
    "completePowerPointJob",
    "failPowerPointJob",
    "downloadMailAttachments",
    "updatePowerPointOnlinePresentation",
}
DESKTOP_CONTENT = {
    "captureDesktopScreenshot",
    "captureWindow",
    "queryUi",
    "captureWorkbenchSessionScreenshot",
    "captureEdgeBrowserSessionScreenshot",
    "capturePowerPointOnlineSessionScreenshot",
}
CREDENTIAL_CONTENT = {
    "startMicrosoftAuthorizeProbe",
    "getLatestMicrosoftAuthorizeProbeStatus",
    "getMicrosoftAuthorizeProbeStatus",
    "startMicrosoftDeviceLogin",
    "getLatestMicrosoftDeviceLoginStatus",
    "getMicrosoftDeviceLoginStatus",
    "cleanupMicrosoftAuthWindows",
    "getPowerAutomateMcpStatus",
    "startPowerAutomateMcpBridge",
    "openPowerAutomateMcpEdge",
    "cleanupPowerAutomateMcpEdge",
    "readPowerAutomateMcpFlow",
    "updatePowerAutomateMcpFlow",
}
GATE_OVERRIDES = {
    "getLatestMicrosoftAuthorizeProbeStatus": ("none", "none", "pending"),
    "getMicrosoftAuthorizeProbeStatus": ("none", "none", "pending"),
    "getLatestMicrosoftDeviceLoginStatus": ("none", "none", "pending"),
    "getMicrosoftDeviceLoginStatus": ("none", "none", "pending"),
    "getPowerAutomateMcpStatus": ("none", "none", "pending"),
    "cleanupPowerAutomateMcpEdge": ("none", "ownedResource", "pending"),
    "cleanupMicrosoftAuthWindows": ("none", "ownedResource", "pending"),
    "enqueuePowerPointJob": ("none", "ownedResource", "pending"),
    "completePowerPointJob": ("none", "ownedResource", "pending"),
    "failPowerPointJob": ("none", "ownedResource", "pending"),
    "getMailStatus": ("none", "none", "pending"),
}


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


def openapi_operations(document: dict) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for path, path_item in document["paths"].items():
        for method, operation in path_item.items():
            if method not in HTTP_METHODS:
                continue
            request_schema = (
                operation.get("requestBody", {})
                .get("content", {})
                .get("application/json", {})
                .get("schema", {})
            )
            success_schema = (
                operation.get("responses", {})
                .get("200", {})
                .get("content", {})
                .get("application/json", {})
                .get("schema", {})
            )
            result[operation["operationId"]] = {
                "namespace": operation["x-windows-operator-namespace"],
                "requestSchemas": schema_names(request_schema),
                "successSchemas": schema_names(success_schema),
            }
    return result


def lifecycle(operation_id: str) -> str:
    if operation_id in ARTIFACT_OPERATIONS:
        return "artifact"
    if operation_id in POLLING_OPERATIONS:
        return "polling"
    if operation_id in SESSION_OPERATIONS:
        return "session"
    return "synchronous"


def idempotency(entry: dict) -> str:
    operation_id = entry["operationId"]
    if entry["method"] == "GET":
        return "safeRead"
    if operation_id in IDEMPOTENT_CLEANUPS:
        return "idempotentCleanup"
    if operation_id in CALLER_KEYED:
        return "callerKeyed"
    return "nonIdempotent"


def timeout_policy(entry: dict) -> str:
    namespace = entry["namespace"]
    if namespace == "mail.outlook":
        return "serverBounded:180s"
    if namespace == "powerpoint.online":
        return "serverBounded:660s"
    if entry["lifecycle"] == "session":
        return "serverBounded:60s"
    return "serverBounded:30s"


def concurrency_policy(entry: dict) -> str:
    operation_id = entry["operationId"]
    if operation_id == "claimPowerPointJob":
        return "atomicClaim"
    if entry["method"] == "GET":
        return "parallelRead"
    if entry["namespace"] in {"browser.edge", "powerpoint.online"}:
        return "serializePerSession"
    if entry["namespace"] in {"desktop", "input", "uia", "mail.outlook", "auth.microsoft"}:
        return "serializePerDesktop"
    return "independentRequests"


def sensitivity(entry: dict) -> str:
    operation_id = entry["operationId"]
    namespace = entry["namespace"]
    if operation_id in CREDENTIAL_CONTENT:
        return "credentialMetadata"
    if namespace == "mail.outlook":
        return "mailContent"
    if namespace.startswith("powerpoint"):
        return "documentContent"
    if operation_id in ARTIFACT_OPERATIONS:
        return "privateArtifact"
    if operation_id in DESKTOP_CONTENT or namespace in {"desktop", "input", "uia", "browser.edge"}:
        return "desktopContent"
    return "runtimeMetadata"


def negative_case(entry: dict) -> str:
    path = entry["path"]
    if entry["operationId"] in {"evaluateEdgeBrowserDevScript", "runPowerPointOnlineDevScript"}:
        return "Disabled development automation returns typed permission OperatorError."
    if "{" in path:
        return "Unknown resource identifier returns typed notFound OperatorError."
    if entry["method"] != "GET":
        return "Malformed or incomplete request returns typed validation OperatorError."
    return "Wrong HTTP method returns typed validation OperatorError."


def prerequisites(entry: dict) -> list[str]:
    values = ["campaign build running on Windows"]
    if entry["handlerOwner"] == "desktopAgentViaHost":
        values.append("interactive desktop agent healthy")
    if entry["credentialGate"] != "none":
        values.append(entry["credentialGate"])
    if entry["mutationGate"] == "consequentialApproval":
        values.append("operator approval")
    return values


def main() -> None:
    document = json.loads(SPEC.read_text())
    policy = json.loads(POLICY.read_text())
    source = openapi_operations(document)

    for entry in policy["operations"]:
        operation_id = entry["operationId"]
        projection = source[operation_id]
        entry.update(projection)
        if operation_id in GATE_OVERRIDES:
            credential_gate, mutation_gate, proof_state = GATE_OVERRIDES[operation_id]
            entry["credentialGate"] = credential_gate
            entry["mutationGate"] = mutation_gate
            if entry.get("proofState") not in {"verified", "blocked"}:
                entry["proofState"] = proof_state
        entry["errorSchemas"] = ["OperatorError"]
        entry["lifecycle"] = lifecycle(operation_id)
        entry["idempotency"] = idempotency(entry)
        entry["retryPolicy"] = (
            "safeWithSameRequest"
            if entry["idempotency"] == "safeRead"
            else "typedRetryableOnly"
            if entry["idempotency"] in {"idempotentCleanup", "callerKeyed"}
            else "neverWithoutObservedStatus"
        )
        entry["timeoutPolicy"] = timeout_policy(entry)
        entry["cancellationPolicy"] = (
            "pollByExplicitId"
            if entry["lifecycle"] == "polling"
            else "requestCancellation"
        )
        entry["concurrencyPolicy"] = concurrency_policy(entry)
        entry["sensitivity"] = sensitivity(entry)
        entry["successCase"] = entry["observableEffect"]
        entry["negativeCase"] = negative_case(entry)
        entry["prerequisites"] = prerequisites(entry)
        entry["proofEnvironment"] = "liveWindowsCampaignRuntime"
        entry["dispositionRationale"] = (
            "External consumer job requires compatibility."
            if entry["proposedSurface"] == "stable"
            else "Operator-only diagnostics excluded from ordinary relay and compatibility."
            if entry["proposedSurface"] == "diagnostic"
            else "Development-only mechanism excluded from ordinary relay and compatibility."
        )
        entry["unresolvedDecision"] = None
        entry["decisionOwner"] = "contractOwner"

    POLICY.write_text(json.dumps(policy, indent=2) + "\n")


if __name__ == "__main__":
    main()
