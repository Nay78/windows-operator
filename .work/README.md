# Work Queue Index

Last reviewed: 2026-07-31

This directory contains project-local planning state and retained implementation
evidence. Status in this index governs queue selection; detailed historical
roadmaps remain evidence, not proof of current readiness.

## Active Queue

1. `comprehensive-v1-contract-roadmap.md`
   - Status: portfolio-grounded; recoverable source checkpoint current,
     current-runtime publication authorized but Legion unavailable, live
     acceptance gated.
   - Priority: governing Windows Operator roadmap.
   - Current: `cp-20260731-01` maps to local commit
     `f8a3c4225de9cf7e77479ae2309e5a4be5663a63`. Full Windows solution passes
     339 tests. The published live runtime is older than the checkpoint, so
     historical live evidence is retained as a 44-verified/23-blocked policy
     ledger.
   - Next operator action: `LEGION-CONNECTIVITY`. Power on/wake Legion
     (`DESKTOP-6BT2OFE`), restore SSH port 22, and log in/unlock as `alejg` for
     Desktop Agent verification. Publication authority remains available.
   - External gates: Legion connectivity, credentialed or consequential live
     proof, relay deployment, and release tag/push.
2. `vm-workbench-smoothing-todo.md`
   - Status: partial.
   - Remaining: blocker detection, PowerPoint URL diagnostics, owned cleanup,
     and richer run artifacts.

Capacity recommendation: reserve one campaign slot for the comprehensive v1
contract because other applications depend on that boundary. Admit the next
campaign only after `LEGION-CONNECTIVITY` returns and the already-authorized
publication completes. Keep workbench smoothing queued separately; its
Host/Agent/PowerPoint overlap makes it unsuitable as a parallel fallback.

## Deferred Queue

- `powerpoint-online-surface-profile-improvements.md`
  - Deferred profile work; retain until its stated S1/S2 evidence condition is
    met.
- Additional language SDKs remain deferred pending named consumer demand.

## Current Evidence And Runbooks

- `external-consumer-integration-roadmap.md` retains the pre-v1 implementation
  ledger, audit evidence, and contract-hardening precursor.
- `powerpoint-online-docs-index.md` owns PowerPoint source ranking.
- `powerpoint-online-editing-harness-completion-audit.md` records completion
  evidence.
- `powerpoint-online-mutation-proof-runbook.md` owns repeatable mutation proof.
- `powerpoint-online-editing-harness-roadmap.md` retains design history and
  residual public-surface context.

## Completed Or Superseded Planning

- `operator-harness-alignment-roadmap.md`: completed campaign; execution ledger
  and final proof govern over original planned-status sections.
- `power-automate-mcp-harness-handoff.md`: superseded initial handoff; current
  implementation exceeds its three-route scope. Contract hardening belongs to
  the comprehensive v1 roadmap.
- PowerPoint historical handoffs, validations, approval packet, and discovery
  proofs are classified by `powerpoint-online-docs-index.md`; do not treat them
  as active queue items.

## Review Conditions

Review this index when:

- an active roadmap reaches its acceptance gate;
- Legion connectivity returns or current-runtime publication completes;
- any critical proof dependency changes availability;
- checkpoint fingerprint or `Current-Relation` changes;
- a new `.work` file is added;
- runtime or test evidence contradicts a completion claim;
- an operator authorizes a release, migration, or other external effect.
