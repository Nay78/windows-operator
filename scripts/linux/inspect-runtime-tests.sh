#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
script_path="$repo_root/scripts/windows/inspect-runtime.ps1"

python3 - "$script_path" <<'PY'
import pathlib
import re
import sys

script = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")

assert not re.search(
    r"Get-CimInstance(?:\s+-ClassName)?\s+Win32_Process\s*\|",
    script,
), "inspect-runtime.ps1 must not enumerate every Win32_Process instance"
assert "[int[]]$Ports = @(43117, 43119)" in script
assert "Get-NetTCPConnection -State Listen -LocalPort $port" in script
assert '-Filter ("ProcessId = {0}" -f $runtimeProcessId)' in script
assert "-Property ProcessId, Name, ExecutablePath, CommandLine" in script
PY

printf 'inspect-runtime tests passed\n'
