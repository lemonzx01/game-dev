# T018 visual polish handoff

| Field | Value |
|---|---|
| Task | T018 (presentation polish requested by the owner: "ทำไมภาพมันแปลกๆ") |
| Agent | Claude Code (self-reviewed, A009) |
| Branch | `feat/T018-visual-polish` (worktree `F:\Code\game-dev\worktrees\T018v-claude`), from main `5702032` |
| State | DONE — merged to main |

## Changes
- **Renderer** now uses the project's placeholder pixel-art atlases (`Game/Assets/Placeholders`, copies of `Assets/Placeholders`, CC0/project-owned): terrain baked into one texture per map revision (grass, forest floor, mountain, snow; water touching land is shallow, otherwise deep); berry bushes (full/empty from real stock), oak/pine trees and stumps on wood nodes; farmland with growing/ripe crops; storage shed at town centres; sprites for sheep, deer, wolf, rabbit (tinted small deer until a rabbit sprite exists), humans (skin, blue tunic, hair) and demons (red skin, dark tunic, horns); selection ring; grey dead bodies.
- **UI:** translucent panels with padding (HUD, inspector, towns, feed, God line) that size to their text and no longer overlap; HUD shows year, season, day, clock, temperature and speed/PAUSED; **F3** toggles debug details.
- **Camera** starts at zoom 2.5 over the first town (mouse wheel zooms out). **Day/night** CanvasModulate follows the simulation clock (presentation only).
- New read models: `SimulationWorld.ReadResourceNodes()`, bridge `ResourceNodes()`, `TicksPerDay`. Project setting: nearest texture filter.

## Evidence (2026-11-24)
- `dotnet build Game/Game.csproj` 0 warnings; Godot headless acceptances T007, T008, GodTools, Settlement all pass; simulation 74/74.
- Rendered frame `docs/handoffs/T018-visuals.png` (Year 1 Spring day 1, 12:29, 17 °C): Ashford with settlers, farm plots, animals, bushes, trees, mountains, shallow/deep water.

## Open
- Placeholder art only (no walk animations, no rabbit sprite, buildings beyond storage not used yet); a real art pass and the atlas animation frames are future work.
