#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

spec_path="openapi/windows-operator.openapi.json"
readme_path="README.md"
missing=()

while IFS= read -r path; do
  if ! rg -F -- "$path" "$readme_path" >/dev/null; then
    missing+=("$path")
  fi
done < <(jq -r '.paths | keys[]' "$spec_path")

if (( ${#missing[@]} > 0 )); then
  {
    echo "README route inventory is missing OpenAPI paths:"
    printf '  %s\n' "${missing[@]}"
  } >&2
  exit 1
fi

count="$(jq '.paths | length' "$spec_path")"
echo "README route inventory covers $count OpenAPI paths."
