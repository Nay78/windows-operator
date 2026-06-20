# AGENTS.md

Project: `WindowsOperator.PowerPointAddIn`

Purpose: apply queued PowerPoint text/image updates to the active presentation through Office.js.

## Communication

- Be terse.
- Keep technical substance exact.
- Distinguish verified behavior from inference.
- Final answers: changed files, checks run, unresolved risks.

## Start Points

- Architecture: `docs/architecture.md`
- Specs: `docs/spec.md`
- Runtime notes: `docs/vm-connection.md`
- Office.js entry: `src/app.ts`
- Update boundary: `src/update/updateEngine.ts`
- Office adapter: `src/office/presentationAdapter.ts`

## Boundary Rules

- Primary path: Windows Operator Host queue + Office.js task pane mutation.
- Host owns job queue, artifact staging, static add-in hosting, queue status, and stable REST errors.
- Add-in owns active presentation mutation through Office.js only.
- No Graph access.
- No desktop PowerPoint COM edit path.
- Do not mutate slides through browser DOM clicks.
- Do not expose Office.js internals, local queue paths, or artifact staging paths to external callers.

## Deep Modules

- Choose owner hiding most complexity while simplifying callers.
- Keep public contracts small: desired state in, structured result out.
- Put artifact staging, retryable queue status, vendor quirks, API checks, and error translation inside owning modules.
- Prefer one upstream boundary fix over scattered caller conditionals.
- Avoid pass-through wrappers and tiny abstractions unless they hide real complexity or match existing patterns.
- Surface stable domain errors; keep implementation-specific exceptions internal.
- When adding a module, state what complexity it hides before adding interface.

## Office.js Rules

- Use `PowerPoint.run(async context => { ... })` for application-specific APIs.
- Queue reads/writes, load only needed properties, and minimize `context.sync()` calls.
- Never call `context.sync()` in tight loops; batch work first.
- Gate APIs with `Office.context.requirements.isSetSupported("PowerPointApi", version)`.
- Treat requirement sets as runtime constraints.
- Use `Office.context.document.getFileAsync(Office.FileType.Compressed, ...)` only to export current open presentation bytes.
- Always call `File.closeAsync()` after `getFileAsync()` slices are consumed.

## File Handling

- Treat `.pptx` as a zipped Open XML package if offline mutation is ever added.
- Do not edit `.pptx` bytes with string operations.
- Keep generated test decks small and synthetic.
- Avoid committing real customer decks unless user explicitly asks.
- Strip or avoid tenant/user/file IDs in fixtures.

## Verification

- Run `npm run typecheck` after TypeScript changes.
- Run `npm test` after behavior changes.
- Run `npm run build` before handing off runnable frontend changes.
- Run `npm run manifest:validate` after manifest changes.
- For Windows-dependent behavior, verify through live Windows Operator or PowerPoint; mocks prove only local contracts.
- For binary mutations, open/render the deck and inspect affected slides, not only API success.

## Safety

- Never use production tenant credentials in examples.
- Never log bearer tokens, auth codes, refresh tokens, cookies, or full signed sharing URLs.
- Do not bypass tenant policy, sharing restrictions, DLP, retention labels, sensitivity labels, or file locks.
- Stop and ask before bulk-updating many files or replacing existing library content.
