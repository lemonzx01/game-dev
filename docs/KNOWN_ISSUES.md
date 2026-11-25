# Known issues (2026-11-25)

Consolidated from handoff "Open" sections, independent reviews and observed play. Update this list when fixing or finding issues;
link the fix branch. Severity: **H** = wrong outcome or blocks play, **M** = noticeable or risky, **L** = cosmetic or future work.

## Being fixed now
| ID | Sev | Issue | Owner / where |
|---|---|---|---|
| T019 #13 | M | Durable milestone archive cap (ARCHITECTURE §9) still open; design proposal in `docs/handoffs/FIX-R2.md`. | unassigned |

Done: FIX-R1 (12/12), FIX-R2 (14 of 15; #13 open), FIX-R2-visuals, R1, R2, FIX-R3.

## Gameplay / simulation
| ID | Sev | Issue | Notes |
|---|---|---|---|
| K10 | M | IMPROVED by RB + RP: 10 seeds x 30 days starvation avg 16.1% (8/10 under 25%), all species alive; 60 days: wolves survive 5/5 seeds, starvation <25% on 4/5 (winter herbivore die-off where wolves are few). Still: predator-prey swings on a small map, demon dehydration on some seeds, 0 trades (R5). Was: rabbits 282 of 348 creatures, deer down to 2, starvation 58% of deaths, 0 trades. | `docs/reports/example-world-1-30d.md`. Needs a balance task (reproduction vs food regrowth vs predation) measured with WorldReport over several seeds. |
| K11 | L | FIXED by FIX-R3 except #4 (dense-crowd noise budget, partly) and #15 (animals sleep in place). Was: R1 review: 17 findings (3 High: sleep→rest healing differs live vs loaded; noise lost when the event log is full; arriving home at dawn sleeps a whole day). | `docs/reviews/2026-11-codex-review-R1.md`; fix as FIX-R3 after R2 merges (same files). |
| K1 | — | FIXED by R2 (2026-11-25): guards retreat and raise the alarm when outmatched. Previously: guards never retreat. Against a dragon, each new guard volunteers and fights to the death one after another; a town can lose most adults. | Observed in the T019 visual run (Ashford 10 → 7 people in ~20 s). Needs a "too strong" check (compare power and health) and retreat or group defence. |
| K2 | M | People who fear an animal (wolves, dragons) can never tame it: they flee before feeding. | T019 handoff. Needs a brave-tamer rule. |
| K3 | L | Early games have almost no mages: magic needs one finished book (300 pages of idle reading). | By design for now; tune after playtest. |
| K4 | L | No weapons or armour items; guards' damage stands in for being armed. Dragons have no flight or breath attack. | GDD equipment and dragon abilities are future work. |
| K5 | L | Population balance is untuned (per-species vitals rates exist since R1). | See K10 / task RB. |
| K6 | L | Temperature does not affect animals or water; seasons change in steps. | T012 open item. |
| K7 | L | One generic book per town; there is no teaching, and no book topics. | T017 open item. |
| K8 | L | Fixed prices; trade starts only when a town runs short. | T016 open item. |
| K9 | L | Old saves are rejected after every schema bump (currently 20); there is no migration code. | T013 open item; fine before the first release. |

## Presentation
| ID | Sev | Issue | Notes |
|---|---|---|---|
| P1 | L | Characters face left or right only; no job outfits or child sprites (rows lack Job and LifeStage). | T018-art open item. |
| P2 | L | Carcasses are not drawn (no read model). | T008/T009 open item. |
| P3 | L | Buildings beyond the town store don't exist in the simulation, so towns look sparse. | Needs a houses/construction design first. |
| P4 | L | The warm lighting, lamps and tilt-shift requested by the owner are not done yet. | ChatGPT/Codex, prompt 4 (T018-mood). |

## Process
| ID | Sev | Issue | Notes |
|---|---|---|---|
| X1 | M | Codex cannot write to the shared `.git` or reach GitHub from its sandbox. Its work arrives as separate local clones that Claude Code fetches and merges. | Keep the "set to REVIEW, Claude integrates" path in prompts. |
| X2 | L | Running Godot `--import` rewrites `.import` files with different line endings. | Revert with `git checkout -- Game/Assets` before committing. |
