# Task board — shared flexible multi-agent pool

T001 is DONE. A007 revision 2 is Accepted. **Any agent (Codex, Claude Code, Antigravity) may claim any unblocked TODO task or assist a handed-off task** by following [docs/MULTI_AGENT_WORKFLOW.md](docs/MULTI_AGENT_WORKFLOW.md) (`1 Task ID = 1 Branch = 1 Worktree = 1 Active Owner`).

## Active Claims

> Check this table + run `.\scripts\agent-status.ps1` before starting ANY task.

| Task ID | Active Owner | Branch | Worktree / Checkout | Locked Zone | Status | Handoff / Note |
|---|---|---|---|---|---|---|
| T002 | Codex | `feat/T002-simulation-foundation` | `T001-review/` (legacy clone) | `Simulation/`, `Tests/` | `BLOCKED (runtime verification)` | Commit `48bb41f`; needs normal-terminal `dotnet test` + review (`0x800711C7` in Codex sandbox) |
| Prep + N3 | Antigravity | `chore/multi-agent-workflow` (`77ba409`+) | `F:/Code/game-dev` | `Assets/`, `Sandbox/`, `docs/`, `scripts/` | `REVIEW / READY TO MERGE` | N3 done + Sandbox tests passed; waiting merge with `T002` |
| T007 | Unclaimed (`BLOCKED`) | — | — | `Game/` | `BLOCKED` | Waiting `T002`/`T003` (`Simulation.csproj`) on `main` |

## Task Backlog

> Any agent (`Codex`, `Claude Code`, `Antigravity`) may claim an unblocked `TODO` task (or assist a `BLOCKED` / `HANDOFF_READY` task) by registering in `## Active Claims` first.

| ID | Priority | Pool / Suggested | Task | Depends | Acceptance (summary) | Status |
|---|---|---|---|---|---|---|
| T001 | Must | Any (Done: Codex + Claude) | Verify toolchain, solution layout, agree contracts | — | actual version/build commands, contract signatures recorded, no Godot reference in Simulation | DONE |
| T002 | Must | Any (Active: Codex in T001-review) | Headless runner, test project, seeded RNG, IDs | T001 | build + same seed produces same baseline output; all 9 T002 acceptance tests below | BLOCKED (runtime verification) |
| T003 | Must | Any | World clock, grid map, resources, commands, tick scheduler | T002 | deterministic map, pause/step, invalid commands rejected; N1 + N4 below | TODO |
| T004 | Must | Any | Entity state, needs, health, death | T003 | food/water consumption, elapsed time, no dead actions, no negative stock | TODO |
| T005 | Must | Any | Utility AI and action lifecycle | T004 | hunger selects reachable food, rest and flee feasible, interrupt, no rapid oscillation | TODO |
| T006 | Must | Any | Spatial grid and bounded movement/pathfinding | T003,T005 | nearby results correct, blocked path handled, request budget enforced | TODO |
| T007 | Must | Any (N3 done by Antigravity) | Godot project, map renderer, camera, simulation bridge | T001; N3 (done); integrate T003 | actual snapshots displayed, camera/input independent of state, placeholders allowed; events via ReadEvents cursor (N3) | BLOCKED |
| T008 | Must | Any | Observer UI, pause/speed, selection, follow, event feed | T005,T007 | inspect and follow any living entity; tracking doesn't affect sim; dead history retained | TODO |
| T009 | Must | Any | Species data + predator/prey + resources | T004–T006 | 3–5 species including farm/wild animal and predator, kills consume/transfer real resources | TODO |
| T010 | Must | Any | Basic personality/emotions/memory | T005,T009 | two personalities can choose differently under defined inputs; danger event changes emotion; verify per-entity memory/relationship caps, eviction and durable history (Architecture §9) | TODO |
| T011 | Must | Any | Basic birth and ageing/death | T009 | new ID, valid parents/age, capped reproduction/resource checks | TODO |
| T012 | Should | Any | Seasons, temperature and resource growth | T003,T009 | predictable season progression; seasonal growth differs; UI reports season | TODO |
| T013 | Must | Any | Versioned save/load | T003 onward | state, pending action, RNG and time roundtrip; malformed save rejected; replay continuation test; N2 below | TODO |
| T014 | Must | Any + Reviewer | Profiling, correctness, optimization | T006–T013 (applicable) | measured scenarios 100+ entities; attempt 1,000 if stable; document bottlenecks and no regressions | TODO |
| T015 | Should | Any | Two settlements, humans/demons, farm/storage/jobs | T009,T010 | stock conservation, production/consumption and display; no inherent good/evil rule | TODO |
| T016 | Should | Any | Basic trade and guards | T015 | seller loses stock and receives payment, buyer inverse; guard responds to threat | TODO |
| T017 | Should | Any | Skills and reading | T005,T010 | gains from activities; interrupted read awards actual progress, no duplicate XP | TODO |
| T018 | Must | Any + Owner | Playtest, bugfix, export build and docs | all completed Musts | Windows build if toolchain supports it, 15–30 minute observer trial, known bugs and verified commands | TODO |
| T019 | Stretch | Any | Simple combat, magic, dragon, tame | T009,T010,T014 | separate small tasks; costs and valid targets; one dragon species/tame trust if time | TODO |
| T020 | Stretch | Any | Possession movement/interact | T008,T014 | take/release control, same ID/state, AI resumes, other agents continue | TODO |

## First assignments
Codex starts T001; Claude performs focused review after Codex proposes contracts. Antigravity can prepare placeholder assets and UI sketch without changing core contracts, then implement T007 after T001. Once interface is frozen, work in parallel on separate branches/worktrees. If quotas are low, do T001–T009 first and reduce optional scope.

## Requirements carried from T001 review (A007 rev 2, Accepted 2026-11-24)
Full rationale in PROGRESS.md (Claude Code T001 Re-Review). None of these are implemented or tested yet.

**T002 acceptance tests (all NOT RUN):**
1. Record real `dotnet --list-sdks` and Godot .NET `--version` output in PROGRESS.md.
2. `dotnet build Simulation/Simulation.csproj -c Release` succeeds separately from Game; references (incl. transitive) and DLL metadata contain no GodotSharp.
3. `dotnet test Tests/Simulation.Tests -c Release` passes.
4. C# RNG matches test vector: seed=42, systemTag=2, scopeId=17 → init `a95b939b9ebb349a`, draws `5fb88fd6df7259e5`, `b8b82154685001e7`.
5. EntityId starts at 1, increases monotonically, is never reused after death; counters fail explicitly before overflow.
6. Same seed in two separate processes gives identical StateHash every tick, including when the host sleeps and hash-collection insert/remove order differs.
7. StateHash uses the §4 canonical encoding: no reflection, GetHashCode or Dictionary/HashSet iteration order.
8. Event log fixture per docs/TESTING.md row H2 (read 1–50 in pages of 7; capacity=4 returns Gap [1,46]).
9. Calling the API from another thread or re-entrantly during Step throws InvalidOperationException before any mutation.

**T003:**
- N1 — A tick emitting more events than log capacity must not evict that tick's command outcomes. Either cap/aggregate events per tick and validate capacity ≥ per-tick cap, or keep a bounded outcome store queryable by CommandSequence. Test: one tick with 5,000 events plus one command, host reading every tick, always sees the command outcome.
- N4 — Define the source of trusted session grants: for MVP pass grants via simulation construction options and persist them in the save. Test: Observer cannot mutate; SpawnEntity without God grant is Rejected; payload cannot self-grant.

**T013:**
- N2 — History archive must be bound to the save timeline: on load ignore/truncate records with EventSequence ≥ checkpoint NextEventSequence; write the archive only at save boundaries (no file I/O inside Step). MVP may keep all death summaries inside the checkpoint. Test: save A → X dies → save B → load A → ReadHistory/ReadEntityDetails show no death of X.

**Antigravity, before T007:**
- N3 — **[DONE on `prep/N3-presentation-contract-sync`]** Updated `docs/PRESENTATION_PLAN.md`: drain `ReadEvents(cursor, limit)` per frame independently of drawing, handle `Gap` + `ReadHistory` milestone catch-up + `OutcomeUnknown` receipts, request `ReadSnapshot()` at most once per frame, and verified Gap UI / animation / optimization in isolated `Sandbox/PresentationTest/` (Mock Data only).
