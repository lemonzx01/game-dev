# RP — Predator viability (wolves)

Measured with `Tests/WorldReport` (game opening, realism preset, 128×128 map, two towns). New option `--trace-wolves` prints a daily WOLF line covering:
- how many wolves are alive and how hungry they are
- hunts started, kills and the prey species killed
- meals eaten
- wolf deaths by cause, with the killer's species, the death site and the wolf's last action

Batch runs use `scratchpad/rb_batch.py`, the same as RB.

## Starting point (main after RB)
- 60 days: wolves die out on 4 of 5 seeds.

## Diagnosis, step by step (60 days, seeds 1–5)

| Run | Change | Wolves alive (seeds 1–5) | Main wolf killers | Finding |
|---|---|---|---|---|
| RB | none | 0 on 4 of 5 seeds | starvation, combat | Hunts that start usually succeed. Hungry wolves rarely *start* one: perception is 8 tiles and they wander one tile at a time. |
| rp2–rp6 | hungry wolves roam 12 tiles in a steady heading; wolves don't hunt in town grounds; wolf pairs; sheep live at the towns; wolf hunger 50% | 0–2 | combat 55, starvation 31, dehydration 25 | The trace shows the combat killers are townsfolk (Human and Demon). |
| rp7 | wolves leave town grounds before resting or sleeping | 0–5 | starvation 66, dehydration 37 (combat gone from the top) | The wolves were dying while *resting* inside the town radius, where guards defend. Now they boom, then starve once wild prey crashes (8 hungry wolves, 0 hunts, 7 deer left on the map). |
| rp8 | one wolf pair per 32 animals (was one wolf per 8) | 0 on 1 of 5 seeds | dehydration (seed 4: 6 of 9 wolf deaths) | Water is searched only within 8 tiles. With none in range a wolf wanders one tile per decision and dies of thirst. |
| rp9 | any wild animal whose water or food search failed roams the same way | alive on 5 of 5 | starvation 14, combat 15 | Wolves survive. Starvation is still 21–37%, mostly rabbits in winter waves. |
| rp10 | rabbits breed every 7 days (was 4), density cap 3 | alive on 4 of 5 | — | Fewer rabbits starve, but this one change is not enough. |
| rp11/12 | winter forage grows at 60% (was 40%) | alive on 5 of 5 | — | Starvation is lowest where wolves are most numerous (seed 3: 10 wolves, 6%). Where few wolves remain (seed 2: 2 wolves, 37%), herbivores overshoot and starve in winter. |
| **rp13 = final** | wolf litter 2 (as on main), wolf density cap 2 | **alive on 5 of 5** | starvation 38, dehydration 23 (over 80 births) | see below |

## Final result (defaults on main, no override file)

60 days, acceptance seeds:

| Seed | Alive | Deer | Sheep | Rabbit | Wolf | Human | Demon | Starv % | Max share % |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 120 | 18 | 4 | 17 | 3 | 40 | 38 | 18.1 | 33.3 |
| 2 | 183 | 21 | 68 | 19 | 4 | 38 | 33 | **29.3** | 37.2 |
| 3 | 187 | 37 | 40 | 29 | 14 | 35 | 32 | 9.9 | 21.4 |
| 4 | 141 | 29 | 20 | 24 | 2 | 33 | 33 | 10.0 | 23.4 |
| 5 | 249 | 52 | 104 | 14 | 12 | 34 | 33 | 18.7 | 41.8 |

- **Wolves survive on every seed:** met, 5 of 5. Also 10 of 10 on seeds 6–10 with the same settings.
- **No species above 50%:** met, 5 of 5 (max 41.8%).
- **Starvation under 25% of deaths:** met on 4 of 5. Seed 2 is at 29.3%: a winter die-off of wild rabbits and deer on a seed where few wolves are left. On seeds 6–10 it is met 5 of 5 (3.3–24.1%).
- **Hunt success** is measured by `--trace-wolves`: hunts started, kills and the prey mix per day.

30 days, 10 seeds (the RB check):
- Starvation averages **16.1%**; RB was 18.7%.
- Under 25% on **8 of 10** seeds; RB was 7 of 10.
- Max species share is 45.4%.
- Every species is alive on every seed.

## Still open
- **Small populations swing:**
  - Seed 9 (60 days): 19 wolves wiped out the deer.
  - Seed 8: one wolf left.
  - These are realistic predator–prey swings, but a small map exaggerates them.
- **Winter die-offs** of herbivores on predator-poor seeds (seed 2) keep starvation above 25% there.
- **Demon dehydration:** demons die of thirst on some seeds (seeds 8 and 9: 11–26 deaths). This was not compared against main and looks like a town and water issue at Cinderhold. Worth its own check.
- **Trades:** still 0 (R5).
