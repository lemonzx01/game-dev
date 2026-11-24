# T018-art handoff — Character art pass (animated sprites)

| Field | Value |
|---|---|
| Task | T018 presentation follow-up (owner request: better character visuals) |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `feat/T018-character-art` (worktree `F:\Code\game-dev\worktrees\T018-art-claude`), from main `099330a` |
| State | DONE — merged to main |

## What changed
- **New sheet** `Game/Assets/Art/characters_24.png`: project-owned CC0 art, generated deterministically by `tools/art/make_characters.py` (Python + Pillow).
  - Frames are 24×24 px with auto outlines, rim shading and a ground shadow.
  - 13 columns: idle ×2, walk ×4, work with a hoe ×2, attack ×2 (sword for people, bite lunge for wolves), reading (sitting with a book) ×2, dead.
  - 16 rows:
    - 8 human variants: skin, hair (short, long, straw hat) and tunic colours
    - 4 demon variants: horns, tail, glowing eyes
    - sheep, deer, rabbit, wolf (the rabbit gets its own sprite instead of a tinted deer)
- **Renderer** (`MapRenderer`), presentation only; it never touches the simulation:
  - Creatures glide between cells instead of jumping, and snap if they move more than 3 cells (God teleport or load).
  - They face the direction they last stepped, using a mirrored frame.
  - Each creature is depth-sorted by drawn y.
  - The animation comes from the row's `Action` and whether the creature is moving:
    - walk while moving (faster when fleeing)
    - Work → hoe swing
    - Read → sitting with a book
    - Hunt or Defend → attack
    - an animal eating or drinking → head down
    - dead → lying down
  - Human and demon variants are picked from a hash of the entity id, so a creature keeps its look.
  - Start zoom is now 3 (an integer zoom keeps pixels crisp).
- Old placeholder atlases are still used for terrain, plants, the storage shed and the selection ring.

## Evidence (2026-11-24)
- In-game frame (movie-writer capture, night tint): `docs/handoffs/T018-art.png`. Ashford villagers walking and facing both ways, one sitting and reading at the town centre. Sheet preview: `docs/handoffs/T018-art-sheet.png`.
- Godot headless: T007, T008, GodTools and Settlement acceptances pass. The simulation is untouched, so the 82/82 suite and the golden hash are unaffected.

## Open
- Side view only, mirrored left/right; no up or down facing.
- No job-specific outfits (the row does not carry Job) and no child sprites (the row does not carry LifeStage).
- Buildings, trees and terrain are still the placeholder atlases. Re-run the script after palette or pose edits, then `--import`.
