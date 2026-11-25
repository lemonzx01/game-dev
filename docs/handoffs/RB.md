# RB handoff — Ecology balance (measured with WorldReport)

| Field | Value |
|---|---|
| Task | RB (K10) |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `feat/RB-ecology-balance` (worktree `F:\Code\game-dev\worktrees\RB-claude`) |
| State | DONE (partial acceptance; see below) — merged to main |
| Schema | 19 (golden re-pinned) |

## Acceptance (TASKS: 5 seeds × 30 days)
- **No species above 50%:** met on all 10 seeds (max 43.8%).
- **Every opening species survives:** met on all 10 seeds at day 30.
- **Starvation under 25% of deaths:** met on **7 of 10** seeds (average 18.7%, worst 34.8%); the baseline was about 55%.
- **60 days:** wolves die out on 4 of 5 seeds. That is the open root cause, now task **RP** (predator viability).
- Full numbers and the step-by-step tuning: `docs/reports/RB-balance.md`.

## Changes
- **`Realism.Apply(options)`** (in `Simulation/SimulationWorld.Sleep.cs`): one preset for the game (`SimulationBridge`) and `Tests/WorldReport`, so both run the same world.
  - Body on
  - carcasses last 4,800 ticks
  - winter growth 40% down to -3 °C
  - berry bushes regrow every 240 ticks

  Scenario tests keep the plain defaults.
- **Breeding seasons** (`BodyConfig.SeasonalBreeding`): with Body on, wild (non-sapient) animals conceive only in spring and summer.
- **Species table** (`SpeciesCatalog.Default` and `Data/species.json`, kept identical):
  - rabbits: maturity 4 days, lifespan 50 days, pregnancy 1.5 days, breeding gap 4 days, litter 1, density cap 4, hunger 120%, thirst 110%
  - deer move 1 tick/tile
  - sheep tether 10
- **WorldReport:** per-species births and deaths by cause, a `SUMMARY` JSON line, and `--species <file.json>` to try species tables without rebuilding.
- **Tests:**
  - T019 tamed-wolf test: now applies its rocks one tick before spawning (its rabbit ID was read from the terrain command's event and was 0), and checks that the tamed wolf hunts wild game rather than requiring a kill.
  - EffectsAcceptance: the preview dragon spawns only in live mode (in headless runs it raised an alarm that made the wolf check pass or fail by chance).

## Evidence (2026-11-25)
- `dotnet test Tests/Simulation.Tests -c Release -m:1` → **136/136** (new RB groups: breeding seasons, preset). Mutation check: removing the breeding-season gate fails "deer do not court in autumn or winter".
- WorldReport: 10 seeds × 30 days and 5 seeds × 60 days with the final defaults (identical to the last override file, v14).
- Godot headless: T007, T008, GodTools, Settlement, Effects, Mood pass. `--body` 1,000-entity p99 1.63 ms.
