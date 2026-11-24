# Prompt for Antigravity (copy and paste)

Work split (no file overlap):

| Who | Task | Files it may write |
|---|---|---|
| **Antigravity** | FIX-R1: fix the review findings for T015–T017 | `Simulation/**`, `Tests/**`, `docs/handoffs/FIX-R1.md`, its own entry in `PROGRESS.md` |
| **Claude Code** | T019 visuals: dragon sprite, pixelfx effects, inspector, event feed, God tools | `Game/**`, `tools/art/**`, `Assets/CREDITS.md`, `docs/handoffs/T019-visuals*`. **Does not touch `Simulation/**` or `Tests/**`** |

---

```
You are a developer on Fantasy World Simulator (Godot 4.7.2 .NET + pure C# deterministic simulation)
Repo: F:\Code\game-dev (GitHub lemonzx01/game-dev, branch main)

Before starting, read: AGENTS.md (team and workflow rules), ANTIGRAVITY.md, ARCHITECTURE.md (determinism, save/hash, budgets),
and especially docs/reviews/2026-11-codex-review-T015-T017.md (Codex's independent review: 12 findings with file:line references)

## Task: FIX-R1, fix the review findings for T015–T017
Fix in this order: High (1, 2) → Medium (3–11) → Low (12). For every finding:
- apply the "Minimal fix" from the report (or something better, explaining why in the handoff)
- write the regression test from the report's "Test" section, in a new file Tests/Simulation.Tests/FixR1Tests.cs
  and register it in Tests/Simulation.Tests/Program.cs (add the line .. FixR1Tests.Cases)
- the test must fail before the fix and pass after it (run it to check; don't just assume)
Findings 1 and 2 are related (trade routes); fix them together. For finding 2 you may pick the "bounded deterministic" option:
cap BFS expansions for long routes and treat a route over the cap as unreachable (then use the fix from finding 1), without making the search resume across ticks
Finding 10: cache the aggregation inside the simulation; keep the ReadSettlements() signature unchanged so you don't have to touch Game/**
If any finding can't be finished, record it as Open in the handoff with the reason; don't fake a pass

## Strict file boundaries (Claude Code is working on the Godot/presentation layer at the same time)
You may write ONLY: Simulation/**, Tests/**, docs/handoffs/FIX-R1.md, and your own entry in PROGRESS.md
You must NOT edit: Game/**, tools/**, Assets/**, Data/** (unless a fix really needs it; then explain in the handoff), and other people's handoffs or reviews
If a fix requires a change in Game/**, don't make it; write it as a "Request" in the handoff

## Simulation invariants (do not break)
- Deterministic: same seed → same result; no Math.Random, no wall-clock time, no Dictionary iteration order deciding outcomes
- No full-population scan per agent; no unbounded work per tick
- If you change the hash or save (fields, order, validation that changes format): bump SimulationWorld.StateSchema
  (in SimulationWorld.Save.cs), then re-pin the golden hash in Tests/Simulation.Tests/Program.cs, Replay(), like this:
    dotnet build Tests/Simulation.Tests -c Release
    B=Tests/Simulation.Tests/bin/Release/net8.0
    dotnet exec --runtimeconfig $B/Simulation.Tests.runtimeconfig.json $B/Headless.dll 42 1   (take the first line)
    replace baseline[0] == "..." and the message "schema N canonical byte order" with the new values
  If the hash doesn't change, don't re-pin; a golden that changes without the schema changing is a bug

## Workflow
1. git fetch, then create a worktree from the latest main:
   git worktree add worktrees/FIX-R1-antigravity -b fix/R1-review-T015-T017 origin/main
   (if the shared .git rejects it, clone into F:\Code\game-dev\worktrees\FIX-R1-antigravity instead and work on the same branch;
   Claude Code will fetch your branch from that folder and merge it)
2. Claim first: write docs/handoffs/FIX-R1.md with state IN_PROGRESS, agent, branch, and the files you will touch, then commit
3. Commit each finding or group of findings separately so it's easy to review. No force push, reset --hard, or deleting others' files
4. Before reporting done:
   - dotnet test Tests/Simulation.Tests -c Release -m:1 (every group must pass; record the number of groups)
   - dotnet build Tests/Benchmarks -c Release, then dotnet Tests/Benchmarks/bin/Release/net8.0/Benchmarks.dll 1000 --ticks 3000
     compare p99 with the old value (about 2.6 ms); if it gets noticeably worse, explain why
   - dotnet build Game/Game.csproj -c Debug (must still build, even though you didn't touch Game)
5. Merge (the owner allows self-review, A009): git fetch; merge origin/main into your branch; rerun the tests;
   then git merge --no-ff into main in F:\Code\game-dev and push origin main + your branch (never force)
   If you can't merge or push (no access to the shared .git or the network): set the handoff to REVIEW, list the branch and the folder it's in,
   and Claude Code will integrate it
6. Handoff: a table with finding no. → what was fixed → test name → status (Fixed/Open), plus the real commands you ran and their results
7. Report to the owner in Thai: which items were fixed, which are still open, the test results, and the benchmark numbers
```
