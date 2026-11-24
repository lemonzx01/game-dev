# Playing Fantasy World Simulator (prototype)

## Start
- Double-click `Play.cmd` in the repository root (or run it in a terminal). It builds the C# projects, imports assets the first time, and opens the game with the Godot 4.7.2 .NET installed by Scoop (`scoop install godot-mono`; .NET SDK 10.0.404 per `global.json`).
- **Standalone build:** `build/windows/FantasyWorldSimulator.exe` (with `FantasyWorldSimulator.pck` and `data_Game_windows_x86_64/`, ~182 MB, self-contained .NET runtime; copy the whole folder). Rebuild it with:
  ```powershell
  & "$env:USERPROFILE\scoop\apps\godot-mono\current\godot-mono.console.exe" --headless --path Game --export-release "Windows Desktop" ../build/windows/FantasyWorldSimulator.exe
  ```
  This needs the Godot 4.7.2 .NET export templates in `%USERPROFILE%\scoop\apps\godot-mono\current\editor_data\export_templates\4.7.2.stable.mono` (installed 2026-11-24 from the official `Godot_v4.7.2-stable_mono_export_templates.tpz`, SHA-512 verified) and `Game/Game.sln` (the .NET exporter requires a solution). `build/` is git-ignored.

## What you see
A 128×128 pixel-art world generated from seed 1 (placeholder art, `Assets/CREDITS.md`) with 120 animals and two towns: Ashford (humans, blue tunics) and Cinderhold (demons, red skin and horns). The camera starts close to Ashford; zoom out with the mouse wheel. Day and night follow the simulation clock. They eat, drink, rest, flee, hunt, breed, age and die on their own; seasons change the plants. The top line shows year, season, day and tick.

## Controls
| Key / mouse | Action |
|---|---|
| Arrow keys, mouse wheel | Pan, zoom |
| Space | Pause / resume |
| `.` | One tick while paused |
| 1 / 2 / 3 / 4 | Speed ×1 / ×2 / ×4 / ×8 |
| Left click | Select the animal under the cursor (inspector: vitals, action, personality, fear, memories, bonds; dead animals keep their history) |
| F | Follow the selected animal |
| Esc | Clear selection |
| F3 | Show/hide technical details (tick, entities, event cursor, backlog) |
| G | God mode on/off |
| Q / W (God mode) | Spawn tool / terrain brush |
| Tab (God mode) | Next species / next terrain |
| Right click (God mode) | Spawn or paint on that tile (the world can refuse, e.g. painting a mountain on an animal) |
| F5 / F9 | Quick save / quick load (`%APPDATA%\Godot\app_userdata\Fantasy World Simulator\saves\quicksave.fws`) |

The bottom-right feed lists births, deaths (with cause and killer), attacks, spells, tamings, your God actions and save/load results.

## What people and animals can do (T015–T019)
- Towns farm, gather, store, trade with coins and keep guards (one per 8 members). Idle townsfolk read the town book; finishing it makes them casters with mana (Firebolt at range, Heal for wounded townmates).
- Villagers flee danger, but cornered or brave ones with friends nearby fight back. Damage depends on skill, health, allies and the target's toughness.
- Idle townsfolk may feed nearby deer, rabbits or wolves from town food; the animal can refuse. With enough trust it is tamed by the town, stays nearby and is not treated as a threat.
- **Dragon** (God spawn only: G, Q, Tab until Dragon): armoured, eats large game, everyone fears it, and it turns on whoever hurts it.
- Effects show real events: fireball and impact for Firebolt, green light for Heal, impacts for hits, a sparkle for a tamed animal.

Playtest checklist: `docs/PLAYTEST.md`. Known issues: `docs/KNOWN_ISSUES.md`.
