#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

spec_path="openapi/windows-operator.openapi.json"
readme_path="README.md"
missing_paths=()
missing_routes=()

while IFS= read -r path; do
  if ! rg -F -- "$path" "$readme_path" >/dev/null; then
    missing_paths+=("$path")
  fi
done < <(jq -r '.paths | keys[]' "$spec_path")

while IFS= read -r route; do
  if ! rg -F -- "$route" "$readme_path" >/dev/null; then
    missing_routes+=("$route")
  fi
done < <(
  jq -r '
    .paths
    | to_entries[]
    | .key as $path
    | .value
    | keys[]
    | select(test("^(get|put|post|delete|patch|head|options|trace)$"))
    | (ascii_upcase + " " + $path)
  ' "$spec_path"
)

if (( ${#missing_paths[@]} > 0 || ${#missing_routes[@]} > 0 )); then
  {
    if (( ${#missing_paths[@]} > 0 )); then
      echo "README route inventory is missing OpenAPI paths:"
      printf '  %s\n' "${missing_paths[@]}"
    fi
    if (( ${#missing_routes[@]} > 0 )); then
      echo "README route inventory is missing OpenAPI method/path entries:"
      printf '  %s\n' "${missing_routes[@]}"
    fi
  } >&2
  exit 1
fi

count="$(jq '.paths | length' "$spec_path")"
route_count="$(
  jq '
    [.paths[]
      | keys[]
      | select(test("^(get|put|post|delete|patch|head|options|trace)$"))]
    | length
  ' "$spec_path"
)"
echo "README route inventory covers $count OpenAPI paths and $route_count method/path entries."
