# Entra App Inspection

Goal: inspect existing Entra app registrations from Windows Operator without tenant mutations.

Main constraint:

- No new app registrations
- No new secrets or certificates
- No sourced secret values from existing apps
- Use already-registered apps only
- Prefer flows we can actually complete sign-in with from this VM/session

Operational constraint:

- Browser success text is not proof. Token endpoint `200` is proof.
- For authorization-code flow, unregistered redirect/reply URI blocks that specific redirect path.
- Current `AADSTS500113` on `ams-prd-rpamail` means guessed auth-code redirects should be avoided unless Entra app config shows that exact redirect is already registered.
- Device-code flow does not depend on redirect URI, so do not reject device-code only because auth-code hit `AADSTS500113`.
- App-only/client-credentials paths are only useful if a usable secret or certificate value already exists locally. Visibility of “1 secret” in Entra is not enough.
- `Mail.Read` paths that already show admin-approval wall are lower priority unless tenant/admin state changes.
- Entra portal browser state is not reliable source of truth for completion; use token endpoint and, if needed, Graph response.

## Navigation

Scope:

- Page: `https://entra.microsoft.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps`
- Tenant: `Grupo Minero Antofagasta Minerals`

Reliable UIA path:

1. Outer left nav `App registrations` uses `ListItem`. Click that, not breadcrumb text.
2. Portal lands on `Owned applications`. This account is not an owner, so click button `View all applications in the directory`.
3. App grid rows expose the display-name cell as `DataItem` with automation IDs like `fxc-gc-cell-content_<grid>_<offset>`.
4. Open app detail by double-clicking the display-name cell `DataItem`. Single-click only selects the row.
5. Inside app detail, inner left menu entries such as `Overview`, `Certificates & secrets`, and `API permissions` also expose a `ListItem`. Click the `ListItem`. Hyperlink sibling was less reliable.
6. On `API permissions`, inspect configured permissions by querying `DataItem` with `includeOffscreen:true`. Table rows stay available in UIA even when below the fold.

Traps:

- Outer left nav `Overview` opens tenant overview, not app overview.
- Returning with outer left nav `App registrations` resets view to `Owned applications`; click `View all applications in the directory` again before opening another app.
- On the app grid, `Enter` after row selection was unreliable. Double-click on the display-name cell was reliable.
- `Certificates & secrets` column `-` on the grid matched app detail pages that showed `Add a certificate or secret`.

Oddities from repeated sessions:

- Entra portal can load the correct app URL but still throw blocking modal:
  - `Interaction required`
  - `no_tokens_found`
- Direct browser-address navigation to `https://entra.microsoft.com/#view/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/~/RegisteredApps` is a reliable recovery move after the modal. It restored the `App registrations` page without needing the modal buttons.
- Windows Edge signed-in work profile on this VM resolves from `Local State` as profile directory `Default`.
  - `profile.info_cache.Default.user_name = nmartinez.drs@mineracentinela.cl`
  - `profile.last_used = Default`
  - Browser/session automation should launch work mode with:
    - user data dir `%LOCALAPPDATA%\\Microsoft\\Edge\\User Data`
    - profile directory `Default`
  - Launching work mode without explicit `--profile-directory=Default` can land on wrong browser state or Entra login.
- Clicking portal modal buttons such as `Ignore` can return UIA success while the modal remains visible.
- App detail tab title may show `Sleeping` while UIA tree remains queryable.
- Edge accumulates many stale sign-in windows with identical title `Sign in to your account - Work - Microsoft Edge`.
- `GET /v1/windows` often shows many stale `hwnd` values for old auth flows. Always use latest foreground window or refresh before screenshot/click.
- Screenshot against stale `hwnd` is easy to do because old windows remain open and still render old auth states.
- `View all applications in the directory` can stay visible and clickable even after both UIA click success and raw screen-click success. In some sessions the button appears to no-op.
- Global Entra search box can be typed into via UIA, but submit behavior is unreliable. One `uia/type` request with `submit:true` returned `422`.
- Global Entra search overlay can stay stuck on an old query. Later `uia/type` attempts may appear to succeed while result text still reflects the previous term.
- When the global search overlay is open, querying `Text` often gives better clues than querying `ListItem`; category labels and the retained search term show up there even when result rows do not.
- On Microsoft account-picker pages, the visually obvious account row is often not the reliable click target.
  - Better target: button with long accessible name like `Sign in with nmartinez.drs@mineracentinela.cl work or school account.`
- On password pages, field is often already populated with masked value. Typing is usually unnecessary; `Sign in` click is enough.
- Some apps insert extra trust/store prompts after password:
  - example `ams-prd-rpamail` showed `Are you trying to sign in ... ?` then `Continue`
- Windows Operator `device-login` helper can land on wrong code-entry state or mis-enter code. Verify actual code visible in browser before trusting later browser results.
- Current `device-login` helper can also fail to populate the Microsoft device-code field even when result actions say `device_code_submitted`. Screenshot truth wins.
- On some retries with `reuseExistingProfile=true`, Edge can explode into many duplicate windows/tabs:
  - `Sign in to your account - Work - Microsoft Edge`
  - `login.microsoftonline.com - Work - Microsoft Edge`
  - `login.microsoftonline.com/common/wrongplace - Work - Microsoft Edge`
  - This is strong evidence that app classification should prefer token endpoint truth over window count or browser observer output.

## Candidate Findings

Inspected apps:

- `Aconex`
  - Client ID: `2165ee4c-c7d0-41a7-a7c1-e3dfa7f20275`
  - Overview showed:
    - `Client credentials`: `Add a certificate or secret`
    - Redirect counts: `1 web, 0 spa, 0 public client`
    - Supported account types: `Multiple organizations`
  - `API permissions`: `No permissions added`

- `ActiveDirectoryClient`
  - Client ID: `9eec0b11-c98b-4e77-894a-4e45039d9a5c`
  - Overview showed:
    - `Client credentials`: `Add a certificate or secret`
    - Redirect counts: `0 web, 0 spa, 1 public client`
    - Supported account types: `Multiple organizations`
  - `API permissions` rows visible in UIA:
    - `ActiveDirectoryService` -> `user_impersonation` -> `Delegated`
    - `Azure Active Directory Graph` -> `User.Read` -> `Delegated`
  - No visible `Microsoft Graph` or `Mail.Read`

- `ActiveDirectoryService`
  - Client ID: `29f6adcc-b534-4e99-b562-f94ae7d21ee0`
  - `API permissions` rows visible in UIA:
    - `Azure Active Directory Graph` -> `User.Read` -> `Delegated`
  - No visible `Microsoft Graph` or `Mail.Read`

- `Actas Directorio-VPL (Microsoft Copilot Studio)`
  - Client ID: `ef54a74b-424b-40e9-bff6-1c0da77c2e10`
  - Overview showed `Add a certificate or secret`
  - `API permissions`: `No permissions added`

## Decision

Best existing app for a delegated device-code Graph probe:

- `ActiveDirectoryClient` (`9eec0b11-c98b-4e77-894a-4e45039d9a5c`)

Why:

- Already configured as `1 public client`
- Existing delegated permissions prove it is used as an interactive client
- Device-code initiation for Graph scopes succeeded against this client ID

Limits:

- No inspected app had visible `Microsoft Graph` `Mail.Read`
- No inspected app had visible client secret or certificate
- So no inspected app is ready for app-only Graph mail access
- `ActiveDirectoryClient` is only a delegated/public-client starting point; real Graph mail use still needs consent for `Mail.Read`

## Mail.Read Tester

Linux helper:

```bash
scripts/linux/test-microsoft-graph-mail-read.sh \
  --tenant-id <tenant-id> \
  --client-id <client-id> \
  --handoff windows-script
```

What it does:

1. Calls Entra device-code endpoint for Graph scopes including `Mail.Read`.
2. Opens Edge in the Windows desktop session with `scripts/windows/login-microsoft-device-code.ps1`.
3. Polls the token endpoint.
4. On success, probes `GET https://graph.microsoft.com/v1.0/me/messages?$top=1`.

Current live results:

- `ActiveDirectoryClient` (`9eec0b11-c98b-4e77-894a-4e45039d9a5c`)
  - Browser reached `Need admin approval`
  - Not usable without admin consent
  - Additional `User.Read` probe with `reuseExistingProfile=true`:
    - `POST /v1/auth/microsoft/device-login`
    - `runId`: `active-directory-client-userread-3`
    - completed `2026-05-18T13:17:52Z`
    - result: `status:"timedOut"` from browser observer, but visual proof showed more progress than before
    - live page after code submission:
      - `Pick an account`
      - `You're signing in to ActiveDirectoryClient on another device located in Chile.`
      - account row `Nayguel Alejandro Martinez Cordova` marked `Connected to Windows`
    - token polling after that still returned `authorization_pending`
  - Meaning:
    - public-client + existing-session reuse works up to connected-Windows account picker
    - still not proven end-to-end token mint for `User.Read`
    - this is current best non-mail delegated surface candidate
  - Stronger follow-up run:
    - `POST /v1/auth/microsoft/device-login`
    - `runId`: `active-directory-client-userread-4`
    - existing Work-profile browser moved:
      - account picker
      - then password page for `nmartinez.drs@mineracentinela.cl`
      - password field already contained masked value
    - after `Sign in` click, direct token polling still returned only `authorization_pending`
      through at least `2026-05-18T13:41:20Z`
  - Meaning:
    - delegated flow is real, not immediate admin-consent failure
    - but automatic completion still not proven, and token mint still not observed

- `ams-prd-n8n-mail` (`33af835b-78c8-48fe-9409-9c28c1a67840`)
  - Overview showed `0 certificate, 2 secret`, `1 web, 0 spa, 0 public client`
  - `API permissions` included granted Graph mail scopes:
    - `Mail.ReadWrite`
    - `Mail.ReadWrite.Shared`
    - `Mail.Send`
    - `Mail.Send.Shared`
    - mailbox/calendar/contact scopes
  - Browser still reached `Need admin approval`
  - Not usable through user device-code flow as-is

- `ams-prd-rpamail` (`4d7414f8-221b-4a9d-9117-1ca3ade51b21`)
  - Overview showed `0 certificate, 1 secret`
  - `API permissions` included granted Graph mail scopes:
    - `Mail.Read`
    - `Mail.Read.Shared`
    - `Mail.ReadWrite`
    - `Mail.ReadWrite.Shared`
    - `Mail.Send`
  - Browser reached success page: `You have signed in to the ams-prd-rpamail application on your device`
  - Token polling stayed `authorization_pending` for 120 seconds
  - Best candidate found, but device-code compatibility still not proven

- `Node.js Outlook Tutorial` (`5ef6c564-9e58-4878-b428-c502e0ff744c`)
  - Overview showed expired secret and `0 public client`
  - `API permissions`: only `User.Read`
  - Not relevant for mail

## 2026-05-19 Manual Device-Code Retest

Fresh retest corrected earlier noise from browser automation.

Key correction:

- Windows Operator `device-login` helper can land on wrong code-entry state.
- At least one live run typed a bad device code into the Microsoft page:
  - intended: `ENLQ8C83A`
  - page showed failure text and rendered `ENLQ8C*#A`
- So earlier device-code conclusions based only on browser progression were weak.
- New runs below used direct UIA correction and step-by-step completion.

### `ActiveDirectoryClient` works for non-mail delegated Graph

- App: `ActiveDirectoryClient` (`9eec0b11-c98b-4e77-894a-4e45039d9a5c`)
- Run: `adc-userread-1`
- Flow:
  - `POST /v1/auth/microsoft/device-login`
  - account picker
  - password page
  - consent page `Permissions requested`
  - clicked `Accept`
  - browser success page:
    - `You have signed in to the ActiveDirectoryClient application on your device.`
- Token truth:
  - direct `POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token`
  - result: `200`
  - granted scopes included:
    - `https://graph.microsoft.com/User.Read`
    - `https://graph.microsoft.com/Files.Read`
    - `https://graph.microsoft.com/Files.ReadWrite`
- Meaning:
  - this app works now with no secret
  - but not for mail
  - useful confirmed surfaces: user/profile and file scopes

## Autonomous Auditor

Linux-side controller now lives at `scripts/linux/audit_entra_apps.py`.

Purpose:

- Drive planned Windows Operator Edge session REST from Linux
- Persist resumable metadata/probe state under one output root
- Rank candidates by public/delegated/mail/auth signals
- Probe `User.Read` first, then `Mail.Read` only for mail-relevant apps

Usage:

```bash
python3 scripts/linux/audit_entra_apps.py \
  --tenant-id <tenant-id> \
  --host-base-url http://127.0.0.1:43117 \
  --output-root artifacts/entra-audit
```

Useful modes:

- `--resume`
  - reuse existing `run.json`, `apps.jsonl`, and per-app artifacts
- `--metadata-only`
  - inspect Entra portal metadata only, skip OAuth probes
- `--probe-candidates-only`
  - skip portal inspection, probe candidates already persisted in `apps.jsonl`
- `--max-apps 10`
  - cap metadata/probe work

Output layout:

- `apps.jsonl`
  - append-only journal for app metadata and probe results
- `run.json`
  - controller/session progress, errors, counters
- `summary.json`
  - aggregate counts, top candidates, artifact paths
- `artifacts/<client-id>/*.json`
  - raw session states, navigation/click/fill records, metadata, probe results

Current assumptions:

- Endpoint names follow browser session routes:
  - `POST /v1/browser/edge/reset`
  - `POST /v1/browser/edge/session/start`
  - `GET /v1/browser/edge/session/{sessionId}/state`
  - `POST /v1/browser/edge/session/{sessionId}/navigate`
  - `POST /v1/browser/edge/session/{sessionId}/dom/click`
  - `POST /v1/browser/edge/session/{sessionId}/dom/fill`
  - `POST /v1/browser/edge/session/{sessionId}/close`
- `state` payload must expose enough DOM/text data for:
  - visible app row discovery
  - app detail text extraction
  - Microsoft device-code input/button targeting
- Browser success text still not proof. Script treats token endpoint `200` as proof.

### `ams-prd-n8n-mail` fails on admin approval

- App: `ams-prd-n8n-mail` (`33af835b-78c8-48fe-9409-9c28c1a67840`)
- Run: `n8n-device-1`
- Flow:
  - corrected bad code-entry state manually
  - account picker
  - password page
  - after `Sign in`, browser reached:
    - `Need admin approval`
    - `ams-prd-n8n-mail needs permission to access resources in your organization that only an admin can grant.`
- Token truth:
  - direct `POST /oauth2/v2.0/token`
  - still `authorization_pending` while browser sat on admin-approval wall
- Meaning:
  - this app does not work for us now
  - blocker is tenant admin approval, not browser session or reply URL

### `ams-prd-rpamail` browser completes, token exchange demands secret

- App: `ams-prd-rpamail` (`4d7414f8-221b-4a9d-9117-1ca3ade51b21`)
- Run: `rpamail-device-2`
- Flow:
  - account picker
  - password page
  - trust prompt:
    - `Are you trying to sign in to ams-prd-rpamail?`
    - clicked `Continue`
  - browser success page:
    - `You have signed in to the ams-prd-rpamail application on your device.`
- Token truth:
  - direct `POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token`
  - result at `2026-05-19T00:18:44Z`:
    - `AADSTS7000218`
    - `invalid_client`
    - `The request body must contain the following parameter: 'client_assertion' or 'client_secret'.`
- Meaning:
  - browser-side sign-in succeeds
  - device-code token exchange still fails
  - under current no-secret constraint, this app is not usable for Graph device-code auth
  - this strongly suggests confidential-client expectation on the token leg

### `Aconex` is assignment-blocked

- App: `Aconex` (`2165ee4c-c7d0-41a7-a7c1-e3dfa7f20275`)
- Run: `aconex-userread-1`
- Flow:
  - `POST /v1/auth/microsoft/device-login`
  - account picker
  - selected connected Windows work account
  - browser then reached explicit failure page
- Browser result:
  - `AADSTS50105`
  - `Your administrator has configured the application Aconex ... to block users unless they are specifically granted ('assigned') access to the application.`
- Meaning:
  - public/delegated flow exists
  - but app assignment blocks this user
  - not usable for us now

### `ActiveDirectoryService` starts like a public delegated flow

- App: `ActiveDirectoryService` (`29f6adcc-b534-4e99-b562-f94ae7d21ee0`)
- Run: `ads-userread-1`
- Browser reached:
  - account picker
  - connected Windows work account visible
- Meaning:
  - public/delegated flow likely exists here too
  - not yet stronger than `ActiveDirectoryClient`
  - lower value unless `ActiveDirectoryClient` later fails for some needed surface

### `ActiveDirectoryService` retest shows confidential-client token leg

- App: `ActiveDirectoryService` (`29f6adcc-b534-4e99-b562-f94ae7d21ee0`)
- Run: `ads-userread-4`
- Setup:
  - hard-killed all `msedge` processes first to recover visible auth windows
  - requested device code for delegated Graph `User.Read`
  - browser was driven manually after Windows Operator failed to type the code reliably
- Browser path:
  - device-code page first showed empty code field even though `POST /v1/auth/microsoft/device-login` reported `device_code_submitted`
  - after manual assist:
    - `Pick an account`
    - selected connected Windows account `Nayguel Alejandro Martinez Cordova`
    - password page appeared with masked password already populated
    - `Permissions requested`
    - clicked `Accept`
    - browser success page:
      - `You have signed in to the ActiveDirectoryService application on your device.`
- Token truth:
  - direct `POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token`
  - result at `2026-05-22T10:03:38Z`:
    - `AADSTS7000218`
    - `invalid_client`
    - `The request body must contain the following parameter: 'client_assertion' or 'client_secret'.`
- Meaning:
  - browser-side device flow is real
  - token exchange still behaves like confidential client
  - under current no-secret constraint, this app is not usable

### `Node.js Outlook Tutorial` stays low value

- App: `Node.js Outlook Tutorial` (`5ef6c564-9e58-4878-b428-c502e0ff744c`)
- Run: `nodeoutlook-userread-1`
- Observed:
  - device-code start succeeded
  - initial token poll stayed `authorization_pending`
- Meaning:
  - still no evidence of better permissions or better auth shape than `ActiveDirectoryClient`
  - keep low priority

### `Actas Directorio-VPL` is still unresolved and low-value

- App: `Actas Directorio-VPL (Microsoft Copilot Studio)` (`ef54a74b-424b-40e9-bff6-1c0da77c2e10`)
- Device-code init:
  - `POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/devicecode`
  - delegated Graph `User.Read`
  - result: `200`, device-code payload returned
- Live browser retest:
  - Run: `actas-userread-1`
  - foreground browser landed on:
    - `https://login.microsoftonline.com/appverify`
    - `Resubmit the form?`
    - `ERR_CACHE_MISS`
- Meaning:
  - device-code init works
  - current existing-profile browser path is unstable/noisy
  - no token success, no admin-approval proof, no secret/client-assertion proof yet
  - lower value than already-classified apps until browser path becomes deterministic

Current conclusion:

- Only one existing app is proven to work end-to-end now:
  - `ActiveDirectoryClient`
  - delegated non-mail Graph only
- No existing app is proven usable for `Mail.Read` under current constraints.
- Mail candidates split cleanly:
  - `ams-prd-n8n-mail`: blocked by admin approval
  - `ams-prd-rpamail`: blocked by secret/client-assertion requirement on token exchange

## Auth-Code Probe

Windows Operator now exposes:

```bash
curl -X POST http://127.0.0.1:43117/v1/auth/microsoft/authorize-probe \
  -H 'Content-Type: application/json' \
  -d '{"authorizeUrl":"https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize?..."}'
```

What it does:

1. Opens the authorize URL in Edge.
2. Launches Edge with either:
   - dedicated temporary profile and remote debugging port, or
   - existing signed-in Edge profile when `reuseExistingProfile=true`
3. Observes live page URL/title through Edge DevTools.
4. Returns one of:
   - `redirectObserved`
   - `needsUserAction`
   - `timedOut`
   - `failed`

Live result for `ams-prd-rpamail` with native-client redirect and PKCE:

- Endpoint:
  - `POST /v1/auth/microsoft/authorize-probe`
- Request shape:
  - `client_id`: `4d7414f8-221b-4a9d-9117-1ca3ade51b21`
  - `response_type=code`
  - `redirect_uri=https://login.microsoftonline.com/common/oauth2/nativeclient`
  - Graph scopes included `Mail.Read`
- Result:
  - `status: needsUserAction`
  - `browserState: browser_needs_user_action`
  - `browserTitle: Sign in to your account`
  - `observedUrl`: still the original authorize URL on `login.microsoftonline.com`
  - `observedCodePresent: false`
- Manual browser evidence during same run:
  - account field accepted `nmartinez.drs@mineracentinela.cl`
  - next page required password
  - submit without password yielded `Please enter your password.`

Meaning:

- Current no-secret probe did not reach redirect.
- So `ams-prd-rpamail` auth-code flow is not yet proven usable from Windows Operator.
- Important limiter: current probe uses a temporary fresh Edge profile, so it does not reuse the already signed-in Work profile session seen elsewhere in the VM.

Existing-profile retry:

- Same endpoint with `"reuseExistingProfile": true`
- Live browser evidence changed:
  - saved work account picker appeared
  - password wall from fresh profile disappeared
- After clicking saved account `nmartinez.drs@mineracentinela.cl`, Microsoft returned:
  - `AADSTS500113: No reply address is registered for the application.`
- Exact live endpoint run:
  - `POST /v1/auth/microsoft/authorize-probe`
  - `runId`: `rpamail-existing-profile-probe-4`
  - completed `2026-05-18T12:50:41Z`
  - result: `status:"timedOut"`, `browserState:"browser_observation_timed_out"`
  - action trail: `reuse_existing_profile`, `edge_opened`, `edge_activated:process`, `browser_observed:TimedOut`
  - observed redirect/code: none

Meaning:

- Existing signed-in Work profile reuse works.
- `ams-prd-rpamail` still fails for this auth-code probe.
- Blocker is app registration reply URL config for this flow, not local browser/session state.
- Current probe limitation: when reusing existing Edge profile, DevTools observation still misses the tenant error page and ends as timeout. Manual/UIA evidence remains source of truth for the specific `AADSTS500113` failure.

## Ranked Candidates

1. `ActiveDirectoryClient` (`9eec0b11-c98b-4e77-894a-4e45039d9a5c`)
   - Proven working now
   - End-to-end device-code token mint succeeded
   - Confirmed scopes:
     - `User.Read`
     - `Files.Read`
     - `Files.ReadWrite`
   - `Mail.Read` still blocked by admin approval

2. `ams-prd-n8n-mail` (`33af835b-78c8-48fe-9409-9c28c1a67840`)
   - Mail scopes present
   - Device-code browser flow is real
   - Hard blocker for us now:
     - `Need admin approval`

3. `ams-prd-rpamail` (`4d7414f8-221b-4a9d-9117-1ca3ade51b21`)
   - Mail scopes present
   - Browser-side device flow completes
   - Hard blocker for us now:
     - token exchange returns `AADSTS7000218 invalid_client`
     - requires `client_secret` or `client_assertion`
   - Auth-code path also still hit `AADSTS500113` on tested native redirect

4. Everything else inspected
   - No useful mail scope, or no proven public/delegated path worth more time
   - `Aconex` now also removed from shortlist because of `AADSTS50105` assignment block
   - `ActiveDirectoryService` now also removed from shortlist because token exchange returned `AADSTS7000218 invalid_client`
   - `Actas Directorio-VPL` remains unresolved but lower value than apps already classified

## Working Theory

- Reusing existing Edge Work profile was necessary and works.
- `ActiveDirectoryClient` is the only proven no-secret delegated success so far.
- `ams-prd-n8n-mail` fails because tenant admin approval is required.
- `ams-prd-rpamail` fails because browser completion still ends in confidential-client token requirements.
- `Aconex` fails because user assignment is blocked by app policy.
- `ActiveDirectoryService` fails because browser flow completes but token exchange still demands secret/assertion.
- `Node.js Outlook Tutorial` still has no evidence that it can beat `ActiveDirectoryClient` for useful surfaces.
- `Actas Directorio-VPL` currently stalls in unstable browser/appverify state and is not yet worth promoting.
- For `Mail.Read`, current registered-app set does not give a working no-secret path.

## Lower-Value Paths

- `ams-prd-rpamail` + guessed auth-code redirect `https://login.microsoftonline.com/common/oauth2/nativeclient`
  - low value for now because `AADSTS500113` shows that redirect path is not registered
- `ams-prd-rpamail` device-code without secret
  - low value now because token exchange ends `AADSTS7000218 invalid_client`
- `Aconex`
  - low value now because browser reached explicit `AADSTS50105` assignment block
- `ActiveDirectoryService`
  - low value now because it also ends in `AADSTS7000218 invalid_client`
- `Actas Directorio-VPL`
  - low value for now because device-code init works but browser path is unstable and no token truth exists
- `Node.js Outlook Tutorial`
  - low value for now because it has no stronger scope story and no mail advantage
- any confidential/app-only path where we only know a secret exists but do not possess the secret value
  - low value under current no-secret constraint
- any `Mail.Read` attempt on `ActiveDirectoryClient`
  - low value until admin-consent situation changes
- treating browser success page or sign-in progression as success without token mint
  - invalid evidence; not enough to call a path working

## Continue Rules

- Continue only on flows that can still change the token endpoint outcome.
- Prefer:
  - device-code or public/delegated flows on apps that already show real consent/account progression
  - manifest/authentication inspection to confirm actual supported redirect/public-client shape
- Stop a path when one of these becomes true:
  - auth-code redirect not registered and no other existing redirect or flow remains to test
  - only app-only auth remains and no secret/cert value exists
  - token endpoint returns explicit `invalid_client` or client-secret requirement
  - token endpoint stays `authorization_pending` through completed visible browser flow and eventual device-code expiry

## Search Blockers

- Entra portal itself became flaky during deeper inspection.
- Current visible app page `SSO Proactive Office PRD` repeatedly shows modal:
  - `Interaction required`
  - `The portal encountered an issue while attempting to retrieve access tokens`
  - detail: `no_tokens_found`
- This blocks reliable broader app inventory through the portal until the portal session is refreshed or bypassed.

## Next Slice

Goal: decide whether any untested already-registered app can beat current ranking.

Build:

1. Keep `ActiveDirectoryClient` as known-good baseline for non-mail Graph.
2. Stop spending cycles on `rpamail` device-code unless secret-bearing flow becomes allowed.
3. Stop spending cycles on `n8n-mail` unless admin approval changes.
4. If continuing search, inspect only untested apps with one of:
   - existing Graph mail scopes
   - explicit public-client shape
   - existing delegated Graph permissions beyond `User.Read`

Acceptance:

- Any new candidate must produce one of:
  - token `200`
  - explicit admin-approval block
  - explicit secret/client-assertion requirement
- Do not promote any candidate based on browser-only success.
