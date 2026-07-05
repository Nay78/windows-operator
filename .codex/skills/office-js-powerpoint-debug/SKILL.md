---
name: office-js-powerpoint-debug
description: Use when debugging PowerPoint Online automation in this repo, especially Office.js add-in activation, PowerPoint bindings, table targets, add-in task panes, template/discovery jobs, DevTools script harness output, or live browser evidence for PowerPoint web decks.
---

# Office.js PowerPoint Debug

## Workflow

1. Start from typed repo APIs before browser scripting:
   - PowerPoint update/discovery jobs.
   - Named template targets with `TARGET_<TARGET_ID>` and `bindNamedTargets`.
   - Table operations: `readTable`, `replaceTableCell`, and `replaceTableRange`.
   - PowerPoint Online session/probe/save/template endpoints.
   - Named dev scripts under `/v1/dev/powerpoint/online/sessions/{sessionId}/script`.
2. Use raw JS only when named scripts and typed APIs cannot answer the question. Raw JS must be dev-gated with automation enabled, raw JS allowed, and request body `allowUnsafeRawJs=true`, then requested through `/v1/dev/browser/edge/sessions/{sessionId}/eval`.
3. Keep Edge test sessions lean: one PowerPoint tab, capture evidence only when needed, cleanup sessions after live checks.
4. Treat dry-run, schema, and mock tests as routing proof only. Browser, COM, Office.js, add-in activation, and save behavior require live Windows proof when user-visible.

## Dev Script Order

- `ppt.dom.snapshot`: page text, controls, frames, geometry.
- `ppt.ribbon.commands`: visible ribbon/command labels, add-in launch clues.
- `ppt.addin.frames`: iframe/taskpane candidates and cross-origin accessibility.
- `ppt.office.context`: `Office.context` only where visible to page context.
- `ppt.save.state`: saved/saving/sync indicators.

## Office.js Reference

Read `references/office-js-powerpoint.md` when working on Office.js behavior, add-in activation, binding discovery, or live PowerPoint Online quirks.

Update that reference in the same change whenever new Office.js behavior, limitation, workaround, or live evidence is discovered. Use entries with:

- `Date`
- `Observed behavior`
- `Implication`
- `Working command/script`
- `Evidence path`
