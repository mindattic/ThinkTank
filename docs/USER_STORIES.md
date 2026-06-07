---
codex: 1
project: Think Tank
code: TT
layer: stories
status: living
updated: 2026-06-07
---

# Think Tank — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites the test.
> Status evidence: `dotnet test ThinkTank.UnitTests` → 293 passed / 0 failed (2026-06-07).

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
- **TT-US-E3 ✅** As a developer, no real-looking key is ever committed to the repo. *(verified by
  `ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles`.)*

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

### Audit log
No story has been changed since its original ask; the LLMVoting integration plan recorded in the
repo `CLAUDE.md` "Planned Feature" section graduated as shipped Epic C (manual + marker-triggered
voting), with auto-vote explicitly deferred to RFC 0001 — matching that plan's stated
"Out of Scope (follow-on): Auto-vote after N rounds" (original spec — audit log).
