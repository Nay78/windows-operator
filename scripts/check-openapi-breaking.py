#!/usr/bin/env python3
"""Detect breaking stable-surface changes against a frozen OpenAPI baseline."""

import argparse
import json
from pathlib import Path


HTTP_METHODS = {"delete", "get", "head", "options", "patch", "post", "put", "trace"}


def stable_operations(document: dict) -> dict[str, dict]:
    return {
        operation["operationId"]: operation
        for path_item in document["paths"].values()
        for method, operation in path_item.items()
        if method in HTTP_METHODS
        and operation.get("x-windows-operator-surface") == "stable"
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("current", type=Path)
    args = parser.parse_args()
    baseline = json.loads(args.baseline.read_text())
    current = json.loads(args.current.read_text())
    errors: list[str] = []

    old_operations = stable_operations(baseline)
    new_operations = stable_operations(current)
    for operation_id in sorted(old_operations.keys() - new_operations.keys()):
        errors.append(f"removed stable operation: {operation_id}")

    old_schemas = baseline.get("components", {}).get("schemas", {})
    new_schemas = current.get("components", {}).get("schemas", {})
    for name, old_schema in old_schemas.items():
        if name not in new_schemas:
            errors.append(f"removed schema: {name}")
            continue
        new_schema = new_schemas[name]
        old_properties = set(old_schema.get("properties", {}))
        new_properties = set(new_schema.get("properties", {}))
        for prop in sorted(old_properties - new_properties):
            errors.append(f"removed property: {name}.{prop}")
        old_enum = set(old_schema.get("enum", []))
        new_enum = set(new_schema.get("enum", []))
        for value in sorted(old_enum - new_enum):
            errors.append(f"removed enum value: {name}.{value}")
        added_required = set(new_schema.get("required", [])) - set(old_schema.get("required", []))
        for prop in sorted(added_required):
            errors.append(f"new required property: {name}.{prop}")

    if errors:
        raise SystemExit("breaking contract changes:\n- " + "\n- ".join(errors))
    print(f"No breaking stable changes against {args.baseline}.")


if __name__ == "__main__":
    main()
