# Performance — T014 baseline (2026-11-24)

Measured, not estimated. Machine: Intel Core i5-11400H (6 cores / 12 threads, 2.7 GHz), 31.7 GB RAM, Windows 11, .NET SDK 10.0.404 (net8.0 target), Release build, single-threaded simulation, workstation GC.

## How to reproduce
```powershell
dotnet build Tests/Benchmarks -c Release
dotnet Tests/Benchmarks/bin/Release/net8.0/Benchmarks.dll 100 1000 5000 10000 --ticks 3000
dotnet Tests/Benchmarks/bin/Release/net8.0/Benchmarks.dll 1000 5000 10000 --ticks 3000 --tuned
```
Scenario: generated map (128×128 up to 1,000 animals, 256×256 above), seed 20261124, all four default species in a fixed mix (deer, rabbit, sheep, wolf), spawned through God commands, 300 warm-up ticks, then 3,000 measured `Step()` calls with a host draining events every tick (like the Godot bridge). Times are per tick. "Decisions pending/overdue" counts discretionary queue entries including superseded (stale) ones, so it overstates live lateness.

## Results

Default budgets
| entities (start) | map | ticks | p50 ms | p95 ms | p99 ms | max ms | ticks/s | alloc KB/tick | gen0/gen1/gen2 | working set MB | live (end) | path backlog max/end | decisions pending max/end | overdue end | save KB | save ms | load ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 100 | 128x128 | 3000 | 0.054 | 0.159 | 0.408 | 4.763 | 13319 | 3.7 | 4/3/2 | 56 | 109 | 0/0 | 176/113 | 0 | 673 | 189 | 102 |
| 1000 | 128x128 | 3000 | 2.225 | 3.504 | 4.388 | 6.385 | 438 | 45.8 | 27/6/3 | 110 | 1006 | 38/0 | 1348/1048 | 76 | 3533 | 55 | 107 |
| 5000 | 256x256 | 3000 | 3.240 | 4.976 | 6.636 | 14.051 | 293 | 65.9 | 45/16/9 | 258 | 4719 | 2164/0 | 6824/6052 | 4616 | 14105 | 137 | 187 |
| 10000 | 256x256 | 3000 | 6.482 | 10.281 | 13.911 | 38.137 | 146 | 74.3 | 51/19/6 | 558 | 9409 | 4227/0 | 16553/14342 | 13141 | 25942 | 181 | 207 |

Tuned budgets
| entities (start) | map | ticks | p50 ms | p95 ms | p99 ms | max ms | ticks/s | alloc KB/tick | gen0/gen1/gen2 | working set MB | live (end) | path backlog max/end | decisions pending max/end | overdue end | save KB | save ms | load ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1000 | 128x128 | 3000 | 1.806 | 2.672 | 3.352 | 7.227 | 555 | 46.8 | 30/8/6 | 78 | 1006 | 38/0 | 1348/1048 | 76 | 3533 | 190 | 183 |
| 5000 | 256x256 | 3000 | 8.039 | 10.988 | 12.864 | 42.317 | 123 | 132.8 | 79/17/9 | 257 | 4703 | 6/0 | 7371/5621 | 2541 | 15234 | 192 | 243 |
| 10000 | 256x256 | 3000 | 35.540 | 57.539 | 73.318 | 276.899 | 26 | 247.9 | 137/23/6 | 431 | 9153 | 0/0 | 13463/10839 | 6507 | 29962 | 215 | 306 |

Tuned = `DiscretionaryWorkBudget = max(256, population / 10 × 1.25)` and `PathNodeBudgetPerTick = max(4000, population × 4)`.

## Findings
1. **Target met:** 100 and 1,000 animals run far inside the 100 ms tick budget of 10 ticks/s (1,000: p99 ≈ 3–4 ms). 5,000 and 10,000 also run in real time at 1× on this machine.
2. **Bug fixed — stale decisions consumed the budget:** superseded Think entries counted against `DiscretionaryWorkBudget`, starving live decisions as the population grew. `Scheduler.Run` now counts only items that do work; stale ones are still dequeued in order (determinism unchanged).
3. **Bug fixed — saves with a decision backlog were rejected (T013):** load required all pending work to be in the future; carried-over discretionary work is legitimately overdue. Regression test added (`T013 save with carried-over decision backlog`).
4. **Allocations cut ~75 %:** candidate sorting in `Think` is now an in-place insertion sort and BFS path searches reuse world-owned buffers (stamp array, ring queue) instead of a Dictionary/Queue per search. At 1,000 animals: 191 → 47 KB/tick; at 5,000: 291 → 66 KB/tick, p50 1.95 → 1.53 ms (1,000), with identical simulation results (all tests and the golden hash unchanged).
5. **Budget sizing (configuration, not code):** with default budgets, decisions fall behind above ~2,500 animals (demand ≈ population / ThinkIntervalTicks per tick). For large worlds set `DiscretionaryWorkBudget` ≈ population / 10 × 1.25 and scale `PathNodeBudgetPerTick`. With tuned budgets 10,000 animals cost p50 ≈ 36 ms, p99 ≈ 73 ms (max spike ≈ 277 ms), enough for 1× but not higher speeds.

## Remaining bottlenecks (next optimisation targets)
- **Decision cost at 5k–10k:** each Think runs several grid queries (threat, prey, mate/density) through lambdas that allocate closures (≈ 250 KB/tick at 10k tuned). Next step: struct visitors or one shared perception pass per think, and staggering mate checks.
- **Spikes:** max tick time 40–280 ms at 5k–10k coincides with gen1/gen2 collections and bursts of births/deaths; reduce allocations further and consider spreading birth/death bursts.
- **Save size:** ~3 KB per animal (milestones and memories included); fine for now, revisit with compression or the §9 archive for long runs.
- Not measured yet: Godot rendering frame time with thousands of rows (T018), and multi-hour soak (§10 long-run).

## Regression guard
`dotnet test Tests/Simulation.Tests -c Release -m:1` → 68/68 groups pass after the optimisations (StateHash golden unchanged, two-process replay identical).
