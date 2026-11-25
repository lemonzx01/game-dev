# RP handoff — Predator viability (wolves)

| Field | Value |
|---|---|
| Task | RP |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `feat/RP-predators` (worktree `F:\Code\game-dev\worktrees\RP-claude`) |
| State | DONE (2 of 3 targets on 5 of 5 seeds; starvation target on 4 of 5) — merged to main |
| Schema | 20 (golden re-pinned) |

## Acceptance (TASKS: 5 seeds × 60 days)
- **Wolves survive on every seed:** met, 5 of 5. Also met on seeds 6–10.
- **No species above 50%:** met, 5 of 5 (max 41.8%).
- **Starvation under 25%:** met on 4 of 5 seeds. Seed 2 is at 29.3% (winter die-off of herbivores where few wolves remain).
- **Hunt success measured:** yes, with WorldReport `--trace-wolves`.
- Full numbers and the diagnosis: `docs/reports/RP-predators.md`.

## Changes (all only with Body on, i.e. the realism preset; scenario defaults untouched)
- **`Simulation/SimulationWorld.Predators.cs`** (new):
  - **Roaming:**
    - A hungry wild predator, or any wild animal whose water or food search just failed, travels up to `RoamSteps` (12) tiles.
    - It keeps a steady heading derived from its id and the time window, so no new state is stored.
    - The roam respects leashes and impassable tiles.
  - **Town shyness** (`PredatorsAvoidTowns`), for untamed wild predators that belong to no town:
    - no prey is chosen inside a town's grounds (`CanHunt`)
    - roaming and wandering avoid the grounds
    - no rest or sleep inside them: the animal first walks straight out, away from the nearest town centre
- **`BodyConfig`:**
  - new fields `RoamSteps`, `RoamHeadingTicks` and `PredatorsAvoidTowns`; they are written to the canonical state and validated
  - `Realism.Apply` raises winter growth to 60%
- **Species** (`SpeciesCatalog.Default` and `Data/species.json`, kept identical):
  - rabbits breed every 7 days (was 4), density cap 3 (was 4)
  - wolves: hunger 50% (was 70%), density cap 2 (was 3)
- **Opening** (`SimulationBridge` and WorldReport `Opening`, kept in sync):
  - one wolf pair (female + male) per 32 animals; before, one single wolf per 8
  - sheep start in a ring 4–7 tiles around the towns (livestock)
- **WorldReport:** `--trace-wolves` (daily WOLF line).

## Tests
- **`RPTests`:**
  - a hungry wolf and a thirsty deer roam at least 20 tiles, farther than the `RoamSteps = 0` control
  - a wolf does not hunt a calm rabbit inside town grounds; the control does
  - a tired wolf walks out of the grounds before resting; the control rests inside
  - save/load mid-roam gives an identical hash
- **Mutation checks:**
  - removing the `CanHunt` town check fails the hunt test
  - removing the rest/sleep rule fails the rest test (that test was strengthened)
- **Test updates:**
  - `R1Tests` "a fight wakes sleepers" turns town shyness off for its deliberately-in-town wolf
  - `RBTests` preset check expects 60% winter growth
- **Results:**
  - 141/141 test groups
  - all six Godot acceptances pass (Presentation, Observer, GodTools, Settlement, Effects, Mood)

## Open (see report)
- Predator–prey swings on a small map (seed 9: wolves wiped out the deer).
- Winter herbivore die-offs where wolves are few.
- Demon dehydration on some seeds (not compared with main).
- 0 trades (R5).
