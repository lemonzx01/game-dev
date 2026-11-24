# Copy/paste prompts

The same prompts work for Codex, Claude Code and Google Antigravity; no agent has a fixed role. Replace `<TASK_ID>` with an actual ID or leave it out to let the agent pick the next unclaimed task. These prompts are manual entry points and work even if a tool does not auto-read its instruction file.

## Short prompts (Thai) — copy/paste

Use these for daily work instead of the long prompts below. Close any old orchestrator session first so no automation hands out the same task.

### ทำงานต่อ / เปิด Session ใหม่
```text
ตรวจสอบความคืบหน้าล่าสุดของ Fantasy World Simulator จาก Source Code, Git History, TASKS.md, PROGRESS.md และ docs/handoffs/

ตรวจสอบว่า Agent ตัวอื่นกำลังทำงานอะไรอยู่ (git worktree list, git branch -a, docs/handoffs/) แล้วเลือกงานสำคัญที่ยังไม่มีผู้รับผิดชอบหรือรับช่วงงานที่หยุดค้างไว้ จองงานด้วย branch feat/<TASK_ID>-<slug> ใน worktree ของตัวเอง

ลงมือพัฒนาต่อทันที หากพบปัญหาให้แก้ไขเองก่อน หากติด Blocker ที่แก้ไม่ได้ให้บันทึกไว้แล้วเปลี่ยนไปทำงานอื่น

เมื่อเสร็จให้ทดสอบ สร้าง Commit บันทึก Handoff ใน docs/handoffs/<TASK_ID>.md และเลือกงานถัดไป ห้าม Merge หรือ Push โดยไม่ได้รับอนุญาต

ไม่ต้องแก้ Orchestrator หรือ Dashboard ให้เน้นพัฒนาเกมจริง

รายงานเป็นภาษาไทย
```

### รับช่วงงานเมื่อ AI ตัวเดิมโควตาหมด
```text
รับช่วงพัฒนา Fantasy World Simulator ต่อจาก Agent ก่อนหน้า

ตรวจสอบ Worktree, Branch, Git Diff, Git History และ docs/handoffs/ ของงานที่ทำค้างไว้

ยืนยันว่า Agent เดิมหยุดทำงานแล้ว ตรวจสอบโค้ดที่เขียนไว้ และพัฒนาต่อจากจุดล่าสุดโดยไม่เขียนใหม่ทั้งหมดโดยไม่จำเป็น

แก้ไขปัญหาที่เหลือ รัน Acceptance Tests สร้าง Commit และบันทึก Handoff ห้าม Merge หรือ Push โดยไม่ได้รับอนุญาต

เมื่อเสร็จให้เลือกงานถัดไปที่สามารถดำเนินการได้ทันที

รายงานเป็นภาษาไทย
```

## Any agent — start or continue development
```text
Read AGENTS.md (plus CLAUDE.md / ANTIGRAVITY.md if you are that tool), then the relevant parts of GAME_DESIGN.md, ARCHITECTURE.md, TASKS.md, PROGRESS.md and docs/TESTING.md. Inspect git status, `git worktree list`, branches and docs/handoffs/ to see what other agents are doing. Work on <TASK_ID>, or if none is given, the next TASKS.md task whose dependencies are met and that nobody has claimed. Claim it with its own feat/<TASK_ID>-<slug> branch/worktree and a docs/handoffs/<TASK_ID>.md IN_PROGRESS note. Follow the approved design and A007 contract, write tests, run build/tests and record the actual results. When done, write the handoff and continue with the next unblocked task. If blocked, record the blocker and switch to another task.
```

## Any agent — review another agent's task
```text
Read AGENTS.md and the relevant sections of GAME_DESIGN.md, ARCHITECTURE.md, TASKS.md, PROGRESS.md and docs/TESTING.md. Review task <TASK_ID> and its diff on its branch (you must not be the agent that wrote it). Prioritize correctness, simulation independence, resource conservation, determinism, performance and interface risks. Report findings with severity, file/line, minimal fix and test, and record the review in docs/handoffs/<TASK_ID>.md.
```

## Any agent — take over a stopped task
```text
Read AGENTS.md, TASKS.md, PROGRESS.md and relevant GDD/architecture sections. Confirm nobody is still editing <TASK_ID>, then continue it from the latest recorded branch/commit; inspect its handoff, git status and diff first. Preserve existing changes; do not restart or overwrite work. Complete the remaining acceptance criteria, run relevant tests and record actual results. If quota is low, leave a concise handoff with the exact next command.
```

## Session end reminder
```text
Write docs/handoffs/<TASK_ID>.md with branch, changed files, actual commands/results, remaining issues and exact next action. Do not mark DONE if tests or acceptance are missing. Preserve all unrelated changes. TASKS.md and PROGRESS.md are updated by the owner or the agent the owner designates when integrating.
```
