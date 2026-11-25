# WORLDREPORT

| Field | Value |
|---|---|
| Agent / branch / state | Codex / feat/world-report / REVIEW |
| Base | Local origin/main at 417c011 |
| Files | Tests/WorldReport/{WorldReport.csproj,Opening.cs,Program.cs,Report.cs}, docs/WORLD_REPORT.md, docs/reports/example-world-1-30d.md, this handoff |
| Changes | Console host mirroring the game opening, daily snapshot census and town inventories, cursor-based event counts, daily Step timing and Markdown/CSV output. All simulation access uses public APIs. |

## Verification

- `dotnet build Tests/WorldReport -c Release` succeeded with zero warnings and errors. Used a temporary NuGet feed from installed packages for offline restore.
- `dotnet run --project Tests/WorldReport -c Release --no-build -- --days 30 --seed 1 --out Tests/WorldReport/bin/evidence/on.md`. Repeated with `--no-body` to off.md and then repeated both to separate files.
- For each pair, compared full Markdown up to the timing section and every CSV field other than mean_ms/p99_ms. Both pairs matched across all 30 days. Checked every daily closing census against opening balance plus births minus deaths in both modes. No rejected commands, dropped events or cursor gaps on these runs.
- `dotnet test Tests/Simulation.Tests -c Release -m:1` passed 117/117 groups, 0 failed.
- At day 30, Body on/off yielded 348/310 alive, 645/708 births, 437/538 deaths, 96/193 predation deaths and 252/253 starvation deaths. Sleep accounted for 0.41%/0% of all action starts. See the committed example for town stocks and the remaining metrics.

## Integration

The checkout is an independent clone at worktrees/world-report-codex. The shared Git metadata is read-only here and GitHub DNS is unavailable. Claude Code can fetch feat/world-report from this local clone, merge it into current main, rerun the build and suite, then push. Only the comparison example belongs in version control. No public API request is outstanding.
