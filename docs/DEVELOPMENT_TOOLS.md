# Development tools: evaluation and shared use

Evaluated by Claude Code on 2026-11-25 for Fantasy World Simulator (Godot 4.7.2 .NET, C#, Windows 11, Node 22, Python 3.12, .NET SDK 10.0.404).
Sources were read read-only through the GitHub API and raw files (README, LICENSE, manifests, entry scripts); nothing was cloned or installed during the evaluation.
All three agents (Codex, Claude Code, Antigravity) may use any tool here; nobody owns a tool.

> **สรุปภาษาไทย**
> - **ใช้อยู่แล้ว:** pixelfx (ทดสอบแล้ว ใช้ทำเอฟเฟกต์ในเกม)
> - **ติดตั้งแล้วเมื่อ 2026-11-25 (เจ้าของอนุมัติ):**
>   - jsfxr: สร้างเสียงประกอบแบบ offline เพราะเกมยังไม่มีเสียงเลย
>   - awesome-gamedev-agent-skills: เลือกเฉพาะบางสกิลของ Godot และ C#
> - **ลองทีหลัง:** Pixelbolt, godot-dotnet-mcp, FastNoiseLite (ใช้เฉพาะฝั่งภาพ)
> - **ไม่ใช้ตอนนี้:** GoRogue, mountain-goap, better-terrain, LPC generator, pixel-asset-gen
>   - เหตุผล: ซ้ำกับระบบที่มี ขัดกับ architecture หรือ license ยุ่งยาก
> - ห้ามเปลี่ยน simulation engine เพราะเจอ library ใหม่ (กฎข้อ 9 ของเจ้าของ)

## 1. Summary table

| Tool | Category | License (code / assets) | Offline, no paid API | Fits Godot .NET + Windows | Installed? | Decision |
|---|---|---|---|---|---|---|
| [patch-gg/pixelfx](https://github.com/patch-gg/pixelfx) | Agent Skill + External Asset Tool | MIT (© Patch, © Hugo Duprez; some effects adapt Sprite Fusion, MIT) / outputs are ours | Yes (Node only, zero deps) | Yes (sprite sheets + JSON) | **Yes**: `F:\Code\pixelfx`, linked for all 3 agents | **Use now** (in use since T019) |
| [chr15m/jsfxr](https://github.com/chr15m/jsfxr) | External Asset Tool (SFX) | Unlicense (public domain) | Yes (`sfxr-to-wav` CLI, no runtime deps) | Yes (WAV → Godot AudioStreamWAV) | **Yes**: `F:\Code\jsfxr` @ `b7b6aa2` (installed 2026-11-25, owner approved) | **Use now** |
| [gamedev-skills/awesome-gamedev-agent-skills](https://github.com/gamedev-skills/awesome-gamedev-agent-skills) | Agent Skills (79 + router) | Apache-2.0 (NOTICE: original work) | Yes (Markdown; Python only for its own validation) | Godot 4.7.2 + C# skills present | **Yes, 10 skills**: `F:\Code\gamedev-skills` @ `c6ca6c4`, linked for all agents (2026-11-25, owner approved) | **Use now** |
| [skulitom/Pixelbolt](https://github.com/skulitom/Pixelbolt) | Agent Skill + MCP + External Asset Tool | MIT | Yes (Node ≥20, zero deps, "no API key, no network service") | Yes (PNG sheets; has a `godot/` addon folder) | No | Later trial (overlaps pixelfx + our Python generators) |
| [LuoxuanLove/godot-dotnet-mcp](https://github.com/LuoxuanLove/godot-dotnet-mcp) | MCP (Godot editor plugin) | MIT | Yes (in-editor service) | Godot 4.6+ .NET | No | Later (only with the editor open; our workflow is headless) |
| [Auburn/FastNoiseLite](https://github.com/Auburn/FastNoiseLite) | Runtime Library (single C# file) | MIT | Yes | Yes | No | Later, **presentation only** |
| [Portponky/better-terrain](https://github.com/Portponky/better-terrain) | Godot Addon (GDScript) | Unlicense | Yes | Yes (works in .NET builds) | No | Not now (we bake terrain, no TileMap) |
| [Chris3606/GoRogue](https://github.com/Chris3606/GoRogue) | Runtime Library (.NET) | MIT (deps: SadRogue.Primitives, Troschuetz.Random) | Yes | netstandard2.1 … net10 | No | Not now (duplicates our grid/path/RNG) |
| [caesuric/mountain-goap](https://github.com/caesuric/mountain-goap) | Runtime Library (GOAP) | MIT (two MIT notices, hence GitHub "NOASSERTION") | Yes | netstandard2.1 | No | Not now (AGENTS: no unbounded GOAP) |
| [LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator](https://github.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator) | External Asset Tool | Code GPL-3.0; **assets per file**: CC-BY-SA 3.0/4.0, GPL 2/3, OGA-BY, CC0, with per-layer author credits | Yes (browser app) | PNG sheets | No | Not now (1.5 GB, share-alike + per-layer attribution, 64 px 4-direction style differs from ours) |
| [MozeeB/pixel-asset-gen](https://github.com/MozeeB/pixel-asset-gen) | External Asset Tool (Python + Pillow) | MIT | Yes | PNG + JSON + Godot sample | No | Not now (style modelled on a specific commercial game; we keep our own generators) |

No tool needs a paid API. None is required by the game at runtime except a runtime library would be, and none is adopted.

## 2. Per-tool notes

### pixelfx (installed, in use)
- **Helps with:** VFX sprite sheets (fire, hits, heals, magic, weather…) without drawing by hand. Recolour, reseed and resize with `--opts`.
- **Use:**
  - Read `F:\Code\pixelfx\skills\pixelfx\SKILL.md`. Run the tools from `F:\Code\pixelfx`:
    - `node tools/list.mjs <words>` to find effects
    - `node tools/render.mjs <id> [--opts JSON]` and look at `records/<id>/contact.png`
    - `--sheet` to export a sheet
    - `node tools/check.mjs <id>` to check an effect
  - Copy only the `.png` + `.json` into `Game/Assets/Effects/` and add a row to `Assets/CREDITS.md` with the effect id, options and pixelfx commit.
- **Tested 2026-11-25:**
  - `check.mjs` passes for all 4 shipped effects (`fire/fireball`, `impacts/blunt-hit`, `holy/heal-aoe`, `pickups/collect-burst`)
  - re-rendering `fire/fireball` with the recorded options gives a sheet byte-identical to `Game/Assets/Effects/fireball.png` (SHA-256 prefix `d4370d98483162da`), so outputs are reproducible
- **Limits:**
  - default canvases are large (heal 256×136); scale with options (`reach`, `radius`, `height`) or in the renderer
  - glow is baked into alpha
  - presentation only; never drive outcomes
  - do not use the skill's "Publish on Patch" step

### jsfxr (installed)
- **Helps with:** the game has **no sound at all**. jsfxr makes retro SFX (hit, explosion, power-up, pickup, blip) offline as WAV. It covers attacks, Firebolt, Heal, taming, deaths and UI clicks in minutes. The output is ours, and Unlicense means no attribution is needed.
- **Install:** shallow clone `F:\Code\jsfxr` (commit `b7b6aa2`, 0.8 MB). No `npm install` needed; dev dependencies are unused.
- **Use:**
  1. Design a sound in the local `F:\Code\jsfxr\index.html` (open in a browser, works offline), or start from a preset in Node:
     `node -e "const {sfxr}=require('F:/Code/jsfxr/sfxr.js'); const p=sfxr.generate('hitHurt'); console.log(p.toB58())"`
     The module exports `{ sfxr, Params, SoundEffect, convert, ... }`, so use `.sfxr.generate`; the README's `sfxr.generate` assumes the browser global.
  2. Keep the design in the repo as `tools/sfx/<name>.b58` (or the JSON).
  3. Render: `node F:/Code/jsfxr/sfxr-to-wav <b58> Game/Assets/Audio/<name>.wav`.
     **On Windows pass the b58 string as an argument:** the stdin/JSON path reads `/dev/stdin` and fails (ENOENT).
  4. Import the WAV in Godot (8-bit mono 44.1 kHz by default).
- **Security read:**
  - `sfxr-to-wav` reads arguments or stdin and writes one WAV
  - `sfxr.js` requires only `./riffwave.js`
  - no install hooks; dev dependencies only (playwright, tape, live-server) are not needed
- **Tested 2026-11-25:**
  - `hitHurt`, `pickupCoin`, `powerUp` and `explosion` rendered to valid WAVs (e.g. hit: mono, 8-bit, 44.1 kHz, 0.089 s)
  - square/saw/sine designs (`pickupCoin`, `powerUp`) re-render **byte-identical**
  - **noise designs (`wave_type` 3, e.g. `explosion`, many hits) do not**: the synth fills its noise buffer with `Math.random`, so each render differs slightly
- **Limits:**
  - `generate(preset)` is random: always keep the b58/JSON design
  - commit the rendered WAV itself for noise sounds, and treat the design as a source, not a reproducible build input
  - 8-bit by default (set `sample_size = 16` in a small wrapper if needed)
  - Node prints a harmless `Buffer()` deprecation warning
  - importing and playing the WAV in Godot is not tested yet; that belongs to the first audio task
  - playing audio belongs to Godot (a future presentation task); the simulation stays silent

### awesome-gamedev-agent-skills (installed selectively)
- **Helps with:** concise, version-pinned (Godot 4.7.2) references so agents write correct Godot C#, shaders, export settings and UI the first time. That means less trial and error in `Game/**`.
- **Useful here:**
  - Godot: `godot-csharp`, `godot-shaders`, `godot-ui-control`, `godot-export`, `godot-audio`, `godot-2d-movement` (for T020)
  - general: `procedural-gen`, `save-systems`, `game-feel`, `ai-behavior-trees-utility-ai`
- **Install:**
  - shallow clone `F:\Code\gamedev-skills` (commit `c6ca6c4`, 8.2 MB)
  - the 10 skills above are linked as Windows **junctions** (`New-Item -ItemType Junction`, no admin) into `%USERPROFILE%\.claude\skills`, `%USERPROFILE%\.agents\skills` (Codex) and `%USERPROFILE%\.geminintigravity\skills`, next to `pixelfx`
  - no router, no other skills
- **Checked before linking:**
  - each linked folder holds `SKILL.md` and `references/`; two also have small local GDScript helpers (`check_audio_buses.gd`, `test_character_controller.gd`)
  - no download, install or API-key commands
- **Tested 2026-11-25:** Claude Code's skill list shows all 10 right after linking; the same `SKILL.md` reads through the Antigravity and Codex links.
- **Add or remove one:** create or delete the junction in all three folders; never copy the files.
- **Do not** run `npx skills add … -g`: it installs all 79 skills plus a router into every agent and crowds every session's skill list.
- **Update:** `git -C F:\Code\gamedev-skills pull`, with owner approval, then re-read changed skills.
- **Limits:**
  - generic advice. **AGENTS.md, ARCHITECTURE.md and the owner's decisions win** whenever a skill disagrees (e.g. GOAP, per-entity nodes, non-deterministic RNG).
  - the README carries sponsor links; ignore them.

### Pixelbolt (later trial)
- **Helps with:** editable JSON "recipes" for sprites, tiles, props and fluid-sim effects. It can re-read and fix single pixels. Candidates: houses, market stalls and banners for the warm-town look (P3/P4 in KNOWN_ISSUES).
- **Why later:**
  - it overlaps with pixelfx (effects) and our deterministic Python generators (`tools/art/*.py`)
  - a 52 MB checkout
  - its default skill install (`npx -y pixelbolt@latest skill install`) fetches the latest version on every run; prefer a pinned local clone
- Offline, MIT, zero runtime dependencies, no install hooks (checked `package.json`).

### godot-dotnet-mcp (later)
- **Helps with:** an MCP bridge into a *running Godot editor*: scene tree, inspector, runtime errors, input injection, frame capture. Useful for T020 possession input testing and UI work.
- **Why later:**
  - it is an editor addon in `Game/addons` that must be enabled in `project.godot`, which changes the shared project
  - it is only useful with the editor GUI open (our agents work headless with acceptance scenes and `--write-movie`)
  - small user base (40 stars); its README says the code is entirely AI-generated
  - before any use: review the source, confirm the service binds to localhost only, and keep it out of export builds
- **Limits:** Godot 4.6+ .NET only; one client config per agent.

### FastNoiseLite (later, presentation only)
- Single-file MIT C# noise (Perlin, Simplex, Cellular, fractal).
- **Not for `Simulation/**`:** it computes in `float` (`FNLfloat = System.Single`), and the world map already has its own deterministic generator in `Simulation/Core/WorldMap.cs`. Swapping it would change every StateHash and save and risk cross-machine drift. That violates the owner's rule 9 and our determinism contract.
- Acceptable in `Game/**` for cosmetic noise (cloud shadows, grass sway, water shimmer) or in an offline map-preview tool. Copy the single file with its MIT header and record it in `Assets/CREDITS.md`.

### better-terrain (not now)
- Unlicense GDScript addon for autotiling Godot `TileMap`/`TileMapLayer` terrains.
- We bake terrain into one texture in `MapRenderer` (T018-env shoreline masks), so adopting it means moving terrain to TileMapLayer, which is a presentation-architecture change. Reconsider only if terrain transitions grow beyond what the bake does.

### GoRogue (not now)
- MIT .NET library: FOV, A* and goal maps, spatial maps, map generation, dice, RNG, component system.
- **Rejected for the simulation:**
  - it duplicates our spatial grid, budgeted BFS, scheduler and seeded RNG streams
  - it brings `Troschuetz.Random` and its own types whose state is not in our save/StateHash
  - it would add a dependency to a Godot-free core that we keep minimal
- **Possible later use:** a reference oracle in tests (compare our path lengths to GoRogue A* on random maps) inside `Tests/**` only, never shipped.

### mountain-goap (not now)
- MIT GOAP planner with string-keyed `Dictionary` state.
- AGENTS.md forbids unbounded GOAP. Our utility AI is budgeted and deterministic; a planner would need search caps, deterministic ordering and save/hash of plans.
- Revisit only for a bounded, small-domain planner (e.g. a mage's spell sequence) after a design decision.

### Universal LPC Spritesheet Character Generator (not now)
- Huge layered character base.
- **Licence burden:** every layer has its own author list and licence, often CC-BY-SA (share-alike) or GPL. Shipping them requires per-sprite credits and share-alike for derived sheets.
- The 64 px four-direction look also does not match our 24 px side-view art.
- If the owner later wants four-direction humans, prefer CC0/OGA-BY layers only and generate `CREDITS.csv` from the tool's metadata.

### pixel-asset-gen (not now)
- MIT Python + Pillow procedural sprite generator (186 assets, Godot sample).
- Its README describes the output as imitating a specific commercial game's style. Our rules forbid copying a game's look, and we already generate matching art with `tools/art/make_*.py`. At most, read its techniques (atlas packing, metadata), don't ship its sprites.

## 3. Rules for adding any tool (all agents)
1. Ask the owner before any download or install. State the source, size and destination.
2. Read README, LICENSE, the manifest (dependencies, install hooks) and entry scripts before running anything. Note network access and API keys. Prefer pinned local clones over `npx …@latest`.
3. Shared tools live **once**, beside the repo (`F:\Code\<tool>`), and are linked into each agent's skills folder. Never clone a second copy.
4. Code licence and asset/output licence are recorded separately in `Assets/CREDITS.md` for anything that ships. Keep upstream notices (e.g. `Game/Assets/Effects/LICENSE-pixelfx.txt`).
5. Runtime libraries need a written check of determinism, StateHash, save/load, performance and ARCHITECTURE fit, plus tests, **before** use. Never swap a working simulation system just because a library exists.
6. Agent Orchestrator and Dashboard are out of scope (owner rule).
