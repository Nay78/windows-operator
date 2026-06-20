# Runtime Notes

Default URLs:

- Windows Operator REST: `http://127.0.0.1:43117`
- PowerPoint add-in HTTPS host: `https://localhost:3003`

Host REST always binds `http://127.0.0.1:43117`. The add-in HTTPS host binds only when `PowerPointAddIn:enabled=true`. The add-in should call same-origin `/v1/powerpoint/jobs/*` when served by Host.

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

Autostart Host uses:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows\register-host-autostart.ps1 -RepoRoot Z:\windows-operator
```

The script publishes Host to `%ProgramData%\WindowsOperator\host`. When `dist\taskpane.html` exists, it copies the built add-in to `%ProgramData%\WindowsOperator\host\powerpoint-addin`, provisions a trusted LocalMachine `localhost` certificate, writes Host local config, and enables `https://localhost:3003`. If `dist` is missing or `-DisablePowerPointAddIn` is passed, Host REST still starts and the add-in listener stays disabled.

## Verification

Local checks prove compile and contract behavior only:

- `npm run typecheck`
- `npm test`
- `npm run build`
- `npm run manifest:validate`
- `dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj`

Live runtime checks before PowerPoint sideload:

- `GET http://127.0.0.1:43117/v1/health`
- Windows-side `Invoke-WebRequest https://localhost:3003/taskpane.html`
- `POST http://127.0.0.1:43117/v1/powerpoint/jobs`
- `POST http://127.0.0.1:43117/v1/powerpoint/jobs/claim`

Full edit verification requires PowerPoint running in Windows with the add-in sideloaded and a real presentation open.
