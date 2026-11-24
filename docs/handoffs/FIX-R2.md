# FIX-R2 — T019 Review Fixes (Simulation)

- **Task ID:** FIX-R2
- **Agent:** Google Antigravity
- **Branch:** `fix/R2-review-T019` (worktree `worktrees/FIX-R2-antigravity`)
- **State:** `IN_PROGRESS`
- **Review source:** `docs/reviews/2026-11-codex-review-T019.md`
- **Files in scope:** `Simulation/**`, `Tests/Simulation.Tests/FixR2Tests.cs`, `Tests/Simulation.Tests/T019Tests.cs`, `Tests/Simulation.Tests/Program.cs`, `docs/handoffs/FIX-R2.md` (and `PROGRESS.md` entry on integration)
- **Changes:** Claimed FIX-R2 simulation review fixes (`1–9, 13, 14, 15-sim`). Claude Code already completed `10–12, 15-visual` in `FIX-R2-visuals`.
- **Tests + results:** Not yet run after claim.
- **Unresolved issues:** Findings `1–9, 13, 14, 15-sim` queued for implementation in order: High (`1, 2`) -> Medium (`3–9, 13`) -> Low (`14, 15-sim`).
- **Exact next step:** Write regression tests and implement fixes for High findings `1` (re-check hunt eligibility at execution) and `2` (reject invalid taming/mana/feeder IDs on load).
