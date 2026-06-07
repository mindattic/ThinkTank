---
codex: 1
project: Think Tank
code: TT
layer: bible
status: living
updated: 2026-06-07
---

# Think Tank — Project Bible
> Single source of truth for what Think Tank IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/run; this says how to think about the system.

## 1. The one sentence {#TT-§1}
Think Tank convenes the world's frontier LLMs as a panel of advisors that debate, refine, and
vote on a topic in a single browser-native roundtable — every model call routed through
MindAttic.Legion, no vendor lock-in.

## 2. The product promise {#TT-§2}
- **Multi-provider out of the box.** 11 providers (OpenAI, Anthropic, Google, DeepSeek, Mistral,
  xAI, Groq, Together AI, OpenRouter, Fireworks, Cohere), one UI, zero glue code. Think Tank
  never holds an HTTP endpoint — dispatch leaves through [TT-LAW-1](#TT-LAW-1).
- **Personalities, not prompts.** Each participant is a markdown persona (optionally backed by a
  Legion library persona with a psychometric profile), assignable per seat, AI-generatable.
- **Vote-driven decisions.** A `Call Vote` breaks circular arguments; participants can self-call a
  vote mid-response via `[REQUEST_VOTE: question]`. Voting runs through Legion's `LLMVotingService`.
- **Cloud-credential ready.** Keys can live in Azure App Service Application Settings / Key Vault
  and never touch disk (see [TT-LAW-2](#TT-LAW-2)).
- **Browser-native.** ASP.NET Core Blazor Server on .NET 10, SignalR transport. No installer, no
  native binaries, no telemetry. Run multiple debates in parallel tabs.

## 3. What it is NOT {#TT-§3}
- **NOT a single-model chat client.** The unit of work is a *panel*, not a 1:1 conversation.
- **NOT a provider SDK or HTTP client.** All transport (URLs, auth headers, retry, timeout) lives
  in MindAttic.Legion's `LlmProviderCatalog`; Think Tank assembles prompts + history only.
- **NOT a credential store.** Cloud-resolved keys live in a runtime-only side map and are never
  written back to `Settings.json` or the shared `%APPDATA%\MindAttic\LLM\providers.json`.
- **NOT a multi-user/auth product (yet).** It is single-host shared global state; there are no
  accounts. MindAttic.Authentication ([HOUSE-LAW-7]) is not adopted.
- **NOT a desktop/MAUI app anymore.** The MAUI shell was retired; the host is the Blazor Server
  web app only (see [TT-A1](AMENDMENTS.md#TT-A1)).

## 4. Architecture canon {#TT-§4}

```
+--------------------------------------------------------------+
|                         Browser                              |
|   Blazor Server components (Home / Chat / Settings / ...)    |
+------------------------------|-------------------------------+
                               | SignalR (interactive server)
+------------------------------v-------------------------------+
|                ThinkTank.Blazor (ASP.NET Core host)          |
|   Program.cs wires DI: Legion, Vault, services, voting       |
+------------------------------|-------------------------------+
        +----------------------+----------------------+
        v                      v                      v
+---------------+    +-------------------+    +----------------+
| ThinkTank.    |    | ThinkTank.Core    |    | ThinkTank.     |
|   Shared      |    |  (services +      |    |   Blazor       |
| (Razor lib +  |    |   models)         |    |   (host)       |
|   wwwroot)    |    +---------+---------+    +----------------+
+---------------+              |
                               v
                  +--------------------------+
                  |    MindAttic.Legion      |
                  |  LegionClient +          |
                  |  LLMVotingService +      |
                  |  LlmProviderCatalog +    |
                  |  PersonaStore            |
                  +-------------+------------+
                                |
            +-------------------+-------------------+
            v                   v                   v
        OpenAI            Anthropic            ... 9 more
       (ChatGPT)            (Claude)             providers
```

All services are registered as **singletons** in `ThinkTank.Blazor/Program.cs` for shared global
state across user circuits ([HOUSE-LAW-6] — one engine, one DI graph).

### 4.1 Projects {#TT-§4.1}
| Project | SDK / target | Role |
|---------|--------------|------|
| `ThinkTank.Core` | classlib, net10.0 | Services + models; references MindAttic.Legion + MindAttic.Vault. No UI. |
| `ThinkTank.Shared` | Razor SDK, net10.0 | Razor component library + `wwwroot` (CSS, 18 themes, theme.js, vendored Bootstrap). |
| `ThinkTank.Blazor` | Web SDK, net10.0 | ASP.NET Core host. `Program.cs` is the single composition root. |
| `ThinkTank.UnitTests` | NUnit + bUnit, net10.0 | Service, model, component, security, and psychometric tests. |

### 4.2 Domain model — the NOUNS {#TT-§4.2}
Defined in `ThinkTank.Core/Models/`.
- **`ParticipantTemplate`** (`ChatModels.cs`) — reusable seat definition: `ProviderId`,
  `DisplayName`, `PersonalityMarkdown`, optional `AuthOverrideJson`, `IsDefault`, optional
  `PersonaId` (Legion `Persona.Id` for psychometric lookup).
- **`ChatParticipant`** (`ChatModels.cs`) — a template instantiated into one conversation with a
  unique `ParticipantId`; exposes `EffectivePersonaId` (explicit `PersonaId`, else the
  `legion-{id}` `TemplateId` convention).
- **`ChatConversation`** (`ChatModels.cs`) — one tab: `ChatId`, `Title`, observable
  `Participants`, `Topic`, per-conversation `MaxTokens`/`MaxRounds`/`ResponseLength`, `Messages`,
  `StatusEvents`, `Diagnostics`.
- **`SharedTurn` / `ConversationMessage` / `LlmModel`** (`LlmModels.cs`) — the turn log a
  participant sees, a rendered message, and a catalog model entry.
- **`ProviderAuthConfig`** (`ProviderAuthConfig.cs`) — `(ProviderId, Json)` auth blob.
- **Persistence DTOs** (`PersistenceModels.cs`) — `PersistedConversation`, `PersistedParticipant`,
  `PersistedMessage`, `PersistedTurn`, `PersistedStatusEvent`.
- **`ChatLogEntry`** (`ChatLogModels.cs`), **`AppearanceMode`** (`AppearanceMode.cs`),
  **`ResponseLengthPreset`** (`ResponseLengthPreset.cs`).

### 4.3 Key services — the VERBS {#TT-§4.3}
Defined in `ThinkTank.Core/Services/`.
- **`ThinkTankService`** — orchestration engine. Builds per-participant history, applies the
  personality markdown as the system prompt, trims to `MaxContextTurns`, sanitizes self-reference
  prefixes, emits redacted `Diagnostics`. Every model call goes via `LegionClient`.
- **`VotingService`** — maps `ChatParticipant[]` → Legion `VoterProfile[]`
  (`MapToVoterProfiles`), delegates to `LlmVotingService.VoteWithProfilesAsync`.
- **`SettingsService`** + **`SettingsServiceVaultOverlay`** — persistence to
  `%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json` plus the Vault runtime-overlay credential
  resolution (`GetKeyForProvider` precedence).
- **`PsychometricProfileService`** + **`PsychometricNarrator`** — resolve a participant's Legion
  persona profile and render it as a behavioral brief (OCEAN/HEXACO/MBTI/Enneagram/DISC) appended
  to the system prompt.
- **`AppearanceService`** — 18-theme + control-height/gutter/border-radius state, applied via JS
  interop, persisted via `SettingsService`.
- **`ChatConversationsService`** — tab lifecycle over `ObservableCollection<ChatConversation>`.
- **`ChatLogService`** — in-memory event log with a `Changed` event.
- **`HumanNameService`** / **`NameGeneratorService`** — random + AI-generated participant names.

## 5. The Laws {#TT-§5}
Think Tank **inherits all org-wide laws** from `../MindAttic.HouseRules.md` by reference — do not
restate them here. Most load-bearing for this repo: `[HOUSE-LAW-3]` (Vault credentials),
`[HOUSE-LAW-4]` (Legion provider-agnostic), `[HOUSE-LAW-6]` (one DI graph), `[HOUSE-LAW-8]`
(verified-done). Project-specific laws below.

### {#TT-LAW-1} TT-LAW-1 — Every provider call leaves through Legion
No code path in Think Tank holds a provider HTTP endpoint, SDK, or auth header. All dispatch goes
through `MindAttic.Legion.LegionClient`; the provider catalog, retry, and transport are Legion's.
This is the project-level realization of `[HOUSE-LAW-4]`. *(Guarded by `CallProvider_UnknownProvider_ThrowsArgumentException`, `GenerateFirstName_RoutesThroughLegion_NotDirectly`.)*

### {#TT-LAW-2} TT-LAW-2 — Cloud keys are runtime-only and never persisted
A resolved Vault/cloud `apiKey` lives in the runtime side map `RuntimeApiKeyOverrides` and is
**never** written to `Settings.json` or the shared `%APPDATA%\MindAttic\LLM\providers.json`. The
disk projection may be entirely empty in a cloud deployment. Realizes `[HOUSE-LAW-3]`.
*(Guarded by `Save_AfterOverlay_DoesNotPersistRuntimeKey`, `Construction_DoesNotWriteCredentialsToSharedStore`, `SetAuthJson_StripsApiKey_PreservesModelAndMaxTokens`.)*

### {#TT-LAW-3} TT-LAW-3 — Credential resolution has a fixed precedence
`GetKeyForProvider` resolves in order: (1) explicit per-call override (e.g. participant
`AuthOverrideJson`), (2) non-empty on-disk `apiKey` in `Settings.json`, (3) the Vault/cloud
runtime override. A higher tier always wins. *(Guarded by `GetKeyForProvider_ExplicitOverride_WinsOverEverything` and the `GetKeyForProvider_*` family.)*

### {#TT-LAW-4} TT-LAW-4 — A vote is a synthetic shared turn, not a side channel
A vote result is injected back into the conversation as one `SharedTurn`
(`[VOTE] Question: … / Decision: … / Summary: …`) so subsequent participants see the decision
identically to any other turn. Auto-votes are triggered by the `[REQUEST_VOTE: question]` marker,
which is stripped from the visible response. *(Guarded by the `VoteMarkerTests` family and
`MapToVoterProfiles_*`.)*

### {#TT-LAW-5} TT-LAW-5 — Persistence must fully reconstruct a conversation
Everything needed to recreate a conversation after restart is persisted: tabs/participants in
`Settings.json`, the append-only turn log in `Conversations/<chatId>/chat.json`, and per-participant
perspective markdown. Loading degrades gracefully on missing files/fields. *(Guarded by the
`LoadTurnsAsync_*` and `ChatStorage` families.)*

### {#TT-LAW-6} TT-LAW-6 — Diagnostics and committed files are secret-free
API responses surfaced in the Diagnostics panel are redacted, and no real-looking provider key is
ever committed to the repo. *(Design law; the guard test `ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles`
exists in `ThinkTank.UnitTests/Security/NoSecretsCommittedTests.cs` but is currently commented out
— see [TT-A4](AMENDMENTS.md#TT-A4). Enforced by code review and `.gitignore`/`Settings.json`
placement policy.)*

## 6. Verified state {#TT-§6}
**Build:** `dotnet build` / `dotnet test` on .NET 10 SDK `10.0.300` — clean.
**Tests (verified 2026-06-07):** `dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj` →
**Passed: 293, Failed: 0, Skipped: 0** (duration ~2 s). This is the evidence behind every ✅ in
[USER_STORIES.md](USER_STORIES.md).

Proven working (test-backed): multi-provider dispatch routing through Legion; provider-prefix
sanitization; history trimming; the Vault credential overlay + precedence; `ChatParticipant`→
`VoterProfile` mapping; `[REQUEST_VOTE:]` marker parsing/stripping; conversation persistence +
turn replay; the 18-theme appearance service with clamping; psychometric profile rendering;
Razor component rendering (Home, NavMenu, NotFound, ConfirmationDialog, SettingsAppearance).

Not yet test-proven (UI-only / e2e): the live round loop, user chat injection, title generation,
and provider connectivity polling are exercised by Cypress specs (`navigation`, `settings`,
`chat`, `vote-dialog`) which require a running dev server and are not part of the unit run — see
[USER_STORIES.md](USER_STORIES.md) priority backlog.

**Note (2026-06-07 sync):** The no-secrets-committed guard test
(`ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles`) is present in the test file
but commented out; TT-US-E3 is therefore downgraded to 🟡. See [TT-A4](AMENDMENTS.md#TT-A4).

## 7. Active frontier {#TT-§7}
- **RFC [0001](rfc/0001-auto-vote-after-n-rounds.md)** — auto-vote after N rounds of no
  convergence (manual + marker-triggered voting already ship).
- **Epics** (see [USER_STORIES.md](USER_STORIES.md)): A Roundtable · B Personalities · C Voting ·
  D Persistence · E Credentials · F Appearance. Backlog headline: graduate the Cypress e2e flows
  into verified ✅ and land auto-vote.

## 8. Quality bar {#TT-§8}
A feature is **done** only when (per `[HOUSE-LAW-8]`): the solution builds clean; the NUnit/bUnit
suite is green; the change has at least one test that names the behavior; anything user-facing has
a bUnit component test or a Cypress e2e assertion; no secret is committed; and any LLM call still
routes through Legion ([TT-LAW-1](#TT-LAW-1)). Docs mark `✅` only with that evidence; otherwise
`🟡`/`⬜`.

## 9. Glossary {#TT-§9}
- **Participant** — one AI seat at the roundtable (`ChatParticipant`), instantiated from a
  `ParticipantTemplate`.
- **Persona / personality** — the markdown system prompt for a seat; optionally backed by a Legion
  library **Persona** with a **psychometric profile**.
- **Psychometric profile** — OCEAN/HEXACO/MBTI/Enneagram/DISC scores from Legion's `PersonaStore`,
  rendered to prose by `PsychometricNarrator`.
- **Round** — one pass in which every participant speaks once.
- **Shared turn** — one entry in the conversation log (`SharedTurn`) visible to all participants;
  a vote result is a synthetic shared turn.
- **Vote** — a poll of every participant via Legion's `LlmVotingService`; types: consensus,
  free-form, direction. Self-triggered by `[REQUEST_VOTE: question]`.
- **Legion** — `MindAttic.Legion`, the provider-agnostic LLM dispatch + voting + persona library.
- **Vault** — `MindAttic.Vault`, cloud-native credential resolution from `IConfiguration`.
- **Vault overlay** — the runtime-only credential side map; never persisted ([TT-LAW-2](#TT-LAW-2)).
