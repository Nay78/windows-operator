#!/usr/bin/env python3
"""Project a full Windows Operator OpenAPI document to selected route surfaces."""

import argparse
import json
from pathlib import Path


HTTP_METHODS = {
    "delete",
    "get",
    "head",
    "options",
    "patch",
    "post",
    "put",
    "trace",
}
SURFACE_KEY = "x-windows-operator-surface"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument(
        "--surface",
        action="append",
        required=True,
        choices=("stable", "diagnostic", "development"),
    )
    args = parser.parse_args()

    document = json.loads(args.source.read_text())
    selected = set(args.surface)
    filtered_paths = {}
    for path, path_item in document["paths"].items():
        filtered_item = {
            key: value
            for key, value in path_item.items()
            if key not in HTTP_METHODS
            or value.get(SURFACE_KEY) in selected
        }
        if any(key in HTTP_METHODS for key in filtered_item):
            filtered_paths[path] = filtered_item

    document["paths"] = filtered_paths
    args.destination.write_text(
        json.dumps(document, separators=(",", ":"), ensure_ascii=False)
    )


if __name__ == "__main__":
    main()
