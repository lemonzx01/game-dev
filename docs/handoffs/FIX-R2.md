# FIX-R2 — T019 Review Fixes (Simulation)

| Field | Value |
|---|---|
| Task ID | FIX-R2 |
| Agent | Google Antigravity (self-reviewed per A009) |
| Branch | `fix/R2-review-T019` |
| Worktree | `F:\Code\game-dev\worktrees\FIX-R2-antigravity` |
| State | `DONE` (Finding 13 intentionally recorded `Open` with §9 design proposal) |
| StateSchema | 15 (bumped from 14; golden hash re-pinned in `Tests/Simulation.Tests/Program.cs`) |
| Source Review | `docs/reviews/2026-11-codex-review-T019.md` |
| Scope Split | Simulation findings `1–9, 13, 14, 15-sim` (presentation `10–12, 15-visual` completed by Claude Code in `docs/handoffs/FIX-R2-visuals.md`) |

## Files Modified
- `Simulation/Core/Scheduler.cs`
- `Simulation/Queries/SpatialGrid.cs`
- `Simulation/SimulationWorld.Combat.cs`
- `Simulation/SimulationWorld.Ecology.cs`
- `Simulation/SimulationWorld.Entities.cs`
- `Simulation/SimulationWorld.Life.cs`
- `Simulation/SimulationWorld.Mind.cs`
- `Simulation/SimulationWorld.Paths.cs`
- `Simulation/SimulationWorld.Save.cs`
- `Simulation/SimulationWorld.Taming.cs`
- `Simulation/SimulationWorld.Trade.cs`
- `Simulation/SimulationWorld.cs`
- `Simulation/Systems/MindConfig.cs`
- `Simulation/Systems/Navigation.cs`
- `Tests/Simulation.Tests/FixR1Tests.cs`
- `Tests/Simulation.Tests/FixR2Tests.cs`
- `Tests/Simulation.Tests/Program.cs`
- `Tests/Simulation.Tests/T019Tests.cs`
- `docs/handoffs/FIX-R2.md`

No `Game/**`, `tools/**`, `Assets/**`, or `Data/**` files were modified. `ReadAdventure` and all public simulation signatures remain unchanged (no `Game/**` requests needed). For upcoming T020 possession reuse, eligibility checks and cost deductions are factored into shared helpers: `CanCastFirebolt`/`CastFirebolt`, `CanHeal`/`CastHeal`, `CanHunt`, `IsEligibleFeeder`/`CanFeedAnimal`/`Feed`, and `Attack`/`Hurt`.

## Findings Summary

| # | Severity | What was fixed | Test name | Status |
|---|---|---|---|---|
| 1 | High | Extracted shared `CanHunt(hunter, prey)` predicate used by both `TryScanNearestPrey` and `HuntStep`. `HuntStep` re-checks `CanHunt` (or `CanHarm` for `ActionKind.Defend`) before moving, attacking, adding fatigue, or granting XP, finishing invalidated hunts cleanly on execution. | `FIX-R2 #1 hunt rechecks eligibility before movement, damage, fatigue or XP` | Fixed |
| 2 | High | `ReadAdventureFrom` rejects negative `ManaTick`, future `ManaTick > tick`, and negative `TamerId`. `ValidateSettlementEntities` enforces `Trust <= species.TameTrust`, `TamedTo > 0` requires full `Trust` and valid `TamerId` matching a living or dead (`deathIndex`) sapient entity (with matching settlement if living), while preserving pre-T009 non-tameable zero-trust saves. | `FIX-R2 #2 load rejects absent town, future mana, excessive trust and unallocated tamer, permits dead tamer` | Fixed |
| 3 | Medium | Extracted `IsEligibleFeeder`, `HasActiveAttemptClaim`, and `CanFeedAnimal`; revalidated in `TameStep` and at `Feed` execution before spending settlement food or drawing from `PsycheRng` when two feeders race on the same animal. | `FIX-R2 #3 two simultaneous feeders preserve second town food, trust, tamer and RNG` | Fixed |
| 4 | Medium | Added `EntityState.TameAttemptUntil` (`long`, schema 15 + save validation). `Feed` sets `TameAttemptUntil = now + CooldownTicks + GiveUpTicks` on incomplete attempts and clears it to `0` once `TamedTo > 0`, so refused/abandoned claims expire while completed durable bonds (`TamedTo`/`TamerId`) still survive feeder death. | `FIX-R2 #4 abandoned refused taming attempt expires while completed bond survives` | Fixed |
| 5 | Medium | Extracted shared `CanHeal`/`CastHeal(caster, target, now)` charging `AddFatigue(caster, CombatCfg.FatiguePerAttack, now)` after target, range, health-threshold, and mana validation; `TryHeal` delegates to `CastHeal`. | `FIX-R2 #5 heal charges exact mana and fatigue and leaves ineligible casts unchanged` | Fixed |
| 6 | Medium | Added `MagicConfig.Validate()` bound `BaseMana <= long.MaxValue - 100` and widened `Damage` multiplier scaling to `ScalePercent(Int128, Int128)` with `BigInteger` fallback and `long.MaxValue` saturation. | `FIX-R2 #6 damage multipliers use wide arithmetic without wrapping and stay monotonic` | Fixed |
| 7 | Medium | Fixed `ManaAt` overflow (`e.Mana + Math.Min(max - e.Mana, gain)`) and `Feed` trust cap (`animal.Trust + Math.Min(sd.TameTrust - animal.Trust, TrustPerFeed)`); verified near `long.MaxValue` across save/load. | `FIX-R2 #7 near-limit mana and trust saturate safely without negative wrap across save/load` | Fixed |
| 8 | Medium | Added `HasAnimalLeash`, `WithinAnimalLeash`, `StepHonorsLeash`, `ReturnHomeStep`, and optional `canVisit` predicate in `Navigation.FindNearest`. Leashed animals obey town radius across `ChaseStep`, `FleeStep`, `WanderStep`, `Eat`/`Drink` pathfinding, and return home monotonically when outside. (Adjusted `T019Tests.TamedWolf` mountain rocks at `(31,1),(30,2)` so wild rabbit remains inside town radius 10). | `FIX-R2 #8 tamed animals stay within town radius on chase/flee/eat/wander and return home when outside` | Fixed |
| 9 | Medium | Added `MindConfig.CandidateVisitBudgetPerTick` (default `16384`) and `QuerySliceBudget` (default `256`), `SpatialGrid.IGridVisitor` zero-allocation range queries + grid `Version`, per-tick scan result cache (`scanResultCache` keyed on tick, grid version, `perceptionVersion`), resumable `ScanPhase` / bucket+ID cursor persisted in schema 15, `Scheduler.Run` discretionary think deferral when visit budget is exhausted, and short-circuiting in `AlliesFighting`/`FightsBack`/`TryScanMate`. Why better than naive slice-only caps: preserves exact `(distance, ID)` tie-break semantics across dense pockets while dropping `Benchmarks` allocation churn from `88.0 -> 26.1 KB/tick` and `p99` latency from `~2.6 -> 2.05 ms`. | `FIX-R2 #9 dense perception queries obey candidate budget, short-circuit, cache per tick, and keep ID tie-breaks` | Fixed |
| 10–12 | Medium | Visuals/Inspector findings fixed in `FIX-R2-visuals` by Claude Code (`EffectsLayer`, `InspectorPanel`, `MapRenderer`, `EffectsAcceptance`). | `EFFECTS_ACCEPTANCE_PASS` | Out of scope (Claude Code) |
| 13 | Medium | Hot milestone archive cap (`256` per `ARCHITECTURE.md §9`) is architectural across `SimulationWorld.Entities.cs`, `Life.cs`, `Taming.cs`, `ReadHistory`, and N2 save-archive sync; left Open cleanly rather than half-implemented. See Design Proposal below. | — (proposed in handoff) | **Open** |
| 14 | Low | Strengthened `T019Tests.Dragon` (sequence-ordered `priorAggressors`, distinguishing `Defend` retaliation vs `Hunt`, no same-tick initial cascade), strengthened `T019Tests.SaveMidAction` (observed partial trust, cast, and live fight before save; per-tick hash, mana, feed rolls, and paged `AnimalTamed` history after load), plus dedicated `FixR2Tests #14` exercising two aggressors, timid bystander, aggressor death retargeting, and reload continuity. | `FIX-R2 #14 dragon retaliation with two aggressors/bystander/death and verified mid-action save replay` (+ strengthened `T019Tests.Dragon` & `SaveMidAction`) | Fixed |
| 15 (sim) | Low | Added `CanCastFirebolt` (shared with `CastFirebolt`) and test helpers; strengthened `T019Tests.Magic` (exact mana, fatigue, and HP deltas around every Firebolt and Heal tick) and added `FixR2Tests #15` verifying exact cast costs and zero debit/fatigue/hit/events for insufficient mana, out-of-range, dead targets, same-town offensive targets, and cross-town Heal. (Visual part done in `FIX-R2-visuals`). | `FIX-R2 #15 Firebolt and Heal charge exact costs and rejected targets cause zero debit or hit` (+ strengthened `T019Tests.Magic`) | Fixed |

## Finding 13 (Open) — §9 Milestone Archive Design Proposal
Per `ARCHITECTURE.md §9` (lines 116) and `docs/reviews/2026-11-codex-review-T019.md` item 13:
1. **Bounded hot cache (cap 256):** Keep at most `HistoryCacheCap = 256` recent `WorldEvent` milestones in memory (`hotMilestones`), preserving causal order and O(1) recent reads.
2. **External indexed `IMilestoneArchive`:** Flush oldest milestones in batches to an indexed archive (keyed by `Sequence` and `(Actor, Target)`) BEFORE evicting them from `hotMilestones`. Keep lightweight death summary lookup/archive index so dead-entity validation (`ReadEntityDetails`, `tamer` checks on save/load) never requires scanning unbounded hot arrays.
3. **Deterministic archive digest in `StateHash` / Save (N2 bound):** Maintain `archivedMilestoneCount` + rolling SHA-256 `archivedMilestoneDigest` updated deterministically on each archival flush. `StateHash()` mixes `(archivedMilestoneCount, archivedMilestoneDigest)` plus `hotMilestones` (bounded O(256) work per hash).
4. **Atomic checkpoint & N2 validation on Load:** Store `(archivedMilestoneCount, archivedMaxSequence, archivedMilestoneDigest)` in save payload and flush/verify archive checkpoint before releasing hot copies; reject mismatched/corrupt archives on load without mutating live state.
5. **Regression test:** Generate >256 milestones (births, deaths, tamings), evict live event ring (`ReadEvents` returns `Gap`), verify `hotMilestones.Count <= 256`, save/load, then page `ReadHistory` from old archived cursors and recent hot cursors asserting zero duplicates, exact records, and identical `StateHash`.

## Owner Decision Request (Finding 8 Leash Exceptions)
Currently tamed/domestic animals enforce `WithinAnimalLeash` (town `Radius`) on **every** path (`ChaseStep`, `FleeStep`, `WanderStep`, `Eat`/`Drink` pathfinding) and take `ReturnHomeStep` whenever outside radius. If you want an exception later, please decide:
- Should a thirsty/starving tamed animal be allowed to exit `Radius` when no water/food exists inside town?
- Should a tamed animal (`TamedWolf`) be allowed 1–2 tiles outside `Radius` when actively retaliating (`ActionKind.Defend`)?

## Verification Commands & Actual Results

- **Pre-fix failure verification:** Regression tests `#1` through `#9` were executed before their respective fixes and verified to fail (`task-482`, `task-526`, `task-632`, `FixR2Tests #6/#7/#9`). Tests `#14` and `#15` directly verified caught fixture/cornered-bystander/checkpoint failures during stabilization before final pass.
- **Schema 15 golden hash:**
  - `dotnet build F:\Code\game-dev\worktrees\FIX-R2-antigravity\Tests\Simulation.Tests -c Release --no-restore` -> 0 Warnings, 0 Errors
  - `dotnet exec --runtimeconfig .../Simulation.Tests.runtimeconfig.json .../Headless.dll 42 1` -> `1 6169E3B9DB1C72B55C0C7F35FAEC8177A21797E8121BB03010FA8A5C33BF9F9F`
- **Simulation tests (post-merge with `origin/main`):**
  - `dotnet test F:\Code\game-dev\worktrees\FIX-R2-antigravity\Tests\Simulation.Tests -c Release -m:1` -> **112/112 test groups passed**, exit 0.
- **Benchmark (`1000` entities, `3000` ticks, post-merge with `origin/main`):**
  - `dotnet build F:\Code\game-dev\worktrees\FIX-R2-antigravity\Tests\Benchmarks -c Release` -> 0 Warnings, 0 Errors
  - `dotnet F:\Code\game-dev\worktrees\FIX-R2-antigravity\Tests\Benchmarks\bin\Release\net8.0\Benchmarks.dll 1000 --ticks 3000`:
    - `p50 0.936 ms | p95 1.294 ms | p99 1.912 ms | max 9.044 ms | 1008 ticks/s | 26.1 KB/tick | gen 19/7/5 | 80 MB | live 999 | path backlog 180/0 | decisions pending 1459/1057 | overdue end 12 | save 3652 KB / 143 ms / load 83 ms`
    - Comparison vs pre-FIX-R2 baseline (`~2.6 ms` p99, `88.0 KB/tick`): allocation dropped ~70% (`88.0 -> 26.1 KB/tick`) and `p50`/`p99` improved (`~1.7 -> 0.94 ms` p50, `~2.6 -> 1.91 ms` p99) via zero-alloc `IGridVisitor` scans and per-tick cache.
- **Godot build & headless acceptance:**
  - `dotnet build F:\Code\game-dev\worktrees\FIX-R2-antigravity\Game\Game.csproj -c Debug` -> Build succeeded, 0 Warning(s), 0 Error(s).
  - `%USERPROFILE%\scoop\apps\godot-mono\current\godot-mono.console.exe --headless --path Game --import` -> succeeded (`.import` changes reverted afterward with `git checkout -- Game/Assets`).
  - `res://Tests/EffectsAcceptance.tscn` -> `EFFECTS_ACCEPTANCE_PASS tick=8`
  - `res://Tests/SettlementAcceptance.tscn` -> `SETTLEMENT_ACCEPTANCE_PASS tick=3002 Ashford:pop10/food217/wood62 Cinderhold:pop10/food207/wood50`
