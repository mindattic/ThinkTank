# LLMThinkTank Project Rules

## Planned Feature: LLMVoting Integration

Wire in the sibling `MindAttic.LLMVoting` library so participants can call for a vote mid-discussion to break circular arguments and reach a decision.

### Concept
At the end of any round, participants are polled via LLMVoting. The result is injected back into `sharedHistory` as a `[VOTE RESULT]` message so subsequent participants see the decision. If quorum is reached, the moderator is offered a stop.

### Trigger Mechanisms
Start with **manual only**. Auto-vote after N rounds is a follow-on.

- **Manual:** A "Call Vote" button in the UI during an active run. Pauses the round loop, runs the vote, injects the result, then resumes.
- **Auto (follow-on):** After a configurable number of rounds with no convergence, fire automatically.

### Participant → VoterProfile Mapping
`ChatParticipant` maps directly to `VoterProfile`:

| ChatParticipant field | VoterProfile field |
|-----------------------|--------------------|
| `ProviderId` | `ProviderId` |
| `DisplayName` | `Name` |
| `PersonalityMarkdown` | `PersonalityMarkdown` |
| `AuthOverrideJson["apiKey"]` | `ApiKeyOverride` |
| `AuthOverrideJson["model"]` | `ModelOverride` |

### Vote Types to Expose in UI

| Vote Type | LLMVoting call | Quorum |
|-----------|---------------|--------|
| "Have we reached consensus?" | `VoteAsync`, options `["Yes", "No"]` | `SimpleMajority` |
| "What is our conclusion?" | `VoteAsync`, free-form | `Plurality` |
| "What direction next?" | `VoteAsync`, options from a text field | `SimpleMajority` |

### Result Injection
Vote result becomes a synthetic `SharedTurn`:
```
ModelId   = "vote"
ModelName = "Vote Result"
Text      = "[VOTE] Question: {question}\nDecision: {consensus} ({strength:P0} agreement)\nSummary: {narrativeSummary}"
Round     = current round
```
Participants see this as a `[Vote Result]:` user message in subsequent turns, identical to how they see each other's responses.

### Implementation Steps (in order)

1. **`LLMThinkTank.Core.csproj`** — add `<ProjectReference>` pointing to `../../../LLMVoting/LLMVoting/LLMVoting.csproj` (sibling repo, same relative path used by StreetSamurai)
2. **`LLMThinkTank.Blazor/Program.cs`** — register `services.AddLLMVoting(sp => ...)` using `SettingsService` API keys
3. **`LLMThinkTank.Core/Services/VotingService.cs`** (new) — thin wrapper mapping `ChatParticipant[]` + question → `VoterProfile[]` → `LLMVotingService.VoteWithProfilesAsync()`
4. **`LLMThinkTank.Shared/Components/Pages/Chat.razor`** — "Call Vote" button + vote config dialog (question text, vote type, quorum), pause/resume loop logic, result injection into `sharedHistory`
5. **`ChatModels.cs`** — no changes needed; vote result is just a special `SharedTurn`

### Out of Scope (follow-on)
- Auto-vote after N rounds of no convergence
- Scored evaluation mode during a discussion
- Persisting vote results separately from messages
