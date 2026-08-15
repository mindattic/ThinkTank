# Think Tank

**Stop arguing with one AI. Convene a roundtable.**

Think Tank turns frontier LLMs into a panel of advisors that debate, refine, and decide together.
Pick participants from **11 providers** (OpenAI, Anthropic, Google, DeepSeek, Mistral, xAI, Groq,
Together AI, OpenRouter, Fireworks, Cohere), assign each a personality, drop a topic into the room,
and watch them think out loud. Inject your own messages mid-discussion. Call a vote when they go in
circles. Run multiple debates in parallel tabs — all in a plain browser tab, no install.

This document is the practical "how do I build/run/test it" tour. For the canonical architecture
record (what the system IS / is NOT, the Laws, verified test state), see
**[docs/BIBLE.md](docs/BIBLE.md)** — this README does not duplicate that; it links to it.

---

## Table of contents

- [What it is](#what-it-is)
- [Documentation canon (Codex)](#documentation-canon-codex)
- [Solution & project layout](#solution--project-layout)
- [Architecture](#architecture)
- [ThinkTank.Blazor — the host](#thinktankblazor--the-host)
- [ThinkTank.Core — services & models](#thinktankcore--services--models)
- [ThinkTank.Shared — pages & components](#thinktankshared--pages--components)
- [Feature tour](#feature-tour)
- [Configuration](#configuration)
- [Voting](#voting)
- [Data persistence](#data-persistence)
- [Getting started](#getting-started)
- [Testing](#testing)
- [Cypress e2e coverage](#cypress-e2e-coverage)
- [Supported providers](#supported-providers)
- [Other files at the repo root](#other-files-at-the-repo-root)
- [Known gaps / stale notes](#known-gaps--stale-notes)

---

## What it is

Think Tank is an **ASP.NET Core Blazor Server** web app (.NET 10, SignalR transport, no WASM, no
native shell). It is **not** a single-model chat client — the unit of work is a *panel*, not a 1:1
conversation. It is **not** a provider SDK — every model call is routed through
[MindAttic.Legion](../MindAttic.Legion/), which owns the provider catalog, HTTP transport, retries,
and voting logic; Think Tank only assembles prompts, history, and personas. It is **not** a
credential store — cloud-resolved API keys live in a runtime-only side map via
[MindAttic.Vault](../MindAttic.Vault/) and are never written to disk. It is **not** multi-user —
state is shared globally on a single host with no accounts.

It used to be a .NET MAUI desktop app; the MAUI shell was fully retired in favor of Blazor Server
(see `AMENDMENTS.md#TT-A1`). It was also originally called `LLMThinkTank`/"Arena" before being
renamed (`AMENDMENTS.md#TT-A2`).

## Documentation canon (Codex)

This repo has adopted the MindAttic **Codex** documentation standard: each fact lives in exactly
one layer, linked by stable ID rather than line number.

| Layer | File | Purpose |
| --- | --- | --- |
| L0 | [docs/BIBLE.md](docs/BIBLE.md) | What the system IS / is NOT, architecture, the Laws (`TT-LAW-n`), verified test state, glossary. Anchors `{#TT-§N}` / `{#TT-LAW-n}`. |
| L1 | [docs/AMENDMENTS.md](docs/AMENDMENTS.md) | Append-only change log (`TT-A<n>`). An amendment *wins* over the Bible; entries are never rewritten, only superseded. |
| L2 | [docs/USER_STORIES.md](docs/USER_STORIES.md) | Stories `TT-US-<Epic><n>`; every ✅ cites the NUnit test that proves it. |
| rfc | [docs/rfc/](docs/rfc/) | Design notes for work not yet graduated into L0/L2. Currently: [`0001-auto-vote-after-n-rounds.md`](docs/rfc/0001-auto-vote-after-n-rounds.md). |
| generated | [docs/BIBLE.digest.md](docs/BIBLE.digest.md) | Produced by `tools/codex.ps1 digest`. Never hand-edit. |

Status legend used across the canon: `✅ done` (test/build-verified) · `🟡 partial` ·
`⬜ planned` · `🗑️ cut` · `living`.

Regenerate/validate the canon with:

```powershell
powershell -File tools/codex.ps1 digest    # regenerate docs/BIBLE.digest.md
powershell -File tools/codex.ps1 doctor    # validate IDs, links, cited tests/paths, digest freshness
```

## Solution & project layout

`ThinkTank.slnx` (the newer XML `.slnx` solution format) lists exactly four projects, in this order:

```
ThinkTank/
├── ThinkTank.slnx
├── ThinkTank.Core/            Services + models class library (no UI)
│   ├── Models/                 ParticipantTemplate, ChatParticipant, ChatConversation, SharedTurn, ...
│   └── Services/                ThinkTankService, VotingService, SettingsService, AppearanceService, ...
├── ThinkTank.Shared/           Razor class library — every page and component + wwwroot assets
│   ├── Components/Layout/       MainLayout, NavMenu
│   ├── Components/Pages/        Home, Chat, Settings, SettingsAppearance, NotFound
│   ├── Components/Shared/       ConfirmationDialog
│   └── wwwroot/                 app.css, theme.js, vendored Bootstrap CSS
├── ThinkTank.Blazor/           ASP.NET Core host — Program.cs is the single DI composition root
├── ThinkTank.UnitTests/        NUnit 4 + bUnit — 21 test files
├── cypress/e2e/                Cypress specs: navigation, settings, chat, vote-dialog
├── cypress.config.js
├── docs/                       Codex canon (BIBLE.md, AMENDMENTS.md, USER_STORIES.md, digest, rfc/)
├── index.htm                   Static MindAttic.com landing/marketing page (see below — currently stale)
├── package.json                Cypress + landing-page build/deploy scripts
├── scripts/cli/                Landing-page build/deploy tooling (partially untracked, see note below)
└── tools/codex.ps1             Codex digest/doctor tooling
```

## Architecture

```
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
        +--------------------+--------------------+
        v                    v                     v
     OpenAI              Anthropic            ... 9 more
    (ChatGPT)              (Claude)              providers
```

All services are registered as **singletons** in `ThinkTank.Blazor/Program.cs` — one shared engine
and DI graph across every Blazor circuit (per the org-wide `HOUSE-LAW-6`). Full architecture
narrative, the domain model ("nouns"), the service list ("verbs"), and the five project Laws
(`TT-LAW-1`..`TT-LAW-6`) live in [docs/BIBLE.md §4](docs/BIBLE.md#TT-§4) and
[§5](docs/BIBLE.md#TT-§5) — summarized in the table below.

| Project | SDK / target | Role |
| --- | --- | --- |
| `ThinkTank.Core` | classlib, net10.0 | Services + models; references `MindAttic.Legion` (22.0.0) + `MindAttic.Vault` (1.0.0). No UI. |
| `ThinkTank.Shared` | Razor SDK, net10.0 | Razor component library + `wwwroot` (CSS, 18 themes, `theme.js`, vendored Bootstrap). |
| `ThinkTank.Blazor` | Web SDK, net10.0 | ASP.NET Core host. `Program.cs` is the single composition root. |
| `ThinkTank.UnitTests` | NUnit 4 + bUnit, net10.0 | Service, model, component, security, and psychometric tests. |

None of the four `.csproj` files carries a `<Version>` tag — Think Tank is run via `dotnet run`,
not packaged/published as a versioned artifact (unlike the NuGet-consumed `MindAttic.Legion`
22.0.0 / `MindAttic.Vault` 1.0.0 it depends on).

## ThinkTank.Blazor — the host

`ThinkTank.Blazor.csproj` targets `Microsoft.NET.Sdk.Web` / net10.0 and references
`ThinkTank.Core` + `ThinkTank.Shared`. Its only files are `Program.cs`, `Components/App.razor`,
`Components/Routes.razor`, `Components/_Imports.razor`, and `Properties/launchSettings.json`.

`Program.cs` is the entire composition root:

1. `builder.Configuration.AddMindAtticVaultFiles()` — layers config `appsettings.json` →
   Vault files (`%APPDATA%\MindAttic\LLM\providers.json`) → environment variables (Azure App
   Service / Key Vault in production).
2. `AddRazorComponents().AddInteractiveServerComponents()` — confirms Blazor **Server** render mode
   (not WebAssembly, not Hybrid).
3. `builder.Services.AddLegionClient()` — registers `MindAttic.Legion`; Legion owns its own
   `IHttpClientFactory` internally.
4. A singleton factory builds `SettingsService`, seeds `ProviderDefaults` from configuration
   (mapping e.g. `claude`→`"anthropic"`, `gemini`→`"google"`, else `"bearer"`), then layers the
   Vault overlay via `settings.OverlayFromConfiguration(config)`.
5. Registers `ThinkTankSettingsService`, `ChatLogService`, `AppearanceService`,
   `ChatConversationsService`, `HumanNameService`, `NameGeneratorService`,
   `PsychometricProfileService`, `ThinkTankService`, `AddLLMVoting(...)` (with
   `VotingConfiguration { AllowedProviderIds = LlmProviderCatalog.DefaultIds }`), `VotingService`
   — **all as singletons**.
6. Pipeline: exception handler / HSTS / HTTPS redirect only outside Development, `UseStaticFiles`,
   `UseAntiforgery`, `MapRazorComponents<App>().AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(ThinkTank.Shared.Components.Pages.Home).Assembly)`.

## ThinkTank.Core — services & models

Class library, no UI. Two folders:

**`Models/`**

| File | Types |
| --- | --- |
| `ChatModels.cs` | `ParticipantTemplate` (seat definition: `ProviderId`, `DisplayName`, `PersonalityMarkdown`, optional `AuthOverrideJson`, `IsDefault`, optional `PersonaId`), `ChatParticipant` (an instantiated seat with `EffectivePersonaId`), `ChatConversation` (one tab: `ChatId`, `Title`, `Participants`, `Topic`, per-tab `MaxTokens`/`MaxRounds`/`ResponseLength`, `Messages`, `StatusEvents`, `Diagnostics`) |
| `LlmModels.cs` | `SharedTurn`, `ConversationMessage`, `LlmModel` |
| `PersistenceModels.cs` | `PersistedConversation`, `PersistedParticipant`, `PersistedMessage`, `PersistedTurn`, `PersistedStatusEvent` |
| `ProviderAuthConfig.cs` | `(ProviderId, Json)` auth blob |
| `ChatLogModels.cs`, `AppearanceMode.cs`, `ResponseLengthPreset.cs` | Log entries, theme mode, response-length presets |

**`Services/`**

| Service | Responsibility |
| --- | --- |
| `ThinkTankService` | Orchestration engine — builds per-participant history, applies personality markdown as system prompt, trims to `MaxContextTurns`, sanitizes self-reference prefixes (e.g. strips leading `[Claude]:`), emits redacted `Diagnostics` events. Every model call goes through `LegionClient`. |
| `VotingService` | Maps `ChatParticipant[]` → Legion `VoterProfile[]` (`MapToVoterProfiles`), delegates to `LlmVotingService.VoteWithProfilesAsync`. |
| `SettingsService` + `SettingsServiceVaultOverlay` | Persists to `%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json`; `GetKeyForProvider` implements the fixed credential precedence; `BuildAuthJson`. |
| `PsychometricProfileService` + `PsychometricNarrator` | Resolve a participant's Legion persona and render OCEAN/HEXACO/MBTI/Enneagram/DISC as a behavioral brief appended to the system prompt. |
| `AppearanceService` | 18-theme + control-height/gutter/border-radius state, applied via JS interop, persisted through `SettingsService`. |
| `ChatConversationsService` | Tab lifecycle over `ObservableCollection<ChatConversation>` — `NewId()`, `CreateConversation`, `SetActive`, `CloseConversation`. |
| `ChatLogService` | In-memory event log with a `Changed` event. |
| `HumanNameService` / `NameGeneratorService` | Random and AI-generated participant names (`GenerateFirstName`, falls back to `"Alex"` on empty/garbage output). |

## ThinkTank.Shared — pages & components

Razor class library (`Microsoft.NET.Sdk.Razor`, `EnableDefaultCssItems=false`), references
`ThinkTank.Core`, pulls in `Microsoft.AspNetCore.Components.Web`. Styling is hand-rolled
`wwwroot/app.css` plus vendored Bootstrap CSS (no component library like MudBlazor/Radzen);
`wwwroot/theme.js` applies the 18-theme CSS variables via JS interop.

| Page/component | Route | What it does |
| --- | --- | --- |
| `Home.razor` | `/` | Trivial landing page — links to "Open Think Tank" (`/thinktank`) and "Settings" (`/settings`). |
| `Chat.razor` | `/thinktank` | The centerpiece (~1,800 lines). See feature tour below. |
| `Settings.razor` | `/settings` | Three tabs: **Personas** (edit `ParticipantTemplate`s — name, provider, model override, custom auth JSON), **Defaults** (global max tokens/rounds/response-length, Claude-fallback toggle, a notice that keys are managed entirely via MindAttic Vault — no in-app key entry), **Appearance** (delegates to `SettingsAppearance`). |
| `SettingsAppearance.razor` | (embedded) | 18 themes + control-height/gutter/border-radius sliders. |
| `NotFound.razor` | (fallback) | 404 page. |
| `Shared/ConfirmationDialog.razor` | (embedded) | Reusable confirm/cancel dialog. |
| `Layout/MainLayout.razor`, `Layout/NavMenu.razor` | (layout) | App shell + nav. |

## Feature tour

Everything below is implemented in `Chat.razor`'s markup + `@code` block:

- **11 providers**, routed exclusively through `MindAttic.Legion` — Think Tank never calls a
  provider directly (`TT-LAW-1`).
- **Conversation tabs** — a tab strip supports multiple independent debates in parallel, with a
  right-click context menu for rename/close; tabs persist across restarts.
- **Personality system** — each seat is a markdown personality template; personas can instead be
  picked from the Legion persona library ("Add persona from Legion library" dialog, searchable by
  name/personality text) with a psychometric profile rendered into the system prompt.
- **Setup panel** — topic textarea, a "Random topic" button (calls an LLM to generate one),
  participant pill grid for template selection, Start button (requires ≥2 participants).
- **Live round loop** — `StartActive()` builds shared history from persisted messages, supports
  mid-session resume (detects an incomplete last round), loops up to `MaxRounds`, calling
  `ThinkTankService.CallProvider(...)` once per participant per round with the personality markdown
  + vote-request instruction, auth override, topic, shared history, response length, and persona id.
- **User injection** — pause the conversation, type a message in the bottom chat bar; the round
  loop resumes automatically afterward.
- **Vote-driven decisions** — a **Call Vote** button opens a dialog with three vote types
  (Consensus Yes/No, Free-form, Direction with comma-separated custom options); participants can
  also self-trigger a vote by emitting `[REQUEST_VOTE: question]`, detected and stripped from their
  visible response.
- **Live status UI** — member sidebar shows who's "speaking" with animated typing dots,
  per-conversation Max Tokens/Max Rounds/Response Length overrides, a "Claude only" fallback
  toggle, Export transcript, Pause/Resume, and a collapsible Status Log with three tabs
  (Perspective / Context / Diagnostics).
- **Chat title generation** — after round 1, each participant is asked for a title suggestion in
  the background, and one participant picks the best.
- **18 themes** — dark, light, spring, summer, autumn, winter, matrix, ice, sunset, neon, dracula,
  solarized, midnight, aurora, ember, ocean, forest, mono.
- **Perspective tracking** — per-participant markdown per conversation, visible in the status panel.
- **Fault surfacing** — `ObserveFault` wires fire-and-forget background tasks (title generation,
  auto-vote) so errors land in the Diagnostics panel instead of vanishing silently.

## Configuration

### Provider auth

```json
{ "type": "bearer", "apiKey": "sk-...", "model": "gpt-4o", "maxTokens": 2048 }
```

`type` values: `"bearer"` (OpenAI-compatible), `"anthropic"`, `"google"`.

### Cloud credentials

Keys resolve from `%APPDATA%\MindAttic\LLM\providers.json` (via `AddMindAtticVaultFiles`) or
`MindAttic:Vault:LLM:<providerId>:apiKey` in the environment / Azure App Service Application
Settings. Cloud-resolved keys are held in a runtime-only side map and are **never** written back to
`Settings.json` (`TT-LAW-2`). Resolution precedence (`TT-LAW-3`): explicit per-call override →
on-disk `apiKey` in `Settings.json` → Vault/cloud runtime override.

### Appearance

Settings → Appearance: theme (18 options), control height (28–60px), gutter (0–30px), border
radius (0–24px). All values are clamped.

## Voting

The **Call Vote** button polls every participant and injects the aggregated result into shared
history as a synthetic turn (`TT-LAW-4`):

```
[VOTE] Question: <question>
Decision: <consensus> (<percentage> agreement)
Summary: <narrativeSummary>
```

Vote types: **Consensus** (Yes/No), **Free-form** (open-ended), **Direction** (custom options).
Implemented in `VotingService` via `MindAttic.Legion.LLMVotingService`.

**Auto-vote:** every participant's system prompt ends with an instruction permitting them to emit
`[REQUEST_VOTE: question]` to trigger an immediate vote; the marker is stripped from the visible
response. See `Chat.razor` → `VoteRequestInstruction`. Auto-vote *after N rounds of no convergence*
(as opposed to a self-triggered marker) is not yet implemented — see
[docs/rfc/0001-auto-vote-after-n-rounds.md](docs/rfc/0001-auto-vote-after-n-rounds.md).

## Data persistence

Persistence must fully reconstruct a conversation after restart (`TT-LAW-5`):

```
%LOCALAPPDATA%\MindAttic\ThinkTank\
├── Settings.json          All app settings (provider auth, templates, conversations, appearance)
├── Personalities/
│   └── {templateId}.md
└── Conversations/
    └── {chatId}/
        ├── chat.json       Append-only event log (chat-start, turn, user)
        └── {participantId}.md
```

## Getting started

```powershell
dotnet restore
dotnet run --project ThinkTank.Blazor
# -> https://localhost:5001

# First launch: Settings > Providers -> enter API key(s), select models
# Then: Conversations -> enter topic, select participants, click Start
```

## Testing

`ThinkTank.UnitTests` uses **NUnit 4.4.0** + **bUnit 1.31.3** (component testing) on net10.0, with
`Microsoft.Extensions.Caching.Memory` pinned to 10.0.5 to override a vulnerable preview transitive
dependency (NU1903 / GHSA-qj66-m88j-hmgj).

```powershell
dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj
```

As of the last BIBLE sync (2026-06-07): **293 passed, 0 failed, 0 skipped** (~2s). See
[docs/BIBLE.md §6](docs/BIBLE.md#TT-§6) for exactly what is and isn't test-proven.

21 test files, roughly grouped:

| Area | Files |
| --- | --- |
| Services | `AppearanceServiceTests`, `ChatConversationsServiceTests`, `ChatLogServiceTests`, `HumanNameServiceTests`, `NameGeneratorServiceTests`, `SettingsServiceTests` (largest, 40 `[Test]`s), `SettingsServiceVaultOverlayTests`, `ThinkTankServiceTests`, `VotingServiceTests` |
| Models / parsing | `ModelTests`, `ProviderAuthConfigParsingTests`, `VoteMarkerTests` |
| Persistence | `ChatStorageTests` |
| Psychometrics | `PsychometricsTests` |
| bUnit components | `ConfirmationDialogComponentTests`, `HomePageComponentTests`, `NavMenuComponentTests`, `NotFoundPageComponentTests`, `SettingsAppearanceComponentTests` |
| Security | `Security/NoSecretsCommittedTests` (guard test currently **disabled** — see `AMENDMENTS.md#TT-A4`) |
| Setup | `TestAssemblySetup` |

## Cypress e2e coverage

```powershell
# Blazor app must be running on http://localhost:5100
dotnet run --project ThinkTank.Blazor --urls http://localhost:5100

npm install
npx cypress run    # headless
npx cypress open   # interactive
```

`cypress.config.js`: `baseUrl http://localhost:5100` (override with `CYPRESS_BASE_URL`),
`specPattern cypress/e2e/**/*.cy.js`, no support file, screenshots on failure, videos off,
15s command timeout, 60s page-load/response timeout, 1600×900 viewport, `chromeWebSecurity: false`.

Four specs in `cypress/e2e/`:

| Spec | Coverage |
| --- | --- |
| `navigation.cy.js` | Basic routing between pages. |
| `settings.cy.js` | Settings page tabs/behavior. |
| `chat.cy.js` | Tab-strip rendering, "+ New" tab creation, right-click rename/close context menu. Deliberately does not intercept live LLM dispatch (that happens server-side via Legion) — left to the C# unit suite. |
| `vote-dialog.cy.js` | Setup-panel participant pills + Start button, pill `aria-pressed` toggling, opening the Call Vote dialog (asserts all 3 vote-type radios present, default text matches Consensus), switching to Direction reveals the custom-options input, Cancel closes the dialog. Deliberately does not submit an actual vote (would trigger real LLM calls). |

Both `chat.cy.js` and `vote-dialog.cy.js` use a `blazorClick` helper that dispatches a raw
`MouseEvent` at the document level to bypass Cypress actionability checks against Blazor Server's
`@onclick`-bound elements, and wait ~2500ms after `cy.visit` for the SignalR circuit to hydrate.

These flows are UI-only — not yet proven by the unit suite (see
[docs/USER_STORIES.md](docs/USER_STORIES.md) priority backlog: graduating the round loop and vote
dialog from Cypress-only to unit-proven is an open item).

## Supported providers

All calls route through `MindAttic.Legion.LegionClient`. Default models:

| Provider | Default model |
| --- | --- |
| Claude (Anthropic) | `claude-sonnet-4-6` |
| ChatGPT (OpenAI) | `gpt-4.1-mini` |
| Gemini (Google) | `gemini-2.5-flash` |
| DeepSeek | `deepseek-chat` |
| Mistral | `mistral-large-latest` |
| Grok (xAI) | `grok-3-mini-fast` |
| Groq | `llama-3.3-70b-versatile` |
| Together AI | `meta-llama/Llama-3-70b-chat-hf` |
| OpenRouter | `meta-llama/llama-3.1-8b-instruct:free` |
| Fireworks | `accounts/fireworks/models/llama-v3p1-70b-instruct` |
| Cohere | `command-r-plus` |

## Other files at the repo root

- **`index.htm`** — a static, self-contained MindAttic.com marketing/landing page (synced by
  `MindAttic.UiUx/sync/sync-landing-page.ps1` tooling — not hand-edited). Embeds a base64 "Outfit"
  variable font. This file is out of scope for this README to modify; see the stale-content note
  below.
- **`package.json`** (`thinktank-e2e`) — Cypress scripts (`e2e`, `e2e:open`, `e2e:nav`) plus
  landing-page `build`/`deploy` scripts (`node scripts/cli/build-html.js`,
  `powershell ... scripts/cli/deploy.ps1`). Dependencies `highlight.js`/`marked` support that
  build/deploy tooling, not the app itself.
- **`scripts/cli/`** — only `deploy.settings.json` is tracked in git; `build-html.js` and
  `deploy.ps1` referenced by `package.json` exist locally but are untracked/gitignored.
- **`tools/codex.ps1`** — Codex `digest`/`doctor` tooling for `docs/`.
- **`tools/build-readme.ps1`** — thin wrapper that regenerates `README.htm` from this file via the
  shared `codex-standard/build-readme.ps1` engine (same engine every MindAttic repo uses).

## Known gaps / stale notes

- `index.htm`'s meta description currently reads *".NET 10 MAUI + Blazor desktop app for
  multi-LLM conversations across 11 providers"* — the MAUI shell was retired
  (`docs/AMENDMENTS.md#TT-A1`); the product is Blazor Server web-only. This is a landing-page sync
  lag, not a fact about the current codebase.
- `CLAUDE.md` still contains a large "Planned Feature: LLMVoting Integration" section describing
  the original design for voting. That feature has since **shipped** (Epic C —
  `docs/AMENDMENTS.md#TT-A3`); treat that section as historical design notes, not current backlog.
- The no-secrets-committed guard test (`Security/NoSecretsCommittedTests.cs`) is present but
  commented out; `TT-US-E3` is downgraded to 🟡 (`docs/AMENDMENTS.md#TT-A4`).
- Auto-vote after N rounds of stalemate is design-only (RFC 0001), not implemented.
