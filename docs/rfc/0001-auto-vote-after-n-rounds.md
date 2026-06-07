---
codex: 1
project: Think Tank
code: TT
layer: rfc
status: planned
updated: 2026-06-07
---

# RFC 0001 — Auto-vote after N rounds of no convergence

## Problem
Manual `Call Vote` and participant-triggered `[REQUEST_VOTE: question]` voting ship today
([TT-LAW-4](../BIBLE.md#TT-LAW-4)). But a debate can circle indefinitely without any participant
emitting the marker and without the user noticing. We want the room to *automatically* poll for
consensus after a configurable number of rounds with no convergence.

## Options compared
1. **Round-counter trigger** — after `AutoVoteEveryNRounds`, fire a consensus vote. Simple,
   predictable, easy to test. No semantic "are we converging?" detection.
2. **Convergence heuristic** — measure similarity/repetition across recent turns and fire when it
   plateaus. More intelligent, much harder to test deterministically, risk of false triggers.
3. **Moderator-LLM trigger** — a dedicated Legion call judges "stuck?" each round. Most flexible,
   but adds latency and a per-round cost on every conversation.

## Decision
Adopt **Option 1** for v1 (round-counter), behind an opt-in setting, reusing the existing
`VotingService` → `LLMVotingService.VoteWithProfilesAsync` path and the
[TT-LAW-4](../BIBLE.md#TT-LAW-4) synthetic-turn injection. Treat Option 2/3 as a later RFC.

## What NOT to do
- Do NOT add a second voting code path — auto-vote MUST reuse `VotingService.MapToVoterProfiles`.
- Do NOT call any provider directly to detect convergence ([TT-LAW-1](../BIBLE.md#TT-LAW-1)).
- Do NOT persist auto-vote results differently from a normal vote turn ([TT-LAW-5](../BIBLE.md#TT-LAW-5)).
- Do NOT fire while the user has the conversation paused for chat injection.

## Phased plan (with risk)
1. Add `AutoVoteEveryNRounds` (0 = off) to settings + Appearance/Conversation config UI. *(low risk)*
2. In the `Chat.razor` round loop, after each round, if the counter trips and not paused, run a
   consensus vote and inject the result. *(medium risk — loop/timing interplay with Stop & pause.)*
3. Add unit coverage for the trigger predicate + a Cypress assertion for an end-to-end auto-vote.
   *(medium risk — e2e timing.)*

## Graduates into
- [TT-§7 Active frontier](../BIBLE.md#TT-§7) and a new Epic C story in
  [USER_STORIES.md](../USER_STORIES.md) once a test proves the trigger.
