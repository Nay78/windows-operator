#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
version_file="$repo_root/src/WindowsOperator.Core/Contracts/OperatorContractVersion.cs"

version="$(
  sed -nE 's/^[[:space:]]*public const string Value = "([^\"]+)";/\1/p' "$version_file"
)"

if [[ -z "$version" ]]; then
  echo "failed to read contract version from $version_file" >&2
  exit 1
fi

printf '%s\n' "$version"
