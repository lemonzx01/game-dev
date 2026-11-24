# Prompts for ChatGPT / Codex, round 2 (copy and paste)

Current split (nobody writes the same file):

| Who | Task | Files it may write |
|---|---|---|
| **Antigravity** | FIX-R1: fix the 12 review findings (in progress) | `Simulation/**`, `Tests/**` |
| **ChatGPT / Codex** | Prompt 3: review T019 (read-only) → Prompt 4: warm-light mood pass | Prompt 3: `docs/reviews/**` only. Prompt 4: `Game/Presentation/DayNightTint.cs`, new lighting files, `Game/Presentation/Main.tscn` (adding nodes) |
| **Claude Code** | Integrating everyone's work, playtest checklist / known bugs (T018), T020 design (docs) | `docs/**` (except reviews), `TASKS.md`, `PROGRESS.md` |

Run Prompt 3 first, then Prompt 4 (or in parallel in a separate folder; they share no files).
Note: last round you couldn't write to the shared .git or push. If that happens again, set the handoff to REVIEW,
tell us the branch and the folder, and Claude Code will fetch and merge it like last time.

---

## Prompt 3: Independent review of T019 (read-only)

```
You are a developer on Fantasy World Simulator, repo F:\Code\game-dev (branch main). Read AGENTS.md first.
T019 (combat, magic, dragon, taming), both the simulation and its visuals, was self-reviewed by Claude Code (A009)
and has not had an independent review. Review it as a different agent, in the same style and depth as your
review of T015–T017 (docs/reviews/2026-11-codex-review-T015-T017.md).

Scope:
- docs/handoffs/T019.md, docs/handoffs/T019-visuals.md
- Simulation/SimulationWorld.Combat.cs, Simulation/SimulationWorld.Taming.cs, Simulation/Core/Species.cs (dragon and new fields),
  the T019 parts of SimulationWorld.Mind.cs / .Ecology.cs / .Trade.cs / .Save.cs / .Entities.cs, Data/species.json
- Tests/Simulation.Tests/T019Tests.cs
- Game/Presentation/EffectsLayer.cs, the dragon section of MapRenderer.cs, Game/Tests/EffectsAcceptance.cs, tools/art/make_dragon.py

Look especially for:
- invalid targets that can still be hit or cast at, costs (fatigue, mana, town food) charged without effect or not charged at all,
  overflow and negative values (mana, trust, damage × percentages), retaliation loops (dragon ↔ guard),
  tamed state that never ends or leaks to the wrong town, the AnimalTamed milestone versus save/load and ReadHistory
- determinism (Aptitude from the seed and id, the PsycheStream RNG used by taming) and save/hash validation of the new fields
- grid scans per agent (FightsBack, AlliesFighting, TameCandidate, TryHeal on every think) and their budget
- tests that don't really check what they claim (the way you found with T016)
- EffectsLayer: effects that pile up or leak memory at high speed, wrong timing between Firebolt and its impact

Rules: read-only review. Do NOT edit any files except your report (Antigravity is editing Simulation/** and Tests/**)
Write the report to docs/reviews/2026-11-codex-review-T019.md, using the same format as before:
severity (High/Medium/Low), file:line, why it is a problem, minimal fix, test that should be added
Run dotnet test Tests/Simulation.Tests -c Release -m:1 and record the result
Work on branch review/T019-codex (git worktree add worktrees/review-T019-codex -b review/T019-codex origin/main,
or clone into that folder if the shared .git can't be used), commit only the report
Report to the owner in Thai: how many findings and which ones are urgent
```

---

## Prompt 4: Warm-light mood pass (2D, Godot)

```
You are a developer on Fantasy World Simulator (Godot 4.7.2 .NET), repo F:\Code\game-dev. Read AGENTS.md first.
The owner wants the mood of a warm "pixel diorama": golden light in the evening, lamps glowing in town at night,
soft light, and a toy-model look (tilt-shift). We stay 2D top-down (no switch to 3D) and don't copy any specific game.

## Task: T018-mood
1. Day/night colours: improve Game/Presentation/DayNightTint.cs so dawn and dusk are warm gold/orange,
   noon is bright and not washed out, and night is deep blue but still readable. It still uses only the simulation's clock (as now)
2. Town lights at night: one or two warm PointLight2D per town (at the store at the centre), fading in at dusk and out at dawn
   - read positions from bridge.Settlements() only (a new town gets a light, a lost town loses it); no per-entity nodes (AGENTS.md)
   - the renderer is gl_compatibility: check that PointLight2D and CanvasModulate work together and give real light at night
3. Glow/bloom: check whether Godot 4.7 gl_compatibility supports 2D glow through WorldEnvironment (background canvas + glow)
   - if it does, add soft glow so lamps and the spell effects (FxLayer) glow
   - if it doesn't, or it costs too much, don't add it, and record in the handoff why
   - do NOT change the renderer in project.godot (that needs the owner's decision first)
4. Tilt-shift: a screen-space shader that gently blurs the top and bottom edges of the screen, giving the diorama feel
   - toggle with F4 (default: off at first, the owner will try it and decide), not covering the UI (put it between the world and the UI CanvasLayer)
5. Performance: measure FPS before/after at the start zoom and zoomed all the way out (F3 shows debug); it must not drop noticeably

## Strict file boundaries
You may write ONLY: Game/Presentation/DayNightTint.cs, new files Game/Presentation/Lighting*.cs, Game/Presentation/Shaders/** (new),
Game/Presentation/Main.tscn (adding nodes/resources; don't touch existing nodes except DayNight), a new Game/Tests/MoodAcceptance.*,
docs/handoffs/T018-mood*, your own entry in PROGRESS.md
You must NOT edit: Simulation/**, Tests/**, Game/Presentation/MapRenderer.cs, Game/Presentation/EffectsLayer.cs,
Game/Integration/**, Game/project.godot, Assets/** (unless needed for a new texture; then add a row to Assets/CREDITS.md)

## Checks
- dotnet build Game/Game.csproj -c Debug
- godot-mono.console.exe --headless --path Game --import, then run all 5 acceptances; each must PASS:
  Presentation (T007_ACCEPTANCE_PASS), Observer (T008_...), GodTools, Settlement, Effects (EFFECTS_ACCEPTANCE_PASS)
- add Game/Tests/MoodAcceptance: at night there is a town light per town, at noon the lights are off, F4 toggles tilt-shift
- record images: godot-mono.console.exe --path Game --write-movie <dir>/f.png --fixed-fps 30 --quit-after 240 --resolution 1280x720
  (the game starts at night) and save the night town frame as docs/handoffs/T018-mood-night.png; also capture a dusk/day frame if you can
  (actually open and look at the images; don't assume)
- after running --import, revert .import files that only changed line endings (git checkout -- <file>) so they don't pollute the commit

## Workflow
Branch feat/T018-mood (git worktree add worktrees/T018-mood-codex -b feat/T018-mood origin/main, or clone into that folder)
Claim first with docs/handoffs/T018-mood.md (IN_PROGRESS), commit only on your branch, no force push
When done, merge into main and push if you can; if you can't, set it to REVIEW and Claude Code will integrate it
Report to the owner in Thai with the before/after images
```
