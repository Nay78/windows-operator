#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

dotnet run --project src/WindowsOperator.OpenApi/WindowsOperator.OpenApi.csproj -- openapi/windows-operator.openapi.json
