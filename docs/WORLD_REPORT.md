# WorldReport

Run from the repository root with .NET 8 or a compatible SDK. The console program starts a fresh world, issues the game’s opening God commands, then observes it for whole simulation days. It references only Simulation.

~~~sh
dotnet run --project Tests/WorldReport -c Release -- --days 30 --seed 1
dotnet run --project Tests/WorldReport -c Release -- --days 30 --seed 1 --no-body --out docs/reports/world-1-30d-off.md
~~~

The first command writes `docs/reports/world-1-30d.md` and `docs/reports/world-1-30d.csv`. A custom Markdown path places the CSV beside it. Running again with the same path replaces both files. Use distinct paths for comparisons and keep generated pairs out of commits.

| Option | Default | Meaning |
|---|---:|---|
| `--days` | 30 | Number of complete days, 2,400 ticks each in the current game |
| `--seed` | 1 | Unsigned 64-bit world seed |
| `--size` | 128 | Square map side, 2–4,096 cells |
| `--animals` | 120 | Requested mixed herd. Zero selects the bridge’s single-deer test opening, with no towns |
| `--settlers` | 10 | Settlers per town. Zero skips town founding |
| `--no-body` | off | Disable the R1 body configuration |
| `--out` | see above | Markdown filename, relative to the current directory or absolute |

The opening follows SimulationBridge’s west/east town search, settler sex/order and mixed-herd land scan. Terrain can limit placements. Requested opening commands must fit the game’s 1,024-command queue. The opening census is taken after tick 1. The daily population is the count of living snapshot rows at ticks 2,400, 4,800, etc. A dragon column at zero from the start means none was observed in the opening.

## Reading the output

- Read the summary for overall survival, then the living-population and death-cause tables to identify which trends diverge. Day 1 includes ticks 1–2,400. Births count newborn children; opening spawns are excluded. Deaths are attributed to the cause in the death event.
- Town inventories and membership are end-of-day samples. A positive closing food balance can hide a shortage earlier that day. World-wide starvation deaths need not be town residents.
- Action and important-event columns count observed starts or occurrences. Divide a particular action count by all action starts in the same period for its share. Sleep includes starting the trip home to sleep. These counts do not measure hours asleep or at work.
- The CSV has one row per day. Species, causes, actions, towns and notable events have separate columns. A blank town cell means there was no settlement view at that census.
- Tick timings are wall-clock milliseconds for `SimulationWorld.Step()` alone. Daily p99 uses the nearest-rank value across that day’s steps; report collection and file I/O are outside the stopwatch. Machine load and JIT warm-up affect timings.
- To compare repeat runs of a build, keep all options identical, write to separate paths, then compare Markdown before the final timing section and CSV excluding `mean_ms` and `p99_ms`.

## Warnings

| Signal | Trigger and interpretation |
|---|---|
| Extinction | A species present in the opening or a preceding daily census reaches zero. These samples locate the day, not the precise tick. |
| Food shortage | A town closes with zero food for three consecutive days. One warning per streak. |
| Starvation | More than 25% of all deaths across the run have cause Starvation. |
| Sustained growth | Total population increases at five successive daily censuses. This prompts a longer run to check whether it levels off. |
| Incomplete opening or events | Requested towns or population were not reached, a command was rejected, or the simulation dropped events. Dropped noncritical events make action/event counts lower bounds. A cursor gap aborts the run. |

[See the measured 30-day comparison for seed 1](reports/example-world-1-30d.md).
