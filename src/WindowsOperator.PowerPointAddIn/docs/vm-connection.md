# Runtime Notes

Default URLs:

- Windows Operator REST: `http://127.0.0.1:43117`
- PowerPoint add-in HTTPS host: `https://localhost:3003`

Host process binds both URLs. The add-in should call same-origin `/v1/powerpoint/jobs/*` when served by Host.

## Local Dev

```bash
cd src/WindowsOperator.PowerPointAddIn
npm install
npm run dev
```

Sideload `manifest.xml` in PowerPoint. Dev server uses `https://localhost:3003`.

## Host Static Build

```bash
cd src/WindowsOperator.PowerPointAddIn
npm run build
```

Build output:

- `dist/taskpane.html`
- `dist/commands.html`
- `dist/manifest.xml`
- `dist/assets/*`

Windows Operator Host serves `dist` by default from sibling path `../WindowsOperator.PowerPointAddIn/dist`. Override with `PowerPointAddIn:StaticRoot`.

## Verification

Local checks prove compile and contract behavior only:

- `npm run typecheck`
- `npm test`
- `npm run build`
- `npm run manifest:validate`
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`

Live verification requires PowerPoint running in Windows with the add-in sideloaded and a real presentation open.
