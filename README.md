# Think Tank

**Stop arguing with one AI. Convene a roundtable.**

Think Tank turns the world's frontier LLMs into a panel of advisors that debate, refine, and decide — together. Pick your participants from **11 providers** (OpenAI, Anthropic, Google, DeepSeek, Mistral, xAI, Groq, Together AI, OpenRouter, Fireworks, and Cohere), assign each one a personality, drop a topic into the room, and watch them think out loud. Inject your own messages mid-discussion to steer the conversation. Call a vote when they go in circles. Run multiple debates in parallel tabs.

Whether you're stress-testing a product decision, exploring ethical edge cases, drafting strategy, or just curious whether Claude and ChatGPT actually disagree — Think Tank gives you the room, the seats, and the gavel.

**Why it's different:**

- **Multi-provider out of the box** — no vendor lock-in, no glue code. One UI, 11 backends, every call routed through MindAttic.Legion.
- **Personalities, not prompts** — markdown templates per participant, with AI-generated personas if you don't want to write your own.
- **Vote-driven decisions** — break stalemates with a single click, or let participants call their own votes when they detect deadlock.
- **Cloud-credential ready** — keys can live in Azure App Service Application Settings or Key Vault and never touch disk.
- **Browser-native** — ASP.NET Core Blazor Server on .NET 10. No installer, no native binaries, no telemetry. Open it in a tab, share it on the LAN, or deploy it to Azure.

---

A .NET 10 ASP.NET Core Blazor Server application that orchestrates multi-participant AI discussions across every major LLM provider. Create conversation panels where ChatGPT, Claude, Gemini, and DeepSeek debate topics with customizable personalities — and inject your own messages to steer the conversation in real time.

---

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
  - [Provider Auth](#provider-auth)
  - [Cloud-Native Credentials (MindAttic.Vault)](#cloud-native-credentials-mindatticvault)
  - [Personalities](#personalities)
  - [Appearance](#appearance)
  - [Voting](#voting)
- [How It Works](#how-it-works)
  - [Conversation Flow](#conversation-flow)
  - [User Chat Injection](#user-chat-injection)
  - [Title Generation](#title-generation)
- [Project Structure](#project-structure)
- [Services](#services)
- [Theming](#theming)
- [Data Persistence](#data-persistence)
- [JavaScript Interop](#javascript-interop)
- [Supported Providers](#supported-providers)
- [Security](#security)

---

## Overview

Think Tank lets you pit multiple AI models against each other in structured conversations. Each participant has a customizable personality, and the app manages turn-taking, history sharing, and conversation persistence automatically. You can run multiple conversations in parallel using tabs, watch the discussion unfold, and jump in with your own messages at any time.

**Built with:**

- .NET 10 (ASP.NET Core Blazor Server, SignalR transport)
- [MindAttic.Legion](../MindAttic.Legion/) for unified multi-provider LLM dispatch and voting
- [MindAttic.Vault](../MindAttic.Vault/) for cloud-native credential resolution
- Bootstrap (vendored in `ThinkTank.Shared/wwwroot/lib/`)
- NUnit + bUnit for unit/component tests; Cypress for end-to-end UI tests

---

## Features

### Multi-Provider AI Conversations

- **11 providers**: OpenAI, Anthropic, Google, DeepSeek, Mistral, xAI, Groq, Together AI, OpenRouter, Fireworks, and Cohere
- Every participant call is routed through MindAttic.Legion — Think Tank itself never talks to a provider directly
- Configurable model and max tokens per provider; per-participant overrides supported

### Conversation Tabs

- Run multiple independent conversations simultaneously
- Each tab has its own topic, participants, and message history
- Typing indicator (animated dots) on tabs with active conversations
- Right-click context menu for rename and close
- Conversations persist across server restarts

### Personality System

- Markdown-based personality definitions per participant
- Built-in default templates for every supported provider
- Create unlimited custom personalities
- AI-powered personality generation — describe what you want and the LLM writes it
- Per-template auth override (use different API keys or models per participant)

### User Chat Injection

- Click the message input to pause the conversation
- Type a message and press Enter or click Send
- Your message is injected into the shared history for the next round
- Conversation automatically resumes after sending

### Random Topic Generation

- Use any configured provider to generate a discussion topic
- Dropdown to select which AI generates the topic

### Theme System

- **18 themes**: dark, light, spring, summer, autumn, winter, matrix, ice, sunset, neon, dracula, solarized, midnight, aurora, ember, ocean, forest, mono
- Customizable control height (28-60px), gutter spacing (0-30px), and border radius (0-24px)
- All visual properties driven by CSS variables, updated via JS interop

### Provider Connectivity

- Real-time online/offline status indicators per provider
- Built-in connectivity test sends a test message to verify API access
- Debounced polling (10-second intervals)

### Vote-Driven Decisions

- **Call Vote** button polls every participant on a question to break circular arguments
- Three vote types: consensus (Yes/No), free-form conclusion, or pick-a-direction (custom options)
- Result is injected back into shared history so subsequent turns see the decision
- Participants can self-trigger an auto-vote mid-response by emitting `[REQUEST_VOTE: question]`
- Voting itself is delegated to MindAttic.Legion's `LLMVotingService`

### Perspective Tracking

- Each participant generates a markdown perspective file per conversation
- Tracks personality, latest response, and conversation context
- Viewable in the status panel's Context tab

### Diagnostics

- Raw API response logging (redacted for security)
- Status events for conversation milestones
- Three-tab status panel: Perspective, Context, Diagnostics

---

## Architecture

```
+--------------------------------------------------------------+
|                         Browser                              |
|   Blazor Server components (Home / Chat / Settings / ...)    |
+------------------------------|-------------------------------+
                               | SignalR (interactive server)
+------------------------------v-------------------------------+
|                ThinkTank.Blazor (ASP.NET Core)               |
|   Program.cs wires DI: Legion, Vault, services, voting       |
+------------------------------|-------------------------------+
                               |
        +----------------------+----------------------+
        v                      v                      v
+---------------+    +-------------------+    +----------------+
| ThinkTank.    |    | ThinkTank.Core    |    | ThinkTank.     |
|   Shared      |    |   (services +     |    |   Blazor       |
| (Razor lib +  |    |    models)        |    |   (host)       |
|   wwwroot)    |    +---------+---------+    +----------------+
+---------------+              |
                               v
                  +--------------------------+
                  |    MindAttic.Legion      |
                  |   (LegionClient +        |
                  |    LLMVotingService)     |
                  +-------------+------------+
                                |
            +-------------------+-------------------+
            v                   v                   v
        OpenAI            Anthropic            ... 9 more
       (ChatGPT)            (Claude)             providers
```

All services are registered as singletons in `ThinkTank.Blazor/Program.cs` for shared global state across user circuits.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An API key for at least one supported provider
- For local development, MindAttic.Legion and MindAttic.Vault must be available either as NuGet packages on `C:\LocalNuGet` (default) or installed from the public feed

### Build & Run

```bash
# Restore dependencies
dotnet restore

# Run the Blazor Server app (default URL: https://localhost:5001)
dotnet run --project ThinkTank.Blazor

# Or specify a port
dotnet run --project ThinkTank.Blazor --urls http://localhost:5100
```

Open the URL in any modern browser. The app uses SignalR for interactive components, so a stable connection is required (it reconnects automatically on transient drops).

### First Launch

1. Navigate to **Settings > Providers**
2. Enter your API key(s) in the JSON editor for each provider
3. Click model chips to set your preferred model
4. Go to **Conversations** (Think Tank)
5. Enter a topic, select participants, and click **Start**

### Testing

The project ships with two complementary test suites.

**Unit / component tests** — NUnit + bUnit covering services, model parsing, Razor component rendering, the cloud-credential overlay, voting marker regexes, and the `ChatParticipant`→`VoterProfile` mapping:

```bash
dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj
```

**End-to-end UI tests** — Cypress, 4 specs (`navigation`, `settings`, `chat`, `vote-dialog`) covering top-level affordances. Run a dev server in one terminal, then the suite in another:

```bash
# Terminal 1 — start the Blazor app on http://localhost:5100
dotnet run --project ThinkTank.Blazor --urls http://localhost:5100

# Terminal 2 — install once, then run the suite
npm install
npx cypress run

# Or open the interactive runner
npx cypress open
```

Override `CYPRESS_BASE_URL` if the server is on a different host/port. The suite assumes a freshly seeded settings store and does not fire live LLM calls — LLM dispatch through MindAttic.Legion is covered by the unit suite.

---

## Configuration

### Provider Auth

Each provider's configuration is stored as a JSON object:

```json
{
  "type": "bearer",
  "apiKey": "sk-your-key-here",
  "model": "gpt-4o",
  "maxTokens": 2048
}
```

| Field | Description |
|-------|-------------|
| `type` | Auth type: `"bearer"` (OpenAI-compatible), `"anthropic"` (Claude), or `"google"` (Gemini) |
| `apiKey` | Your API key for the provider |
| `model` | Model identifier to use for API calls |
| `maxTokens` | Maximum output tokens per response (default: 2048) |

Models surfaced as selectable chips in Settings are sourced from `MindAttic.Legion`'s `LlmProviderCatalog`. As Legion's catalog grows, the Think Tank UI picks up new providers and models automatically.

### Cloud-Native Credentials (MindAttic.Vault)

For deployments where API keys live outside `Settings.json` — Azure App Service Application Settings, Key Vault references, or shared dev user-secrets — Think Tank reads each provider's `apiKey` from `IConfiguration` at the path:

```
MindAttic:Vault:LLM:<providerId>:apiKey
```

Configuration sources are layered in `ThinkTank.Blazor/Program.cs` (later wins): `appsettings.json` → `%APPDATA%\MindAttic\LLM\providers.json` (via `AddMindAtticVaultFiles`) → project user-secrets → shared user-secrets (`mindattic-vault-shared`) → environment variables (App Service Application Settings + Key Vault refs).

**Precedence within `GetKeyForProvider`:**

1. Explicit per-call override (e.g. a participant's `AuthOverrideJson`).
2. The on-disk `apiKey` in `Settings.json` if non-empty — explicit local edits win.
3. The runtime override resolved from any of the configuration sources above.

Cloud-resolved keys live in a runtime-only side map (`RuntimeApiKeyOverrides`) and are **never** written back to `Settings.json` or the shared `%APPDATA%\MindAttic\LLM\providers.json` store. A cloud deployment can therefore run with a completely empty disk projection.

### Personalities

Personalities are markdown templates that define how each AI participant behaves in conversation. The personality markdown is sent as the system prompt to the provider's API.

- **Default templates**: one per supported provider/runtime (built-in, cannot be deleted)
- **Custom templates**: Create via Settings > Personalities > "+ Add"
- **Model override**: Custom templates can pin a persona to a specific model
- **Generate**: Click "Generate" to have the AI write a personality for you
- **Auth Override**: Each template can optionally override the provider's default auth config (different API key, model, or maxTokens)

### Appearance

All visual customization is in **Settings > Appearance**:

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| Theme | 18 options | dark | Color scheme for the entire app |
| Control Height | 28-60px | 40px | Height of buttons, inputs, and interactive elements |
| Gutter | 0-30px | 10px | Spacing between UI elements |
| Border Radius | 0-24px | 10px | Roundness of corners |

### Voting

The **Call Vote** button (visible in the conversation header once a tab has 2+ participants and the conversation has started) polls every participant on a question and injects the aggregated decision back into the shared history so subsequent turns see it. The vote itself runs through `MindAttic.Legion.LLMVotingService`.

**Vote types:**

| Type | Question | Options | Quorum |
|------|----------|---------|--------|
| Consensus | "Have we reached consensus?" | Yes / No | Simple majority |
| Free-form | "What is our conclusion?" | (open-ended) | Plurality |
| Direction | "What direction next?" | Comma-separated user-supplied options | Simple majority |

The result lands in the conversation as a synthetic turn formatted:

```
[VOTE] Question: <question>
Decision: <consensus> (<percentage> agreement)
Summary: <narrativeSummary>
```

**Auto-vote:** the system prompt for every participant ends with an instruction telling them they may emit `[REQUEST_VOTE: question]` anywhere in their response if they detect a stalemate. The marker is stripped from the visible response and triggers an immediate consensus vote on the requested question. Implementation lives in `Chat.razor` (search for `VoteRequestInstruction` and `RunParticipantVoteAsync`); regex coverage is in `VoteMarkerTests.cs`.

The mapping from `ChatParticipant` to Legion's `VoterProfile` (preserving each participant's persona and per-participant API/model overrides) is implemented in `VotingService.MapToVoterProfiles`.

---

## How It Works

### Conversation Flow

```
User enters topic + selects participants + clicks Start
         |
         v
    +---------+
    | Round N  |<------------------------------+
    +----+-----+                               |
         |                                     |
         v                                     |
   +-----------+    yes    +----------+        |
   |User typing?+--------->|  Wait... |        |
   +-----+-----+          +----+-----+        |
     no  |                      | (user sends  |
         |                      |  or clears)  |
         |<---------------------+              |
         v                                     |
   For each participant:                       |
    +-- Set as current speaker (typing dots)   |
    +-- Call LegionClient.SendAsync(...)        |
    +-- Add response to messages               |
    +-- Save to chat.json + perspective.md     |
    +-- Wait 400ms                             |
         |                                     |
         v                                     |
   +-------------+   no                        |
   |Stop pressed? +--------------------------->/
   +------+------+
      yes |
          v
     Conversation ends
```

### User Chat Injection

1. Focus the message input field at the bottom of the chat
2. The conversation pauses (overlay appears: "Conversation Paused")
3. Type your message and press **Enter** or click **Send**
4. Your message appears in the chat as "You" with a user avatar
5. The message is added to shared history for the next round
6. The conversation resumes automatically

### Title Generation

After the first round completes, the app generates a conversation title in the background:

1. Each participant is asked to suggest a concise title (max 5 words)
2. The first participant then picks the best title from all suggestions
3. The tab title updates automatically

---

## Project Structure

```
ThinkTank/
+-- ThinkTank.Blazor/                  # ASP.NET Core host
|   +-- Components/
|   |   +-- App.razor                  # Root component
|   |   +-- Routes.razor
|   |   +-- _Imports.razor
|   +-- Program.cs                     # DI registration (Legion, Vault, services)
|   +-- Properties/launchSettings.json
|   +-- ThinkTank.Blazor.csproj        # Web SDK, net10.0
+-- ThinkTank.Shared/                  # Razor class library (UI)
|   +-- Components/
|   |   +-- Layout/
|   |   |   +-- MainLayout.razor       # App shell (NavMenu + content)
|   |   |   +-- NavMenu.razor          # Top nav bar (Home, Conversations, Settings)
|   |   +-- Pages/
|   |   |   +-- Home.razor             # Landing page
|   |   |   +-- Chat.razor             # Main think tank UI
|   |   |   +-- Settings.razor         # Provider auth + personality editor
|   |   |   +-- SettingsAppearance.razor
|   |   |   +-- NotFound.razor
|   |   +-- Shared/
|   |       +-- ConfirmationDialog.razor
|   +-- wwwroot/                       # Global CSS, themes, JS interop, Bootstrap
|   |   +-- app.css                    # All 18 theme definitions
|   |   +-- theme.js                   # JS interop for theme/control sizing
|   |   +-- lib/bootstrap/
|   +-- ThinkTank.Shared.csproj        # Razor SDK, net10.0
+-- ThinkTank.Core/                    # Services + models (no UI)
|   +-- Services/
|   |   +-- ThinkTankService.cs        # Core AI orchestration via Legion
|   |   +-- VotingService.cs           # ChatParticipant -> VoterProfile mapping
|   |   +-- SettingsService.cs         # Persistence + provider auth
|   |   +-- SettingsServiceVaultOverlay.cs
|   |   +-- AppearanceService.cs       # Theme management
|   |   +-- ChatConversationsService.cs
|   |   +-- ChatLogService.cs
|   |   +-- HumanNameService.cs        # Random name generation
|   |   +-- NameGeneratorService.cs    # AI-powered name generation
|   +-- Models/
|   |   +-- ChatModels.cs
|   |   +-- ChatLogModels.cs
|   |   +-- LlmModels.cs
|   |   +-- PersistenceModels.cs
|   |   +-- ProviderAuthConfig.cs
|   |   +-- AppearanceMode.cs
|   +-- ThinkTank.Core.csproj          # References MindAttic.Legion + Vault
+-- ThinkTank.UnitTests/               # NUnit + bUnit
+-- cypress/                           # Cypress e2e specs
|   +-- e2e/{chat,navigation,settings,vote-dialog}.cy.js
+-- ThinkTank.slnx
```

---

## Services

### ThinkTankService

The core orchestration engine. Every LLM call is delegated to `MindAttic.Legion.LegionClient`; Think Tank's job is to assemble the prompt and history in Legion's canonical shape.

**Key responsibilities:**
- Build conversation history per participant from the shared turn log
- Apply each participant's personality markdown as the system prompt
- Trim history to the last 8 turns (`MaxContextTurns`) to stay within context limits
- Sanitize model output (strip self-referencing prefixes like "[ChatGPT]:")
- Emit diagnostics events for API response logging (redacted)
- Support per-participant auth overrides (different API keys, models, or token limits) via Legion's override hooks

### VotingService

Maps `ChatParticipant[]` (with per-participant API/model overrides) into Legion's `VoterProfile[]` and delegates to `LLMVotingService.VoteWithProfilesAsync`. Returns a `VoteResult` shaped for injection back into the shared conversation history.

### SettingsService

Handles all persistence to `%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json`.

**Manages:**
- Provider auth configs (API keys, models, max tokens)
- Participant templates (personalities)
- Conversation state (tabs, messages, participants)
- Appearance settings (theme, control height, gutter, border radius)

**Migrations:**
- Auto-creates default templates on first launch
- Marks legacy templates as defaults when upgrading
- Injects `maxTokens` into existing auth configs that don't have it

### AppearanceService

Manages the 18-theme system and visual customization. Changes are applied via CSS variable updates through JS interop and persisted via SettingsService.

### ChatConversationsService

Manages the conversation tab system. Uses `ObservableCollection<ChatConversation>` for reactive UI updates. Handles tab creation, switching, closing, and persistence.

### ChatLogService / ChatStorage

**ChatLogService**: In-memory log of chat events with a `Changed` event for UI updates.

**ChatStorage** (static): File-based persistence per conversation:
- `chat.json` — append-only event log (starts, turns, user messages)
- `{participantId}.md` — per-participant perspective markdown files

---

## Theming

The app uses CSS custom properties (variables) defined per theme in `ThinkTank.Shared/wwwroot/app.css`. Each theme is a `html[data-theme="..."]` selector block that overrides the root variables.

### Provider Colors

Each provider has a dedicated color used throughout the UI for avatars, message bubbles, borders, and accents:

| Provider | Color Variable | Default Hex | Avatar |
|----------|---------------|-------------|--------|
| ChatGPT | `--openai` | `#10a37f` | ⬡ |
| Claude | `--claude` | `#cc785c` | ◈ |
| Gemini | `--gemini` | `#4285f4` | ✦ |
| DeepSeek | `--deepseek` | `#a855f7` | ◉ |

### Core CSS Variables

| Variable | Purpose |
|----------|---------|
| `--bg` | Main background color |
| `--surface` | Card/panel background color |
| `--border` | Border color |
| `--text` | Primary text color |
| `--muted` | Secondary/dimmed text color |
| `--control-height` | Interactive element height (buttons, inputs) |
| `--gutter` | Element spacing |
| `--radius` | Border radius |
| `--title-gradient` | Nav brand text gradient |

### Available Themes

| Theme | Style |
|-------|-------|
| dark | Default dark mode |
| light | Light mode with subtle backgrounds |
| spring | Green, pink, blue, purple accents |
| summer | Cyan, gold, sky, purple accents |
| autumn | Orange, pink, gold, purple — warm tones |
| winter | Green, blue hues — cool and crisp |
| matrix | Green monochrome terminal aesthetic |
| ice | Cyan and blue frosty tones |
| sunset | Pink, gold, blue, purple gradient feel |
| neon | Bright neon accents on dark background |
| dracula | Classic Dracula color scheme |
| solarized | Ethan Schoonover's Solarized palette |
| midnight | Dark with bright accent colors |
| aurora | Cyan, green, purple, pink — northern lights |
| ember | Warm orange and red tones |
| ocean | Deep blue oceanic tones |
| forest | Natural green forest palette |
| mono | Grayscale monochrome |

---

## Data Persistence

All data is stored in the local application data directory of the user running the host process:

```
%LOCALAPPDATA%\MindAttic\ThinkTank\
+-- Settings.json              # All app settings
+-- Personalities/             # Personality markdown files
|   +-- {templateId}.md
+-- Conversations/
    +-- {chatId}/
        +-- chat.json          # Append-only event log
        +-- {participantId}.md # Perspective files
```

When deployed to Azure App Service, `%LOCALAPPDATA%` maps under the App Service home directory (typically `D:\local\LocalAppData\MindAttic\ThinkTank\`). API keys should be promoted out of `Settings.json` and into Application Settings + Key Vault via the [Vault overlay](#cloud-native-credentials-mindatticvault).

### Settings.json Structure

```json
{
  "ProviderAuth": {
    "openai": "{ \"type\": \"bearer\", \"apiKey\": \"...\", \"model\": \"gpt-4o\", \"maxTokens\": 2048 }",
    "claude": "{ \"type\": \"anthropic\", \"apiKey\": \"...\", \"model\": \"claude-sonnet-4-6\", \"maxTokens\": 2048 }",
    "gemini": "{ \"type\": \"google\", \"apiKey\": \"...\", \"model\": \"gemini-2.5-flash\", \"maxTokens\": 2048 }",
    "deepseek": "{ \"type\": \"bearer\", \"apiKey\": \"...\", \"model\": \"deepseek-chat\", \"maxTokens\": 2048 }"
  },
  "Templates": [ ... ],
  "Conversations": [ ... ],
  "AppearanceTheme": "dark",
  "ControlHeight": 40,
  "Gutter": 10,
  "BorderRadius": 10
}
```

### What Is Restored on Restart

- Conversation tabs and participants from `Settings.json`
- Message history from `Conversations/<chatId>/chat.json`
- Status events from `Settings.json`
- All appearance settings and provider configs

### chat.json Event Types

| Type | Description |
|------|-------------|
| `chat-start` | Conversation initialization with chatId and topic |
| `turn` | AI participant response with participantId, providerId, round, text |
| `user` | User-injected message with round and text |

---

## JavaScript Interop

The app uses JS interop for DOM operations that Blazor can't handle natively:

| Function | Location | Purpose |
|----------|----------|---------|
| `setTheme(mode)` | theme.js | Sets `html[data-theme]` attribute |
| `setControlHeight(px)` | theme.js | Updates `--control-height` CSS variable |
| `setGutter(px)` | theme.js | Updates `--gutter` CSS variable |
| `setBorderRadius(px)` | theme.js | Updates `--radius` CSS variable |
| `isNearBottom(el, threshold)` | App.razor | Checks if element is scrolled near bottom |
| `scrollToBottom(el)` | App.razor | Scrolls element to bottom |
| `blurActive()` | App.razor | Blurs the currently focused element |

---

## Supported Providers

Every LLM call leaves Think Tank through `MindAttic.Legion.LegionClient` — Think Tank itself does not hold a single HTTP endpoint. Provider HTTP details (URLs, auth headers, request shape, retries, timeouts) all live in Legion's `LlmProviderCatalog` and per-provider clients.

| Provider | Auth | Default Model |
|----------|------|---------------|
| Claude (Anthropic) | `x-api-key` header | `claude-sonnet-4-6` |
| ChatGPT (OpenAI) | Bearer token | `gpt-4.1-mini` |
| Gemini (Google) | API key (query parameter) | `gemini-2.5-flash` |
| DeepSeek | Bearer token (OpenAI-compatible) | `deepseek-chat` |
| Mistral | Bearer token | `mistral-large-latest` |
| Grok (xAI) | Bearer token (OpenAI-compatible) | `grok-3-mini-fast` |
| Groq | Bearer token (OpenAI-compatible) | `llama-3.3-70b-versatile` |
| Together AI | Bearer token | `meta-llama/Llama-3-70b-chat-hf` |
| OpenRouter | Bearer token | `meta-llama/llama-3.1-8b-instruct:free` |
| Fireworks | Bearer token | `accounts/fireworks/models/llama-v3p1-70b-instruct` |
| Cohere | Bearer token | `command-r-plus` |

By default, the UI surfaces the first-party set (`claude`, `openai`, `gemini`, `deepseek`); the remaining seven are opt-in by adding an entry in **Settings > Providers**.

---

## Security

- **Do not commit API keys.** Provider auth JSON is stored in Local AppData and should remain local.
- API responses in the Diagnostics panel are redacted (content replaced with "...") to prevent accidental exposure.
- All provider traffic uses HTTPS, end-to-end via Legion.
- When hosting Think Tank on a network (LAN, Azure, etc.), serve only over TLS and gate access — Blazor Server's circuit holds a live SignalR connection and any reachable client can browse to the URL.
