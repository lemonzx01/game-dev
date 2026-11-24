# Development Roadmap — 30-day initial sprint

Assumptions: solo owner with three AI tool groups, limited quotas, placeholder/licensed assets, substantial daily time if available. Dates are relative working days, not guaranteed calendar commitments. No need to purchase $200 before measuring actual blockers. If less time, extend schedule; do not skip acceptance tests.

| Days | Primary goal | Must / gate |
|---|---|---|
| 1–3 | Toolchain, repository, architecture contracts, headless runner | T001–T002; build and sample deterministic tick |
| 4–9 | World clock, map, data, needs, utility AI, spatial queries | T003–T006; agents survive or die for valid reasons |
| 10–14 | Godot view, camera, pause/speed, selection, animals, event log | T007–T009; first visible autonomous world |
| 15–20 | Personality, basic emotions, memory, reproduction, seasons | T010–T012 as time permits; prioritize stable core |
| 21–24 | Save/load, integration, benchmark and correction | T013–T014; no critical correctness bugs |
| 25–27 | Optional settlements, trade, guards, skills | T015–T017 only if Must gate passes |
| 28–30 | Playtest, bugfix, Windows build, documentation and handoff | T018; no new features in final days |

Schedule dependency: start save schema and tests early, implement/recheck through sprint. Any agent can take any task; work can parallelize across separate branches/worktrees once interfaces are agreed, with no overlapping edits to the same files. If schedule slips, defer Should/Stretch before dropping Must tests. Early playable milestone: simple map + moving agents + meaningful needs + observer controls.

## Phase 2–8 (after prototype; sequencing, not calendar promises)
2. Robust life/mind, relationships, skills, books, families, species ecology, seasons.
3. Settlements, jobs, real resource inventories, markets, guards, caravans and laws.
4. Civilizations, politics, alliances, armies, war, leadership, conquest and history.
5. Magic, legendary creatures, taming, breeding/evolution, dungeons, demon lord.
6. Full RPG possession, equipment, combat, party, command, expanded God tools.
7. Observer story tools, history, cultural/knowledge evolution, performance optimization and mod support where practical.
8. Multiple worlds, portals, trade/war/migration across worlds, larger population benchmarks.

Phases can overlap after interfaces stabilize. Maintain playable builds. No milestone promises 100k population or production-quality systems without measurements.
