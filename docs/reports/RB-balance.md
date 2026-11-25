# RB ecology balance — measured with Tests/WorldReport (2026-11-25)

Game configuration: `Realism.Apply(...)` (Body on) and the default species table. The map is 128×128 with 120 starting animals and two towns of 10.
Tool: `dotnet run --project Tests/WorldReport -c Release -- --days N --seed S` (prints a `SUMMARY` JSON line per run).

## Before → after, 30 days

| | Before RB (seeds 1–5) | After RB (seeds 1–10) |
|---|---|---|
| Starvation share of deaths | 46.9–61.8% (avg ≈ 55%) | 4.7–34.8% (avg 18.7%); **7 of 10 seeds under 25%** |
| Largest species share at day 30 | rabbits 72.8–88.1% | 23.7–43.8% (rabbits or deer; never above 50%) |
| Deer at day 30 | 0–7 | 11–33 |
| Rabbits at day 30 | 193–531 | 18–57 |
| Wolves at day 30 | 1–19 | 1–9 (alive on every seed) |
| Every opening species alive at day 30 | 4 of 5 seeds | **10 of 10 seeds** |

## 60 days (seeds 1–5, after RB)
Deer, sheep, rabbits and both peoples persist. **Wolves die out on 4 of 5 seeds** (0–1 left), mostly from starvation (83) and town guards (43). Starvation share is 17–41%.

## What changed and why (each step measured on 5–10 seeds)
| Step | Change | Effect |
|---|---|---|
| v1 | Rabbits: adult at 4 days (was 2), 1.5-day pregnancy (was 1), 6-day breeding gap (was 2), local density cap 4 (was 8) | rabbit boom gone: births 4,540 → 833; starvation ≈ 30% |
| v2 | **Breeding seasons** (Body on): wild animals conceive only in spring and summer | the winter die-off of autumn-born young stops; starvation 17–26% |
| v4 | **Realism preset** (game + WorldReport): carcasses last 2 days (was 0.5) | predators return to a kill instead of killing again |
| v6 | Deer move as fast as wolves (1 tick/tile) | deer stop collapsing to predation |
| v8 | Preset: winter forage — 40% plant growth in winter, and growth down to -3 °C (winter is -4 °C, so before, nothing grew) | winter starvation spike largely gone (best run: 3–17%) |
| v10/v14 | Rabbits live 50 days (was 20, shorter than one breeding gap, so rabbits died out in year 2); litter 1 with a 4-day gap; hunger/thirst 120%/110% | rabbits survive across years without booming |
| — | Sheep tether 10 tiles (was 6: tethered sheep far from water died of thirst); berry bushes regrow every 240 ticks | fewer sheep dehydration deaths |
| tried, removed | Stamina (prey tire when fleeing, exhausted creatures move at half speed) | no measurable effect on wolves; removed rather than shipped untested |

## Open (new task RP — predator viability)
- **Wolves:** each wolf starts only about one hunt a day and the pack makes about 3 kills a day (winter: about 3 hunts a day for the whole pack). The wolves mostly fail to *find* prey. Contributing factors:
  - prey as fast as wolves (deer and rabbits)
  - the wolves' night/prey's day schedules
  - guards killing wolves that enter towns
- A wolf population of 15 on a 128×128 map is fragile on its own. The fix needs a look at the hunting AI (prey search, pack hunting or ambush, avoiding towns), not more numbers.
- **Dehydration** of animals far from water (rabbits 159, deer 70, sheep 66 over 10 seeds) points to the same "search radius vs. sparse resources" issue.
- **Trade** never happened in any run (0 sales): both towns stay self-sufficient. This belongs with R5 (economy).
