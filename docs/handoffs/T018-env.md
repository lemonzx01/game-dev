# T018-env — Environment pixel art

State: DONE — reviewed and merged into main by Claude Code (2026-11-25) on top of T019; 89/89 tests and all four Godot acceptances pass on the merged main
Agent: Codex | Branch: feat/T018-environment-art | Base: 69e3a51 | Claim: 58d191a
Checkout: F:/Code/game-dev/worktrees/T018-env-codex (independent local repository).

- Deterministic Pillow generator tools/art/make_environment.py. Two environment atlases and their Godot import metadata in Game/Assets/Art, two rows in Assets/CREDITS.md.
- MapRenderer.cs reads terrain, actual resource amounts, plots and settlement factions. Grass variants and tree type depend on coordinates. Four shore sides compose at corners. Three phase-offset water highlights use presentation time and visible bounds. A harvested wood node draws a stump. Crop and shed visuals use the existing plot and town data.
- Character methods, simulation and tests unchanged. Self-review checked the scoped diff, coordinate stability, stock predicates, nearest filtering and native sprite sizes.

## Verification
- python tools/art/make_environment.py twice: both PNG SHA-256 sums unchanged.
- dotnet build Game/Game.csproj -c Debug -m:1 -p:NuGetAudit=false: 0 errors, 0 warnings.
- Godot 4.7.2 .NET from Scoop, same binaries staged in a temporary directory; offline NuGet feed; tests used a disposable copy so editor-generated files do not alter other assets.
- godot-mono.console.exe --headless --path Game --import: exit 0, both environment textures imported and displayed; Godot also logged one Windows safe-save error at startup.
- godot-mono.console.exe --headless --path Game res://Tests/<scene>Acceptance.tscn (Presentation, Observer, GodTools, Settlement): T007_ACCEPTANCE_PASS, T008_ACCEPTANCE_PASS, GODTOOLS_ACCEPTANCE_PASS, SETTLEMENT_ACCEPTANCE_PASS.
- dotnet test Tests/Simulation.Tests -c Release -m:1 -p:NuGetAudit=false: 82/82 groups passed.
- godot-mono.console.exe --path Game --write-movie <temp>/movie/f.png --fixed-fps 30 --quit-after 240 --resolution 1280x720: 240 frames. Movie Maker reported its viewport at 1152x648. Inspected frame 120, docs/handoffs/T018-env.png. Ashford at night shows a blue-roofed store, planted rows, berry bushes, both trees and shoreline; UI and characters remain visible.
- git diff --check: passed.

## Integration
Shared .git rejected FETCH_HEAD writes. Fetching GitHub from the task checkout also failed (DNS).
Next: fetch this local checkout's feat/T018-environment-art into the shared repo, fetch current origin/main, integrate and retest any changes before the authorized merge and push. No merge or push performed.
