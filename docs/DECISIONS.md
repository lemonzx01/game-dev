# Decisions and open questions

## Design decisions from project discussion
D001 Autonomous fantasy world: fun to observe without intervention. D002 Any chosen protagonist may die; no special protection. D003 Real dragons, diverse ordinary and magical animals; taming instead of spirit rings. D004 Civilized races including humans/demons build cities, can trade, ally and fight. D005 Survival, seasons, predator/prey, emotions, skills/books, trade/guards and wars. D006 Multiple worlds later, not just war. D007 God mode + observer + direct character control, command mode as later expansion.

## Proposed technical defaults (owner can revise before implementation)
A001 Godot 4 .NET + separate pure C# simulation; rationale: headless testing and rendering independence. A002 Utility AI first, GOAP later; no mandatory runtime LLM. A003 Stable IDs, fixed ticks, event scheduler, spatial grid, compact data; multithreading only when measured. A004 Modular top-down pixel art, placeholder/licensed assets first. A005 Shared repository and flexible AI ownership via handoff. A006 MVP smaller than long-term game, 20–100 initial NPCs, 100–1,000 initial benchmark entities; population of 100k is research.

## Open decisions — do not invent final answers
- Final game title, art style/palette, camera projection and exact sprite sizes.
- Exact Godot/.NET versions and minimum hardware; CPU benchmark before capacity claims.
- Time scale, day length, seasonal duration, reproduction rates, hunger/health balance.
- Species roster, magic taxonomy, religion/culture, inheritance, political rules and moral systems beyond confirmed design.
- How detailed combat, dungeon and multiworld simulations should be; long-term scope before implementation.
- Licensing for assets and distribution, modding format, release platform beyond initial Windows build.

## ADR template
ID/date/status (Proposed/Accepted/Superseded), problem, options, decision, rationale, consequences, affected contracts, owner approval, migration/test plan. Add new entries instead of silently changing shared contracts.

## A007 · 2026-11-24 · Accepted (revision 2) · T001

ให้ Simulation ถือ world state และชนิดข้อมูลของ interface ใน ARCHITECTURE.md §3 ทั้ง Game และ headless host ใช้สัญญาเดียวกัน พิจารณาการส่ง Godot signals จาก core แล้วเลือกให้ host อ่านผลหลัง fixed tick เพื่อให้ทดสอบซ้ำได้โดยไม่ต้องโหลด engine การแชร์ immutable chunks ลดการคัดลอกตาม render FPS โดยแลกกับการจัดการอายุ snapshot ฝั่งผู้เรียก ซึ่งควรเก็บเฉพาะภาพที่กำลังใช้

Claude Code ตรวจลำดับคำสั่ง อายุ snapshot และการอ่าน event ระหว่างหลาย tick ก่อนเสนอให้เจ้าของรับรอง ยังไม่มี caller หรือ save เดิมให้ migrate ขั้นตรวจเมื่อมี implementation คือ replay คำสั่งที่แย่งทรัพยากร เก็บ snapshot เก่าข้าม tick และเทียบผล headless กับ host ที่เปลี่ยนกล้อง/จังหวะวาด รุ่น SDK, Godot และ target framework ยังเปิดไว้จนทดสอบ build คู่จริง
**Review 2026-11-24 (Claude Code):** CHANGES REQUESTED — ต้องแก้ ARCHITECTURE.md ที่ซ้ำ (H1), แยก event log ที่มี cursor (H2), นิยาม SubmitCommand/TargetTick และเก็บคิวคำสั่งใน save (H3), EntityId ไม่ reuse + นโยบายการตาย (H4) รายละเอียดใน PROGRESS.md สถานะ A007 ยังเป็น Proposed

### A007 revision 2 · Accepted · 2026-11-24

สัญญาฉบับที่ส่งตรวจซ้ำอยู่ใน ARCHITECTURE.md §3–4, §8–9 และใช้แทนข้อเสนอเดิมเรื่องการส่ง events หลังทุก Step การใช้ cursor แยกจาก snapshot ทำให้ host ตามเหตุการณ์ทันโดยไม่ต้องสร้างภาพทุก tick ยอมให้ผลคำสั่งเก่าที่หลุดจาก ring มีสถานะ unknown พร้อมรายงานช่วงที่หาย ส่วนประวัติการตายยังค้นได้ การเก็บคิว ตัวนับ และสถานะ RNG ทำให้ save ระหว่างรับคำสั่งกับประมวลผลมีความหมายชัดเจน

เพดาน queue/log/page, memory/relationship และ history cache เป็นค่าเริ่มต้นที่เสนอให้ทดสอบ ไม่ใช่ผล benchmark รุ่น SDK, Godot และ target framework รอการยืนยันบนเครื่องจริง ยังไม่มี save เดิมให้ migrate สถานะ A007 รอ Claude และเจ้าของรับรอง หลักฐานทดสอบอยู่ใน docs/TESTING.md และ PROGRESS.md
**Re-review 2026-11-24 (Claude Code):** ARCHITECTURE APPROVED สำหรับ revision 2 — เงื่อนไข N1 (ผลคำสั่งถูก evict ใน tick ที่มี event เกิน capacity → T003) และ N2 (archive ต้องผูกกับ timeline ของ save → T013) รายละเอียดใน PROGRESS.md รอเจ้าของเปลี่ยนสถานะเป็น Accepted
**Owner approval 2026-11-24:** เจ้าของรับรอง A007 revision 2 เป็น Accepted สัญญาใน ARCHITECTURE.md §3–4, §8–9 ถือเป็น frozen contract การเปลี่ยนต่อจากนี้ต้องทำ ADR ใหม่ เงื่อนไข N1 (T003) และ N2 (T013) ยังต้องปิดตามที่ระบุ
ข้อกำหนดที่ยกไป: N1, N4 → T003; N2 → T013; N3 → Antigravity ก่อน T007; acceptance tests 9 ข้อ → T002 (TASKS.md) T001 ยัง BLOCKED (environment)

## A008 · 2026-11-24 · Accepted (owner) · Process
**Decision:** ยกเลิกการแบ่งบทบาท AI เดิม Codex, Claude Code และ Google Antigravity เป็นนักพัฒนาเท่าเทียมกัน ทำได้ทุกงาน (Simulation, Godot, UI/UX, tests, review, bugfix, performance, takeover) ผู้ใช้เป็นผู้ควบคุมและเปิด AI แต่ละตัวเอง ไม่มี orchestrator / dashboard / การแจกงานอัตโนมัติ / การสั่งงาน AI อื่นผ่าน MCP
**Kept:** Game Design และ A007 rev 2 (frozen), acceptance criteria, automated/regression tests, branch/worktree ต่องาน, ห้ามแก้ไฟล์เดียวกันพร้อมกัน, handoff, ผู้ใช้ (หรือผู้ที่ผู้ใช้มอบหมาย) รวม TASKS.md / PROGRESS.md, งานที่ต้อง review ต้องใช้ reviewer ที่ไม่ใช่ผู้เขียน
**Claim mechanism:** branch `feat/<TASK_ID>-<slug>` + `docs/handoffs/<TASK_ID>.md` (IN_PROGRESS) ดู AGENTS.md Workflow 1–2
**Supersedes:** "Team ownership" เดิมใน AGENTS.md, Role ใน CLAUDE.md / ANTIGRAVITY.md, prompt แยกบทบาทใน START_HERE.md, คอลัมน์ Default owner ใน TASKS.md การมอบ N3 ให้ Antigravity ใน A007 ข้างบนเป็นบันทึกประวัติ ปัจจุบัน N3 ทำโดย AI ใดก็ได้ก่อน T007

## A009 · 2026-11-24 · Accepted (owner) · Process
**Decision:** The owner waives independent agent review for now: Claude Code reviews and integrates its own work ("รีวิวเองทำเองเลย"). Merges record "self-reviewed"; another agent continues from the handoffs if Claude Code hits its limits. Tests, acceptance evidence and handoffs remain mandatory. Earlier merges marked "without independent review" fall under this decision.
