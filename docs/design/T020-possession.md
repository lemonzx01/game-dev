# T020 design — Possession (take control of a living character)

Status: **DESIGN READY**, written by Claude Code on 2026-11-25. Implementation starts after FIX-R1 merges, because both edit `Simulation/**`.
Acceptance (TASKS.md):
- take and release control
- the character keeps the same ID and state
- its AI resumes after release
- other agents continue meanwhile

> **สรุปภาษาไทย:** ผู้เล่นกด P เพื่อสิงร่างตัวละครที่เลือกไว้ (คนหรือสัตว์ก็ได้) แล้วควบคุมด้วยคีย์บอร์ด
> - **ควบคุม:** WASD เดินทีละช่อง, E กินหรือดื่ม, R ตี, T ยิง Firebolt, H รักษา, Y ป้อนอาหารสัตว์
> - **ระหว่างสิง:** AI ของตัวนั้นหยุด แต่ร่างกายยังหิว เหนื่อย บาดเจ็บ และตายได้จริง ไม่มีเกราะพระเอก
> - **คนอื่นในโลก:** ใช้ชีวิตต่อตามปกติ
> - **ปล่อยร่าง (X):** ตัวละครเป็นคนเดิม ID เดิม ความจำเดิม และ AI กลับมาทำงานต่อ
> - **ทุกการกระทำเป็น command:** ผ่านการตรวจสิทธิ์และกฎเดียวกับ AI จึง replay และ save ได้ตรงเป๊ะ

## 1. Authority and binding (ARCHITECTURE §Commands and authority)
- New grant `AuthorityGrant.Possess = 4`, supplied only through `SimulationOptions.Grants`; a command can never grant itself authority.
- New mode `CommandModes.Player = "Player"`. Possession commands must use it. God grants do not imply Possess. The game session holds both.
- The binding is world state, saved and hashed (ARCHITECTURE §Save item 2, "trusted gameplay grants/possession"):
  - `SortedDictionary<long issuerId, long entityId>`, at most one entity per issuer and one issuer per entity
  - `EntityState.PossessedBy` (long, 0 = free), mirrored for O(1) checks
- `ReadPossession(long issuerId) → EntityId?` read model; `EntityDetails` gains `PossessedBy`.

## 2. Commands (new `CommandKind` values; all are checked at submission and again at execution)
| Kind | Payload | Execution rules | New `CommandReason` |
|---|---|---|---|
| `Possess = 5` | entity id | grant + Player mode; entity alive; not possessed; an issuer already possessing another entity releases it first (one event each) | `AlreadyPossessed` |
| `Release = 6` | — | issuer has a binding | `NotPossessing` |
| `Move = 7` | dx, dy ∈ {N,W,E,S} | bound actor alive; target in bounds and passable; `now ≥ NextMoveTick` (honours `MoveTicksPerTile`) | `TooSoon` |
| `Interact = 8` | verb, optional target id | verb `Eat`/`Drink` → `TryConsume` (same stock rules as AI); `Attack` → `Attack()` (same `CanHarm`, adjacency, fatigue, cooldown `AttackReadyTick`); `Firebolt`/`Heal` → `CastFirebolt` / a target-specific heal (same mana and range); `Feed` → `Feed()` (same town food reserve, trust roll) | existing reasons plus `TooSoon`, `InvalidTarget` |

All gameplay effects reuse the AI's own functions, so player actions and AI actions cannot drift apart: same validity, costs, events, XP.
A rejected interaction costs nothing (same rule as T019).

## 3. Simulation behaviour while possessed
- **AI suspended:**
  - Possess runs `StartIdle` (reading credits pages, rest ends) and bumps `ThinkToken` and `StepToken`, so pending work becomes stale and consumes no budget (scheduler rule from T014).
  - `Think` returns early for a possessed entity and does not reschedule. Perception side effects still run: fear rises and threats are remembered, so the mind stays coherent after release.
- **Body continues:**
  - vitals anchors, death booking, ageing, pregnancy and fatigue are unchanged
  - other creatures can hunt, attack, heal or tame it normally
- **Retaliation (T019):** `Hurt → StartFight` skips possessed victims; the player decides.
- **Settlement:** job and membership stay. A possessed guard still counts as a guard. A possessed member is never chosen by `PlanWork` or trade.
- **Death while possessed:** `Die` clears the binding and emits `Released` with Reason = died. The player sees the death; there is no protection (D002).
- **Release:** clears the binding, schedules `Think` at `now + 1`, and emits `Released` with Reason = command. Memories, relationships, skills, mana and trust are untouched (acceptance: same ID and state).
- **Events:** `Possessed = 230` (Actor = issuer, Target = entity) and `Released = 231` (Reason: 0 command, 1 died, 2 switched). These are regular events, not milestones.

## 4. Determinism, save, hash
- Commands carry `TargetTick`/`CommandSequence`, so replaying the admitted command stream reproduces the run (existing N1 test style).
- Save and hash add the binding table (sorted by issuer), `PossessedBy` and `NextMoveTick`. Load validates that each binding points to a live entity, that both sides agree, and that there are no duplicates. Bump the schema.

## 5. Godot (new `Game/Presentation/PossessionController.cs`; small edits to `Main.tscn`, `CameraController`, `PLAYING.md`)
- **P:** possess the selected entity (Observer selection). **X:** release.
- While possessing:
  - the camera follows the character
  - God tools are disabled
  - the HUD shows `Possessing #id — WASD move, E eat/drink, R attack, T Firebolt, H heal, Y feed, X release`
- **Movement:** holding WASD sends one `Move` per `MoveTicksPerTile`, never more than one pending receipt. Arrow keys keep panning.
- **Targets:** the nearest valid creature in front (facing from the last move), or the clicked creature.
- **Feedback:** the existing `EffectsLayer`/feed already show attacks and spells. Rejections show briefly in the HUD with their reason.

## 6. Tests (Simulation, `T020Tests.cs`)
1. Possess → no AI `ActionStarted` for that id while possessed; other entities keep acting; `Released` clears the state.
2. Move: exactly one tile per accepted command; out of bounds, impassable, `TooSoon` and dead actor are rejected with reasons; no movement on rejection.
3. Release → same id, skills, memories and relationships; AI acts again within `ThinkIntervalTicks`.
4. Authority: Observer/God-only issuers → `Unauthorized`; a second issuer on the same entity → `AlreadyPossessed`; switching entities releases the first.
5. Death while possessed → binding cleared, `Released` reason 1, later commands → `ActorDead`/`NotPossessing`.
6. Interact parity: a player `Attack` costs the same fatigue and does the same damage as an AI attack in the same situation; invalid targets cost nothing.
7. Save mid-possession and replay of the same command stream → identical `StateHash`.
8. Godot acceptance: P/WASD/X drive the bridge; the camera follows; the HUD shows the binding; God tools are off.

## 7. Out of scope for T020
Dialogue/talk, inventory and items, click-to-move pathfinding, multiple players, possession limits or costs. Record these as follow-ups.

## 8. Questions for the owner (defaults used if unanswered)
1. Can any living creature be possessed, including animals and the dragon? Default: **yes**.
2. Should possession need anything in-world (e.g. only God mode)? Default: **available in the observer game; God tools turn off while possessing**.
3. Key layout above OK? Default: **yes**.
