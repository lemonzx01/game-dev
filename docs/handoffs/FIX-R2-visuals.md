# FIX-R2-visuals handoff — T019 review findings in the presentation layer

| Field | Value |
|---|---|
| Task | FIX-R2-visuals (Codex review `docs/reviews/2026-11-codex-review-T019.md`) |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `fix/R2-visuals` (worktree `F:\Code\game-dev\worktrees\R2v-claude`), from main `5311f94` |
| State | DONE — merged to main |
| Files | `Game/Presentation/EffectsLayer.cs`, `MapRenderer.cs` (`SpriteOf`), `InspectorPanel.cs`, `Game/Tests/EffectsAcceptance.cs` only. Simulation findings 1–9, 13, 14 and the simulation half of 15 belong to Antigravity's FIX-R2 (`docs/prompts/antigravity-fix-r2.md`) |

| # | Fix | Regression in `EffectsAcceptance` | Status |
|---|---|---|---|
| 10 | Firebolt suppression is now a bounded (64), one-use list of pending (tick, caster, target) entries that survives across event drains. Entries expire 2 ticks after their tick. | SpellCast and its Attacked handled in **separate** `Handle` calls: no early impact, the suppression is used once, an unrelated melee hit still plays at once, and the landing draws exactly one impact. Mutation check: disabling the lookup makes the test fail ("fireball in flight, no early impact across drains"). | Fixed |
| 11 | When `bridge.WorldGeneration` changes, `Reset()` clears active effects and pending suppressions before the new frame is handled. | Save; start a fireball; `LoadFrom`; process one frame → no effects, no pending entries, and no stale impact after 1 s. | Fixed |
| 12 | Room is checked before allocating an `Fx` or looking up a caster. The suppression entry is still recorded, so a matching hit is never drawn early. | 400 hits + 200 bolts in one batch → active count ≤ `MaxActive`, pending ≤ 64, and everything expires. | Fixed |
| 15 (visual part) | `MapRenderer.SpriteOf(row)` is now the single source for what `DrawCreature` draws. The test uses real rows instead of the dragon as both caster and target. | During the real fight, every frame checks that the dragon uses `dragon_48.png` with a 48×48 region. The first frames have a body (>300 opaque pixels). Each sideways step's mirroring matches its direction (left ↔ mirrored). Spells come from a real human toward the dragon. | Fixed |
| 2 (inspector guard) | The inspector no longer indexes settlements with an unchecked `TamedTo`; it shows `settlement #id` if unknown. Load-time validation is in the simulation (Antigravity). | Covered by the build and by inspector use in acceptances; the full regression is in FIX-R2 (simulation). | Fixed (UI part) |

## Evidence (2026-11-25)
- `dotnet build Game/Game.csproj -c Debug`: 0 errors.
- Godot headless, all pass: T007, T008, GodTools, Settlement, Effects (`EFFECTS_ACCEPTANCE_PASS tick=8`), Mood.
- The simulation is untouched.
