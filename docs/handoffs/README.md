# Per-Task Handoff Directory

ใช้โฟลเดอร์นี้เก็บไฟล์บันทึกส่งต่องานแยกตาม Task ID (`<TASK_ID>.md`, เช่น `T002.md`, `T003.md`, `T007.md`) เพื่อป้องกัน Merge Conflict บน `PROGRESS.md` เมื่อหลายแอปทำงานขนานกันคนละ branch

## Template (`docs/handoffs/<TASK_ID>.md`)

```markdown
# <TASK_ID> — <Short Title>

- **Current Owner / Agent:** Codex | Claude Code | Antigravity
- **Status:** CLAIMED | IN_PROGRESS | HANDOFF_READY | BLOCKED | REVIEW | DONE
- **Branch:** `feat/<TASK_ID>-<short-name>`
- **Worktree:** `.worktrees/<TASK_ID>-<agent>`
- **Locked File Zones:** `Simulation/...` or `Game/...`
- **Last Updated:** YYYY-MM-DD HH:MM

## Summary of Changes
- ...

## Verification Commands & Actual Results
- Command: `...` -> Result: `...` (or `NOT RUN` + exact reason)

## Unresolved Issues / Blockers
- ...

## Exact Next Step (for next agent or reviewer)
- ...
```
