---
codex: 1
project: Think Tank
code: TT
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

# Think Tank — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test.
> Status evidence: `dotnet test ThinkTank.UnitTests` → 296 passed / 0 failed (2026-10-03).

## Epic A — Roundtable orchestration
- **TT-US-A1 ✅** As a user, I can dispatch every participant's turn through one provider-agnostic
  router, so I'm not locked to a vendor. *Given an unknown provider id, When a call is built, Then
  it throws rather than guessing an endpoint.* *(verified by `CallProvider_UnknownProvider_ThrowsArgumentException`.)*
- **TT-US-A2 ✅** As a user, I never see a model talk to itself by name, so the transcript reads
  cleanly. *Given a response prefixed `[Claude]:`/`Assistant:`, When sanitized, Then the prefix is
  stripped case-insensitively.* *(verified by `SanitizeModelOutput_StripsClaudePrefix`,
  `SanitizeModelOutput_StripsProviderPrefixes`, `SanitizeModelOutput_CaseInsensitive`.)*
- **TT-US-A3 ✅** As a user, I can run long debates without blowing the context window, so calls
  stay valid. *Given more than `MaxContextTurns` turns, When history is built, Then it trims to the
  last N.* *(verified by `TrimHistory_Over`, `TrimHistory_Exactly`, `TrimHistory_Under`,
  `TrimHistory_Empty_ReturnsEmpty`.)*
- **TT-US-A4 ✅** As a developer, I can subscribe to redacted diagnostics for every API call, so I
  can debug without leaking content. *(verified by `DiagnosticsEvent_CanSubscribe`.)*
- **TT-US-A5 🟡** As a user, I can start a topic, watch each participant respond round by round, and
  Stop the loop. *(round loop is UI logic in `Chat.razor`; covered only by Cypress `chat.cy.js`,
  not the unit run — see backlog.)*

## Epic B — Personalities & personas
- **TT-US-B1 ✅** As a user, each participant gets a markdown personality as its system prompt, with
  optional per-seat model/key override. *(verified by `ChatParticipant_StoresAllFields`,
  `ParticipantTemplate_WithExpression`, `EffectivePersonaId_PrefersExplicitField`,
  `EffectivePersonaId_FallsBackToLegionTemplateIdConvention`.)*
- **TT-US-B2 ✅** As a user, library-backed participants are profiled psychometrically and *embody*
  the traits rather than reciting them, so personas feel distinct. *(verified by
  `Narrator_RendersAllFiveInstruments`, `Narrator_InstructsToEmbodyNotRecite`,
  `Service_ResolvesAndRendersStoredProfile`, `PersonaIdFromVoterId_StripsGuidSuffix`.)*
- **TT-US-B3 ✅** As a user, default templates exist for every provider and have unique ids. *(verified
  by `Templates_ContainsDefaultTemplatesForAllProviders`, `Templates_CoverAllProviders`,
  `Templates_AllHaveUniqueIds`.)*
- **TT-US-B4 ✅** As a user, I can have the LLM generate a participant name, with safe fallbacks for
  empty/garbage output. *(verified by `GenerateFirstName_ReturnsCleanName_WhenLLMRespondsCleanly`,
  `GenerateFirstName_FallsBackToAlex_WhenResponseIsEmpty`, `GenerateFirstName_StripsNonLetters`.)*

## Epic C — Vote-driven decisions
- **TT-US-C1 ✅** As a participant, I can call a vote mid-response by emitting `[REQUEST_VOTE: q]`,
  which is detected and stripped from my visible text. *(verified by `BasicMarker_Matches`,
  `ExtractsQuestion_Correctly`, `StripMarker_RemovesTagLeavesRemainingText`,
  `MarkerIsCaseInsensitive`, `MultipleMarkers_FirstOneMatched`.)*
- **TT-US-C2 ✅** As a user, a vote polls every participant preserving their persona and per-seat
  API/model overrides. *(verified by `MapToVoterProfiles_PreservesIdNameProviderPersonality`,
  `MapToVoterProfiles_FullAuthBlob_PopulatesBoth`, `MapToVoterProfiles_MultipleParticipants_PreservesOrder`.)*
- **TT-US-C3 ✅** As a user, malformed per-seat auth in a vote degrades safely to null overrides
  rather than crashing the vote. *(verified by `MapToVoterProfiles_MalformedAuthOverride_OverridesAreNull`,
  `ExtractField_MalformedJson_ReturnsNull`.)*
- **TT-US-C4 🟡** As a user, the `Call Vote` button + config dialog injects the result back into the
  shared history. *(dialog/injection is `Chat.razor` UI; covered only by Cypress `vote-dialog.cy.js`.)*

## Epic D — Conversation persistence
- **TT-US-D1 ✅** As a user, my conversations survive a restart — turns replay from the on-disk log.
  *(verified by `LoadTurnsAsync_ParsesTurnEntries`, `LoadTurnsAsync_MultipleTurns_PreservesOrder`,
  `LoadTurnsAsync_ReplaysUserVoteAndSystemEntries`, `AppendChatJsonAsync_CreatesFileAndFolder`.)*
- **TT-US-D2 ✅** As a user, a missing/corrupt log never crashes load. *(verified by
  `LoadTurnsAsync_MissingFile_ReturnsEmpty`, `LoadTurnsAsync_MissingFields_DefaultsGracefully`.)*
- **TT-US-D3 ✅** As a user, each participant's perspective markdown round-trips to disk. *(verified by
  `WriteThenReadPerspective_Roundtrips`, `WritePerspectiveAsync_OverwritesExisting`,
  `ReadPerspectiveAsync_MissingFile_ReturnsEmpty`.)*
- **TT-US-D4 ✅** As a user, the conversation tab lifecycle (create/switch/close) keeps the active
  tab coherent. *(verified by `CreateConversation_SetsAsActive`, `CloseConversation_ActiveClosedUpdatesToLast`,
  `CloseConversation_ActiveClosed_NoRemaining_SetsNull`, `SetActive_SwitchesToCorrectConversation`.)*

## Epic E — Cloud-native credentials
- **TT-US-E1 ✅** As an operator, I can supply keys via Vault/`IConfiguration` and they never touch
  disk. *(verified by `Save_AfterOverlay_DoesNotPersistRuntimeKey`,
  `Construction_DoesNotWriteCredentialsToSharedStore`, `OverlayFromConfiguration_VaultKeySet_PopulatesRuntimeOverride`.)*
- **TT-US-E2 ✅** As an operator, credential precedence is deterministic (override > disk > vault).
  *(verified by `GetKeyForProvider_ExplicitOverride_WinsOverEverything`,
  `GetKeyForProvider_IgnoresDiskKey_UsesRuntime`, `GetKeyForProvider_EmptyDiskKey_FallsBackToRuntime`.)*
- **TT-US-E3 ✅** As a developer, no real-looking key is ever committed to the repo; see
  [TT-LAW-6](BIBLE.md#TT-LAW-6). *(verified by `ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles`,
  `KeyPatterns_DetectRealShapes_AndIgnoreOrdinaryText`.)*

## Epic F — Appearance
- **TT-US-F1 ✅** As a user, I can pick any of 18 themes and it persists, falling back to dark on an
  unknown value. *(verified by `ThemeSelect_HasAllEighteenThemes`, `Constructor_RestoresEveryPersistedTheme`,
  `Constructor_UnknownTheme_FallsBackToDark`, `SetMode_PersistsToSettings`.)*
- **TT-US-F2 ✅** As a user, control height / gutter / border radius clamp to valid ranges. *(verified
  by `SetControlHeight_ClampsToMin`, `SetControlHeight_ClampsToMax`, `SetGutter_ClampsToMax`,
  `SetBorderRadius_ClampsToMin`.)*

## Priority backlog
1. **🟡→✅ Graduate the round loop** (TT-US-A5): wire a bUnit/integration assertion or stabilize
   `chat.cy.js` so the start→round→stop flow is verified, not just UI-tested.
2. **🟡→✅ Graduate the vote dialog** (TT-US-C4): assert injection of the synthetic vote turn.
3. **⬜ Auto-vote after N rounds** — see RFC [0001](rfc/0001-auto-vote-after-n-rounds.md); graduates
   into [TT-§7](BIBLE.md#TT-§7) and a new Epic C story.
4. **⬜ Title generation** verification (currently background UI logic, untested in the unit run).
5. **⬜ Provider connectivity polling** assertion.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| TT-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| TT-T2 | 2026-10-12 | chore | closed | /commit now logs every commit as a ticket in this table | `.claude/skills/commit/SKILL.md` |
| TT-T3 | 2026-10-14 | chore | open | NUnit tests for ThinkTankService.CallProvider dispatch and auth resolution (ThinkTank.Core/Services/ThinkTankService.cs): drive it through a LegionClient over a stubbed HttpMessageHandler and cover ClaudeFallbackMode rerouting a non-claude provider to claude with the WrapPersonaForClaudeFallback persona and the per-template auth override dropped, claude itself not rerouted, InvalidOperationException when no API key resolves, apiKey/model/maxTokens precedence (maxTokensOverride over auth override JSON over disk auth JSON over 2048 default; override model blank falls through), malformed auth override JSON emitting an isError Diagnostics event yet still falling back, and a provider failure emitting an error Diagnostics event then rethrowing - this is the core LLM call path and is untested beyond SanitizeModelOutput | — |
| TT-T4 | 2026-10-14 | chore | open | NUnit tests for ThinkTankService prompt building (private BuildPrompt/ResolveMaxContextTurns in ThinkTank.Core/Services/ThinkTankService.cs, via reflection or by capturing the outgoing request from a stubbed HttpMessageHandler): empty history yields a single opening-thoughts user turn, the speaker's own turns map to assistant and others to user tagged [ModelName]:, speakerId wins over providerId for self-recognition (so fallback-to-claude still recognises the original participant), a trailing own turn appends Please continue the discussion, blank persona uses the default roundtable framing, topic is embedded in the system prompt, GlobalMaxContextTurns below 2 clamps to 2 and over-long history emits a context truncated Diagnostics event, and a responseLength preset appends ResponseLengthPreset.InstructionFor while null/blank leaves the prompt unchanged | — |
| TT-T5 | 2026-10-14 | chore | open | NUnit tests for ResponseLengthPreset (ThinkTank.Core/Models/ResponseLengthPreset.cs) and its SettingsService setters: Normalize maps null, blank, unknown and mixed-case/padded values (e.g. '  Detailed ') correctly with concise as Default, InstructionFor returns a distinct leading-newline block per preset and the concise text for unknown input, Options lists concise/balanced/detailed in display order, plus SettingsService.SetGlobalResponseLength coercing junk to concise and SetGlobalMaxContextTurns clamping 0/1/negative to 2 - these presets control every roundtable prompt and have zero coverage | — |
| TT-T6 | 2026-10-14 | chore | open | NUnit tests for SettingsService load/save and migration (ThinkTankSettingsService.TryLoad, EnsureDefaultsIfMissing, Save, StripApiKey, MigrateLegacyStorageFolder in ThinkTank.Core/Services/SettingsService.cs; add a minimal settings-root seam so tests do not touch the real LOCALAPPDATA folder): an explicit ProviderAuth null in Settings.json keeps templates and conversations, persisted GlobalMaxContextTurns 0/1 is floored to 2, unknown GlobalResponseLength normalises, missing numeric fields fall back to defaults, corrupt JSON reinitialises with defaults, EnsureDefaultsIfMissing removes unsupported providers and stale default templates, inserts missing default templates after existing defaults, backfills missing keys like maxTokens into existing provider JSON without overwriting set values, and Save never writes apiKey to disk and writes via a .tmp file - this guards against silent loss or credential leakage of user settings | — |
| TT-T7 | 2026-10-14 | chore | open | NUnit tests for SettingsService key and persona helpers (ThinkTank.Core/Services/SettingsService.cs): BuildAuthJson round-trips keys containing quotes, backslashes and newlines and omits model/maxTokens when blank/null, SetProviderKey/HasOwnProviderKey write to the thinktank-scoped store in the sandboxed MINDATTIC_LLM_CREDENTIALS dir and GetKeyForProvider prefers explicit override then own key then RuntimeApiKeyOverrides (empty string clears the own key), and LoadLegionPersonaTemplates with count 0 or negative is a no-op, replaces Templates with count unique personas, only assigns providers that have a resolvable key with the rest on fallbackProviderId, sets IsDefault false and PersonaId recovered from the voter id, using a seeded Random for determinism | — |
| TT-T8 | 2026-10-14 | chore | open | NUnit tests for VotingService.VoteAsync and MapToVoterProfilesWithPsychometrics (ThinkTank.Core/Services/VotingService.cs): VoteAsync refreshes the shared VotingConfiguration ApiKeys and ModelOverrides from current settings (keys added after construction become visible, providers without a key or model are omitted, malformed provider auth JSON does not throw) and forwards question/context/options/quorum to LlmVotingService; MapToVoterProfilesWithPsychometrics appends the PsychometricNarrator brief and sets Psychometrics for a scored persona (use a temp PsychometricProfileService store) while unscored personas match MapToVoterProfiles output exactly - only the pure MapToVoterProfiles/ExtractField path is tested today | — |
| TT-T9 | 2026-10-14 | chore | open | NUnit tests for ChatConversationsService persistence round-trip (ThinkTank.Core/Services/ChatConversationsService.cs constructor restore and private Persist): conversations persisted in SettingsService.Conversations restore with Topic, MaxTokens, MaxRounds, ResponseLength, Messages, StatusEvents, Diagnostics and participants including PersonaId, the last restored conversation becomes active, null message/status lists restore as empty, and after CreateConversation/NotifyChanged the settings snapshot round-trips into a fresh service with empty lists persisted as null - only the empty-settings constructor case is covered today, so tab state loss on restart would go unnoticed | — |
| TT-T10 | 2026-10-15 | chore | closed | Remove duplicate command copies (/quickload, /quicksave) now centralized at the user level (~/.claude/commands) | WF-T14 |
| TT-T11 | 2026-10-16 | chore | closed | Tracked .githooks/commit-msg runs the shared MindAttic commit rule: every commit names an existing workflow ticket | M.FLOW agent-standard commit hook |
