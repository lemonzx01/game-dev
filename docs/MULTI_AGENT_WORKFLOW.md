# Multi-Agent Shared Workflow & Anti-Collision Protocol

> **เป้าหมาย:** ให้ `Codex`, `Claude Code` และ `Google Antigravity` ช่วยกันทำงานทุก Task ได้อย่างยืดหยุ่นโดยไม่ล็อกหน้าที่ตามชื่อแอป **แต่ต้องไม่เขียนทับไฟล์เดียวกัน ไม่ทำ Task ซ้ำกัน และตรวจสอบได้ทันทีว่าตอนนี้ใครกำลังทำอะไรอยู่**

---

## 1. หลักการทำงานร่วมกัน (Flexible Task Pool)

- **ไม่มีการแบ่งเขตตายตัวตามชื่อแอป:** ทุก Task ใน [TASKS.md](../TASKS.md) เป็นงานกลางที่แอปใดก็ได้สามารถหยิบมาทำ รีวิว ช่วยรันเทสต์ หรือรับช่วงต่อได้ (เลือกตามความเหมาะสมของงานและโควตา/เครื่องมือในขณะนั้น)
- **กฎเหล็กข้อเดียว:** **1 Task ID = 1 Active Branch = 1 Active Worktree = 1 Active Owner ณ เวลาเดียวกัน**
- ห้ามสองแอปเปิดแก้ไฟล์ในโฟลเดอร์ทำงาน (working tree) เดียวกันพร้อมกันเด็ดขาด

---

## 2. จะรู้ได้อย่างไรว่าตอนนี้ใครทำอะไรอยู่? (Single Source of Truth)

### วิธีที่ 1: รันสคริปต์ดูภาพรวมทั้งหมดใน 1 วินาที
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\agent-status.ps1
```
สคริปต์นี้เป็น **Read-Only** และจะรายงานให้อัตโนมัติ:
1. Worktree / Checkout ทั้งหมดบนเครื่อง (รวม legacy checkout อย่าง `T001-review` หากยังอยู่)
2. Branch ปัจจุบัน, Commit ล่าสุด, และจำนวนไฟล์ที่กำลังแก้ค้าง (Dirty files) ของแต่ละโฟลเดอร์
3. Branch ทั้งหมดใน repository (`feat/Txxx-*` ที่มีอยู่แล้ว = งานนั้นมีคนทำอยู่ ห้ามสร้างซ้ำ)
4. ตาราง **`Active Claims`** จาก [TASKS.md](../TASKS.md)

### วิธีที่ 2: เปิดดูตาราง `## Active Claims` บนสุดของ [TASKS.md](../TASKS.md)
ทุกครั้งที่แอปใดเริ่มงาน, ส่งต่อ, หรือปิดงาน ต้องอัปเดตแถวของ Task นั้นใน `Active Claims` (และสถานะในตาราง Task หลัก) ให้ตรงกัน

---

## 3. กฎ 4 ชั้นเพื่อป้องกันการทำงานชนหรือซ้ำกัน

### ชั้นที่ 1: ใช้ Repo เดียว + `git worktree` แยกตาม Task (ห้าม `git clone` ซ้อน)
- Repo หลักอยู่ที่ `F:/Code/game-dev` (`main` เป็นฐานกลาง)
- เมื่อแอปใดหยิบงาน `<TASK_ID>` ให้สร้าง `git worktree` ใน `.worktrees/<TASK_ID>-<agent>`:
  ```powershell
  git checkout main
  git worktree add .worktrees/T003-codex -b feat/T003-world-clock main
  ```
- เมื่อจบงานและ merge เข้า `main` แล้วจึงลบ worktree ทิ้ง:
  ```powershell
  git worktree remove .worktrees/T003-codex
  git branch -d feat/T003-world-clock
  ```
- *(หมายเหตุกรณี Sandbox ของเครื่องมือบางตัวห้ามเขียน `.git/refs`: ให้เจ้าของรันคำสั่ง `git worktree add` ให้ล่วงหน้า แล้วชี้แอปนั้นไปเปิดทำงานเฉพาะในโฟลเดอร์ `.worktrees/<TASK_ID>-<agent>` แทนการ `git clone` แยก)*

### ชั้นที่ 2: Claim Lock ก่อนลงมือเขียนโค้ด
ก่อนเขียนโค้ดแม้แต่บรรทัดเดียว แอปต้องตรวจ 3 อย่าง:
1. Dependency ของ Task นั้นใน `TASKS.md` เป็น `DONE` (หรือ contract ที่ต้องใช้ frozen แล้ว)
2. ใน `TASKS.md` `## Active Claims` และ `git branch -a` **ยังไม่มีใครถือ `<TASK_ID>` นี้อยู่**
3. ลงชื่อ Claim Task (ระบุ `Agent`, `Branch`, `Worktree`, `Locked Zone`, `Status = IN_PROGRESS`) ก่อนเริ่ม

### ชั้นที่ 3: แยกเขตไฟล์เมื่อทำงานขนานกัน (Conflict Zones)
เมื่อมี 2–3 แอปทำงานพร้อมกันคนละ Task ให้ยึดเขตไฟล์เพื่อไม่ให้ merge ชน:
- **Zone A — Simulation Core:** `Simulation/`, `Tests/Simulation.Tests/`, `Tests/Headless/`, `Data/`
- **Zone B — Godot Presentation & Assets:** `Game/`, `Assets/`, `Sandbox/`
- **Zone C — Frozen Contracts & Shared Config:** `ARCHITECTURE.md §3`, `Simulation/Contracts.cs`, `global.json` → **ห้ามแก้ขณะอีกแอปทำงานอยู่** เว้นแต่ทำ ADR ใหม่และทุกแอปหยุดรอ sync
- **Zone D — Per-Task Handoff:** บันทึกผลการทำงานและหลักฐานการทดสอบของแต่ละงานลงไฟล์เฉพาะ **`docs/handoffs/<TASK_ID>.md`** (ดู [docs/handoffs/README.md](handoffs/README.md)) **ห้ามแย่งกันแก้หัวไฟล์ `PROGRESS.md` บนหลาย branch พร้อมกัน** โดย `PROGRESS.md` บน `main` จะอัปเดตสรุปเมื่อปิด/merge Task เข้า `main`

### ชั้นที่ 4: Handoff & ช่วยกันรับช่วงต่อ (Cross-Agent Assist)
หากแอป A ติดข้อจำกัด (เช่น โควตาหมด, sandbox รัน `dotnet test` ไม่ผ่านอย่างกรณี `0x800711C7`, หรือติดบั๊กที่อยากให้อีกแอปช่วย):
1. Commit ความคืบหน้าล่าสุดลง branch `feat/<TASK_ID>-...` (หรือบันทึก diff ชัดเจน)
2. เขียนสรุปลง `docs/handoffs/<TASK_ID>.md` พร้อมคำสั่งถัดไปที่ต้องรัน
3. เปลี่ยนสถานะใน `Active Claims` เป็น `HANDOFF_READY` (หรือ `BLOCKED (needs-assist)`)
4. แอป B อ่าน `docs/handoffs/<TASK_ID>.md` + `git diff` แล้วรับช่วงต่อใน **branch และ worktree เดิม** ได้ทันทีโดยไม่ต้องเขียนใหม่ตั้งแต่ต้น

---

## 4. ขั้นตอนรวมสาขาเก่าที่ค้างอยู่ตอนนี้ (Migration Plan จากสถานะปัจจุบัน)

1. **`T002` ของ `Codex` อยู่ใน `T001-review/` (`feat/T002-simulation-foundation` @ `48bb41f`):**
   - รัน `dotnet test Tests/Simulation.Tests -c Release -m:1` ใน Terminal ปกติ (หรือให้ `Antigravity` / `Claude Code` ช่วยรันตรวจ)
   - เมื่อ `T002` ผ่านการตรวจและอนุมัติแล้ว merge `feat/T002-simulation-foundation` (และ kit docs จาก `T001-review/`) เข้า `main`
   - Merge `chore/multi-agent-workflow` (ที่มี `Assets/`, `Sandbox/`, `docs/PRESENTATION_PLAN.md`, `scripts/agent-status.ps1`, `docs/MULTI_AGENT_WORKFLOW.md`) เข้า `main`
   - จากนั้นลบ nested repo `T001-review/` ทิ้งและเปลี่ยนมาใช้ `.worktrees/<TASK_ID>-<agent>` ทั้งหมด 100%

---

## 5. Prompt กลางแบบใหม่ (ใช้ได้กับทุกแอป)

```text
อ่าน AGENTS.md, ARCHITECTURE.md, TASKS.md, PROGRESS.md และ docs/MULTI_AGENT_WORKFLOW.md ก่อนเริ่ม
รัน `powershell -ExecutionPolicy Bypass -File .\scripts\agent-status.ps1` เพื่อดูว่าตอนนี้ใครกำลังทำ Task/Branch/Worktree ไหนอยู่
1. ห้ามทำงานซ้ำกับ Task ใน Active Claims หรือ branch feat/<TASK_ID>-* ที่เปิดอยู่แล้ว (เว้นแต่ได้รับมอบหมายให้รับช่วงต่อจาก docs/handoffs/<TASK_ID>.md)
2. เลือกเฉพาะ Task ที่เจ้าของสั่ง หรือ Task ที่ Dependency ครบและยังไม่มีเจ้าของ
3. ทำงานใน git worktree ของ Task นั้นเท่านั้น ห้ามแก้ไฟล์ข้ามเขตที่อีกแอปกำลังใช้อยู่
4. ทดสอบจริง บันทึกผลลง docs/handoffs/<TASK_ID>.md และอัปเดตสถานะใน TASKS.md (และ PROGRESS.md เมื่อ merge เข้า main)
```
