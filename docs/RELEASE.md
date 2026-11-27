---
layer: release
status: living
---
# ThinkTank — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
A Blazor Server app that seats Claude, ChatGPT, Gemini and DeepSeek at one table to debate a topic as personas,
with interjections and LLM-driven votes, every call routed through MindAttic.Legion; an internal tool for the owner.

## 2. The milestone: ThinkTank local 1.0 {#milestone}
- **Milestone:** MS-19, "ThinkTank local 1.0"
- **Published means:** an internal tool on `R:\MindAttic\Apps\ThinkTank\{Debug,Release}`, run through the shared
  run menu (`run.ps1`) against local SQL. Not hosted publicly.
- **The natural stopping point:** ThinkTank gained sign-in, spend limits and an Azure deploy in November 2026, then
  Azure was decommissioned (2026-11-26). Once the Azure leftovers are gone, both builds run from R:, it is on Legion
  28, it logs to MindAttic.Log, its core LLM call path is tested and the docs describe the local app, it is a
  complete, coherent local tool.

## 3. Done when (acceptance checks)
Each line is also a `tickets milestone check` on MS-19.
1. build — `ThinkTank.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\ThinkTank\Release`.
3. signoff — the owner signs in locally, runs a four-seat debate, interjects and calls a vote end to end.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | TT-T82 Local-first runtime | Runs locally from R: on local SQL and Legion 28, no Azure, docs that match | TT-T84 Azure removal (990), TT-T85 Run from R: deploy (980), TT-T86 Legion 28 upgrade (970), TT-T87 Docs match local app (960) |
| 800 | TT-T83 Observability and core tests | MindAttic.Log logging and tests on the core LLM call path | TT-T17 MindAttic.Log logging (890), TT-T88 Core call path tests (880) |

Tasks: TT-T80 under T84; TT-T81 under T85 (also part of the cross-repo MAT-T290); TT-T89 under T86; TT-T16 (after
T80 and T81) under T87; TT-T62 → T63 → T64 under T17; TT-T3, TT-T4, TT-T8 under T88.

## 5. Out of scope (not before this milestone)
- Public hosting anywhere: Hetzner (TT-T67/68/69 closed) and Azure (gone for good).
- Multi-user isolation (per-user conversations), streaming replies, cost/progress indicators, onboarding.
- Moving the UI onto MindAttic.Components / MindAttic.UiUx.
- Auto-vote after N rounds (`docs/rfc/0001-auto-vote-after-n-rounds.md`).
- Further test coverage beyond the core call path (ResponseLengthPreset, SettingsService, conversation persistence).
- Repeat bughunt, run-all-tests and cleanup sweeps.

## 6. Current verified state
- Sign-in (TT-T46), spend limits and usage table (TT-T47), hosted transport hardening (TT-T45), error page and input
  limits (TT-T44), cancellation of runs (TT-T43) are in the code.
- MAT-T306 (6731e99) put ThinkTank on the shared run menu with both runs on Development against LocalDB;
  `R:\MindAttic\Apps\ThinkTank` did not exist yet on 2026-11-27.
- Still present: `.github/workflows/azure-deploy.yml` and the Azure Identity / Data Protection packages (TT-T80).
- `docs/plans/thinktank-improvement-plan.md` (TT-T41, 7d09ea2) is the audit this charter draws on; its Phase 1
  (Azure hosting) is superseded by the decommission.

## 7. Decision record
- 2026-11-27 — charter drafted from README/BIBLE/improvement plan/git log/tickets; Legion vote: **Approve**, 9 of 9
  (Plurality, quorum reached), no dissent.
- Triage (M.LEGN-T61): 21 open → done 1 (TT-T41), duplicate 0, wont-do 10, kept 9 tasks + TT-T17 kept as a
  feature; new: 2 epics, 5 features, 1 task (TT-T89).
