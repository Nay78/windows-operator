#!/usr/bin/env python3
"""Fail when public OperatorError codes and their owning documentation drift."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/WindowsOperator.Core/ErrorCodes.cs"
DOC = ROOT / "docs/operator-error-codes.md"

source_codes = set(
    re.findall(r'public const string \w+ = "([a-z0-9_]+)";', SOURCE.read_text())
)
documented_codes = set(
    re.findall(r"^\| `([a-z0-9_]+)` \|", DOC.read_text(), flags=re.MULTILINE)
)

missing = sorted(source_codes - documented_codes)
stale = sorted(documented_codes - source_codes)
if missing or stale:
    if missing:
        print(f"undocumented OperatorError codes: {', '.join(missing)}", file=sys.stderr)
    if stale:
        print(f"stale documented OperatorError codes: {', '.join(stale)}", file=sys.stderr)
    raise SystemExit(1)

print(f"OperatorError documentation covers {len(source_codes)} source codes.")
