# PowerPoint Add-in Docs

Purpose: update text and images in the active PowerPoint presentation through Office.js, with Windows Operator Host owning job queue and artifact staging.

## Docs

- `architecture.md`: module boundary and runtime flow.
- `spec.md`: queue and Office.js job contracts.
- `vm-connection.md`: local Windows Operator hosting notes.

## Current Decision

`WindowsOperator.PowerPointAddIn` is a deep module inside `windows-operator`.

- Public caller contract: desired update job in, structured job record/result out.
- Hidden inside Host: durable queue files, artifact fetch/staging, artifact URLs, queue claim state, error translation.
- Hidden inside add-in: Office.js API checks, target inspection, `PowerPoint.run`, sync batching, active document guard.
- Not present: Graph, desktop PowerPoint COM edit path, browser DOM mutation.

Verified in this implementation: local contracts, build, add-in tests, manifest validation.

Not verified here: live PowerPoint desktop/web mutation in Windows VM.
