# T018-mood handoff — Warm light, town lamps, tilt-shift

| Field | Value |
|---|---|
| Task | T018-mood (owner request: warm "pixel diorama" mood, `docs/prompts/chatgpt-round2.md` prompt 4) |
| Agents | Codex (implementation, claim `8ff5ec3`; stopped at its usage limit with the work uncommitted) → Claude Code (takeover: review, fix, tests, merge) |
| Branch | `feat/T018-mood` (Codex's local checkout `F:\Code\game-dev\worktrees\T018-mood-codex`) |
| State | DONE — merged to main |
| Files | `Game/Presentation/DayNightTint.cs`, `LightingTowns.cs`, `LightingFocus.cs`, `Shaders/tilt_shift.gdshader`, `Main.tscn` (new nodes only), `Game/Tests/MoodAcceptance.*` |

## What changed
- **Day/night palette** (`DayNightTint`): deep blue night, rose then gold/copper at dawn and dusk, warm near-white noon. It follows the simulation clock only.
- **Town lamps** (`LightingTowns`):
  - one warm additive `PointLight2D` per town at its store, with a lit window pixel
  - strength comes from `DayNightTint.LampStrength`: off by day, fading in through dusk
  - lamps are created and removed from `bridge.Settlements()` (God founding, load / world generation); no per-entity nodes
- **Tilt-shift** (`LightingFocus` + `tilt_shift.gdshader`): a screen-space blur of the top and bottom bands, on a CanvasLayer below the UI. **F4** toggles it; **off by default** until the owner decides.
- **Glow/bloom: not added.** The project uses the `gl_compatibility` renderer. 2D glow through WorldEnvironment was not verified there, and changing the renderer needs the owner's decision, so the lamps use an additive light texture instead.

## Takeover review (Claude Code)
- Codex's work was complete apart from the commit, handoff and one visual bug. In the dusk frame each lamp showed as a **hard-edged rectangle**: additive lights use the texture colour, and the falloff ended at zero alpha but still orange. The falloff now ramps to black.
- Restored the presentation-only header comments, added class comments, and made a failure print `MOOD_ACCEPTANCE_FAIL`.
- Merged the current main into the branch before testing; no conflicts.

## Evidence (2026-11-25, merged branch)
- `dotnet build Game/Game.csproj -c Debug`: 0 errors.
- Godot headless: `T007_ACCEPTANCE_PASS`, `T008_ACCEPTANCE_PASS`, `GODTOOLS_ACCEPTANCE_PASS`, `SETTLEMENT_ACCEPTANCE_PASS`, `EFFECTS_ACCEPTANCE_PASS`, `MOOD_ACCEPTANCE_PASS`. Mood checks: one lit lamp per town at night, F4 toggles tilt-shift on and off, lamps off and the world bright at noon.
- Frames (movie writer, 1280×720):
  - `docs/handoffs/T018-mood-night.png`: Ashford store lamp pool at 00:11
  - `docs/handoffs/T018-mood-dusk.png`: 18:00 gold light with a soft lamp glow and no box
  - `docs/handoffs/T018-mood-before.png`: Codex's before frame
- The simulation is untouched.

## Open
- The tilt-shift look was not captured in a frame (it starts off); the owner should try **F4** and say whether to keep it, make it the default, or remove it.
- FPS was not measured before/after (only 2 lights and one optional full-screen pass were added). Check with F3 at full zoom-out during the playtest.
- Glow needs either verified Compatibility support or a renderer switch (owner decision).
