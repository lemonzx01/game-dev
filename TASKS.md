# Task board — initial state

T001 is DONE. A007 revision 2 is Accepted. See PROGRESS.md for verified toolchain and build evidence.

| ID | Priority | Default owner | Task | Depends | Acceptance (summary) | Status |
|---|---|---|---|---|---|---|
| T001 | Must | Codex + Claude review | Verify toolchain, solution layout, agree contracts | — | actual version/build commands, contract signatures recorded, no Godot reference in Simulation | DONE |
| T002 | Must | Codex | Headless runner, test project, seeded RNG, IDs | T001 | build + same seed produces same baseline output; all 9 T002 acceptance tests below | BLOCKED (runtime verification) |
| T003 | Must | Codex | World clock, grid map, resources, commands, tick scheduler | T002 | deterministic map, pause/step, invalid commands rejected; N1 + N4 below | TODO |
| T004 | Must | Codex | Entity state, needs, health, death | T003 | food/water consumption, elapsed time, no dead actions, no negative stock | TODO |
| T005 | Must | Codex | Utility AI and action lifecycle | T004 | hunger selects reachable food, rest and flee feasible, interrupt, no rapid oscillation | TODO |
| T006 | Must | Codex | Spatial grid and bounded movement/pathfinding | T003,T005 | nearby results correct, blocked path handled, request budget enforced | TODO |
| T007 | Must | Antigravity | Godot project, map renderer, camera, simulation bridge | T001; N3; integrate T003 | actual snapshots displayed, camera/input independent of state, placeholders allowed; events via ReadEvents cursor (N3) | TODO |
| T008 | Must | Antigravity | Observer UI, pause/speed, selection, follow, event feed | T005,T007 | inspect and follow any living entity; tracking doesn't affect sim; dead history retained | TODO |
| T009 | Must | Codex | Species data + predator/prey + resources | T004–T006 | 3–5 species including farm/wild animal and predator, kills consume/transfer real resources | TODO |
| T010 | Must | Codex | Basic personality/emotions/memory | T005,T009 | two personalities can choose differently under defined inputs; danger event changes emotion; verify per-entity memory/relationship caps, eviction and durable history (Architecture §9) | TODO |
| T011 | Must | Codex | Basic birth and ageing/death | T009 | new ID, valid parents/age, capped reproduction/resource checks | TODO |
| T012 | Should | Codex + Antigravity UI | Seasons, temperature and resource growth | T003,T009 | predictable season progression; seasonal growth differs; UI reports season | TODO |
| T013 | Must | Codex | Versioned save/load | T003 onward | state, pending action, RNG and time roundtrip; malformed save rejected; replay continuation test; N2 below | TODO |
| T014 | Must | Codex + Claude review | Profiling, correctness, optimization | T006–T013 (applicable) | measured scenarios 100+ entities; attempt 1,000 if stable; document bottlenecks and no regressions | TODO |
| T015 | Should | Codex + Antigravity | Two settlements, humans/demons, farm/storage/jobs | T009,T010 | stock conservation, production/consumption and display; no inherent good/evil rule | TODO |
| T016 | Should | Codex + Antigravity | Basic trade and guards | T015 | seller loses stock and receives payment, buyer inverse; guard responds to threat | TODO |
| T017 | Should | Codex | Skills and reading | T005,T010 | gains from activities; interrupted read awards actual progress, no duplicate XP | TODO |
| T018 | Must | Antigravity + Codex + owner | Playtest, bugfix, export build and docs | all completed Musts | Windows build if toolchain supports it, 15–30 minute observer trial, known bugs and verified commands | TODO |
| T019 | Stretch | Codex + Antigravity | Simple combat, magic, dragon, tame | T009,T010,T014 | separate small tasks; costs and valid targets; one dragon species/tame trust if time | TODO |
| T020 | Stretch | Antigravity + Codex | Possession movement/interact | T008,T014 | take/release control, same ID/state, AI resumes, other agents continue | TODO |

## First assignments
Codex starts T001; Claude performs focused review after Codex proposes contracts. Antigravity can prepare placeholder assets and UI sketch without changing core contracts, then implement T007 after T001. Once interface is frozen, work in parallel on separate branches/worktrees. If quotas are low, do T001–T009 first and reduce optional scope.

## Requirements carried from T001 review (A007 rev 2, Accepted 2026-11-24)
Full rationale in PROGRESS.md (Claude Code T001 Re-Review). For current T002 evidence see PROGRESS.md. Later task requirements remain assigned below.

**T002 acceptance criteria (results in PROGRESS.md):**
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
- N3 — Update docs/PRESENTATION_PLAN.md (parent checkout, lines ~73 and ~85): drain `ReadEvents(cursor, limit)` per frame and handle Gap instead of reading events from `ReadSnapshot()` after every Step; request at most one snapshot per frame.
