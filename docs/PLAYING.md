# Playing Fantasy World Simulator (prototype)

## Start
- Double-click `Play.cmd` in the repository root (or run it in a terminal). It builds the C# projects, imports assets the first time, and opens the game with the Godot 4.7.2 .NET installed by Scoop (`scoop install godot-mono`; .NET SDK 10.0.404 per `global.json`).
- **Standalone build:** `build/windows/FantasyWorldSimulator.exe` (with `FantasyWorldSimulator.pck` and `data_Game_windows_x86_64/`, ~182 MB, self-contained .NET runtime; copy the whole folder). Rebuild it with:
  ```powershell
  & "$env:USERPROFILE\scoop\apps\godot-mono\current\godot-mono.console.exe" --headless --path Game --export-release "Windows Desktop" ../build/windows/FantasyWorldSimulator.exe
  ```
  This needs the Godot 4.7.2 .NET export templates in `%USERPROFILE%\scoop\apps\godot-mono\current\editor_data\export_templates\4.7.2.stable.mono` (installed 2026-11-24 from the official `Godot_v4.7.2-stable_mono_export_templates.tpz`, SHA-512 verified) and `Game/Game.sln` (the .NET exporter requires a solution). `build/` is git-ignored.

## What you see
A 128×128 world generated from seed 1 with 120 animals (deer brown, sheep white, rabbit tan, wolf dark). They eat, drink, rest, flee, hunt, breed, age and die on their own; seasons change the plants. The top line shows year, season, day and tick.

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
| G | God mode on/off |
| Q / W (God mode) | Spawn tool / terrain brush |
| Tab (God mode) | Next species / next terrain |
| Right click (God mode) | Spawn or paint on that tile (the world can refuse, e.g. painting a mountain on an animal) |
| F5 / F9 | Quick save / quick load (`%APPDATA%\Godot\app_userdata\Fantasy World Simulator\saves\quicksave.fws`) |

The bottom-right feed lists births, deaths (with cause and killer), attacks, your God actions and save/load results.
