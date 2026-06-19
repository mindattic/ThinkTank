# Think Tank

**Stop arguing with one AI. Convene a roundtable.**

Think Tank turns frontier LLMs into a panel of advisors that debate, refine, and decide together. Pick participants from **11 providers** (OpenAI, Anthropic, Google, DeepSeek, Mistral, xAI, Groq, Together AI, OpenRouter, Fireworks, Cohere), assign each a personality, drop a topic into the room, and watch them think out loud. Inject your own messages mid-discussion. Call a vote when they go in circles. Run multiple debates in parallel tabs.

---

## Stack

- .NET 10, ASP.NET Core Blazor Server (SignalR transport)
- [MindAttic.Legion](../MindAttic.Legion/) — unified multi-provider LLM dispatch and voting
- [MindAttic.Vault](../MindAttic.Vault/) — cloud-native credential resolution
- NUnit + bUnit (unit/component tests), Cypress (end-to-end)

---

## Project structure

```
ThinkTank/
├── ThinkTank.slnx
├── ThinkTank.Blazor/      ASP.NET Core host — DI registration (Legion, Vault, services)
├── ThinkTank.Core/        Services + models (no UI)
│   └── Services/          ThinkTankService, VotingService, SettingsService, AppearanceService,
│                          ChatConversationsService, ChatLogService, HumanNameService, NameGeneratorService
├── ThinkTank.Shared/      Razor class library — all pages and components
│   └── Components/Pages/  Home, Chat, Settings, SettingsAppearance, NotFound
├── ThinkTank.UnitTests/   NUnit + bUnit
└── cypress/               Cypress e2e specs (chat, navigation, settings, vote-dialog)
```

---

## Getting started

```powershell
dotnet restore
dotnet run --project ThinkTank.Blazor
# -> https://localhost:5001

# First launch: Settings > Providers -> enter API key(s), select models
# Then: Conversations -> enter topic, select participants, click Start
```

---

## Features

- **11 providers** routed through `MindAttic.Legion` — Think Tank never calls a provider directly
- **Conversation tabs** — multiple independent debates in parallel, persist across restarts
- **Personality system** — markdown templates per participant; AI-generated personas; per-template auth override
- **User injection** — pause the conversation, type a message, conversation resumes automatically
- **18 themes** — dark, light, spring, summer, autumn, winter, matrix, ice, sunset, neon, dracula, solarized, midnight, aurora, ember, ocean, forest, mono
- **Vote-driven decisions** — consensus / free-form / direction vote; auto-vote when a participant emits `[REQUEST_VOTE: question]`
- **Perspective tracking** — per-participant markdown per conversation, visible in the status panel

---

## Configuration

### Provider auth

```json
{ "type": "bearer", "apiKey": "sk-...", "model": "gpt-4o", "maxTokens": 2048 }
```

`type` values: `"bearer"` (OpenAI-compatible), `"anthropic"`, `"google"`.

### Cloud credentials

Keys in `%APPDATA%\MindAttic\LLM\providers.json` (via `AddMindAtticVaultFiles`) or `MindAttic:Vault:LLM:<providerId>:apiKey` in environment / Azure App Service Application Settings. Cloud-resolved keys are never written back to `Settings.json`.

### Appearance

Settings > Appearance: theme, control height (28–60px), gutter (0–30px), border radius (0–24px).

---

## Voting

The **Call Vote** button polls every participant and injects the aggregated result into shared history as:

```
[VOTE] Question: <question>
Decision: <consensus> (<percentage> agreement)
Summary: <narrativeSummary>
```

Vote types: **Consensus** (Yes/No), **Free-form** (open-ended), **Direction** (custom options). Implemented in `VotingService` via `MindAttic.Legion.LLMVotingService`.

**Auto-vote:** Every participant's system prompt ends with an instruction allowing them to emit `[REQUEST_VOTE: question]` to trigger an immediate vote. The marker is stripped from the visible response. See `Chat.razor` → `VoteRequestInstruction`.

---

## Data persistence

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

---

## Tests

```powershell
# Unit / component tests
dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj

# End-to-end (Blazor app must be running on http://localhost:5100)
dotnet run --project ThinkTank.Blazor --urls http://localhost:5100

npm install
npx cypress run    # headless
npx cypress open   # interactive
```

Cypress specs: `navigation`, `settings`, `chat`, `vote-dialog`. Override base URL with `CYPRESS_BASE_URL`.

---

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
