#!/usr/bin/env python3
"""Repository-owned lint for the Windows Operator public contract."""

import argparse
import json
from pathlib import Path
import re


HTTP_METHODS = {"delete", "get", "head", "options", "patch", "post", "put", "trace"}
SURFACES = {"stable", "diagnostic", "development"}
OPERATOR_ERROR_REF = "#/components/schemas/OperatorError"


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("spec", type=Path)
    args = parser.parse_args()

    document = json.loads(args.spec.read_text())
    errors: list[str] = []
    operation_ids: set[str] = set()
    operation_count = 0

    if document.get("openapi") != "3.0.3":
        fail(errors, "openapi must be 3.0.3")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", document["info"]["version"]):
        fail(errors, "info.version must be SemVer")

    for path, path_item in document.get("paths", {}).items():
        path_tokens = set(re.findall(r"{([^}]+)}", path))
        for method, operation in path_item.items():
            if method not in HTTP_METHODS:
                continue
            operation_count += 1
            location = f"{method.upper()} {path}"
            operation_id = operation.get("operationId")
            if not operation_id:
                fail(errors, f"{location}: missing operationId")
            elif operation_id in operation_ids:
                fail(errors, f"{location}: duplicate operationId {operation_id}")
            else:
                operation_ids.add(operation_id)
            if not operation.get("summary"):
                fail(errors, f"{location}: missing summary")
            namespace = operation.get("x-windows-operator-namespace")
            if operation.get("tags") != [namespace] or not namespace:
                fail(errors, f"{location}: namespace/tag mismatch")
            if operation.get("x-windows-operator-surface") not in SURFACES:
                fail(errors, f"{location}: invalid or missing surface")

            parameters = {
                item["name"]: item
                for item in path_item.get("parameters", []) + operation.get("parameters", [])
                if item.get("in") == "path"
            }
            if set(parameters) != path_tokens:
                fail(errors, f"{location}: path parameter set mismatch")
            for token, parameter in parameters.items():
                if parameter.get("required") is not True:
                    fail(errors, f"{location}: path parameter {token} must be required")

            responses = operation.get("responses", {})
            if not any(code.startswith("2") for code in responses):
                fail(errors, f"{location}: missing success response")
            for code in ("4XX", "5XX"):
                ref = (
                    responses.get(code, {})
                    .get("content", {})
                    .get("application/json", {})
                    .get("schema", {})
                    .get("$ref")
                )
                if ref != OPERATOR_ERROR_REF:
                    fail(errors, f"{location}: {code} must use OperatorError")

            if operation.get("deprecated"):
                if not operation.get("x-windows-operator-sunset"):
                    fail(errors, f"{location}: deprecated route missing sunset")
                if not operation.get("x-windows-operator-replacement"):
                    fail(errors, f"{location}: deprecated route missing replacement")

    schemas = document.get("components", {}).get("schemas", {})
    for name, schema in schemas.items():
        if schema.get("type") == "object" and not schema.get("properties") and not schema.get("additionalProperties"):
            fail(errors, f"schema {name}: unconstrained empty object")

    if errors:
        raise SystemExit("OpenAPI lint failed:\n- " + "\n- ".join(errors))
    print(f"OpenAPI lint passed: {operation_count} operations, {len(schemas)} schemas.")


if __name__ == "__main__":
    main()
