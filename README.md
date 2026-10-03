# ThinkTank

Blazor Server web app that seats Claude, ChatGPT, Gemini and DeepSeek at one table to debate your topic, with personas, parallel debates, mid-discussion interjections and LLM-driven votes.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/) [![Blazor Server](https://img.shields.io/badge/Blazor-Server-5C2D91)](https://learn.microsoft.com/aspnet/core/blazor/) [![C#](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/) [![Tests](https://img.shields.io/badge/NUnit-296%20passing-2E7D32)](docs/BIBLE.md) [![License](https://img.shields.io/badge/license-all%20rights%20reserved-lightgrey)](#license)

```text
                       you: "Should we rewrite the billing service in Rust?"
                                          |
          +---------------+---------------+---------------+---------------+
          |               |               |               |               |
     [Claude]        [ChatGPT]        [Gemini]       [DeepSeek]      [you, any time]
     persona A       persona B        persona C       persona D       pause + interject
          |               |               |               |               |
          +---------------+-------+-------+---------------+---------------+
                                  |
                 round 1 .. round N   (shared history, one reply per seat per round)
                                  |
                 "Call Vote"  or  a seat emits [REQUEST_VOTE: question]
                                  |
             [VOTE] Question: ...  Decision: ... (80% agreement)  Summary: ...
```

ThinkTank runs locally with `dotnet run`; there is no hosted demo.

## Why

- Stop arguing with one AI. Put four frontier models in the same room and watch where they agree and where they split.
- Give each seat a point of view: a markdown personality, or a Legion persona with a full psychometric profile.
- Break a stalemate on demand: call a vote and get a decision, an agreement percentage and a summary injected back into the debate.
- Steer without starting over: pause, type your own message into the room, and the round loop picks up again.
- Run several debates side by side in tabs, and come back to every one of them after a restart.
- Keys stay yours: bring your own key per provider, or reuse the shared MindAttic Vault keys, and nothing cloud-resolved is ever written to disk.

## Features

### Roundtable debates

- Four seats out of the box, one per default provider in MindAttic.Legion's catalog: Claude (Anthropic), ChatGPT (OpenAI), Gemini (Google) and DeepSeek. Every call routes through `MindAttic.Legion`; ThinkTank never talks to a provider directly (`TT-LAW-1`).
- Setup panel with a topic box, a "Random topic" button that asks a model of your choice for a topic, a participant pill grid, and a Start button that needs at least two seats.
- Live round loop: each round calls every participant once with its personality, the shared history, the topic and the response-length preset, up to Max Rounds. An interrupted conversation resumes from its last incomplete round.
- User injection: pause, type in the bottom chat bar, and the loop resumes with your message in the shared history.
- Chat title generation: after round 1 every participant suggests a title in the background and one participant picks the best.

### Personas and personalities

- Each seat is a markdown personality template, editable on Settings, Personas, with an AI "Generate" button for custom seats.
- "Add persona from Legion library" dialog, searchable by name and personality text. A Legion persona's OCEAN, HEXACO, MBTI, Enneagram and DISC profile is rendered into a behavioural brief appended to the system prompt.
- Self-reference prefixes such as a leading `[Claude]:` are stripped from replies.

### Voting

- A Call Vote dialog with three vote types: Consensus (yes or no), Free-form conclusion, and Direction (your own comma-separated options).
- Any participant can trigger a vote itself by emitting `[REQUEST_VOTE: question]`; the marker is detected and removed from the visible reply.
- The aggregated result is injected into the shared history as a synthetic `[VOTE]` turn, so later rounds argue from the decision (`TT-LAW-4`).

### Workspace

- Conversation tabs with a right-click menu to rename or close; tabs persist across restarts.
- Member sidebar with animated typing indicators, per-conversation Max Tokens, Max Rounds and Response Length overrides, a Claude-only fallback toggle, Pause and Resume, and Export transcript to Markdown.
- Collapsible status log with Perspective, Context and Diagnostics tabs. Per-participant perspective files are kept per conversation; background faults (title generation, auto-vote) land in Diagnostics instead of vanishing.
- 18 themes: dark, light, spring, summer, autumn, winter, matrix, ice, sunset, neon, dracula, solarized, midnight, aurora, ember, ocean, forest and mono, plus control height (28-60 px), gutter (0-30 px) and border radius (0-24 px) sliders.

## Quick start

Prerequisites: the .NET 10 SDK and at least one API key for Claude, OpenAI, Gemini or DeepSeek.

```powershell
git clone https://github.com/mindattic/ThinkTank.git
cd ThinkTank
dotnet restore
dotnet run --project ThinkTank.Blazor
```

The app listens on `https://localhost:7100` and `http://localhost:5100` (from `ThinkTank.Blazor/Properties/launchSettings.json`).

1. Open Settings, Defaults, and paste a key for each provider you want under API keys, or leave them blank to use keys already in the shared MindAttic Vault file.
2. Open Think Tank (`/thinktank`), enter a topic, pick at least two participants and click Start.
3. Interject, call a vote, or open a new tab for a second debate.

## How it works

```text
                          Browser
     Blazor Server components (Home / Chat / Settings / ...)
                             |  SignalR (interactive server)
                             v
              ThinkTank.Blazor (ASP.NET Core host)
        Program.cs wires DI: Legion, Vault, services, voting
                             |
        +--------------------+--------------------+
        v                    v                     v
  ThinkTank.Shared     ThinkTank.Core         ThinkTank.Blazor
  (Razor lib +         (services + models)     (host)
   wwwroot)                   |
                              v
                    MindAttic.Legion
              LegionClient + LLMVotingService +
              LlmProviderCatalog + PersonaStore
                              |
        +-------------+-------+-------+-------------+
        v             v               v             v
     Claude        ChatGPT         Gemini       DeepSeek
```

ThinkTank is an ASP.NET Core Blazor Server web app (.NET 10, SignalR, no WebAssembly, no native shell). The unit of work is a panel, not a one-to-one chat. Legion owns the provider catalog, HTTP transport, retries and voting; ThinkTank assembles prompts, history and personas. It is single-host and has no accounts: state is shared by every browser connected to the same server.

All services are registered as singletons in `ThinkTank.Blazor/Program.cs`, one DI graph across every Blazor circuit (`HOUSE-LAW-6`). There is no desktop or native shell; the Blazor Server app is the only host (`docs/BIBLE.md` §3).

| Project | SDK and target | Role |
| --- | --- | --- |
| `ThinkTank.Core` | classlib, net10.0 | Services and models. References `MindAttic.Legion` 25.0.0 and `MindAttic.Vault` 4.0.0 from NuGet. No UI. |
| `ThinkTank.Shared` | Razor SDK, net10.0 | Razor component library plus `wwwroot` (CSS, 18 themes, `theme.js`, vendored Bootstrap CSS). |
| `ThinkTank.Blazor` | Web SDK, net10.0 | ASP.NET Core host. `Program.cs` is the single composition root. |
| `ThinkTank.UnitTests` | NUnit 4 and bUnit, net10.0 | Service, model, component, security and psychometric tests. |

None of the four `.csproj` files carries a `<Version>`: ThinkTank is run with `dotnet run`, not packaged.

### The host

`ThinkTank.Blazor` holds only `Program.cs`, `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor` and `Properties/launchSettings.json`. `Program.cs`:

1. `AddMindAtticVaultFiles()` layers configuration: `appsettings.json`, then the Vault file `%APPDATA%\MindAttic\LLM\providers.json`, then environment variables (Azure App Service or Key Vault in production).
2. `AddRazorComponents().AddInteractiveServerComponents()` selects Blazor Server render mode.
3. `AddLegionClient()` registers MindAttic.Legion, which owns its own `IHttpClientFactory`.
4. A singleton factory builds `SettingsService`, seeds `ProviderDefaults` from configuration (`claude` maps to auth type `anthropic`, `gemini` to `google`, anything else to `bearer`), then applies the Vault overlay with `OverlayFromConfiguration`.
5. Registers `ThinkTankSettingsService`, `ChatLogService`, `AppearanceService`, `ChatConversationsService`, `HumanNameService`, `NameGeneratorService`, `PsychometricProfileService`, `ThinkTankService`, `AddLLMVoting` (allowed providers: `LlmProviderCatalog.DefaultIds`) and `VotingService`, all as singletons.
6. Pipeline: exception handler, HSTS and HTTPS redirect outside Development, then `UseStaticFiles`, `UseAntiforgery`, and `MapRazorComponents<App>()` with interactive server render mode and the `ThinkTank.Shared` assembly added.

### Services and models

`ThinkTank.Core/Models`:

| File | Types |
| --- | --- |
| `ChatModels.cs` | `ParticipantTemplate` (seat definition: provider, display name, personality markdown, optional auth override, default flag, optional persona id), `ChatParticipant` (an instantiated seat), `ChatConversation` (one tab: id, title, participants, topic, per-tab max tokens, max rounds and response length, messages, status events, diagnostics) |
| `LlmModels.cs` | `SharedTurn`, `ConversationMessage`, `LlmModel` |
| `PersistenceModels.cs` | `PersistedConversation`, `PersistedParticipant`, `PersistedMessage`, `PersistedTurn`, `PersistedStatusEvent` |
| `ProviderAuthConfig.cs` | `(ProviderId, Json)` auth blob |
| `ChatLogModels.cs`, `AppearanceMode.cs`, `ResponseLengthPreset.cs` | Log entries, the 18 theme modes, response-length presets |

`ThinkTank.Core/Services`:

| Service | Responsibility |
| --- | --- |
| `ThinkTankService` | Orchestration: builds per-participant history, applies the personality as system prompt, trims to `MaxContextTurns`, strips self-reference prefixes, emits redacted diagnostics. Every call goes through `LegionClient`. |
| `VotingService` | Maps participants to Legion `VoterProfile`s and delegates to `LlmVotingService.VoteWithProfilesAsync`. |
| `SettingsService` and `SettingsServiceVaultOverlay` | Persist `Settings.json`; `GetKeyForProvider` applies the fixed key precedence; `BuildAuthJson`; per-app BYOK keys through Vault's `AppScopedCredentialStore`. |
| `PsychometricProfileService` and `PsychometricNarrator` | Resolve a seat's Legion persona and render its psychometric profile as a behavioural brief. |
| `AppearanceService` | Theme, control height, gutter and border radius, clamped, applied through JS interop and persisted. |
| `ChatConversationsService` | Tab lifecycle: `NewId`, `CreateConversation`, `SetActive`, `CloseConversation`. |
| `ChatLogService` | Event log with a `Changed` event and per-conversation storage. |
| `HumanNameService` and `NameGeneratorService` | Random and AI-generated participant names (falls back to "Alex" on empty output). |

### Pages and components

`ThinkTank.Shared` is a Razor class library with hand-rolled `wwwroot/app.css` and vendored Bootstrap CSS; there is no component library.

| Page or component | Route | What it does |
| --- | --- | --- |
| `Home.razor` | `/` | Landing page with links to Think Tank and Settings. |
| `Chat.razor` | `/thinktank` | The debate workspace (about 1,800 lines): setup panel, tabs, round loop, voting, status log. |
| `Settings.razor` | `/settings` | Three tabs: Personas (edit templates: name, provider, model override, custom auth JSON), Defaults (global max tokens, max rounds, response length, Claude fallback, per-provider API keys), Appearance. |
| `SettingsAppearance.razor` | embedded | Theme picker and sizing sliders. |
| `NotFound.razor` | fallback | 404 page. |
| `Shared/ConfirmationDialog.razor` | embedded | Reusable confirm and cancel dialog. |
| `Layout/MainLayout.razor`, `Layout/NavMenu.razor` | layout | App shell and navigation. |

## Configuration

### Provider auth

Each provider's auth is a JSON blob:

```json
{ "type": "bearer", "apiKey": "sk-...", "model": "gpt-4o", "maxTokens": 2048 }
```

`type` is `bearer` (OpenAI-compatible), `anthropic` or `google`.

### Keys and precedence

- Settings, Defaults, API keys stores a key used by ThinkTank only; it never changes what another MindAttic app resolves. Leave a field blank to fall back to the shared default.
- The shared default comes from `%APPDATA%\MindAttic\LLM\providers.json` (through `AddMindAtticVaultFiles`) or from `MindAttic:Vault:LLM:<providerId>:apiKey` in environment variables or Azure App Service settings.
- Cloud-resolved keys live in a runtime-only side map and are never written back to `Settings.json` (`TT-LAW-2`).
- `GetKeyForProvider` resolves in a fixed order (`TT-LAW-3`): explicit per-call override, then the on-disk key in `Settings.json`, then the Vault or cloud runtime override.

### Claude fallback

Settings, Defaults, Claude fallback mode routes every participant through the Anthropic API when other providers are rate-limited or down. Each seat keeps its own personality prompt; only the Claude key is used.

### Appearance

Settings, Appearance: theme (18 options), control height (28-60 px), gutter (0-30 px), border radius (0-24 px). Values are clamped in `AppearanceService`.

## Voting

Call Vote polls every participant through Legion's `LLMVotingService` and injects the result into shared history as a synthetic turn:

```text
[VOTE] Question: <question>
Decision: <consensus> (<percentage> agreement)
Summary: <narrativeSummary>
```

Vote types are Consensus (yes or no), Free-form, and Direction (custom options). Every participant's system prompt ends with an instruction (`VoteRequestInstruction` in `Chat.razor`) allowing it to emit `[REQUEST_VOTE: question]` to trigger a vote immediately. Automatic voting after N rounds without convergence is designed but not built: see [docs/rfc/0001-auto-vote-after-n-rounds.md](docs/rfc/0001-auto-vote-after-n-rounds.md).

## Data persistence

Persistence must fully rebuild a conversation after a restart (`TT-LAW-5`):

```text
%LOCALAPPDATA%\MindAttic\ThinkTank\
  Settings.json            All app settings (templates, conversations, appearance, defaults)
  Personalities\           Personality markdown (seeded: ChatGPT.md, Claude.md, Gemini.md, DeepSeek.md)
  Conversations\
    {chatId}\
      chat.jsonl           Append-only event log, one JSON entry per line
      {modelId}.md         Per-participant perspective file
```

A legacy `chat.json` array file is migrated to `chat.jsonl` on first read or append.

## Testing

`ThinkTank.UnitTests` uses NUnit 4.4.0 and bUnit 1.31.3 on net10.0, with `Microsoft.Extensions.Caching.Memory` pinned to 10.0.5 to override a vulnerable preview dependency (NU1903, GHSA-qj66-m88j-hmgj), and `AngleSharp` pinned to 1.8.3 to override bUnit's vulnerable 1.1.2 (GHSA-pgww-w46g-26qg).

```powershell
dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj
```

Last recorded run (docs/BIBLE.md, 2026-10-03): 296 passed, 0 failed, 0 skipped in about 1 second. The 21 test files:

| Area | Files |
| --- | --- |
| Services | `AppearanceServiceTests`, `ChatConversationsServiceTests`, `ChatLogServiceTests`, `HumanNameServiceTests`, `NameGeneratorServiceTests`, `SettingsServiceTests`, `SettingsServiceVaultOverlayTests`, `ThinkTankServiceTests`, `VotingServiceTests` |
| Models and parsing | `ModelTests`, `ProviderAuthConfigParsingTests`, `VoteMarkerTests` |
| Persistence | `ChatStorageTests` |
| Psychometrics | `PsychometricsTests` |
| bUnit components | `ConfirmationDialogComponentTests`, `HomePageComponentTests`, `NavMenuComponentTests`, `NotFoundPageComponentTests`, `SettingsAppearanceComponentTests` |
| Security | `Security/NoSecretsCommittedTests`: scans every git-tracked file for real-looking provider keys (`TT-LAW-6`) |
| Setup | `TestAssemblySetup` |

### End-to-end tests

Cypress specs live in `cypress/e2e/` and need the app running on port 5100:

```powershell
dotnet run --project ThinkTank.Blazor --urls http://localhost:5100
npm install
npx cypress run
```

Use `npx cypress open` for the interactive runner, or `npm run e2e:nav` for navigation only. `cypress.config.js` sets `baseUrl` to `http://localhost:5100` (override with `CYPRESS_BASE_URL`), a 1600x900 viewport, a 15 s command timeout, 60 s page-load and response timeouts, screenshots on failure, no video, and `chromeWebSecurity: false`.

| Spec | Coverage |
| --- | --- |
| `navigation.cy.js` | Routing between pages. |
| `settings.cy.js` | Settings tabs and behaviour. |
| `chat.cy.js` | Tab strip, new-tab creation, right-click rename and close. Does not trigger live LLM calls. |
| `vote-dialog.cy.js` | Participant pills and Start button, pill toggling, opening Call Vote, the three vote types, the Direction options field, Cancel. Does not submit a vote. |

`chat.cy.js` and `vote-dialog.cy.js` use a `blazorClick` helper that dispatches a raw `MouseEvent` to get past Cypress actionability checks on Blazor Server `@onclick` elements, and wait about 2.5 s after each visit for the SignalR circuit. These flows are UI-tested only; moving the round loop and vote dialog under unit tests is an open item in [docs/USER%5FSTORIES.md](docs/USER%5FSTORIES.md).

## Project layout

```text
ThinkTank/
  ThinkTank.slnx            Solution (XML .slnx format), four projects
  ThinkTank.Core/           Services + models class library (no UI)
  ThinkTank.Shared/         Razor class library: pages, components, wwwroot
  ThinkTank.Blazor/         ASP.NET Core host, Program.cs composition root
  ThinkTank.UnitTests/      NUnit 4 + bUnit, 21 test files
  cypress/e2e/              Cypress specs
  cypress.config.js
  docs/                     Codex canon: BIBLE, USER_STORIES, pending decisions, digest, rfc
  package.json              Cypress scripts
  tools/codex.ps1           Docs digest and doctor
  tools/build-readme.ps1    Renders README.md to README.htm with the shared codex-standard engine
```

## Limitations

- Only the four default Legion providers (Claude, ChatGPT, Gemini, DeepSeek) are exposed as seats. Legion's catalog knows more, but ThinkTank does not surface them.
- Single host, no accounts: every connected browser shares one state.
- Automatic voting after N stalled rounds is design-only (RFC 0001).

## Documentation

ThinkTank follows the MindAttic Codex standard: each fact lives in one layer, linked by a stable ID.

- [docs/BIBLE.md](docs/BIBLE.md) - what ThinkTank is and is not, architecture, the Laws (`TT-LAW-n`), verified test state, glossary.
- [docs/AMENDMENTS.md](docs/AMENDMENTS.md) - decisions not yet folded into the Bible (normally empty).
- [User stories](docs/USER_STORIES.md) - stories `TT-US-<Epic><n>`, each done story citing the test that proves it.
- [docs/rfc/](docs/rfc/) - design notes not yet in the canon.
- [docs/BIBLE.digest.md](docs/BIBLE.digest.md) - generated by `tools/codex.ps1 digest`; never hand-edit.
- [AGENTS.md](AGENTS.md) - instructions for coding agents working in this repo.

```powershell
powershell -File tools/codex.ps1 digest    # regenerate docs/BIBLE.digest.md
powershell -File tools/codex.ps1 doctor    # validate IDs, links, cited tests and paths, digest freshness
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-readme.ps1    # README.md to README.htm
```

## License

This repository has no LICENSE file. All rights reserved.

---

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Legion](https://github.com/mindattic/MindAttic.Legion) (LLM dispatch, voting and personas), [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (credentials).
