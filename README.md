# Think Tank

A .NET MAUI + Blazor desktop application that orchestrates multi-participant AI discussions across multiple LLM providers. Create conversation panels where ChatGPT, Claude, Gemini, and DeepSeek debate topics with customizable personalities — and inject your own messages to steer the conversation in real time.

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

- .NET 10 (MAUI + Blazor WebView)
- Targets: Windows, Android, iOS, macOS (Catalyst)
- No external UI framework beyond Bootstrap (included in wwwroot)

---

## Features

### Multi-Provider AI Conversations

- **11 providers**: OpenAI, Anthropic, Google, DeepSeek, Mistral, xAI, Groq, Together AI, OpenRouter, Fireworks, and Cohere
- Each participant calls its provider's API with full conversation history
- Configurable model and max tokens per provider

### Conversation Tabs

- Run multiple independent conversations simultaneously
- Each tab has its own topic, participants, and message history
- Typing indicator (animated dots) on tabs with active conversations
- Right-click context menu for rename and close
- Conversations persist across app restarts

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
+----------------------------------------------------------+
|                    MAUI Shell (MainPage.xaml)             |
|  +----------------------------------------------------+  |
|  |              Blazor WebView                        |  |
|  |  +----------+  +------------------------------+   |  |
|  |  | NavMenu  |  |         Page Content          |   |  |
|  |  | (top)    |  |  Home | Chat | Settings       |   |  |
|  |  +----------+  +------------------------------+   |  |
|  +----------------------------------------------------+  |
+----------------------------------------------------------+
         |                    |                  |
    +----+-----+    +--------+--------+   +----+------+
    | Settings |    |  ThinkTank   |   |Appearance |
    | Service  |    |    Service       |   | Service   |
    +----+-----+    +--------+--------+   +-----------+
         |                   |
    +----+-----+    +--------+----------------------------+
    |Settings  |    |         HTTP Clients                |
    |  .json   |    |  OpenAI | Claude | Gemini | DeepSeek|
    +----------+    +-----------------------------------------+
```

All services are registered as singletons in `MauiProgram.cs` for shared global state.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) with MAUI workload
- An API key for at least one supported provider

### Build & Run

```bash
# Restore dependencies
dotnet restore

# Run on Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0

# Or use Visual Studio / VS Code with MAUI extension
```

### First Launch

1. Navigate to **Settings > Providers**
2. Enter your API key(s) in the JSON editor for each provider
3. Click model chips to set your preferred model
4. Go to **Conversations** (Think Tank)
5. Enter a topic, select participants, and click **Start**

### Testing

The project ships with two complementary test suites.

**Unit / component tests** — NUnit + bUnit, 278 tests covering services, model parsing, Razor component rendering, the cloud-credential overlay, and the `ChatParticipant`→`VoterProfile` mapping:

```bash
dotnet test ThinkTank.UnitTests/ThinkTank.UnitTests.csproj
```

**End-to-end UI tests** — Cypress, 18 specs across navigation, settings, chat affordances, and the Call-Vote dialog. Run a dev server in one terminal, then the suite in another:

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

**Known Models** (selectable via chip buttons in Settings):

| Provider | Models |
|----------|--------|
| OpenAI | gpt-4, gpt-5, gpt-5-mini, gpt-5-nano |
| Claude | claude-sonnet-4, claude-sonnet-4-6 |
| Gemini | gemini-2.5-flash, gemini-2.5-pro, gemini-2.0-flash, gemini-2.0-flash-lite |
| DeepSeek | deepseek-chat, deepseek-reasoner |

### Cloud-Native Credentials (MindAttic.Vault)

For deployments where API keys live outside `Settings.json` — Azure App Service Application Settings, Key Vault references, or shared dev user-secrets — Think Tank reads each provider's `apiKey` from `IConfiguration` at the path:

```
MindAttic:Vault:LLM:<providerId>:apiKey
```

Configuration sources are layered in `Program.cs` (later wins): `appsettings.json` → `%APPDATA%\MindAttic\LLM\providers.json` → project user-secrets → shared user-secrets (`mindattic-vault-shared`) → environment variables (App Service Application Settings + Key Vault refs).

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

The **Call Vote** button (visible in the conversation header once a tab has 2+ participants and the conversation has started) polls every participant on a question and injects the aggregated decision back into the shared history so subsequent turns see it.

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
    +-- Call provider API with shared history   |
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
+-- Components/
|   +-- Layout/
|   |   +-- MainLayout.razor          # App shell (NavMenu + content)
|   |   +-- MainLayout.razor.css
|   |   +-- NavMenu.razor             # Top nav bar (Home, Conversations, Settings)
|   |   +-- NavMenu.razor.css
|   +-- Pages/
|   |   +-- Home.razor                # Landing page
|   |   +-- Chat.razor                # Main think tank UI (~930 lines)
|   |   +-- Settings.razor            # Provider auth + personality editor (~640 lines)
|   |   +-- SettingsAppearance.razor   # Theme & visual controls
|   |   +-- NotFound.razor
|   +-- Shared/
|   |   +-- ConfirmationDialog.razor   # Reusable confirmation modal
|   +-- Routes.razor
|   +-- _Imports.razor
+-- Services/
|   +-- ThinkTankService.cs        # Core AI orchestration (~565 lines)
|   +-- SettingsService.cs             # Persistence layer (~360 lines)
|   +-- AppearanceService.cs           # Theme management
|   +-- ChatConversationsService.cs    # Tab/conversation management
|   +-- ChatLogService.cs              # Logging + chat storage
|   +-- ProviderAuthConfig.cs          # Auth config record
|   +-- HumanNameService.cs            # Random name generation
|   +-- NameGeneratorService.cs        # AI-powered name generation
+-- wwwroot/
|   +-- app.css                        # Global styles + all 18 theme definitions
|   +-- theme.js                       # JS interop for theme/control sizing
|   +-- index.html                     # MAUI WebView host
|   +-- lib/bootstrap/                 # Bootstrap CSS
+-- Resources/                         # Icons, fonts, images, splash screens
+-- App.xaml / App.xaml.cs             # MAUI app entry
+-- MainPage.xaml                      # BlazorWebView host
+-- MauiProgram.cs                     # DI registration
+-- ThinkTank.csproj               # Project config (.NET 10, MAUI)
```

---

## Services

### ThinkTankService

The core orchestration engine. Routes API calls to the correct provider based on participant configuration.

**Key responsibilities:**
- Build conversation history in each provider's native message format
- Call provider APIs with personality prompts, topic context, and shared history
- Trim history to the last 8 turns (`MaxContextTurns`) to stay within context limits
- Sanitize model output (strip self-referencing prefixes like "[ChatGPT]:")
- Emit diagnostics events for API response logging
- Support per-participant auth overrides (different API keys, models, or token limits)

**Provider endpoints:**

| Provider | Endpoint |
|----------|----------|
| OpenAI | `https://api.openai.com/v1/chat/completions` |
| Claude | `https://api.anthropic.com/v1/messages` |
| Gemini | `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` |
| DeepSeek | `https://api.deepseek.com/chat/completions` |

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

The app uses CSS custom properties (variables) defined per theme in `wwwroot/app.css`. Each theme is a `html[data-theme="..."]` selector block that overrides the root variables.

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

All data is stored in the local application data directory:

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
| `isNearBottom(el, threshold)` | index.html | Checks if element is scrolled near bottom |
| `scrollToBottom(el)` | index.html | Scrolls element to bottom |
| `blurActive()` | index.html | Blurs the currently focused element |

---

## Supported Providers

### OpenAI (ChatGPT)

- **Auth**: Bearer token
- **API**: Chat Completions (`/v1/chat/completions`)
- **Default model**: gpt-4o
- **Message format**: OpenAI chat messages (system/user/assistant roles)

### Anthropic (Claude)

- **Auth**: `x-api-key` header + `anthropic-version` header
- **API**: Messages (`/v1/messages`)
- **Default model**: claude-sonnet-4-6
- **Message format**: Anthropic messages with separate system prompt

### Google (Gemini)

- **Auth**: API key in URL query parameter
- **API**: GenerateContent (`/v1beta/models/{model}:generateContent`)
- **Default model**: gemini-2.0-flash-lite
- **Message format**: Google AI content parts with system instruction

### DeepSeek

- **Auth**: Bearer token
- **API**: Chat Completions (`/chat/completions`) — OpenAI-compatible
- **Default model**: deepseek-chat
- **Message format**: OpenAI-compatible chat messages

---

## Security

- **Do not commit API keys.** Provider auth JSON is stored in Local AppData and should remain local.
- API responses in the Diagnostics panel are redacted (content replaced with "...") to prevent accidental exposure.
- All provider traffic uses HTTPS.
