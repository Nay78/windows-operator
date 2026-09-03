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
for field in (
    "enabled =",
    "principalDetails =",
    "triggers = @(",
    "restartCount =",
    "restartInterval =",
    "executionTimeLimit =",
    "multipleInstances =",
    "startWhenAvailable =",
    "workingDirectory =",
):
    assert field in script, f"inspect-runtime.ps1 missing task policy field: {field}"
PY

host_registration="$repo_root/scripts/windows/register-host-autostart.ps1"
host_registration_test="$repo_root/scripts/windows/test-host-autostart-registration.ps1"
python3 - "$host_registration" "$host_registration_test" <<'PY'
import pathlib
import sys

registration = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
test = pathlib.Path(sys.argv[2]).read_text(encoding="utf-8")

for snippet in (
    "function ConvertTo-TaskDuration",
    "[System.Xml.XmlConvert]::ToTimeSpan($text)",
    "function Get-HostScheduledTaskValidationErrors",
    "Get-ScheduledTask -TaskName \"WindowsOperator.Host\" -ErrorAction Stop",
    "WindowsOperator.Host registration policy validation failed",
    "-RestartCount 3",
    "-RestartInterval (New-TimeSpan -Minutes 1)",
    "-ExecutionTimeLimit ([TimeSpan]::Zero)",
    "-MultipleInstances IgnoreNew",
    "-StartWhenAvailable",
):
    assert snippet in registration, f"register-host-autostart.ps1 missing: {snippet}"

for snippet in (
    "PowerShell parser reported errors.",
    "missing after registration",
    "XML restart duration did not normalize",
    "CIM TimeSpan restart duration did not normalize",
    "RestartCount mismatch was not rejected",
    "StartWhenAvailable mismatch was not rejected",
):
    assert snippet in test, f"test-host-autostart-registration.ps1 missing: {snippet}"
PY

printf 'inspect-runtime tests passed\n'
