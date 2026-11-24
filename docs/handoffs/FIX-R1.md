# FIX-R1 — Fix Codex review findings for T015–T017

| Field | Value |
|---|---|
| Task ID | FIX-R1 |
| Agent | Google Antigravity (self-reviewed per A009) |
| Branch | `fix/R1-review-T015-T017` |
| Worktree | `F:\Code\game-dev\worktrees\FIX-R1-antigravity` |
| State | DONE |
| StateSchema | 14 (bumped from 13; golden hash re-pinned in `Tests/Simulation.Tests/Program.cs`) |
| Source Review | `docs/reviews/2026-11-codex-review-T015-T017.md` |

## Files Modified
- `Simulation/Systems/Navigation.cs`
- `Simulation/Core/Scheduler.cs`
- `Simulation/SimulationWorld.cs`
- `Simulation/SimulationWorld.Entities.cs`
- `Simulation/SimulationWorld.Paths.cs`
- `Simulation/SimulationWorld.Save.cs`
- `Simulation/SimulationWorld.Settlements.cs`
- `Simulation/SimulationWorld.Skills.cs`
- `Simulation/SimulationWorld.Taming.cs`
- `Simulation/SimulationWorld.Trade.cs`
- `Tests/Simulation.Tests/FixR1Tests.cs`
- `Tests/Simulation.Tests/T016Tests.cs`
- `Tests/Simulation.Tests/Program.cs`
- `docs/handoffs/FIX-R1.md`

No `Game/**`, `tools/**`, `Assets/**`, or `Data/**` files were modified. No `Game/**` changes are required (`ReadSettlements()` signature unchanged).

## Findings Summary

| # | What was fixed | Test name | Status |
|---|---|---|---|
| 1 | Added `FailTradeOutbound` on terminal outbound failure (`ProcessPaths` & exhausted `OnPathBlocked` replans) to clear `TradePartner`, preserve `OnTrip` + cargo so `PlanWork` returns `WorkGoal.ReturnHome` immediately, deposit/release `TradersOut` once on `EndTrip` or `Die`, and suppress `(seller, buyer)` in `Settlement.BlockedBuyers` for `TradeConfig.UnreachableCooldownTicks` (default 300; persisted & pruned deterministically). | `FIX-R1 #1 unreachable buyer returns cargo, releases trader slot, bounds retries, resumes when opened` | Fixed |
| 2 | Used bounded deterministic option without cross-tick resumption, improved with targeted A* (`Navigation.FindPath`, Manhattan heuristic + `(F, H, Order)` tie-break) for single-target long routes (`TradeTravel`, `ReturnHome`), capped at `Math.Min(TradeConfig.RouteNodeBudget, MindConfig.PathNodeBudgetPerTick)` (default 2000) and counted in `remaining` / `LastPathExpansions`. Why better than undirected BFS: A* reaches distant towns across `128x128`–`4096x4096` maps within budget while strictly capping unreachable routes. | `FIX-R1 #2 long routes obey combined node budget, count expansions, serve locals, replay identically` | Fixed |
| 3 | Updated `ApplyTerrain` to reject non-passable terrain on any settlement centre and any terrain other than `Terrain.Grass` on any existing farm plot (`CommandReason.Occupied`). | `FIX-R1 #3 flooding empty farm plot or centre is rejected and work continues` | Fixed |
| 4 | Updated `MaybeBecomeGuard` to defer promotion whenever `e.CarryAmount > 0` or `e.Mind.Kind == ActionKind.Work` (any in-flight collection/work action). | `FIX-R1 #4 guard promotion defers until carried load is deposited` | Fixed |
| 5 | Widened `capacity + Level(...) / 2` addition to `long` before `Math.Min` against plot crop / node stock in `DoWork` (`Harvest` & `GatherFood`/`GatherWood`), skipping non-positive amounts. | `FIX-R1 #5 max CarryCapacity with skill bonus does not overflow negative` | Fixed |
| 6 | Added `SkillConfig.MaxScholarBonusPercentPerLevel = 1000` and `SkillConfig.MaxSupportedLevel = 1000` validated in `SkillConfig.Validate()`, and widened `GainXp` multiplication to `Int128` clamped to remaining `long.MaxValue - current`. | `FIX-R1 #6 scholarship XP bonus rejects extreme tuning and saturates at long.MaxValue` | Fixed |
| 7 | Added `ValidateCropScheduler` after loading settlements to verify `CropGrow` items are required (`EntityId == 0`), reference valid settlements/plots, have no duplicates, and match `plot.GrowScheduled`. | `FIX-R1 #7 invalid crop scheduler fixtures (missing plot, duplicate timer, missing timer) are rejected` | Fixed |
| 8 | Added `ValidateSettlementEntities`, `ValidateEntityReading`, `ReadSettlements` bounds/overlap checks, enum/index checks in `ReadEntity`/`ReadPayload`, live counter reconciliation (`Members`, `Guards`, `TradersOut`), unique/valid book keys & open-session timestamps, and unsigned `uint` length checks before narrowing in `ReadSkillsFrom`, `ReadSettlements`, `ReadFound`, and `ReadCommand`. | `FIX-R1 #8 settlement, trade, carry, reading save invariants and positive controls` | Fixed |
| 9 | Tightened `T016Tests.Trade()` to require `paid.Count == sold.Count > 0`, element-wise tick/party/price/amount match, exact stock conservation for both West and East (`Food` and `Wood`), `income > 0`, and per-actor cargo conservation by good; added partial-sale test. | `FIX-R1 #9 partial trade sale reconciles both towns, payments, unsold return, and slot` (+ strengthened `T016Tests.Trade`) | Fixed |
| 10 | Cached immutable `ImmutableArray<SettlementView>` in `SimulationWorld.ReadSettlements()` using a single O(entities) pass per completed tick, invalidated at tick completion (`Advance`), `Load` (`ReadPayload`), and test mutators, keeping signature unchanged. | `FIX-R1 #10 ReadSettlements caches one linear aggregation per tick and updates after promotion/death` | Fixed |
| 11 | Added per-seller cached deal (`CachedDealRevision == marketRevision`), shared buyer lists (`foodBuyers`, `woodBuyers`) rebuilt once per `marketRevision` in ascending settlement ID order, `InvalidateSettlementEconomy` tracking buyer threshold crossings (`FoodTarget`, `WoodTarget`, `FoodPrice`, `WoodPrice`) and seller state changes, plus explicit current-state checks in `LoadTrade`. | `FIX-R1 #11 TradeDeal shares lookup per economic revision with nearest and ascending-ID tie-break` | Fixed |
| 12 | Updated `Found` candidate loop to compute `distance = Math.Abs(dx) + Math.Abs(dy)` and skip `distance == 0 || distance > 3`. | `FIX-R1 #12 founding excludes grass tiles beyond Manhattan distance 3` | Fixed |

## Verification Commands & Actual Results

- **Pre-fix test verification:** Every regression test in `FixR1Tests.cs` was executed before its fix and confirmed to fail (including temporarily verifying `T016Tests.Trade` and `#9` fail when seller debit or `TradePaid` is omitted).
- **Schema 14 golden hash:**
  - `dotnet build worktrees/FIX-R1-antigravity/Tests/Simulation.Tests -c Release -m:1` -> 0 Warnings, 0 Errors
  - `dotnet exec --runtimeconfig .../Simulation.Tests.runtimeconfig.json .../Headless.dll 42 1` -> `1 DD6B0AB8F7FD17B3104F74439801DFE39CD90344FF74DB07A0D325FC44AD5A7F`
- **Simulation test suite (post-merge with `origin/main`):**
  - `dotnet test worktrees/FIX-R1-antigravity/Tests/Simulation.Tests -c Release -m:1` -> **101/101 test groups passed**, exit 0.
- **Benchmark (1,000 entities, 3,000 ticks, post-merge):**
  - `dotnet build worktrees/FIX-R1-antigravity/Tests/Benchmarks -c Release -m:1` -> 0 Warnings, 0 Errors
  - `dotnet worktrees/FIX-R1-antigravity/Tests/Benchmarks/bin/Release/net8.0/Benchmarks.dll 1000 --ticks 3000`:
    - `FIX-R1` run 1: `p50 1.801 ms | p95 2.589 ms | p99 3.058 ms | max 5.720 ms | 559 ticks/s | 88.0 KB/tick | gen 50/8/6 | 81 MB | live 1011`
    - `FIX-R1` post-merge run: `p50 2.040 ms | p95 3.762 ms | p99 6.073 ms | max 11.243 ms | 476 ticks/s | 88.0 KB/tick | gen 50/8/6 | 81 MB | live 1011`
    - Baseline `origin/main` (same machine/session): `p50 1.661 ms | p95 3.658 ms | p99 6.316 ms | max 15.624 ms | 530 ticks/s | 88.0 KB/tick | gen 50/8/6 | 85 MB | live 1011`
    - Note: Identical `88.0 KB/tick` allocation and GC profile as current `main`; variance in p99 (`3.06–6.07 ms` vs T019's `2.63 ms`) matches current `main` (`6.32 ms`) on this host (and the animal-only benchmark has 0 settlements, so settlement/trade/path additions are not on its hot path).
- **Godot project build (post-merge):**
  - `dotnet build worktrees/FIX-R1-antigravity/Game/Game.csproj -c Debug -m:1` -> Build succeeded, 0 Warning(s), 0 Error(s).

## Unresolved Issues
- None. All 12 findings fixed and verified.

## Exact Next Step
- Integrated into `main` (or ready to merge `--no-ff` from `fix/R1-review-T015-T017` if push fails).
