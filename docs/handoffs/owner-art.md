# Owner art handoff — replace character art with the owner's images

| Field | Value |
|---|---|
| Task | Owner request: swap the generated characters for the owner's own images (prompts in `docs/prompts/art-image-prompts.md`) |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `feat/owner-art` (worktree `F:\Code\game-dev\worktrees\art-owner-claude`) |
| State | DONE — merged to main |
| Files | `Assets/Source/owner-art-2026-11-25/*.jpg`, `tools/art/import_owner_art.py`, `Game/Assets/Art/owner_sheet.png`, `Game/Presentation/MapRenderer.cs`, `Game/Tests/EffectsAcceptance.cs`, `Assets/CREDITS.md` |

## What the images were and how they were imported
- Four 2000×1116 JPEGs on magenta. The art was drawn at about 8.33 image pixels per art pixel, so people are about 76 art px tall: much more detail than the 32 px the prompts asked for. All other import steps worked.
- **Import** (`tools/art/import_owner_art.py`, deterministic, takes about 3 s):
  - finds the pixel grid (pitch 8.33)
  - takes the median colour of the centre of each art pixel, which removes JPEG noise
  - keys out the magenta background
  - turns magenta-tinted border pixels into outline ink, leaving purple or pink skin alone
  - cuts the sprites apart (the touching dragons are split at their thinnest column)
- **Poses:** people have one front-facing pose, animals one side pose, the dragon three. The 13 animation columns are made from these by bobbing, leaning and lunging, plus a lying-down pose for death; the dragon cycles its three poses when walking. There is no real leg animation because none was drawn.
- **Sizes (rescaled 2026-11-25 at the owner's request, people were as big as the store):** the importer shrinks every frame to world pixels at real proportions: people 16–18 px (about one cell), deer 15, wolf 11, sheep 10, rabbit 7, dragon 64 (about four people). The 104 px frames are drawn 1:1 with nearest filtering, so pixels stay sharp.
- **Variants and guards:** each human uses one of 4 looks (farmer, woman, elder, scholar), and guards use the guard sprite; the job is read from details at most every 20 ticks. Demons use one of 4 looks. Nearest filtering is used; at the start zoom an art pixel is about 1.26 screen px.
- The old generated sheets (`characters_24.png`, `dragon_48.png`) stay as fallbacks but are not drawn.

## Evidence (2026-11-25)
- `dotnet build Game/Game.csproj -c Debug`: 0 errors.
- Godot headless: T007, T008, GodTools, Settlement, Effects and Mood acceptances pass. The Effects check now requires the dragon to be drawn from row 13 of `owner_sheet.png` at dragon scale, with a non-empty frame and correct mirroring.
- Frames:
  - `docs/handoffs/owner-art-town.png`: dusk, villagers, deer, sheep, rabbits, and the guard in the guard sprite
  - `docs/handoffs/owner-art-dragon.png`: night, the dragon fighting at Ashford with impact effects and the feed

## Open
- No walk-cycle legs and no side views for people; the owner can create walk frames later and re-run the importer.
- License: record the image tool and model, and confirm its terms before any public release (see `Assets/CREDITS.md`).
- A newly promoted guard switches to the guard sprite within 20 ticks (cache).
