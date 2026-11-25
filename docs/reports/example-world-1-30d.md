# Seed 1 over 30 days — Body on and off

Both runs began with 140 living creatures on the same 128 × 128 map, including ten settlers in each town. Each ran for 72,000 ticks on simulation revision 417c011. Only Body changed. Figures below are measured across all 30 days unless marked day 30.

| Metric | Body on | Body off |
|---|---:|---:|
| Alive on day 30 | 348 | 310 |
| Births | 645 | 708 |
| Deaths | 437 | 538 |
| Starvation | 252 (57.7%) | 253 (47.0%) |
| Dehydration | 30 | 36 |
| Predation | 96 | 193 |
| Old age | 50 | 51 |
| Combat | 9 | 5 |
| Sleep starts / all action starts | 6,200 / 1,509,736 (0.41%) | 0 / 1,846,276 (0%) |
| Work starts | 5,900 | 5,917 |
| Hunt starts | 320 | 538 |
| AnimalTamed / SpellCast / TradeSold | 179 / 20 / 0 | 224 / 11 / 0 |
| GuardAlert / WokeUp | 33 / 252 | 24 / 0 |

Predation deaths fell by 97 on this seed. Starvation deaths differed by one, even though both towns ended with food in storage. The death counts cover the whole world. The data here cannot attribute those starvation deaths to a particular town.

| Town on day 30 | Body | Population | Guards | Food | Wood | Coins |
|---|---|---:|---:|---:|---:|---:|
| Ashford | on | 22 | 2 | 1,038 | 50 | 100 |
| Ashford | off | 21 | 2 | 1,624 | 62 | 100 |
| Cinderhold | on | 22 | 2 | 1,291 | 50 | 100 |
| Cinderhold | off | 23 | 2 | 1,239 | 50 | 100 |

On day 30 rabbits accounted for 282 of 348 living creatures with Body on and 240 of 310 with it off. The deer census fell to two and five respectively. Every species present in the opening still had at least one survivor. Neither run recorded a three-day closing food shortage, rejected command or dropped event.

Both configurations crossed the 25% starvation threshold. Five consecutive daily population increases first occurred on day 6 with Body on and day 11 with Body off. More seeds and longer runs are needed to distinguish stable trends from this opening. Action-start shares measure decisions; a single sleep action may span a large part of the night.

[Run the comparison and generate daily CSVs](../WORLD_REPORT.md).
