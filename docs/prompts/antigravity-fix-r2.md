# Prompt for Antigravity: FIX-R2, fix the T019 review findings (copy and paste)

Work split (no file overlap):

| Who | Task | Files it may write |
|---|---|---|
| **Antigravity** | FIX-R2: T019 review findings in the simulation (1–9, 13, 14, and the simulation-test part of 15) | `Simulation/**`, `Tests/**`, `docs/handoffs/FIX-R2.md`, its own entry in `PROGRESS.md` |
| **Claude Code** | FIX-R2-visuals: findings 10, 11, 12, the visual part of 15, and the inspector guard for finding 2 | `Game/**` only |

---

```
You are a developer on Fantasy World Simulator (Godot 4.7.2 .NET + pure C# deterministic simulation)
Repo: F:\Code\game-dev (branch main). Read AGENTS.md, ANTIGRAVITY.md, ARCHITECTURE.md first
(and docs/DEVELOPMENT_TOOLS.md; shared skills are available, e.g. save-systems, ai-behavior-trees-utility-ai)

Then read docs/reviews/2026-11-codex-review-T019.md (Codex's independent review of T019: 15 findings),
docs/handoffs/T019.md, and your own docs/handoffs/FIX-R1.md (same working style as last time; it was done well)

## Task: FIX-R2, fix the T019 review findings in the simulation
Fix in this order:
- High: 1 (re-check hunt eligibility at execution), 2 (reject impossible ownership, mana and trust on load)
- Medium: 3 (two feeders at once), 4 (unfinished taming attempt locks the animal), 5 (Heal doesn't cost fatigue),
  6 (damage overflow), 7 (mana/trust overflow), 8 (tamed animals leave the leash when chasing/fleeing),
  9 (repeated perception scans), 13 (bond history held in memory without a cap)
- Low: 14 (the save-mid-taming and retaliation tests don't prove their preconditions),
  15 simulation-test part only (check exact costs: mana, fatigue, health right before and after each cast;
  insufficient mana, out of range, dead target, same town → no deduction and no hit)

For every finding:
- apply the report's "Minimal fix" (or something better, explaining why in the handoff)
- write the regression test from the report into a new file Tests/Simulation.Tests/FixR2Tests.cs and register .. FixR2Tests.Cases
  in Tests/Simulation.Tests/Program.cs. The test must fail before the fix and pass after it (actually run it)
- finding 13 is architecture-level (the milestone archive has a cap of 256 per ARCHITECTURE.md §9). If it's too big to finish this round,
  do the rest first and record 13 as Open with a design proposal in the handoff, rather than half-implementing it
- finding 4: the "durable bond" (the town keeps a tamed animal even after its feeder dies) is the owner's design and must stay;
  fix only the unfinished attempt that never expires
- finding 8: tamed animals must stay inside the town radius on every movement path (wander/chase/flee), and an animal outside the radius
  must head back home. If any case should be an exception, write it in the handoff for the owner to decide

## Strict file boundaries (Claude Code is fixing the presentation layer at the same time)
You may write ONLY: Simulation/**, Tests/**, docs/handoffs/FIX-R2.md, your own entry in PROGRESS.md
You must NOT edit: Game/** (including InspectorPanel.cs and EffectsLayer.cs; Claude Code is taking those), tools/**, Assets/**, Data/**
(Data/species.json only if a fix really needs it; then explain in the handoff)
If an API used by Game must change (e.g. ReadAdventure), keep the old signature or add a new method,
and write a "Request" in the handoff instead of editing Game yourself

## Simulation invariants (do not break)
- Deterministic: same seed and same commands → same StateHash; no Math.Random, no wall-clock time, no Dictionary iteration order deciding outcomes
- No full-population scan per agent; no unbounded work per tick
- The schema is currently 14. If the hash or save changes, bump SimulationWorld.StateSchema to 15 and re-pin the golden hash in
  Tests/Simulation.Tests/Program.cs, Replay(), exactly the way you did in FIX-R1. Don't re-pin if nothing changed
- Player actions in T020 (possession) will reuse Attack/CastFirebolt/Feed, so make their checks/costs shared functions,
  not logic duplicated per caller

## Workflow
1. git fetch, then create a worktree from the latest main:
   git worktree add worktrees/FIX-R2-antigravity -b fix/R2-review-T019 origin/main
2. Claim first: docs/handoffs/FIX-R2.md with state IN_PROGRESS, branch, files in scope, then commit
3. Commit each finding or group of findings separately. No force push, reset --hard, or deleting others' files
4. Before reporting done:
   - dotnet test Tests/Simulation.Tests -c Release -m:1 (every group must pass; record the number; it's 101 now)
   - dotnet build Tests/Benchmarks -c Release, then dotnet Tests/Benchmarks/bin/Release/net8.0/Benchmarks.dll 1000 --ticks 3000
     and compare p99 with the old value (about 2.6 ms)
   - dotnet build Game/Game.csproj -c Debug must still pass
   - Godot headless: "%USERPROFILE%\scoop\apps\godot-mono\current\godot-mono.console.exe" --headless --path Game --import,
     then run res://Tests/EffectsAcceptance.tscn and res://Tests/SettlementAcceptance.tscn; both must PASS
     (after --import, revert .import files that only changed line endings: git checkout -- Game/Assets)
5. Merge: git fetch; merge origin/main into your branch; rerun the tests; then git merge --no-ff into main in F:\Code\game-dev
   Push origin main + your branch (never force). If you can't merge or push, set the handoff to REVIEW and Claude Code will integrate it
6. Handoff: a table with finding no. → what was fixed → test name → status (Fixed/Open), plus the real commands you ran and their results
7. Report to the owner in Thai: which items were fixed, which are still open and why, the test results, the benchmark numbers

Next task after this (don't do it in this round): T020 possession (docs/design/T020-possession.md)
```
