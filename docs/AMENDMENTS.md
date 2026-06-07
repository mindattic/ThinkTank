---
codex: 1
project: Think Tank
code: TT
layer: amendments
status: living
updated: 2026-06-07
---

# Think Tank — Amendments (append-only; amendment wins over the bible)
> Never rewrite an amendment; supersede it with a new one. Beyond ~25, fold into the bible and
> start a new epoch (note the git tag).

## TT-A1 — Retire the MAUI desktop shell; web is the only host (supersedes —) {#TT-A1}
**What changed:** Think Tank was originally a .NET MAUI desktop app (project `LLMThinkTank`); it
was migrated to an ASP.NET Core Blazor Server web app with the `ThinkTank.{Core,Shared,Blazor}`
split. The MAUI project, `Platforms/`, `Resources/`, and `MauiProgram.cs` were removed; the Razor
components and `wwwroot` moved into `ThinkTank.Shared`. (Evidence: the migration `cp`/`rm` commands
preserved in `.claude/settings.local.json`.)
**Why:** browser-native delivery — no installer, no native binaries, LAN/Azure deployable.
**Migration:** none for users; `[TT-§3](BIBLE.md#TT-§3)` records "NOT a desktop app" as canon.

## TT-A2 — "Arena" renamed to "Think Tank" (supersedes —) {#TT-A2}
**What changed:** the product/app nomenclature changed from "Arena" to "Think Tank" repo-wide
(recorded in `.github/copilot-instructions.md`).
**Why:** the roundtable-of-advisors framing replaced the adversarial "arena" framing.
**Migration:** cosmetic; no data or API change.

## TT-A3 — LLMVoting integration shipped; auto-vote deferred (supersedes —) {#TT-A3}
**What changed:** the planned LLMVoting integration (repo `CLAUDE.md` "Planned Feature") shipped as
Epic C — manual `Call Vote` plus participant-triggered `[REQUEST_VOTE: question]`, routed through
Legion's `LLMVotingService`. Vote types: consensus, free-form, direction.
**Why:** break circular debates with a decision injected back into shared history
([TT-LAW-4](BIBLE.md#TT-LAW-4)).
**Migration:** none. Auto-vote-after-N-rounds remains out of scope — tracked in
RFC [0001](rfc/0001-auto-vote-after-n-rounds.md).
