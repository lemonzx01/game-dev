# Prompt for Antigravity: R2, guards retreat and the town defends together (copy and paste)

Work split (running in parallel with Claude Code's R1; read the boundaries carefully):

| Who | Task | Files it may write |
|---|---|---|
| **Antigravity** | R2: danger response (guards retreat when the foe is far stronger, alarm, group defence, avoiding dangerous places) | `Simulation/SimulationWorld.Combat.cs`, `SimulationWorld.Trade.cs`, `SimulationWorld.Settlements.cs`, new `Simulation/SimulationWorld.Danger.cs`, `Simulation/SimulationWorld.Psyche.cs`, new `Tests/Simulation.Tests/R2Tests.cs`, `docs/handoffs/R2.md` |
| **Claude Code** | R1: per-species vitals, sleep/wake cycle, healing | `Simulation/Core/Vitals.cs`, `Simulation/Core/Species.cs`, `Data/species.json`, `SimulationWorld.Life.cs`, `SimulationWorld.Entities.cs`, new `SimulationWorld.Sleep.cs`, `Tests/Simulation.Tests/R1Tests.cs` |
| **Shared (both may edit; keep the change small)** | | `SimulationWorld.Mind.cs`, `Simulation/Systems/MindConfig.cs`, `SimulationWorld.Save.cs`, `SimulationWorld.cs`, `SimulationOptions.cs`, `Core/Commands.cs`, `Tests/Simulation.Tests/Program.cs`, `TASKS.md`, `PROGRESS.md` |

Rules for the shared files (so the merge stays easy):
- **`SimulationWorld.Mind.cs`:**
  - R2 changes **only** the Defend/Flee candidate block in `Think` (starting at the comment `// Guards fight threats inside the town (T016)...`) and the `case ActionKind.Defend:` block in `TryStart`
  - put new logic in `SimulationWorld.Danger.cs` and call it from there
  - R1 changes other parts (adding the Sleep candidate, and `case ActionKind.Sleep` in `Step`)
- **New `ActionKind` values (reserved in advance):** R1 uses `Sleep = 12`; R2 uses `Gather = 13` if it needs one. Don't change existing values.
- **New `EventKinds` values (reserved):** R1 uses 225–229; R2 uses 230–234.
- **Save/hash:** write your own fields through your own helper (e.g. `WriteDanger(w, s)`) and add a single call line in `Save.cs` / the hash writer. Don't reorder existing fields.
- **`StateSchema`:**
  - R1 uses **16**, R2 uses **17**
  - whoever merges into main second merges main into their branch first, sets the schema to the higher number, re-pins the golden hash in `Tests/Simulation.Tests/Program.cs`, `Replay()`, and runs every test again
- **`Program.cs`:** add only your own line, `.. R2Tests.Cases`.

---

```
You are a developer on Fantasy World Simulator (Godot 4.7.2 .NET + pure C# deterministic simulation)
Repo: F:\Code\game-dev (branch main). Read AGENTS.md, ANTIGRAVITY.md, ARCHITECTURE.md, docs/DEVELOPMENT_TOOLS.md
Then read docs/design/realism-roadmap.md (section R2), docs/KNOWN_ISSUES.md (K1), docs/handoffs/T016.md, T019.md, and your own FIX-R2.md
and read docs/prompts/antigravity-r2.md (this file); the table and "Rules for the shared files" at the top are mandatory
Claude Code is doing R1 (sleep, per-species vitals) at the same time

## Task: R2, a realistic response to danger
Problem (K1): when a dragon comes into town, the guard fights alone until it dies; a new guard volunteers and dies the same way; the town loses its adults
Goal:
1. Threat assessment (deterministic, integer math, no overflow): compare the foe's estimated strength (AttackPower, remaining health, DamageTakenPercent)
   with our side's strength (guards nearby + townmates who are fighting alongside). If the foe is far stronger (threshold in a config, e.g. 3x),
   - the guard does not go in alone: it raises the alarm (a new event) and retreats to the town centre/away from the foe instead of dying
2. Alarm: villagers within the town radius stop work/reading and gather at the centre or flee (use Flee or a new Gather=13)
   and go back to normal when the threat leaves or dies (end the alarm by time or when the threat is gone; deterministic)
3. Group defence: once there are enough guards plus brave townmates at the centre (strength meets the threshold), they attack together (use the T019 ally bonus)
4. Remember dangerous places: survivors remember where a strong threat was (T010 memory) and avoid that area for a while
5. Smaller threats like a single wolf are still fought by the guard as before (existing T016 tests must still pass)

## Required tests (Tests/Simulation.Tests/R2Tests.cs, register .. R2Tests.Cases)
- one guard vs a full-health dragon → the guard raises the alarm and retreats, and is still alive after N ticks (with the old behaviour it would die)
- 4+ guards and brave villagers vs a badly wounded dragon → they attack together (count ActionStarted Defend/Attacked from at least 2 of them)
- one wolf comes into town → the guard fights it alone as before; no alarm raised
- during the alarm villagers have no Work/Read ActionStarted; after the threat dies or leaves they go back to work within a set time
- determinism: save mid-alarm/mid-retreat → continues identically (StateHash)
Each test must fail before the change and pass after it (actually run it)

## Invariants
- deterministic, no Math.Random or wall-clock time; no full-population scan per agent (use grid.Query within a radius, short-circuit early)
- no unbounded work per tick; reuse the shared functions from FIX-R2 (Attack/CanHarm/Damage); don't duplicate logic
- if the hash or save changes, schema = 17 (see the merge rules above)

## Workflow
1. git fetch, then git worktree add worktrees/R2-antigravity -b feat/R2-danger origin/main
2. claim: docs/handoffs/R2.md (IN_PROGRESS, branch, files) and commit; change the R2 row in TASKS.md to IN_PROGRESS
3. before reporting done: dotnet test Tests/Simulation.Tests -c Release -m:1 (everything passes),
   the 1000-entity benchmark (compare p99 with the old value of about 2.6 ms), dotnet build Game/Game.csproj -c Debug,
   Godot headless Settlement + Effects acceptance (after --import, revert line-ending-only changes: git checkout -- Game/Assets)
4. merge: git fetch; merge origin/main into your branch (if R1 is already in, follow the schema/golden rules above); run every test again;
   merge --no-ff into main and push. If you can't push, set REVIEW and Claude Code will push it
5. handoff: design, numbers used (thresholds), tests + real results, what's still open
6. report to the owner in Thai
```
