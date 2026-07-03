set dotenv-load := false

default:
    @just --list

# Sync repo-owned Windows Operator source and PowerShell profile to reachable Windows targets.
sync:
    scripts/linux/windows-sync-available.sh

# Show sync target resolution without connecting.
sync-plan:
    scripts/linux/windows-sync-available.sh --dry-run
