# Prompts for ChatGPT / Codex, round 3 (copy and paste)

Current split (nobody writes the same file):

| Who | Task | Files it may write |
|---|---|---|
| **Antigravity** | R2: guards retreat, alarm, group defence | `SimulationWorld.Combat.cs`, `Trade.cs`, `Settlements.cs`, `Danger.cs`, `Psyche.cs`, `Tests/Simulation.Tests/R2Tests.cs`, and minimal edits to shared files |
| **ChatGPT / Codex** | Prompt 5: review R1 (read-only) → Prompt 6: WorldReport, a world-report tool for tuning realism | Prompt 5: `docs/reviews/**` only. Prompt 6: new `Tests/WorldReport/**`, `docs/handoffs/WORLDREPORT.md`, `docs/WORLD_REPORT.md` |
| **Claude Code** | Integrating everyone's work, R3 design (houses/households) once R2 merges | `docs/**` (except reviews), `TASKS.md`, `PROGRESS.md` |

Run them in order 5 → 6, or in parallel in separate folders (they share no files).
If you can't push again, set the handoff to REVIEW and give the branch and folder; Claude Code will fetch and merge.

---

## Prompt 5: Independent review of R1 (read-only)

```
You are a developer on Fantasy World Simulator, repo F:\Code\game-dev (branch main). Read AGENTS.md first.
R1 (per-species bodies, sleep/wake cycle, faster healing while resting) was self-reviewed by Claude Code (A009).
Review it independently, in the same style and depth as your T015–T017 and T019 reviews (docs/reviews/*).

Scope: docs/handoffs/R1.md, docs/design/realism-roadmap.md (R1),
Simulation/SimulationWorld.Sleep.cs, Simulation/Core/Vitals.cs (HungerPct/ThirstPct/RegenPct in VitalsAnchor),
the EntityState.Vitals property in SimulationWorld.Entities.cs, the Sleep parts of SimulationWorld.Mind.cs / Paths.cs / Save.cs / SimulationWorld.cs (Emit → OnNoise),
Simulation/Core/Species.cs (HungerPercent, ThirstPercent, Sleep), Data/species.json, Tests/Simulation.Tests/R1Tests.cs,
and the Sleep → lying pose in Game/Presentation/MapRenderer.cs

Look especially for:
- the vitals anchor stamping its rates through the property setter: are there places that build/copy VitalsAnchor that bypass the setter, so rates are lost?
  Does ApplyBodyRates after load produce exactly the same vitals as before the save? Does ScheduleDeath book the right death tick when the rates change?
- the fatigue and vitals anchors when the action changes while resting (Sleep → Rest/Flee/Idle): is any period counted at the wrong rate?
- sleep windows that wrap past midnight, WakeTick when "now" falls exactly on a boundary, and a TicksPerDay that doesn't divide evenly
- OnNoise called inside Emit: re-entrancy (FinishAction → Emit → ...), work per Attacked event in a crowded spot, and determinism
- creatures that never wake or sleep repeatedly (e.g. home unreachable, guard promoted while asleep, a trader on a trip, juveniles)
- BodyConfig.Enabled = false must behave exactly as before R1 (equivalent to the old hash/behaviour except the schema)
- tests that don't really check what they claim

Rules: read-only review. Do NOT edit any files except your report (Antigravity is editing Simulation/** and Tests/** for R2)
Write the report to docs/reviews/2026-11-codex-review-R1.md, using the same format as before:
severity (High/Medium/Low), file:line, why it is a problem, minimal fix, test that should be added
Run dotnet test Tests/Simulation.Tests -c Release -m:1 and record the result
Work on branch review/R1-codex (git worktree add worktrees/review-R1-codex -b review/R1-codex origin/main,
or clone into that folder if the shared .git can't be used), commit only the report
Report to the owner in Thai: how many findings and which ones are urgent
```

---

## Prompt 6: WorldReport, a world-report tool for tuning realism

```
You are a developer on Fantasy World Simulator (C# deterministic simulation), repo F:\Code\game-dev. Read AGENTS.md and docs/design/realism-roadmap.md first.
Problem: when tuning realism (R1–R5) we can't see the whole world over long periods: does the population survive, how do creatures die,
is there enough food in the towns, and how much sleep and work happens. We need a tool that runs the world for many days and summarises it.

## Task: WorldReport (new console tool; does not change the simulation)
1. Create a new project Tests/WorldReport (net8.0, console, ProjectReference to Simulation only, like Tests/Benchmarks)
   - command: dotnet run --project Tests/WorldReport -c Release -- --days 30 --seed 1 [--size 128] [--animals 120] [--settlers 10] [--no-body] [--out report.md]
   - build the world the same way the game does (Game/Integration/SimulationBridge.cs: two towns, Ashford (Human) and Cinderhold (Demon),
     120 starting animals, Body = BodyConfig.Realistic). Copy the setup logic into the tool (don't reference Game/Godot);
     if a function needed is internal, use only public APIs (ISimulation / Read* / SubmitCommand)
2. Collect the data each day (cheaply, using public APIs only: ReadSnapshot, ReadEvents with a cursor, ReadSettlements, ReadEntityDetails as needed):
   - population per species (alive) at the end of each day, births and deaths per day by DeathCause
   - per town: Food, Wood, Coins, Population, Guards at the end of each day
   - the count of ActionStarted by ActionKind per day (shows the share of Sleep/Work/Eat/Hunt/Read/Tame)
   - important events: AnimalTamed, SpellCast, TradeSold, GuardAlert, WokeUp
   - the average and p99 time per tick (Stopwatch)
3. Output: a markdown file (default docs/reports/world-<seed>-<days>d.md, but do NOT commit the report the tool generates; only an example)
   with a summary table + day-by-day tables + a short "warnings" section, e.g. a species that went extinct, a town with Food 0 for 3+ days in a row,
   deaths by Starvation over X%, a population that keeps rising without limit
   plus a CSV (per day) for making charts
4. Deterministic: the same seed and settings must give the same report (except the timing numbers), checked by running twice and comparing (ignoring the time lines)
5. Write docs/WORLD_REPORT.md (how to use it + how to read the warnings) and docs/handoffs/WORLDREPORT.md (what was done, real results)
   and include an example summary from a real run: --days 30 --seed 1, with Body on and off, and compare (e.g. deaths, food, the share of Sleep)

## Strict file boundaries
You may write ONLY: Tests/WorldReport/** (new), docs/WORLD_REPORT.md, docs/handoffs/WORLDREPORT.md, docs/reports/example-*.md (one example), your own entry in PROGRESS.md
You must NOT edit: Simulation/**, other projects in Tests/**, Game/**, Data/**
If you need an API the simulation doesn't have, write it as a "Request" in the handoff; don't edit the simulation yourself

## Workflow
Branch feat/world-report (git worktree add worktrees/world-report-codex -b feat/world-report origin/main, or clone into that folder)
Claim with docs/handoffs/WORLDREPORT.md (IN_PROGRESS), commit only on your branch, no force push
Check: dotnet build Tests/WorldReport -c Release; run it with real values; dotnet test Tests/Simulation.Tests -c Release -m:1 must still pass everything
Merge and push if you can; if you can't, set REVIEW and Claude Code will integrate it
Report to the owner in Thai: what the tool shows, plus the most interesting finding from the 30-day example (e.g. "deer go extinct by day 20")
```
