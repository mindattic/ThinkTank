# Plan: make ThinkTank a signed-in, cloud-hosted, impressive web app

Status: proposal, saved 2026-11-19 (TT-T41). Audit only; nothing here is started. No code was changed.

ThinkTank works today as a single-user local tool: pick personas, give a topic, watch several LLMs debate in
rounds, call votes, export the transcript. It has no sign-in, no accounts, no database, no deployment, and every
visitor shares one global set of conversations, so it is not safe to put on the internet with real API keys. This
plan lists what the audit found (with file and line evidence) and a four-phase path: Phase 0 puts a login in front
of everything that spends a key, Phase 1 moves data and keys to Azure and deploys it the way Tutor is deployed,
Phase 2 makes it impressive to a first-time visitor, Phase 3 is polish and scale. Honest total: about 7 to 10 weeks
of focused work, of which Phases 0 and 1 (the part needed before anyone else can use it) are about 3 weeks.

## 1. Current state in plain language

### What it is

A Blazor Server app (.NET 10). Four projects: `ThinkTank.Blazor` (host, one `Program.cs`), `ThinkTank.Shared`
(Razor pages and CSS), `ThinkTank.Core` (services and models), `ThinkTank.UnitTests` (NUnit + bUnit, 291 test
methods). Three pages: `/` (two buttons), `/thinktank` (the roundtable), `/personas` (persona editor).

### The user journey that works

1. Open `/thinktank`. A conversation tab exists or is created (`Chat.razor:578-579`).
2. Type a topic or click Random (one LLM call writes a topic, `Chat.razor:1312-1349`).
3. Pick two or more personas: the four built-in seats (Claude, ChatGPT, Gemini, DeepSeek), custom personas from
   `/personas`, or any of Legion's library personas through the picker (`Chat.razor:124-177`, `763-825`).
4. Click Start. Each round every participant is called once, in order, and sees the last 8 turns
   (`Chat.razor:991-1129`, `ThinkTankService.cs:342-353`). Default 10 rounds.
5. Type a message to steer the debate; focusing the input pauses the loop (`Chat.razor:993-994`, `1261-1304`).
6. Call a vote (consensus, free-form or custom options) or let a participant trigger one with
   `[REQUEST_VOTE: ...]`; the result is fed back in as a turn (`Chat.razor:1474-1629`).
7. Pause, resume (also after a restart; turns are replayed from disk), rename tabs, export Markdown
   (`Chat.razor:1703-1726`).
8. On `/personas`: add a persona, have an LLM write its personality, set a model override, test the connection
   (`Personas.razor:135-189`, `272-310`, `343-398`).
9. "Claude only" fallback routes every seat through Claude in character (`ThinkTankService.cs:162-167`).

All LLM calls go through `MindAttic.Legion` (`ThinkTankService.cs:201-209`, `VotingService.cs:46`); there is no
direct provider HTTP code. Keys come from MindAttic Vault's shared key lists with failover when a key is refused
(`SettingsService.cs:457-481`).

### What is stubbed, missing or stale

- No sign-in, no accounts, no roles. The BIBLE says so itself (`docs/BIBLE.md:39-40`).
- Not deployed anywhere. `MindAttic.Deploy/projects.json:49-50` has the entry disabled: "No workflow yet".
- The "Perspective" status tab is a stub: it logs "X updated their perspective" after every turn and the
  perspective file holds only the latest reply (`Chat.razor:1086-1092`, comment at `1089-1090`).
- `ChatLogService` collects connection-test entries but no page shows them (`Personas.razor:367-372` writes,
  nothing renders `Entries`). `NameGeneratorService` is injected and never used (`Personas.razor:8`).
  `ProgressPct`/`TotalTurns` are never set (`Chat.razor:1437-1439`).
- Auto-vote after N rounds is an unimplemented RFC (`docs/rfc/0001-auto-vote-after-n-rounds.md`).
- Replies are not streamed; each bubble appears whole after the call returns (`Chat.razor:1029`).
- Settings can only be edited in the desktop app MindAttic.Vault.Desktop; the nav cog launches it on the
  server machine (`NavMenu.razor:13`, `41-45`, `ThinkTankSettings.cs:294-298`). That cannot work when hosted.
- The home page is two buttons and a hidden heading (`Home.razor:5-12`).
- Docs are behind the code: README still describes a `/settings` page and `SettingsServiceVaultOverlay`
  (`README.md:148`, `163`), and BIBLE TT-LAW-2/TT-LAW-3 describe a `RuntimeApiKeyOverrides` map and an on-disk
  key tier that no longer exist (`docs/BIBLE.md:140-149` vs `SettingsService.cs:457-462`).

## 2. Architecture as found

| Topic | Finding | Evidence |
|---|---|---|
| Data store | Files only. No SQL Server, no SQLite, no EF Core, no connection string anywhere. Nothing points at localhost or (localdb), so the 2026-11-18 Azure SQL move broke nothing here. | `ThinkTank.Core.csproj:10-13`; `SettingsService.cs:117-148`; `ChatLogService.cs:71-72` |
| Where the files are | `%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json` (personas + every conversation with all messages), `...\Conversations\<chatId>\chat.jsonl` + one `.md` per participant, `...\Personalities\*.md`; app settings in `%APPDATA%\MindAttic\ThinkTank\settings.json`. | same; `ThinkTankSettings.cs:13-16` |
| Shared state | Every service is a singleton, including the conversation list and the "active tab". All browsers see and change the same conversations; one visitor switching tabs switches it for everyone. | `Program.cs:40-63`; `ChatConversationsService.cs:18-21`, `81-86` |
| LLM use | Through Legion only. Providers offered: Claude, OpenAI, Gemini, DeepSeek. | `ThinkTankService.cs:38-44`, `201-209` |
| Background work | None as a service. The round loop runs inside the page component of one browser circuit. | `Chat.razor:905-1134` |
| Configuration | No `appsettings.json` exists. Config = Vault files + environment variables. A hosted site reads settings from `MindAttic:Settings:ThinkTank:*` and keys from `MindAttic:Vault:LLM:*`. | `Program.cs:23-24`, `38-42`; `ThinkTankSettings.cs:95-99` |
| Packages | `MindAttic.Legion 27.0.0`, `MindAttic.Vault 7.0.0`. Neither version is on nuget.org (latest there: Legion 26.0.0, Vault 0.3.0); they restore only from `C:\LocalNuGet`. | `ThinkTank.Core.csproj:10-11`; `NuGet.config:9` |

### What each user action can cost (LLM calls)

| Action | Calls | Limits today | Evidence |
|---|---|---|---|
| Start a roundtable | participants x rounds; default 10 rounds, up to 2048 output tokens each | Rounds settable to 999 and tokens to 32768 per conversation from the page; participant count unlimited; topic length unlimited | `Chat.razor:990`, `1006-1029`, `1633-1647` |
| Type while a reply is in flight | The paid reply is thrown away and the call is made again | none | `Chat.razor:1031-1044` |
| Title after round 1 | one call per participant + one to pick | none | `Chat.razor:1136-1203` |
| Vote (manual or participant-triggered) | one call per participant (inside Legion) | A model can trigger a vote on any turn | `Chat.razor:1048-1054`, `1095-1096`; `VotingService.cs:27-47` |
| Random topic | 1 | none | `Chat.razor:1326-1331` |
| Add persona / Generate / Test connection | 1 each | none | `Personas.razor:159`, `286`, `371` |

Worst case from one click with no sign-in: 999 rounds x N participants x 32768 tokens, in as many tabs as the
visitor opens.

## 3. Findings

Severity: blocker = must fix before anyone but the owner can reach it; high = fix before hosting; medium = fix
before calling it finished; low = cleanup.

| # | Area | Finding | Evidence | Severity |
|---|---|---|---|---|
| 1 | Auth | No authentication or authorization at all. No `AddAuthentication`, no `[Authorize]`, no fallback policy, no reference to MindAttic.Authentication. Every page and the Blazor hub (`/_blazor`) are anonymous. | `Program.cs:26-78`; `ThinkTank.Blazor.csproj:9-12`; `docs/BIBLE.md:39-40` | blocker |
| 2 | Cost | Every LLM-spending action in the table above is reachable by an anonymous visitor, with no per-user quota, no rate limit and no daily budget. | `Chat.razor:1029`, `1326`; `Personas.razor:159`, `286`, `371`; no `AddRateLimiter` in `Program.cs` | blocker |
| 3 | Isolation | One global set of conversations and personas for all visitors; no owner on any record; the active tab is process-wide. Two browsers can run the same conversation at once and double the spend. | `ChatConversationsService.cs:18-21`, `81-86`; `Program.cs:44-48`; `PersistenceModels.cs` (no user field) | blocker |
| 4 | Security | The nav "Settings" button starts a desktop process on the server (`VaultSettings.Open`) from any browser click. Harmless locally; on a host it is an anonymous visitor launching a process. | `NavMenu.razor:13`, `41-45`; `ThinkTankSettings.cs:294-298` | high |
| 5 | Hosting | Data lives under `%LOCALAPPDATA%`, which on App Service is not persistent. Conversations and personas would vanish on restart unless `MINDATTIC_VAULT_LOCAL_ROOT` points at `C:\home\data\...` (as Tutor and Prose set it) or the data moves to a database. | `SettingsService.cs:118`; `ChatLogService.cs:71-72`; Tutor/Prose App Service setting names | high |
| 6 | Hosting | No deploy workflow (only `docs.yml`), no App Service, Deploy entry disabled. | `.github/workflows/`; `MindAttic.Deploy/projects.json:44-55` | high |
| 7 | CI | A GitHub runner cannot restore: Legion 27.0.0 and Vault 7.0.0 are not on nuget.org and `C:\LocalNuGet` does not exist there. Tutor solved this by vendoring packages in `lib/local-packages` (commit 2d47409). | `NuGet.config:3-9`; `ThinkTank.Core.csproj:10-11` | high |
| 8 | Reliability | The round loop is tied to the browser circuit and takes no cancellation token. Pause only acts between turns; closing the browser does not signal the loop to stop (`Dispose` only unsubscribes), so it may keep spending until max rounds; a dropped circuit loses the run's in-memory state. | `Chat.razor:991-1129`, `1219-1223`; `ThinkTankService.cs:211-213` | high |
| 9 | Security | No forwarded-headers handling, no security headers or CSP, no allowed-hosts list, HSTS only by default. Behind App Service's proxy `UseHttpsRedirection` reads the wrong scheme. | `Program.cs:67-74`; no `appsettings.json` | high |
| 10 | Reliability | `UseExceptionHandler("/Error")` points at a page that does not exist; an unhandled error in production returns an empty 404. No Blazor `ErrorBoundary`, no reconnect UI. | `Program.cs:69`; no `Error.razor` in the repo; `Routes.razor:1-8` | high |
| 11 | Ops | No health endpoint, no structured logging (13 `Console.Error.WriteLine` calls, no `ILogger`, no MindAttic.Log), no usage or cost record of any LLM call. | `Program.cs`; e.g. `SettingsService.cs:140`, `242`, `347`; `ThinkTankService.cs:125` | high |
| 12 | Data | No backup and no migration story: one JSON file rewritten whole, plus loose files. | `SettingsService.cs:322-350` | high |
| 13 | Performance | Every change rewrites the entire `Settings.json` (all conversations, all messages, all diagnostics) synchronously, including on every keystroke in the topic box. Messages are stored twice (`Settings.json` and `chat.jsonl`). | `ChatConversationsService.cs:127-163`; `Chat.razor:893-900`; `SettingsService.cs:379-385` | medium |
| 14 | Security | Persona `AuthOverrideJson` may carry an `apiKey` and is saved in plain text in `Settings.json` and copied into each conversation. The editing field is commented out today, so no key is stored at present (checked: the local file has no `apiKey` field). | `ThinkTankService.cs:251-268`; `VotingService.cs:93`; `Personas.razor:68-71` | medium |
| 15 | Security | Raw exception text from providers is shown to the user and persisted in transcripts and diagnostics. | `Chat.razor:1100-1108`, `1342`; `ThinkTankService.cs:221` | medium |
| 16 | Input | No length limits on topic, user message, persona markdown, tab title, vote question or options; no cap on participants or open tabs. | `Chat.razor:192`, `410`, `99`; `Personas.razor:74` | medium |
| 17 | UI standard | Controls are not from MindAttic.Components / MindAttic.UiUx. The app has its own 2,200-line `app.css` with 18 themes plus a vendored Bootstrap it barely uses, and dead rules for the removed key fields. | `App.razor:8-9`; `app.css:1163-1166`, `1986`; `ThinkTank.Shared.csproj` (no component package) | medium |
| 18 | UX | No streaming, no cost or progress indicator, no way to send a message while paused (Send is disabled unless running), home page is empty, no onboarding, no favicon, hover-only persona tooltips. | `Chat.razor:412`, `233-237`; `Home.razor:5-12`; `App.razor:11` | medium |
| 19 | Accessibility | Good base (skip link, focus-visible, reduced motion, ARIA roles, labelled dialogs), but: the tab close control is a `role="button"` span nested inside a `<button>`; dialogs have no focus trap or Escape handling (except rename); several inputs set `outline: none`; contrast is unverified across 18 themes. | `Chat.razor:19-38`, `70-122`; `app.css:10-53`, `1163-1166`, `1938-1986` | medium |
| 20 | Mobile | Breakpoints exist at 540, 580 and 900 px but the Cypress suite only runs at 1600x900; phone layout is untested. | `app.css:1129`, `1252`, `1363`, `1795`; `cypress.config.js:17-18` | medium |
| 21 | Testing | 291 NUnit/bUnit test methods cover services and small components. The round loop, votes, user injection and title generation live inside a 1,860-line Razor file and have no tests. Cypress has 4 specs, needs a hand-started server, is in no workflow, and one refers to a deleted `settings.cy.js`. | `Chat.razor`; `cypress/e2e/vote-dialog.cy.js:29`; `docs/BIBLE.md:186-189` | medium |
| 22 | Process | `.githooks/pre-commit` runs the whole test suite on every commit, against the workspace rule that tests run only on `/test`. | `.githooks/pre-commit:6`; `git config core.hooksPath` = `.githooks` | medium |
| 23 | Build | `ThinkTank.Shared` pins `Microsoft.AspNetCore.Components.Web` to a .NET 10 preview build. | `ThinkTank.Shared.csproj:17` | medium |
| 24 | Maintainability | `Chat.razor` holds the orchestration engine, persistence calls, a Markdown renderer and the UI in one file; the vote text format is duplicated. | `Chat.razor:905-1134`, `1464-1469`, `1730-1859` | medium |
| 25 | Config | Two different sets of default models (property initializers vs. `Backfill`); settings and credential declarations list Kimi and six other providers the roundtable cannot seat. | `ThinkTankSettings.cs:23-32`, `188-203`, `217-224`; `ThinkTankService.cs:38-44` | low |
| 26 | Docs | README and BIBLE describe removed pieces (see section 1). | `README.md:148`, `163`; `docs/BIBLE.md:140-149` | low |
| 27 | Repo | The repo is public. No secret is committed (a test scans for key shapes), but keep it in mind when vendoring packages and writing workflow comments. | `gh repo view` visibility PUBLIC; `ThinkTank.UnitTests/Security/NoSecretsCommittedTests.cs` | low |

Things that are fine and should be kept: all provider calls go through Legion; key failover and "flag a refused
key" (`SettingsService.cs:469-481`); model output is HTML-encoded before the Markdown-lite pass, so replies cannot
inject script (`Chat.razor:1735`); `Settings.json` is written to a temp file then moved (`SettingsService.cs:341-343`);
append-only per-chat log with a per-chat lock (`ChatLogService.cs:99-119`); antiforgery middleware is on
(`Program.cs:74`); the hosted-configuration seams already exist (`ThinkTankSettings.cs:95-99`, `Program.cs:42`).

### Reachable without signing in (complete list)

- Pages: `/`, `/thinktank`, `/personas`, `/not-found`.
- SignalR: the Blazor Server hub `/_blazor` (the only hub). Every button on every page is a hub call.
- HTTP API endpoints: none are mapped. Static files under `_content/ThinkTank.Shared/`.
- LLM-spending actions: Start/Resume, the re-call on user interjection, title generation, Call Vote,
  participant-triggered vote, Random topic, Add Custom Persona, Generate personality, Test connection.
- Other state-changing actions: create/rename/close any conversation, edit or delete any custom persona, toggle
  Claude fallback for everyone, launch Vault Desktop on the server.
- Self-registration: none (there are no accounts to register).

## 4. Roadmap

Sizes: S = under a day, M = days, L = a week or more.

### Phase 0: safe to show someone (about 1.5 weeks)

| # | Change | Where | Size | Depends on | Done when |
|---|---|---|---|---|---|
| 0.1 | Nothing that spends an API key is reachable without a login. Add MindAttic.Authentication with a fallback policy that requires a signed-in, allow-listed user on everything except the login pages and `/healthz`; forced password change and TOTP enrolment at first sign-in; first account seeded from the bootstrap token; pepper from Vault; login rate limit. Follow Prose.Hub's `HostedGate`/`HubHosting` (fail closed when not configured) and Tutor's Blazor wiring (`AddMindAtticAuthentication`, `UseMindAtticAuthentication`, `MapMindAtticAuthEndpoints`). No self-registration. | `ThinkTank.Blazor/Program.cs`, new `Hosting/` folder, `Routes.razor` (`AuthorizeRouteView`), new `ThinkTankAuthDbContext` + migration in `ThinkTank.Core` | M-L | A SQL database for the auth tables (0.2) | Signed out, every route and `/_blazor` returns the login page or 401; an account not on the allow-list is refused with a correct password; the app refuses to start in hosted mode with a missing setting |
| 0.2 | Create the `ThinkTank` database on `sql-mindattic-f65744` (serverless, auto-pause) for the `auth` schema; `EnableRetryOnFailure`; local dev uses the same Azure database (local SQL Server is stopped on purpose). | Azure SQL; connection string name `ConnectionStrings__ThinkTank` | S | Owner approval to create it | `/healthz/db` returns ok after a cold resume |
| 0.3 | Server-side spend limits, enforced in the service, not the page: max participants (for example 6), max rounds (for example 20), max tokens (for example 4096), max concurrent runs per user (1-2), a per-user daily call budget, and a rate limiter on Start/Vote/Generate. Every number is a Vault setting. | `ThinkTankService.cs`, `VotingService.cs`, `ThinkTankSettings.cs`, `Program.cs` (`AddRateLimiter`) | M | 0.1 (needs a user id) | Raising the numbers in the browser has no effect past the cap; the budget blocks the next call with a clear message |
| 0.4 | Record every LLM call (user, conversation, provider, model, tokens in/out when Legion reports them, time, error). | new `LlmUsage` table or log; `ThinkTankService.cs:199-223` | S-M | 0.1, 0.2 | A day's calls per user can be listed |
| 0.5 | Remove the server-side "launch Vault Desktop" button when hosted; hide the `mindattic-vault://` link. | `NavMenu.razor:13-19`, `41-45` | S | none | No browser action starts a process on the server |
| 0.6 | Stop a run when its circuit ends, and pass a cancellation token through `CallProvider` and votes so Pause and Close act at once. | `Chat.razor:905-1134`, `1219-1223`; `ThinkTankService.cs:141-224` | S-M | none | Closing the tab stops calls within one turn; Pause cancels the in-flight call |
| 0.7 | Real `/Error` page, an `ErrorBoundary` around the body, friendly provider-error text (details to the log only), input length limits. | `Program.cs:69`, `MainLayout.razor`, `Chat.razor:1100-1108` | S | none | A forced exception shows a styled page; a provider 401 shows "this provider is unavailable" |
| 0.8 | Forwarded headers, HTTPS redirect, HSTS, allowed hosts, security headers and a CSP that fits Blazor Server. | `Program.cs:67-74` | S | none | Headers present on every response; no redirect loop behind the proxy |

### Phase 1: cloud-complete (about 1.5 to 2 weeks)

| # | Change | Where | Size | Depends on | Done when |
|---|---|---|---|---|---|
| 1.1 | Per-user data. Move personas and conversations out of the global singletons into storage keyed by user id; make conversation/tab state per user (scoped, not singleton). Recommended store: EF Core tables in the `ThinkTank` database (Conversations, Participants, Turns, Personas, Usage), with a one-time import of the owner's existing `Settings.json` and `Conversations\` folders. | `ChatConversationsService.cs`, `SettingsService.cs`, `ChatLogService.cs`, `Program.cs:40-63`, new migrations | L | 0.1, 0.2 | Two accounts in two browsers see only their own tabs and personas; the owner's 17 existing conversations are imported |
| 1.2 | Keys from App Service settings / Key Vault, never from files, when hosted: `MindAttic__Vault__LLM__<provider>__apiKey` (or named keys + `Assignments__ThinkTank`) as Key Vault references; `MindAttic__Settings__ThinkTank__*` for app settings; `MINDATTIC_VAULT_ROAMING_ROOT`/`MINDATTIC_VAULT_LOCAL_ROOT` under `C:\home\data\ThinkTank`. Drop `apiKey` from persona `AuthOverrideJson`. | App Service configuration; `ThinkTankService.cs:251-268`; `VotingService.cs:93` | S-M | owner creates/approves secrets | The hosted app answers with no `providers.json` on disk; no key appears in any stored conversation |
| 1.3 | Deploy workflow, the Tutor way: vendor `MindAttic.Legion`, `MindAttic.Vault`, `MindAttic.Authentication` in `lib/local-packages`; `azure-deploy.yml` with GitHub OIDC (add `repo:mindattic/ThinkTank:ref:refs/heads/main` to app registration `gh-mindattic-webapps-deploy`), build, publish, deploy, restart, smoke test `/healthz`, `/healthz/db`, `/login`; `web.config` with a long startup timeout for the database resume. | `.github/workflows/azure-deploy.yml`, `NuGet.config`, `lib/local-packages/`, `ThinkTank.Blazor/web.config` | M | 0.1, 0.2 | A push to main deploys and the smoke test passes |
| 1.4 | Create the web app `mindattic-thinktank` on plan `ASP-MyApps-99fe` (Windows B1, resource group MyApps, already holds 5 sites) with a managed identity, HTTPS only, WebSockets on, Always On, ARR affinity on (Blazor Server needs sticky sessions). Data Protection key ring in blob storage protected by a Key Vault key, as Tutor does. | Azure (owner-approved); `Program.cs` | S-M | 1.3 | `https://mindattic-thinktank.azurewebsites.net/login` loads; a restart does not sign users out |
| 1.5 | Health: `/healthz` (process up) and `/healthz/db` (database reachable), anonymous, no data. | `Program.cs` | S | 0.2 | Used by the workflow smoke test |
| 1.6 | Logging: replace `Console.Error.WriteLine` with `ILogger` through MindAttic.Log; turn on App Service log stream; sign-in audit comes from the auth library. | all of `ThinkTank.Core/Services` | S-M | none | An LLM failure and a sign-in appear in the log with user and conversation id |
| 1.7 | Backups: confirm the new database is in the Azure SQL backup policy and add it to `docs/ops/azure-migration/BACKUP.md` in the workspace root; add an owner-only "export all my data" (JSON) download. | workspace `docs/ops`; new endpoint | S | 1.1 | A restore drill to a copy database works; export file opens |
| 1.8 | Enable the Deploy entry and fix its note. | `MindAttic.Deploy/projects.json:44-55` | S | 1.3 | `/deploy` routes to the workflow |
| 1.9 | CI build on every push (build only; tests stay on `/test`), and replace the pre-commit test run with a build or remove it. Pin `Microsoft.AspNetCore.Components.Web` to the released 10.0.x. | `.github/workflows/`, `.githooks/pre-commit`, `ThinkTank.Shared.csproj:17` | S | 1.3 (package feed) | Red X on a push that does not compile |

### Phase 2: impressive (about 2 to 3 weeks)

| # | Change | Where | Size | Depends on | Done when |
|---|---|---|---|---|---|
| 2.1 | Streamed replies: text appears as it is generated, with a stop button. | `ThinkTankService.cs`, `Chat.razor` (needs a streaming call in Legion; check what Legion 27 offers) | M-L | 0.6 | First words appear within about a second of a turn starting |
| 2.2 | Move the run loop out of the page into a server-side per-conversation runner, so a debate survives a refresh or a dropped connection and several viewers can watch. | new `RoundtableRunner` in `ThinkTank.Core`; `Chat.razor` becomes a view | L | 1.1 | Refresh mid-debate and it is still running and in sync |
| 2.3 | A real landing page and first-run flow: what it is in one screen, three one-click starter debates (topic + panel preset), an example finished debate to read before spending anything. | `Home.razor`, new presets | M | none | A new user reaches a running debate in two clicks |
| 2.4 | A "verdict" at the end: an automatic closing vote and a short summary card (decision, agreement %, each seat's final position), using the existing voting path and Legion's narrative summary; implement RFC 0001 (auto-vote every N rounds) at the same time. | `Chat.razor:1474-1527`, `VotingService.cs`, `ThinkTankSettings.cs` | M | none | Every finished debate ends with a result card |
| 2.5 | Show the personas properly: persona cards with the psychometric profile that is already loaded (OCEAN, MBTI and so on) and a vote breakdown by trait. | `PsychometricProfileService.cs`, `PsychometricNarrator.cs`, `Chat.razor` picker | M | none | Hover or tap a seat to see its profile; vote card shows who voted what |
| 2.6 | Cost and progress in view: round X of Y, calls and tokens used so far in this debate and today, remaining daily budget. | `Chat.razor` header; usage data from 0.4 | S-M | 0.3, 0.4 | Numbers match the usage table |
| 2.7 | Adopt MindAttic.Components / MindAttic.UiUx for buttons, inputs, dialogs and tabs; drop the vendored Bootstrap and dead CSS; keep one theme (plus light/dark) instead of 18. In-app settings page for the things a hosted user must change (defaults, theme), since Vault Desktop is not reachable from a browser. | `ThinkTank.Shared/wwwroot/app.css`, all Razor files, new `/settings` | L | none | No one-off button styles remain; settings change from the browser |
| 2.8 | Accessibility and mobile pass: fix the nested close button, focus trap + Escape in dialogs, visible focus on every input, contrast check, phone layout for the roundtable, tap alternative to hover tooltips, real favicon and page metadata. | `Chat.razor:19-38`, `43-177`; `app.css` | M | 2.7 | Keyboard-only run-through works; usable at 390 px wide |
| 2.9 | Share and keep: read-only share link for a finished debate (owner-only by default), export to Markdown/PDF, search and archive old debates instead of an ever-growing tab strip. | new page + endpoint | M | 1.1 | A signed-out reader can open a shared debate and nothing else |

### Phase 3: polish and scale (about 2 weeks, open-ended)

| # | Change | Where | Size | Depends on | Done when |
|---|---|---|---|---|---|
| 3.1 | Split `Chat.razor` into components and move orchestration into tested services; NUnit tests for the round loop, resume, vote injection, limits. | `ThinkTank.Shared/Components`, `ThinkTank.UnitTests` | L | 2.2 | The loop is covered without a browser |
| 3.2 | End-to-end tests against a fake Legion provider (no spend), run on demand; phone and desktop viewports; login flow. | `cypress/`, test host | M | 0.1 | `/test` runs them green |
| 3.3 | Admin page: users, usage per user, budgets, disable an account. | new `/admin`, Admin policy | M | 0.4 | Owner can see and cap each user |
| 3.4 | Invite flow (admin creates an account, user sets a password by emailed link) and password reset by email. | auth library reset endpoints + `Notifications` email | M | 3.3 | A second person can be onboarded without the owner knowing their password |
| 3.5 | More providers Legion already knows (Kimi, Grok, Mistral and so on), with per-provider model pickers. | `ThinkTankService.cs:38-44`, `ThinkTankSettings.cs:188` | S-M | none | A seat can use any provider that has a key |
| 3.6 | Performance: stop rewriting whole documents (gone with 1.1), virtualize long transcripts, response compression, static asset caching. | `Chat.razor:380-393`, `Program.cs` | S-M | 1.1 | A 500-message debate scrolls smoothly |
| 3.7 | Scale-out readiness only if needed: Azure SignalR Service and a distributed run lock. Not needed on one B1 instance. | `Program.cs` | M | 2.2 | Two instances serve one debate |
| 3.8 | Bring README, BIBLE and USER_STORIES back in line with the code. | `README.md`, `docs/` | S | each phase | No reference to removed classes or pages |

### Total size

- Phase 0: about 1.5 weeks. Phase 1: 1.5 to 2 weeks. Together about 3 to 3.5 weeks before it is safe to give a
  second person a login on the internet.
- Phase 2: 2 to 3 weeks. Phase 3: about 2 weeks.
- Whole plan: 7 to 10 weeks. The two large, risky items are per-user data (1.1) and the server-side runner (2.2).
- Running cost: no new App Service plan (the B1 plan is already paid for; watch memory with a sixth site on it),
  one serverless Azure SQL database (pauses when idle), small blob and Key Vault usage. LLM spend is the real
  cost and is the reason for 0.3.

## 5. Accounts and secrets inventory (for backup)

### Accounts

ThinkTank has no user accounts today: no auth tables, no user file, no roles, no MFA. There is nothing to back up
yet. When Phase 0 lands, accounts will live in the `auth` schema of the `ThinkTank` Azure SQL database, and the
first account is created from the bootstrap token.

Passwords are stored only as Argon2id hashes over a peppered HMAC. They cannot be read back by anyone, including
an administrator. What to back up is the user name, the pepper, and the TOTP recovery codes shown once at
enrolment. Reset procedure once accounts exist:

1. Normal: "Forgot password" sends a single-use link (15 minutes) to the account's email. This needs the
   `Notifications` email settings on the host.
2. Admin: an Admin account sets a temporary password; the user is forced to change it at next sign-in.
3. Last resort (no admin can sign in): with the auth tables holding no user, set
   `MindAttic__Vault__Security__bootstrap-token` on the host and restart; the first account is re-seeded and must
   change its password and enrol TOTP. Remove the token afterwards.
4. Lost TOTP device: use a recovery code; otherwise an Admin clears MFA for the account and it re-enrols.

If the pepper is lost or changed, every password stops verifying. Back the pepper up.

### Secrets ThinkTank reads today (names and locations only)

| Secret | Where it lives | How ThinkTank reads it |
|---|---|---|
| LLM keys: `claude`, `openai`, `gemini`, `deepseek` (used); `kimi`, `xai`, `cohere`, `mistral`, `together`, `fireworks` (declared, not used by the roundtable) | `%APPDATA%\MindAttic\LLM\providers.json`, fields `<provider>.apiKey` and `<provider>.apiKeys[]` (`key`, `label`) | `AddMindAtticVaultFiles` + `CreateKeySelector` (`Program.cs:23-24`, `42`); configuration path `MindAttic:Vault:LLM:<provider>:...` |
| Which key ThinkTank uses per provider | `%APPDATA%\MindAttic\Vault\key-assignments.json`, app id `ThinkTank` (names, not values) | `KeySelector.GetActiveKey` (`SettingsService.cs:461`) |
| Legion persona profiles (not secret; content) | `%APPDATA%\MindAttic\Legion\personas\` | `PsychometricProfileService.cs:36-37`; env override `MINDATTIC_LEGION_STORE` |

No user-secrets, no `appsettings.json`, no connection string, nothing in the repo.

### Data files worth backing up (not secret)

| File | Holds |
|---|---|
| `%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json` (+ `Settings.json.bak`) | Personas (5) and conversation tabs (2 open) |
| `%LOCALAPPDATA%\MindAttic\ThinkTank\Conversations\` | 17 conversation folders: `chat.jsonl` and per-participant `.md` |
| `%LOCALAPPDATA%\MindAttic\ThinkTank\Personalities\` | Seed personality Markdown |
| `%APPDATA%\MindAttic\ThinkTank\settings.json`, `settings.manifest.json` | App settings (theme, defaults, provider models) |

### Secrets a hosted ThinkTank will need (to be created in Phases 0-1)

| Setting name (App Service) | Purpose | Source / where the master copy is |
|---|---|---|
| `ConnectionStrings__ThinkTank` | Azure SQL database `ThinkTank` on `sql-mindattic-f65744`, managed identity (no password) | Azure |
| `MindAttic__Vault__Security__pepper.v1` | Password pepper | `%APPDATA%\MindAttic\Security\providers.json`, entry `pepper.v1` |
| `MindAttic__Vault__Security__bootstrap-token` | Seeds the first account; remove after first sign-in | same file, entry `bootstrap-token` |
| `MindAttic__Vault__Security__reset-token-key` | Signs password-reset tokens | same file, entry `reset-token-key` |
| `MindAttic__Vault__LLM__claude__apiKey` (and `openai`, `gemini`, `deepseek`) | LLM keys, as Key Vault references | `%APPDATA%\MindAttic\LLM\providers.json`; Key Vault to be chosen (existing vaults: `kv-tutor-f65744`, `kv-mindatticid-l47dc4`, `kv-demo-l47dc4`) |
| `DataProtection__BlobUri`, `DataProtection__KeyVaultKeyId` | Key ring for the login cookie and stored TOTP secrets | Azure blob container + Key Vault key |
| `Hosting__Mode`, `Hosting__AllowedHosts`, `Hosting__Auth__AllowedUsers` | Hosted mode, host name, who may sign in | not secret |
| `MINDATTIC_VAULT_ROAMING_ROOT`, `MINDATTIC_VAULT_LOCAL_ROOT` | Persistent folders under `C:\home\data\ThinkTank` | not secret |
| `MindAttic__Settings__ThinkTank__*` | App settings and spend limits | not secret |
| `MindAttic__Vault__Notifications__email__*` | SMTP for password-reset mail (Phase 3.4) | `%APPDATA%\MindAttic\Notifications` |
| GitHub repo secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | OIDC deploy (ids, not passwords) | app registration `gh-mindattic-webapps-deploy` |

## 6. Open questions for the owner

1. **Who gets in?** Only the owner, or a few named people? Recommendation: an allow-list of named accounts,
   created by the owner, no self-registration, TOTP required for everyone.
2. **Whose keys pay?** Recommendation: the owner's keys for now, with a hard per-user daily budget (0.3). Add
   "bring your own key" per user later only if outsiders use it.
3. **Database or files for conversations?** Recommendation: Azure SQL (`ThinkTank` database) for auth,
   conversations, personas and usage. Files under `C:\home` would be quicker (S instead of L for 1.1) but give no
   per-user queries, no easy backup and no path to more than one instance.
4. **One pepper for all apps, or one per app?** Tutor and Prose both read `pepper.v1` from the shared Security
   bucket. Recommendation: reuse the same value so one backup covers all apps; each app still has its own users.
5. **Create a new Key Vault for ThinkTank or reuse one?** Recommendation: one shared vault for LLM keys used by
   every hosted app (Tutor needs the same keys), so a key is rotated in one place.
6. **Keep 18 themes?** Recommendation: no; move to the MindAttic.UiUx theme with light and dark.
7. **Is the sixth site on the B1 plan acceptable?** Blazor Server holds memory per connected user. Recommendation:
   start there, watch memory for a week, move to its own plan only if it is tight.
8. **Public share links for finished debates (2.9)?** Recommendation: yes, off by default, per debate.
9. **The pre-commit hook that runs the whole test suite.** Recommendation: replace it with a build (finding 22).
10. **Daily budget numbers.** Recommendation to start: 6 participants, 20 rounds, 4096 tokens, 2 concurrent
    debates, 300 LLM calls per user per day; all Vault settings so they can be changed live.

## 7. What was not checked

- The app was built (`dotnet build`, 0 warnings, 0 errors) but not run; no test was run (workspace rule). The UI
  findings come from reading the Razor and CSS, not from a browser, so contrast and phone layout are unmeasured.
- Whether Legion 27 exposes a streaming call (affects the size of 2.1) and whether it returns token counts
  (affects 0.4).
- Whether a run loop really keeps calling providers after its browser tab closes (finding 8); the code has no
  stop signal on dispose, but the behaviour was not observed.
- Memory headroom on plan `ASP-MyApps-99fe` with a sixth site.
- The values of any secret (by design); only names and locations were read.
