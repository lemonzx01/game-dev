# Known issues (2026-11-25)

Consolidated from handoff "Open" sections, independent reviews and observed play. Update this list when fixing or finding issues;
link the fix branch. Severity: **H** = wrong outcome or blocks play, **M** = noticeable or risky, **L** = cosmetic or future work.

## Being fixed now
| ID | Sev | Issue | Owner / where |
|---|---|---|---|
| R1–R12 | H/M/L | 12 findings from the Codex review of T015–T017. The worst two are an unreachable buyer locking the trader slot, and long trade routes bypassing the path budget. | Antigravity, FIX-R1 (`docs/reviews/2026-11-codex-review-T015-T017.md`) |
| — | — | Independent review of T019 (simulation + visuals) not done yet | ChatGPT/Codex, prompt 3 (`docs/prompts/chatgpt-round2.md`) |

## Gameplay / simulation
| ID | Sev | Issue | Notes |
|---|---|---|---|
| K1 | M | Guards never retreat. Against a dragon, each new guard volunteers and fights to the death one after another; a town can lose most adults. | Observed in the T019 visual run (Ashford 10 → 7 people in ~20 s). Needs a "too strong" check (compare power and health) and retreat or group defence. |
| K2 | M | People who fear an animal (wolves, dragons) can never tame it: they flee before feeding. | T019 handoff. Needs a brave-tamer rule. |
| K3 | L | Early games have almost no mages: magic needs one finished book (300 pages of idle reading). | By design for now; tune after playtest. |
| K4 | L | No weapons or armour items; guards' damage stands in for being armed. Dragons have no flight or breath attack. | GDD equipment and dragon abilities are future work. |
| K5 | L | Vitals rates are global, not per species; population balance is untuned. | T009/T011 open items. |
| K6 | L | Temperature does not affect animals or water; seasons change in steps. | T012 open item. |
| K7 | L | One generic book per town; there is no teaching, and no book topics. | T017 open item. |
| K8 | L | Fixed prices; trade starts only when a town runs short. | T016 open item. |
| K9 | L | Old saves are rejected after every schema bump (currently 13); there is no migration code. | T013 open item; fine before the first release. |

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
