# Realism roadmap (simulation first, graphics later)

Owner direction 2026-11-25: focus on believable simulation; graphics only need to stay readable (person, demon, animal, dragon).
Written by Claude Code. Each item is a task with acceptance tests, deterministic and budgeted like T003–T019. All items touch `Simulation/**`, so they start **after FIX-R2 merges**, one owner per set of files.

> **สรุปภาษาไทย:** แผนทำให้โลก "สมจริง" ขึ้น เรียงตามสิ่งที่เห็นผลชัดก่อน
> 1. **R1:** ร่างกายต่างกันตามชนิด และวงจรหลับตื่น
> 2. **R2:** ยามรู้จักถอย และเมืองป้องกันกันเป็นกลุ่ม
> 3. **R3:** บ้านและครอบครัว (เมืองโตจากไม้และอาหารจริง)
> 4. **R4:** อากาศหนาว ร้อน และที่พัก
> 5. **R5:** เศรษฐกิจและการย้ายถิ่น
> 6. **R6:** โรคและบาดแผล
>
> ทุกข้อทดสอบได้ ให้ผลเดิมทุกครั้งที่รันด้วย seed เดิม และไม่ใช้ทรัพยากรเกินงบต่อ tick

## R1 — Bodies differ by species; sleep and wake (recommended first)
- **Why:** today every species gets hungry, thirsty and tired at the same rate (K5) and nobody sleeps, so the world looks equally busy at night and by day.
- **What:**
  - **Per-species vitals rates:** metabolism scales with body size. Rabbits eat little and often; a dragon eats a lot but rarely. Values go in `species.json`.
  - **Sleep:**
    - diurnal species (people, sheep, deer, rabbits) sleep at night; people sleep at home (the town centre until R3 adds houses), animals in forest or near their herd
    - nocturnal hunters (wolves) are active at night
    - sleep restores fatigue faster than resting; sleepers notice threats later (lower perception) and can be woken by noise (attacks within a radius)
  - **Injuries heal over time** only while fed and resting; wounded creatures prefer to rest or flee rather than work.
- **Acceptance:**
  - With identical starting vitals, hunger after one day follows each species' rate.
  - At night most people are at home asleep, at noon none are; wolves start more hunts at night.
  - A sleeper wakes when attacked nearby.
  - Healing happens only while fed and resting.
  - Benchmark p99 at 1,000 entities shows no regression.

## R2 — Danger responses (guards retreat, groups defend) (fixes K1)
- **Why:** guards fight a dragon to the death one after another; the town loses its adults.
- **What:**
  - **Threat assessment:** compare expected damage from the foe's attack power, health and toughness with our own side (guards plus brave allies). If the threat is far stronger, guards raise the alarm and retreat to the centre instead of fighting alone.
  - **Alarm:** villagers within the town radius stop work and gather at or flee to the centre.
  - **Group defence:** guards engage together once enough are present (uses the T019 ally bonus).
  - **Memory of a dangerous place:** survivors avoid it for a while (uses the T010 memories).
- **Acceptance:**
  - A lone guard against a dragon retreats and survives.
  - With 4+ guards and a weakened dragon, they engage together.
  - A wolf is still fought one-on-one.
  - Villagers' work stops during an alarm and resumes afterwards.

## R3 — Houses and households
- **Why:** towns are a single store; population is not limited by anything a player can see.
- **What:**
  - **Construction:** houses (capacity 4) are built from stored wood by idle members (construction labour, multi-tick job, cancelled by danger).
  - **Births:** only when the household has room and the town has food above a reserve.
  - **Households:** couples share a house, children stay near a parent until adult, and parents bring food from storage.
  - **Homes:** a house becomes a sleep place (R1) and a refuge during alarms (R2).
  - **Presentation:** a house sprite at its tile; the art can stay simple.
- **Acceptance:**
  - Wood decreases by the house cost and a house appears only after the labour ticks.
  - Population never exceeds housing capacity from births.
  - A child's position stays within N tiles of a parent most of the time.
  - Save/load mid-construction continues identically.

## R4 — Weather, cold and shelter
- **What:**
  - **Daily weather** (clear, rain, snow by season) from the seeded RNG.
  - **Exposure:** cold below a comfort range drains health unless sheltered (house, forest for animals) or fed. Heat and drought raise thirst.
  - **Seasons:** rain helps crops and berry regrowth, drought slows it. Winter stops crop growth (check T012 behaviour).
- **Acceptance:**
  - With weather fixed by seed, an unsheltered person loses health in a snowy night and a sheltered one does not.
  - Crop growth differs by weather.

## R5 — Economy and migration
- **What:**
  - stored food spoils slowly
  - trade prices follow each town's supply and demand (bounded)
  - long hunger makes members migrate to a richer town (they leave carrying nothing)
  - a town with no members is abandoned and keeps its ruins and history
- **Acceptance:**
  - Spoilage removes the configured share.
  - Prices rise in a scarce town and fall in a rich one, within bounds.
  - Starving members migrate and change settlement.
  - A town falls with a durable milestone.

## R6 — Disease and wounds (later)
Contagion by proximity, recovery and immunity, carcasses as disease sources, and Heal helping. Comes after R1–R5.

## Order and ownership
1. **FIX-R2** (Antigravity, in progress) must merge first.
2. **R1 + R2:** about 2 tasks, can be split between two agents by file sets (`Vitals`/`Life`/`Mind` vs `Trade`/`Combat`).
3. **R3**, then **R4**, then **R5**. **T020** (possession, play as the dragon) can slot in after R1 if the owner wants to play sooner.
4. Graphics needed by these (house sprite, sleeping pose) stay minimal and readable.
