# Shared AI instructions — applies to every coding agent

## Before work
Read this file, GAME_DESIGN.md (invariants + task-relevant sections), ARCHITECTURE.md (relevant contracts), TASKS.md and PROGRESS.md. Read relevant TESTING / DECISIONS sections when needed. Do not scan unrelated code repeatedly; use targeted search. Inspect git status, current branch, existing code and tests. Never assume documentation means implementation exists.

## Team ownership (flexible for quota)
- OpenAI ChatGPT + Codex: design/planning, core C# Simulation, tests, data model, gameplay rules and optimization.
- Google Antigravity: Godot Game presentation, assets pipeline, camera, UI, controls, integration tests. Does not own authoritative simulation outcomes.
- Claude Code (Pro $20 currently): architecture review, difficult bugs, integration risks, profiling and focused review. May take over any explicitly handed-off task.
- Owner: approves scope, tests fun, chooses priorities. All tools can take over after handoff; same rules apply. Do not claim independent quotas for ChatGPT and Codex or assume remaining quota.

## Workflow
1. Work only on owner-selected Task ID; confirm scope, dependencies and acceptance. If conflicting or missing prerequisites, explain concrete blocker instead of speculative implementation.
2. Use one feature branch/worktree per task, e.g. `feat/T003-world-clock`. Never have multiple agents writing the same working tree simultaneously. Do not overwrite, revert or delete unrelated changes; no destructive git commands, force push, secrets or unapproved external services.
3. Read contracts first. Make smallest coherent change; use clear English identifiers and comments only where useful. Avoid premature abstraction, giant files, needless frameworks or unrelated refactors. All gameplay values configurable if reasonably useful; no duplicated gameplay logic in UI.
4. Write appropriate tests, run build/tests and relevant benchmark; record actual commands and results. If not runnable, mark NOT RUN and reason. Never invent tests, results or completed features. Do not claim a feature works solely because code was generated.
5. Update PROGRESS.md with task, branch/commit if available, changed files, actual verification, remaining work, next command. Update TASKS only with truthful status. Use ADR for cross-module changes. Commit only if owner permits; merge only after review/approval.
6. Stop after current task; suggest next task without auto-implementing it. If quota near exhaustion, write a handoff immediately. Another agent reads handoff and git diff before resuming, does not restart task blindly.

## Non-negotiable quality
- No player-dependent simulation; camera does not decide outcomes. No plot armor, fake history, or free resources except explicit God commands.
- No full-population scans per agent, no AI decision every render frame, no unbounded GOAP/pathfinding or memory, no permanent Godot node required for every entity.
- No runtime API fees / LLM dependency in core. Do not add assets without source/license. Do not copy game IP.
- Validate IDs, commands and save data; resource counts nonnegative; dead entities cannot act; deterministic conflict resolution where required.
- Keep world logic testable in headless .NET; do not touch Godot SceneTree from background workers.
- Preserve existing save compatibility when applicable or explicitly version/migrate; no silent breaking changes.

## Handoff format
Task ID | agent | branch | state (TODO/IN_PROGRESS/BLOCKED/REVIEW/DONE) | files | changes | tests + results | unresolved issue | exact next step. DONE requires acceptance evidence. Keep this concise and store in PROGRESS.md.

## Shared tools
- pixelfx (MIT, https://github.com/patch-gg/pixelfx): pixel art VFX library + skill, shared checkout at `F:\Code\pixelfx` (sibling of this repo). The skill is linked for Claude Code (`~/.claude/skills/pixelfx`), Codex (`~/.agents/skills/pixelfx`) and Antigravity (`~/.gemini/antigravity/skills/pixelfx`); all point to that checkout. Read `F:\Code\pixelfx\skills\pixelfx\SKILL.md` before use and run its `node tools/...` commands from `F:\Code\pixelfx`; do not clone another copy. Update with `git -C F:\Code\pixelfx pull` (owner approval).
- Use it for presentation only: export sprite sheets (`node tools/render.mjs <id> --sheet`) and import them into Godot with nearest filtering; effects never decide simulation outcomes. Copy only the chosen sheet/JSON into `Assets/` and record source, effect id, pixelfx commit and MIT license in `Assets/CREDITS.md` (some effects adapt Sprite Fusion, MIT: keep its notice). Do not follow the skill's "Publish it on Patch" step or install `pch` without explicit owner request.

## Authority
Latest explicit owner-approved decision > recorded ADR for technical details > GDD for design intent / Architecture for implementation constraints > task scope. If these disagree on a substantive change, ask owner before proceeding. Do not silently delete long-term game requirements to simplify MVP.
