# Shared AI instructions — applies to every coding agent

## Before work
Read this file, GAME_DESIGN.md (invariants + task-relevant sections), ARCHITECTURE.md (relevant contracts), TASKS.md and PROGRESS.md. Read relevant TESTING / DECISIONS sections when needed. Do not scan unrelated code repeatedly; use targeted search. Inspect git status, current branch, existing code and tests. Never assume documentation means implementation exists.

## Team (equal peers)
- Codex, Claude Code and Google Antigravity are equal developers. None leads or reports to another, and no kind of work is reserved for a particular agent.
- Any agent may design, plan, write Simulation (C#) or Godot code, gameplay, UI/UX, tests, review another agent's code, fix bugs, optimize, take over a handed-off or stalled task, or build Roadmap features.
- The owner (user) controls the project: approves scope and design changes, chooses priorities, tests fun, and starts each agent manually. Do not wait for another agent's approval unless a real dependency (contract, prerequisite task, required review) exists.
- No orchestrator, dashboard, automatic task dispatch, or assigning work to other agents through MCP. Do not claim or assume another agent's remaining quota.

## Workflow
1. Pick work: the task the owner names, otherwise the next TASKS.md item whose dependencies are met and that nobody has claimed. Confirm scope, dependencies and acceptance. If a task is blocked, record the concrete blocker and move to another unblocked task.
2. Check before starting: `git worktree list`, `git branch -a` and `docs/handoffs/`. A task is claimed when a branch named `feat/<TASK_ID>-<slug>` (or `fix/…`) exists or a handoff says IN_PROGRESS. Claim by creating that branch in its own worktree and writing `docs/handoffs/<TASK_ID>.md` with state IN_PROGRESS, agent, branch and the files/areas you will touch. If you cannot tell whether a task is free, ask the owner.
3. One branch/worktree per task. Never let two agents write the same working tree or the same files at once without coordination. Do not overwrite, revert or delete another agent's changes. Keep required dependencies between systems intact. No destructive git commands, force push, secrets or unapproved external services.
4. Read contracts first. Make the smallest coherent change; clear English identifiers, comments only where useful. Avoid premature abstraction, giant files, needless frameworks or unrelated refactors. Gameplay values configurable where reasonably useful; no duplicated gameplay logic in UI.
5. Write appropriate tests (including regression tests for fixed bugs), run build/tests and relevant benchmark; record actual commands and results. If not runnable, mark NOT RUN and reason. Never invent tests, results or completed features. Never report a task done without real output that passes its acceptance criteria.
6. Do not edit `TASKS.md` or `PROGRESS.md` in task branches. The owner, or whoever the owner designates for that integration, updates them when work is integrated. Write handoff notes in `docs/handoffs/<TASK_ID>.md`. Use an ADR in docs/DECISIONS.md for cross-module or contract changes. Commit only if the owner permits; merge or push only with owner approval.
7. Continuous work: there is no fixed task count or session length. After finishing a task, write the handoff and continue with the next task that can start immediately, within the owner's permission and the provider's real quota. Try to solve problems yourself first. If an agent stopped or ran out of quota, you may take over its task after confirming nobody is still editing it: read its handoff and git diff, then continue instead of restarting.

## Review
Any agent may review any other agent's work. Where acceptance requires review (architecture/contract changes, T001/T014-style review tasks, or when the owner asks), the reviewer must be a different agent or the owner — never self-approve. Review reports give severity, file/line, why it matters, minimal fix and test.

## Non-negotiable quality
- No player-dependent simulation; camera does not decide outcomes. No plot armor, fake history, or free resources except explicit God commands.
- No full-population scans per agent, no AI decision every render frame, no unbounded GOAP/pathfinding or memory, no permanent Godot node required for every entity.
- No runtime API fees / LLM dependency in core. Do not add assets without source/license. Do not copy game IP.
- Validate IDs, commands and save data; resource counts nonnegative; dead entities cannot act; deterministic conflict resolution where required.
- Keep world logic testable in headless .NET; do not touch Godot SceneTree from background workers.
- Preserve existing save compatibility when applicable or explicitly version/migrate; no silent breaking changes.
- Approved Game Design and Architecture (A007 rev 2, frozen) still bind every agent; change them only through an owner-approved ADR.

## Handoff format
Task ID | agent | branch | state (TODO/IN_PROGRESS/BLOCKED/REVIEW/DONE) | files | changes | tests + results | unresolved issue | exact next step. DONE requires acceptance evidence. Keep it concise in `docs/handoffs/<TASK_ID>.md`. Write one when you finish or stop mid-task.

## Shared tools
- pixelfx (MIT, https://github.com/patch-gg/pixelfx): pixel art VFX library + skill, shared checkout at `F:\Code\pixelfx` (sibling of this repo). The skill is linked for Claude Code (`~/.claude/skills/pixelfx`), Codex (`~/.agents/skills/pixelfx`) and Antigravity (`~/.gemini/antigravity/skills/pixelfx`); all point to that checkout. Read `F:\Code\pixelfx\skills\pixelfx\SKILL.md` before use and run its `node tools/...` commands from `F:\Code\pixelfx`; do not clone another copy. Update with `git -C F:\Code\pixelfx pull` (owner approval).
- Use it for presentation only: export sprite sheets (`node tools/render.mjs <id> --sheet`) and import them into Godot with nearest filtering; effects never decide simulation outcomes. Copy only the chosen sheet/JSON into `Assets/` and record source, effect id, pixelfx commit and MIT license in `Assets/CREDITS.md` (some effects adapt Sprite Fusion, MIT: keep its notice). Do not follow the skill's "Publish it on Patch" step or install `pch` without explicit owner request.

## Authority
Latest explicit owner-approved decision > recorded ADR for technical details > GDD for design intent / Architecture for implementation constraints > task scope. If these disagree on a substantive change, ask owner before proceeding. Do not silently delete long-term game requirements to simplify MVP.
