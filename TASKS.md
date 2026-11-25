# Task board — initial state

T001 and T002 are DONE. A007 revision 2 is Accepted. Any agent may take any unclaimed task (AGENTS.md, A008). Claims live in branches + `docs/handoffs/<TASK_ID>.md`; `scripts/agent-status.ps1` shows them. See PROGRESS.md for evidence.

**2026-11-24:** T003–T014 merged to main by owner instruction **without independent review** (all automated acceptance tests pass: 67/67 simulation groups + Godot acceptance). A later review by another agent is still recommended; findings go to new fix branches. Handoffs: docs/handoffs/T0xx.md.

| ID | Priority | Task | Depends | Acceptance (summary) | Status |
|---|---|---|---|---|---|
| T001 | Must | Verify toolchain, solution layout, agree contracts | — | actual version/build commands, contract signatures recorded, no Godot reference in Simulation | DONE |
| T002 | Must | Headless runner, test project, seeded RNG, IDs | T001 | build + same seed produces same baseline output; all 9 T002 acceptance tests below | DONE |
| T003 | Must | World clock, grid map, resources, commands, tick scheduler | T002 | deterministic map, pause/step, invalid commands rejected; N1 + N4 below | DONE (merged, 7487eb4) |
| T004 | Must | Entity state, needs, health, death | T003 | food/water consumption, elapsed time, no dead actions, no negative stock | DONE (merged, 6331c26) |
| T005 | Must | Utility AI and action lifecycle | T004 | hunger selects reachable food, rest and flee feasible, interrupt, no rapid oscillation | DONE (merged, 7f432b3) |
| T006 | Must | Spatial grid and bounded movement/pathfinding | T003,T005 | nearby results correct, blocked path handled, request budget enforced | DONE (merged, f4b2267) |
| T007 | Must | Godot project, map renderer, camera, simulation bridge | T001; N3 (done); integrate T003 | actual snapshots displayed, camera/input independent of state, placeholders allowed; events via ReadEvents cursor (N3) | DONE (merged, cb7d30f) |
| T008 | Must | Observer UI, pause/speed, selection, follow, event feed | T005,T007 | inspect and follow any living entity; tracking doesn't affect sim; dead history retained | DONE (merged, a51f0be) |
| T009 | Must | Species data + predator/prey + resources | T004–T006 | 3–5 species including farm/wild animal and predator, kills consume/transfer real resources | DONE (merged, a7719e0) |
| T010 | Must | Basic personality/emotions/memory | T005,T009 | two personalities can choose differently under defined inputs; danger event changes emotion; verify per-entity memory/relationship caps, eviction and durable history (Architecture §9) | DONE (merged, 5cb3116) |
| T011 | Must | Basic birth and ageing/death | T009 | new ID, valid parents/age, capped reproduction/resource checks | DONE (merged, e11b64b) |
| T012 | Should | Seasons, temperature and resource growth | T003,T009 | predictable season progression; seasonal growth differs; UI reports season | DONE (merged, 3959c5e) |
| T013 | Must | Versioned save/load | T003 onward | state, pending action, RNG and time roundtrip; malformed save rejected; replay continuation test; N2 below | DONE (merged, 6226e68) |
| T014 | Must | Profiling, correctness, optimization | T006–T013 (applicable) | measured scenarios 100+ entities; attempt 1,000 if stable; document bottlenecks and no regressions; independent review (not the author) | DONE (merged, 348af0c; independent review still pending) |
| T015 | Should | Two settlements, humans/demons, farm/storage/jobs | T009,T010 | stock conservation, production/consumption and display; no inherent good/evil rule | DONE (merged; self-reviewed per A009) |
| T016 | Should | Basic trade and guards | T015 | seller loses stock and receives payment, buyer inverse; guard responds to threat | DONE (merged; self-reviewed per A009) |
| T017 | Should | Skills and reading | T005,T010 | gains from activities; interrupted read awards actual progress, no duplicate XP | DONE |
| T018 | Must | Playtest, bugfix, export build and docs | all completed Musts | Windows build if toolchain supports it, 15–30 minute observer trial, known bugs and verified commands | IN_PROGRESS (merged: Play.cmd, God tools, save/load, standalone Windows export build/windows/FantasyWorldSimulator.exe; remaining: owner 15–30 min playtest using docs/PLAYTEST.md; known issues in docs/KNOWN_ISSUES.md) |
| T019 | Stretch | Simple combat, magic, dragon, tame | T009,T010,T014 | separate small tasks; costs and valid targets; one dragon species/tame trust if time | DONE (simulation; presentation follow-up after T018-env) |
| T020 | Stretch | Possession movement/interact | T008,T014 | take/release control, same ID/state, AI resumes, other agents continue | DESIGN READY (docs/design/T020-possession.md); implement after FIX-R1 merges |
| R1 | Should | Species bodies (per-species vitals) + sleep/wake cycle + healing | FIX-R2 | per-species hunger rates; night sleep at home, nocturnal wolves; sleepers wake on attacks; healing only fed+resting; no perf regression | DONE (merged; docs/handoffs/R1.md) |
| R2 | Should | Danger response: guards retreat when outmatched, alarm, group defence (K1) | FIX-R2 | lone guard vs dragon retreats and lives; 4+ guards engage together; alarm stops work then resumes | DONE (merged; docs/handoffs/R2.md) |
| R3 | Should | Houses (built from wood) and households; births need housing | R1 | wood cost + labour ticks; population <= housing; children near parents; save mid-construction | TODO |
| R4 | Could | Weather, cold/heat exposure, shelter | R1,R3 | seeded weather; unsheltered cold damage; weather changes crop growth | TODO |
| R5 | Could | Spoilage, supply/demand prices, migration, town abandonment | R3 | bounded prices; starving migrate; abandoned town milestone | TODO |
| FIX-R3 | Must | Fix Codex R1 review findings (17) | R2 merged | each finding fixed with a failing-then-passing regression, or recorded Open with reason | DONE (merged; 15 fixed, #4 partly, #15 open; docs/handoffs/FIX-R3.md) |
| RB | Should | Ecology balance measured with WorldReport (rabbit boom, deer collapse, starvation share, no trade) | FIX-R3 | over 5 seeds x 30 days: no species > 50% of creatures, starvation < 25% of deaths, every opening species survives; numbers in a report | DONE partial (2 of 3 targets on 10/10 seeds; starvation <25% on 7/10; docs/reports/RB-balance.md) |
| RP | Should | Predator viability: wolves find and catch prey, avoid towns, survive 60+ days | RB | over 5 seeds x 60 days wolves survive on every seed with no species > 50% and starvation < 25%; hunt success measured in WorldReport | TODO (docs/reports/RB-balance.md, Open) |

## How tasks are picked
No task is tied to a particular AI. Any agent picks the next task whose dependencies are met and that nobody has claimed (AGENTS.md Workflow 1–2), on its own branch/worktree. Tasks whose acceptance says "independent review" (T014, contract/architecture changes) need a reviewer other than the author. If quotas are low, do T001–T009 first and reduce optional scope.

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

**Before T007:**
- N3 — **DONE** (prep/N3-presentation-contract-sync, merged to main): Update docs/PRESENTATION_PLAN.md (parent checkout, lines ~73 and ~85): drain `ReadEvents(cursor, limit)` per frame and handle Gap instead of reading events from `ReadSnapshot()` after every Step; request at most one snapshot per frame.
