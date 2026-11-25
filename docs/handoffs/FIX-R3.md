# FIX-R3 handoff — Codex review of R1 (17 findings)

| Field | Value |
|---|---|
| Task | FIX-R3 (`docs/reviews/2026-11-codex-review-R1.md`) |
| Agent | Claude Code (author of R1; self-reviewed, A009) |
| Branch | `fix/R3-review-R1` (worktree `F:\Code\game-dev\worktrees\FIX-R3-claude`), from main `2d01b61` (R2 merged) |
| State | DONE — merged to main |
| Schema | 18 (golden re-pinned) |

| # | Sev | Fix | Regression (`Tests/Simulation.Tests/FixR3Tests.cs` unless noted) | Status |
|---|---|---|---|---|
| 1 | High | `TryStart`/`StartIdle` assign the incoming kind **before** changing the rest flag, so heal and fatigue rates come from the new state, exactly as after load. | `#1`: wounded sleeper → Rest → save/load; live and loaded HP equal on every tick for 50 ticks; 250% rate while resting; same hash. Mutation check: reverting the order fails (`live 300800 vs loaded 300650`). | Fixed |
| 2 | High | Noise comes from `Hurt` for every applied hit (kill or wound) instead of from `Emit`, so it no longer depends on event-log capacity. | `#2`: the same sleeping town with `MaxEventsPerTick` 17 vs 2048; per-tick sleep/HP traces are identical and someone does wake. | Fixed |
| 3 | High | `SleepOrReplan` rechecks eligibility (window, needs, guard duty) at the real bed-down tick (arrival or failed path); an expired intent finishes and replans. | `#3`: a "walking home to sleep" villager at 05:00 and a guard at 03:00 do not fall asleep. Mutation check: unconditional `BeginSleep` fails. | Fixed |
| 4 | Med | Noise is coalesced per cell per tick (many hits on one tile cost one query). | Covered by `#2` behaviour and the `--body` benchmark (p99 1.68 ms). | Partly fixed — an indexed sleeper set or budgeted continuation for very dense crowds is still open |
| 5 | Med | `ReanchorBody` does nothing with Body off: no vitals re-anchor or extra death bookings, as before R1. | `#5`: with Body off, the vitals anchor tick is unchanged across Rest/Wander switches. | Fixed |
| 6 | Med | Sleepers skip the heal scan and cast nothing. | `#6`: a trained sleeping healer beside a wounded sleeping townmate casts nothing. | Fixed |
| 7 | Med | A failed sleep path no longer returns out of `ProcessPaths`; it falls back to `SleepOrReplan` and continues the queue. | Code fix; behaviour covered by the R1/`#3` suites (no dedicated queue-throughput test). | Fixed |
| 8 | Med | The window is computed in whole ticks with `long` arithmetic; a window that rounds to nothing means "no sleep". | `#8`: 10-tick day with 101–109‰ → never sleeps; 7-tick day: the wake tick is always within (now, now + day]. | Fixed |
| 9 | Med | Hunger or thirst past the start threshold breaks a sleeper's commitment. | `#9`: a deer near water at night with high thirst wakes, drinks inside the window and survives. | Fixed |
| 10 | Med | Decision: awake creatures keep base healing (pre-R1 behaviour); healing is faster at rest (250%) and asleep (400%). The roadmap R1 text is updated to match. | Existing R1 `Healing` (exact awake and asleep rates). | Resolved (doc) |
| 11 | Med | With Body on, a creature below 500k HP gets an injured Rest candidate (score 220, above work, below sleep and urgent needs), and Rest lasts until it reaches 800k at the rest rate (at most a day). | `#11`: a villager at 300k at noon rests; a healthy one does not; it heals faster than awake. | Fixed |
| 12 | Med | Load rejects Sleep without a window (Body off or no pattern), resting without Rest/Sleep, and a saved death booking that disagrees with the restored vitals. | `#12`: two checksummed fixtures rejected with `SaveFormatException`; a real sleeper still loads. | Fixed |
| 13 | Med | With Body on, options reject any positive base rate that a species or rest percentage rounds down to 0. | `#13`: hunger 1 × dragon 30% is rejected; Body off accepts it. | Fixed |
| 14 | Med | The save test now uses a wounded subject through a state transition and compares HP every tick. | `#1` above. | Fixed |
| 15 | Med | Wild animals still sleep where they stand (no search for cover or herd). | — | Open — needs an owner decision; the roadmap records it as an accepted simplification for now |
| 16 | Low | The R1 tests now assert exact spawn counts (all species, 2 wolves, 6 villagers). | `R1Tests`. | Fixed (a night-vs-day hunt comparison is still open) |
| 17 | Low | The published action (`EntityRow.Action`) is Sleep only once asleep; walking home publishes Wander. | `#17`: for 240 ticks every villager publishes Sleep exactly when asleep. | Fixed |

## Also changed
- `R1Tests.Healing`: the awake subject is set to 600k (above the new injured threshold) so it measures awake healing.
- `BodyConfig` gains `InjuredRestHealth`, `RecoveredHealth`, `InjuredRestScore` (hashed).

## Evidence (2026-11-25)
- `dotnet test Tests/Simulation.Tests -c Release -m:1` → **134/134** (11 new FIX-R3 groups; golden schema 18).
- Benchmark 1,000 entities / 3,000 ticks `--body`: p50 0.94, p99 1.68 ms.
- Godot headless: T007, T008, GodTools, Settlement, Effects and Mood all pass. `Tests/WorldReport` builds.
