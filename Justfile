set dotenv-load := false

default:
    @just --list

# Sync repo-owned source, PowerShell profile, and Codex profile/skills to reachable Windows targets.
sync:
    scripts/linux/windows-sync-available.sh

# Sync only Codex config, agents, rules, and skills to the configured Windows target.
sync-codex:
    scripts/linux/windows-sync-codex-profile.sh

# Show Codex config/skills sync plan without connecting.
sync-codex-plan:
    scripts/linux/windows-sync-codex-profile.sh --dry-run

# Show sync target resolution without connecting.
sync-plan:
    scripts/linux/windows-sync-available.sh --dry-run

# Test the PowerPoint Online final proof runner without live mutation.
ppt-final-proof-test:
    scripts/linux/powerpoint-online-final-proof-tests.sh

# Prepare final PowerPoint Online proof artifacts for a deck URL without posting.
ppt-final-proof-prepare deck_url:
    scripts/linux/powerpoint-online-final-proof.py --deck-url "{{deck_url}}" --allow-deck-mutation

# Verify Host rejects the final SEM27 proof shape before queueing or opening Edge.
ppt-final-proof-host-gate:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-final-proof-host-gate-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --verify-host-gate

# Verify SEM27 non-mutating Office.js/save/reopen readiness without deck mutation.
ppt-final-proof-readiness:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-final-proof-readiness-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --verify-readiness

# Profile the PowerPoint Online surface with safe SEM27 non-mutating readiness proof.
ppt-profile:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-surface-profile-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --verify-readiness

# Agent-friendly alias for the PowerPoint Online surface profile.
easy-profile: ppt-profile

# Profile PowerPoint Online with safe SEM27 readiness, skipping tier3 reopen proof.
ppt-profile-fast:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-surface-profile-fast-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --verify-readiness-fast

# Warm one SEM27 session, run safe validate-only iterations, then cleanup.
ppt-profile-warm:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-surface-profile-warm-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --profile-warm

# Start or reuse a persistent SEM27 hot PowerPoint Online lease.
ppt-hot-start:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-hot-start-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --hot-session-id ppt-hot-sem27 \
        --hot-start

# Run one safe validate-only iteration against the persistent SEM27 hot lease.
ppt-hot-run:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-hot-run-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --hot-run

# Show persistent SEM27 hot lease and live session status.
ppt-hot-status:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-hot-status-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --hot-status

# Close the persistent SEM27 hot lease and remove the lease file.
ppt-hot-cleanup:
    scripts/linux/powerpoint-online-final-proof.py \
        --deck-url 'https://aminerals-my.sharepoint.com/personal/nmartinez_drs_mineracentinela_cl/Documents/SEM27%20-%20Plan%20Semanal%20Servicios%20Mina.pptx?web=1' \
        --run-id "ppt-hot-cleanup-$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')" \
        --hot-cleanup

# Agent-friendly alias for the fast PowerPoint Online surface profile.
easy-profile-fast: ppt-profile-fast
