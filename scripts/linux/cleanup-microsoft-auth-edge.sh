#!/usr/bin/env bash
set -euo pipefail

preserve_recent_seconds="${PRESERVE_RECENT_SECONDS:-0}"
dry_run=false
base_url="${WINDOWS_OPERATOR_BASE_URL:-http://127.0.0.1:43117}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --preserve-recent-seconds)
      preserve_recent_seconds="$2"
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    --base-url)
      base_url="$2"
      shift 2
      ;;
    *)
      echo "Unknown arg: $1" >&2
      exit 2
      ;;
  esac
done

curl -sS -X POST "${base_url%/}/v1/auth/microsoft/cleanup" \
  -H 'Content-Type: application/json' \
  -d "{\"preserveRecentSeconds\":${preserve_recent_seconds},\"dryRun\":${dry_run}}"
