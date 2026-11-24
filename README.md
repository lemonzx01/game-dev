# game-dev

## Fantasy World Simulator — Shared AI Project Kit

ชุดเอกสารตั้งต้น v0.1 | ภาษาไทยสำหรับอธิบายเกม ภาษาอังกฤษสำหรับชื่อระบบและโค้ด | ยังไม่ใช่เกมที่พัฒนาแล้ว

## วิธีใช้
1. แตก ZIP แล้วคัดลอกไฟล์ในโฟลเดอร์นี้ไปไว้ที่ root ของ Git repository เกม (ไม่ต้องสร้าง root ซ้อน) ตรวจสอบไฟล์เดิมก่อนทับ
2. ใช้ Godot 4 .NET และ C# ตาม ARCHITECTURE.md; ตรวจสอบรุ่น Godot/.NET ที่ใช้จริงก่อนสร้างโค้ด
3. Codex, Claude Code และ Google Antigravity เป็นนักพัฒนาที่เท่าเทียมกัน ไม่มีการแบ่งบทบาทตายตัว ใช้ Git branch/worktree แยกต่องานเมื่อทำพร้อมกัน
4. ผู้ใช้เปิด AI แต่ละตัวเองและส่ง prompt ใน START_HERE.md (ระบุ Task ID หรือให้เลือกงานถัดไปที่ยังไม่มีใครจอง)
5. จองงานด้วย branch `feat/<TASK_ID>-…` และ `docs/handoffs/<TASK_ID>.md` ทดสอบจริง บันทึก handoff แล้วทำงานถัดไปที่พร้อมได้ต่อเลย; ผู้ใช้หรือผู้ที่ผู้ใช้มอบหมายเป็นผู้รวม TASKS.md / PROGRESS.md

## แหล่งความจริงร่วมกัน (Single Source of Truth)
| ไฟล์ | หน้าที่ |
|---|---|
| GAME_DESIGN.md | วิสัยทัศน์ กฎของเกม และขอบเขตระยะยาว |
| ARCHITECTURE.md | ข้อตกลงทางเทคนิคและขอบเขต Simulation / Presentation |
| AGENTS.md | กฎกลางสำหรับ AI ทุกตัว (นักพัฒนาเท่าเทียมกัน, การจองงาน, handoff) |
| CLAUDE.md | จุดเริ่มสำหรับ Claude Code |
| ANTIGRAVITY.md | จุดเริ่มสำหรับ Antigravity (ให้อ่านโดย explicit prompt) |
| ROADMAP.md | แผน 30 วันและระยะถัดไป |
| TASKS.md | รายการงาน dependency และเกณฑ์ผ่าน (ไม่ผูกกับ AI ตัวใด) |
| PROGRESS.md | สถานะล่าสุดและบันทึกส่งต่องาน |
| docs/TESTING.md | วิธีทดสอบ correctness, performance และ simulation |
| docs/DECISIONS.md | บันทึกการตัดสินใจและสิ่งที่ยังไม่ยืนยัน |
| START_HERE.md | Prompt เริ่มงาน รีวิว และรับช่วงต่อ (ใช้ได้กับ AI ทุกตัว) |
| docs/handoffs/ | บันทึกการจองงานและส่งต่องานรายงาน |

เอกสารนี้เป็นข้อเสนอฐานที่เจ้าของเกมสามารถแก้ไขได้ ไม่ได้อ้างว่ามี source code, repository หรือผลทดสอบอยู่แล้ว หากพบความขัดแย้งให้หยุดเฉพาะส่วนที่ขัดแย้งและแจ้งเจ้าของเกม ไม่เดาเองหรือแก้เป้าหมายเกมโดยพลการ
