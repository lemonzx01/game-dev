# Progress & handoff

## Current state

**T001 · ARCHITECTURE APPROVED · A007 ACCEPTED (owner 2026-11-24) · BLOCKED (ยังไม่มี project ให้ build)** — ติดตั้ง .NET SDK 10.0.404 และ Godot 4.7.2 .NET แล้ว (ดู handoff ล่าสุด) แต่ยังไม่มี build จริง T002 อยู่ใน TODO

ข้อกำหนดที่ยกไปจากรีวิว: N1+N4 → T003, N2 → T013, N3 → Antigravity ก่อน T007, acceptance tests 9 ข้อ → T002 (ดู TASKS.md "Requirements carried from T001 review") ไม่มีข้อใด implement หรือทดสอบแล้ว

## Handoff log

**2026-11-24 | Claude Code | T001 environment install | feat/T001-simulation-contract**

- ติดตั้งตามคำขอเจ้าของ ผ่าน winget จากแหล่งทางการ (winget ตรวจ installer hash ผ่านทั้งคู่):
  - `.NET SDK 10.0.404` (Microsoft.DotNet.SDK.10) → `dotnet --list-sdks` = `10.0.404 [C:\Program Files\dotnet\sdk]`
  - `Godot 4.7.2 .NET` (GodotEngine.GodotEngine.Mono) — `winget install` ล้มด้วย "Portable package from a different source already exists" จึงใช้ `winget download` (hash ผ่าน) แล้วแตก ZIP เองที่ `%LOCALAPPDATA%\Programs\Godot\Godot_v4.7.2-stable_mono_win64\` ไม่ได้เพิ่มใน PATH; `Godot_v4.7.2-stable_mono_win64_console.exe --version --headless` = `4.7.2.stable.mono.official.ed1daf0bf`
- หมายเหตุ: รุ่นนี้ต่างจากที่ Codex เสนอ (Godot 4.5.1) เพราะ winget มี 4.7.2 เป็น stable ล่าสุด ให้ยืนยัน TargetFramework จาก project ที่ Godot 4.7.2 สร้างจริงก่อน pin
- สิ่งที่ยังไม่ทำ: ยังไม่มี .csproj/project.godot จึงยังไม่มี build/test ใด ๆ (NOT RUN) เกณฑ์ "actual build commands" ของ T001 ยังไม่ครบ เจ้าของตัดสินว่าจะปิด T001 หรือย้ายการยืนยัน build ไปเป็นข้อ 1–3 ของ T002

**2026-11-24 | Claude Code | T001 review close-out | feat/T001-simulation-contract | BLOCKED (environment)**

- เจ้าของรับรอง A007 revision 2 เป็น Accepted (docs/DECISIONS.md) T001 คงเป็น BLOCKED (environment) จนกว่าจะติดตั้ง .NET SDK + Godot .NET และผ่านเกณฑ์ T001
- บันทึก N1, N4 → T003; N2 → T013; N3 → Antigravity ก่อน T007; acceptance tests 9 ข้อ → T002 ใน TASKS.md
- N5: ลบบันทึกเก่าท้าย PROGRESS.md ที่ระบุ T001 = REVIEW (ข้อมูลเดียวกันอยู่ใน handoff ของ Codex แล้ว) และใช้สถานะ BLOCKED (environment) ตรงกันใน TASKS/PROGRESS
- Implementation และ automated tests: NOT RUN ทั้งหมด ไม่มี source code ใน checkout
- ขั้นถัดไป: เจ้าของติดตั้งเครื่องมือแล้วรัน `dotnet --list-sdks` → ปิด T001 → มอบ T002 ให้ Codex

**2026-11-24 | Claude Code | T001 Re-Review (A007 rev 2) | feat/T001-simulation-contract @ 233a9dd (uncommitted)**

ผลแยกเป็นสามส่วน:
1. **Architecture (เอกสาร): ARCHITECTURE APPROVED** — H1–H4, M1–M5, L1–L2 แก้ครบในระดับสัญญา เงื่อนไข N1–N2 ด้านล่างไม่บล็อก T002 แต่ต้องปิดใน task ที่ระบุ
2. **Implementation: NOT RUN** — ไม่มี source/.csproj/project.godot ไม่มี acceptance test ใดผ่าน
3. **Environment: ENVIRONMENT BLOCKED** — รัน `dotnet --list-sdks` ซ้ำ ผลว่าง, ไม่มี `C:\Program Files\dotnet\sdk`, ไม่พบ Godot .NET

หลักฐานที่ตรวจจริงรอบนี้: `grep "^## "` ARCHITECTURE.md ได้ §1–10 ไม่ซ้ำ (H1); อ่าน §3, §4 Deterministic work, §8, §9 และ TESTING.md T001 Revision; คำนวณ RNG test vector ใหม่ด้วย Python อิสระจากสเปกใน ARCHITECTURE.md:78 ได้ init `a95b939b9ebb349a`, draw `5fb88fd6df7259e5`, `b8b82154685001e7` ตรงกับ TESTING.md (สูตร SplitMix64 ตรงมาตรฐาน); parent repo `git check-ignore -v T001-review` → `.gitignore:1` และ `git ls-files --stage -- T001-review` ว่าง (L2)

| ข้อ | ผล | หมายเหตุ |
|---|---|---|
| H1 | ผ่าน | ไม่มีหัวข้อซ้ำ; pause/speed/track อยู่ใน host (§3:47) |
| H2 | ผ่าน | event log แยก, EventSequence ต่อเนื่อง, cursor exclusive, Gap ระบุช่วงชัด, ไม่มี callback (§3:55-59) |
| H3 | ผ่าน | re-entry throw ก่อน mutation, TargetTick = LastCompletedTick+1, reject ไม่ใช้ sequence, ลำดับ apply ตาม sequence ก่อน systems; คิว/ตัวนับอยู่ใน save (§3:47-51, §9:105) |
| H4 | ผ่าน | ID 64-bit ไม่ reuse แม้หลัง load, Dead ใน D และหายจาก D+1, death record durable (§3:63) |
| M1 | ผ่าน (สัญญา) | on-demand, chunk < 85,000 B, host ถือ 2 ภาพ; ตัวเลข allocations ยังไม่วัด |
| M2 | ผ่าน | budget นับเป็นจำนวน, เรียงด้วย key คงที่, เวลา host ไม่มีผล (§4:76) |
| M3 | ผ่าน | SHA-256 + SplitMix64 + StateHash canonical encoding, fixed-point (§4:78-80) |
| M4 | ผ่าน | thread-affine ไม่ผูก main thread, ทางไป sim thread ผ่าน inbox + immutable publish (§8:96) |
| M5 | ผ่าน | 64/32 ต่อ entity + tie-break คงที่ (§9:114) |
| L1 | ผ่าน | 1 instance = 1 WorldId, IssuerId/Mode/grants |
| L2 | ผ่าน | ยืนยันด้วยคำสั่งด้านบน |

ข้อสังเกตใหม่ (ไม่บล็อก T002):
- **N1 Medium · ARCHITECTURE.md:51,55 → T003** ผล Applied/Rejected ถูก emit เป็นอันดับแรกของ tick ถ้า tick เดียวผลิต event > 4096 (เช่น Burst scenario ใน TESTING.md) ผลคำสั่งของ tick นั้นจะถูก evict ก่อนผู้อ่านมีโอกาสอ่าน แม้ host อ่านทุก tick receipt จะกลายเป็น unknown แก้ทางใดทางหนึ่ง: (ก) จำกัด/รวม event ต่อ tick ให้ ≤ capacity และ validate config ว่า capacity ≥ เพดานต่อ tick หรือ (ข) เก็บผลคำสั่งล่าสุดแบบมีเพดานแยกให้ค้นด้วย CommandSequence ทดสอบ: tick ที่ผลิต 5,000 events พร้อมคำสั่ง 1 รายการ อ่านทุก tick แล้วต้องได้ผลคำสั่งเสมอ
- **N2 Medium · ARCHITECTURE.md:108,116 → T013** archive ของ history ที่อยู่นอก checkpoint อาจไม่ตรง timeline เมื่อโหลด save เก่า (ได้ milestone ของอนาคตที่ยังไม่เกิด) และการเขียน archive ระหว่าง Step ทำให้มี file I/O ใน owner thread แก้: ผูก archive กับ save slot/timeline, ตอน load ตัด/ละเว้น record ที่ EventSequence ≥ NextEventSequence ของ checkpoint, เขียน archive เฉพาะที่ save boundary; สำหรับ MVP ยอมเก็บ death summary ทั้งหมดใน checkpoint ได้ ทดสอบ: save A → ฆ่า X → save B → load A → ReadHistory/ReadEntityDetails ต้องไม่เห็นการตายของ X
- **N3 Low · F:/Code/game-dev/docs/PRESENTATION_PLAN.md:73,85 → ก่อน T007** แผน Godot ยังอ่าน WorldEvent จาก `ReadSnapshot()` หลังทุก Step ซึ่งขัดสัญญาใหม่ Antigravity ต้องเปลี่ยนเป็น drain `ReadEvents(cursor, limit)` ต่อ frame + จัดการ Gap และ snapshot ≤ 1 ครั้ง/frame (ส่วน SnapshotReadCache 2 ภาพ, MaxTicksPerFrame, backlog indicator สอดคล้องแล้ว)
- **N4 Low · ARCHITECTURE.md:49 → T003** "trusted session grants" ยังไม่ระบุแหล่งที่มา (ไม่มี API) แนะนำ MVP ให้ส่ง grants ผ่าน construction options ของ simulation และบันทึกใน save
- **N5 Nit** TASKS.md ใช้สถานะ "READY FOR RE-REVIEW" ซึ่งไม่อยู่ในรายการสถานะของ AGENTS.md; PROGRESS.md บรรทัดท้ายไฟล์ (ใต้ Performance baseline) เป็นบันทึกเก่าที่ยังบอก T001 = REVIEW ควรย้ายหรือลบโดยเจ้าของเอกสาร

**Acceptance tests ที่ T002 ต้องผ่าน (ยังไม่ผ่าน — NOT RUN ทั้งหมด):**
1. `dotnet --list-sdks` และ Godot .NET `--version` ได้รุ่นจริง บันทึกลง PROGRESS.md
2. `dotnet build Simulation/Simulation.csproj -c Release` ผ่าน แยกจาก Game; ตรวจ reference (รวม transitive) และ DLL metadata ไม่มี GodotSharp
3. `dotnet test Tests/Simulation.Tests -c Release` ผ่าน
4. RNG C# ให้ค่าตรง test vector: seed=42, tag=2, scope=17 → `a95b939b9ebb349a`, `5fb88fd6df7259e5`, `b8b82154685001e7`
5. EntityId: เริ่มที่ 1, เพิ่มขึ้นตลอด, ไม่ reuse หลังตาย; ตัวนับ fail ชัดเจนก่อน overflow
6. Headless runner seed เดียวกันใน 2 process แยก ได้ StateHash ตรงกันทุก tick (รวมกรณี host sleep และสลับลำดับ insert/remove ใน hash collection)
7. StateHash ไม่ใช้ reflection/GetHashCode/ลำดับ Dictionary; ใช้ encoding ตาม §4:80
8. Fixture event log ตามแถว H2 ใน TESTING.md (อ่าน 1–50 ทีละ 7; capacity=4 ได้ Gap [1,46])
9. API ถูกเรียกจาก thread อื่นหรือซ้อนใน Step ต้อง throw InvalidOperationException ก่อนแก้ state

ขั้นถัดไป: (1) เจ้าของติดตั้ง .NET SDK + Godot .NET แล้วบันทึกรุ่น → ปิด T001 (2) เจ้าของรับรอง A007 เป็น Accepted (3) มอบ T002 ให้ Codex พร้อมรายการ acceptance ข้างบน (4) Antigravity แก้ PRESENTATION_PLAN.md ตาม N3 ก่อนเริ่ม T007

**2026-11-24 | Codex | T001 Revision | feat/T001-simulation-contract @ 233a9dd (uncommitted)**

| ข้อ | ข้อตกลงที่แก้ | หลักฐาน | สถานะ |
|---|---|---|---|
| H1 | รวม §1–3 ที่ซ้ำ กำหนด pause/speed/track ใน host | ARCHITECTURE.md §1–3 | ตรวจหัวข้อและ interface |
| H2 | event cursor แยกจากภาพ มีช่วง Gap และวิธีกู้หลัง overflow | §3 Events and recovery | ตรวจสัญญา; runtime รอ |
| H3 | ห้าม re-entry ระบุ TargetTick, queue, sequence และ checkpoint ที่มี pending commands | §3 Commands, §9 Save/load | ตรวจสัญญา; runtime รอ |
| H4 | ID 64-bit เพิ่มตลอด เก็บ Death และ history แม้ข้ามเฟรม | §3 Identity, §9 History | ตรวจสัญญา; runtime รอ |
| M1 | snapshot เมื่อขอ, chunk <85,000 bytes, host เก็บ 2 ภาพ | §3 Identity | รอวัด allocations/GC |
| M2 | นับงานและ expansions ต่อ tick เลื่อน backlog ตาม stable order | §4 Deterministic work | รอ replay host ที่ช้า |
| M3 | SHA-256, SplitMix64 และ encoding ที่กำหนดแน่นอน | §4 Deterministic work, docs/TESTING.md | ตรวจตัวอย่าง RNG สอง Python processes; C# hash รอ |
| M4 | owner thread แยกได้ภายหลังด้วย inbox และ immutable publications | §8 Multithreading | รอ profile และ threaded parity |
| M5 | memory 64/relationships 32 ต่อ entity; durable milestones และ cache 256 | §9 History, TASKS.md T010 | รอ eviction/archive test |
| L1 | หนึ่งโลกต่อ instance ตรวจ WorldId, IssuerId, Mode และ grants | §3 Commands | ตรวจสัญญา; authorization test รอ |
| L2 | ตรวจ git dirs/worktree และใช้ ignore เฉพาะ path ของ checkout ซ้อน | parent .gitignore บรรทัด 1 | git check-ignore และ ls-files ตรวจจริง |

**Environment Blocker.** Windows x64; git --version = 2.56.0.windows.4; dotnet --info พบ host 10.0.10 และ runtimes 8.0.28/10.0.10; dotnet --list-sdks ว่าง PATH มี C:\Program Files\dotnet\ และเรียก dotnet.exe ได้ Get-Command Godot* ไม่พบ application ค้น executable ใน Downloads, Desktop, Local Programs, WinGet Packages และ Steam common ที่ตรวจได้แล้วไม่พบ Godot .NET ตัว build/test ยังเริ่มไม่ได้ ไม่มี C# source, .csproj, .sln/.slnx หรือ project.godot ใน checkout นี้

**ขั้นตอนติดตั้งสำหรับ Windows x64** (ยังไม่ได้ติดตั้ง) เลือก SDK 10 x64 ของ Microsoft และ Godot 4.5.1 stable รุ่น .NET Windows x86_64 เป็นคู่สำหรับทดลองบน desktop ตามข้อกำหนด Godot 4.5 ที่ใช้ SDK 8 ขึ้นไป แตก ZIP ของ editor ทั้งชุดไว้ด้วยกัน เปิด PowerShell ใหม่ รัน dotnet --list-sdks และเรียกไฟล์ *mono*console*.exe ด้วย full path ตามด้วย --version จึงไม่ต้องแก้ system PATH เมื่อมี project ให้ตรวจ TargetFramework ที่ Godot สร้าง, build Simulation แยกและ Game ตามลำดับ แล้วบันทึกรุ่นจริงก่อน pin ชุดเครื่องมือ แหล่งทางการ [SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Godot .NET](https://godotengine.org/download/archive/4.5.1-stable/), [C# prerequisites](https://docs.godotengine.org/en/4.5/tutorials/scripting/c_sharp/c_sharp_basics.html)

**Checkout/Antigravity.** git worktree list --porcelain ของ parent และ child แสดงคนละ Git repository ที่ base เดียวกัน ไม่ใช่ linked worktree Parent มี Assets/ และ docs/PRESENTATION_PLAN.md ของ Antigravity จึงคงทั้งสอง checkout ไว้ เพิ่มเฉพาะ /T001-review/ ใน parent .gitignore; git check-ignore -v ยืนยัน rule และ git ls-files --stage -- T001-review ไม่พบ gitlink สำหรับ T007 ให้ใช้ ReadEvents + cursor ตาม §3 แทนข้อความเดิมของ PRESENTATION_PLAN.md บรรทัด 73 และ 171 ที่อ่าน event จาก snapshot ทุก Step ส่วน previous/current snapshot และ ReadEntityDetails ยังใช้ได้

**ผลตรวจเอกสารรอบนี้.** PowerShell ตรวจหัวข้อหลัก 1–10, interface เดียว, ฟิลด์สัญญาและ save, ตารางครบ 11 ข้อ, ประวัติ Claude และ task board (1 READY FOR RE-REVIEW, 19 TODO) ผ่านทั้งหมด git diff --check ผ่าน การเรียก dotnet build -c Release และ dotnet test -c Release หยุดที่ SDK resolution ทั้งคู่ จึงไม่มีผล compile หรือ test ของโปรเจกต์ ค่าตัวอย่าง RNG ที่คำนวณด้วย Python สอง process ตรงกันตาม docs/TESTING.md

**ส่งตรวจ.** ใน T001-review รัน git diff -- ARCHITECTURE.md docs/DECISIONS.md docs/TESTING.md TASKS.md PROGRESS.md แล้วตรวจ A007 revision 2 กับตารางด้านบนและ acceptance cases ทุกแถว ผล runtime/benchmark ทั้งหมดยัง NOT RUN รอ Claude ลงความเห็นก่อนพิจารณา T002

**2026-11-24 | Codex | T001 | feat/T001-simulation-contract | base 233a9dd | REVIEW**

- checkout สำหรับตรวจคือ F:/Code/game-dev/T001-review มี Git history และ remote จาก checkout เดิม การสร้าง branch ใน checkout หลักติดสิทธิ์เขียน .git/refs ไม่มีการ commit หรือ push
- ก่อนเริ่ม git ls-files พบ README.md กับ FantasyWorldSimulator_AI_Kit.zip เท่านั้น นำเอกสารชุดกลาง 12 ไฟล์จาก ZIP ไปวางที่ root ตาม README ของชุดเอกสาร เก็บชื่อ repository เดิมไว้ใน README เอกสารที่ปรับสำหรับ T001 คือ README.md, ARCHITECTURE.md §3, docs/DECISIONS.md A007, TASKS.md และ PROGRESS.md
- Windows NT 10.0.26300.0 x64, git --version = 2.56.0.windows.4, dotnet --info = host 10.0.10 (win-x64), Microsoft.NETCore.App และ WindowsDesktop.App 8.0.28 และ 10.0.10 คำสั่ง dotnet --list-sdks ไม่มีผลลัพธ์
- Get-Command godot,godot4,godot-mono ไม่พบคำสั่งใน PATH และค้น *Godot*.exe ใน Downloads, Desktop และ WinGet Packages ไม่พบ executable ที่ยืนยันรุ่นได้ จึงยังระบุรุ่น Godot .NET ไม่ได้
- รัน dotnet build -c Release และ dotnet test -c Release แล้ว CLI แจ้ง No .NET SDKs were found ทั้งคู่ ตัว build และ test ไม่ได้เริ่ม ไม่มี .csproj, .sln หรือ project.godot ให้ทดสอบ headless, Godot integration และ benchmark จึงเป็น NOT RUN
- สัญญา §3 ใช้เฉพาะชนิดข้อมูล C# ของ Simulation ตรวจได้ที่ระดับเอกสาร ยังพิสูจน์ dependency จาก assembly จริงไม่ได้ เมื่อมี SDK และ project ตามงานที่ได้รับอนุมัติ ให้ตรวจ dotnet build Simulation/Simulation.csproj -c Release และ dotnet test Tests/Simulation.Tests/Simulation.Tests.csproj -c Release แยกจาก Game จากนั้นตรวจ Godot .NET ด้วย executable จริง --version และ build Game/Game.csproj ด้วย SDK ที่เข้ากัน
- ขั้นถัดไปสำหรับ Claude Code ใน checkout นี้ ให้รัน git status -sb และ git diff -- ARCHITECTURE.md docs/DECISIONS.md TASKS.md PROGRESS.md เทียบกับเอกสารตั้งต้นผ่าน tar -xOf FantasyWorldSimulator_AI_Kit.zip ARCHITECTURE.md ตรวจการลบ/ตายของ entity จากภาพถัดไป การปฏิเสธคำสั่งระหว่าง tick เดียวกัน และการรับผลทุก tick เมื่อ render ช้า บันทึกผลพร้อม severity, file/line, แนวแก้และวิธีทดสอบในไฟล์นี้
- สถานะเครื่องมือและการตรวจสัญญาเป็นคนละประเด็น การ review เอกสารทำได้ทันที การยืนยัน build ต้องมี SDK และ Godot .NET ก่อนเริ่ม T002 ต้องมีผล review และคำอนุมัติของเจ้าของ

**2026-11-24 | Claude Code | T001 review (A007) | feat/T001-simulation-contract | uncommitted | CHANGES REQUESTED**

- ขอบเขต: ตรวจเอกสารเท่านั้น (ยังไม่มี code) เทียบ ARCHITECTURE.md กับต้นฉบับใน ZIP ด้วย `unzip -p FantasyWorldSimulator_AI_Kit.zip ARCHITECTURE.md | diff - ARCHITECTURE.md` ยืนยัน `dotnet --list-sdks` ไม่มีผลลัพธ์ซ้ำ ไม่ได้แก้ ARCHITECTURE.md / code
- ข้อสรุป: แนวทางหลัก (pure C# sim, fixed tick, owner thread เดียว, immutable snapshot, command queue + Sequence, camera-independent LOD) เหมาะกับ MVP 100–1,000 entities แต่ยังอนุมัติ contract ไม่ได้จนกว่าจะแก้ข้อ H1–H4 และ T001 acceptance (build จริง) ยัง BLOCKED จากไม่มี .NET SDK / Godot .NET
- H1 High · ARCHITECTURE.md:55-88 — การแทรก §3 ใหม่ทำให้ §1 ท้ายส่วน, §2 และ §3 เก่าซ้ำ (บรรทัด 55 ขึ้นต้นด้วย "4 .NET" ที่ถูกตัด) §3 เก่ายังบอก `SetSimulationSpeed`/`SetPaused` เป็น GameCommand ขัดกับ §3 ใหม่ที่ให้ host คุม pause/speed แก้: ลบบรรทัด 55-88 แล้วย้ายรายการ commands/DTO ที่ยังต้องใช้ (SpawnEntity, SetTrackedEntity เป็น UI-only, Possess/Move/Interact) เข้า §3 ใหม่ ทดสอบ: `grep -n "^## " ARCHITECTURE.md` ต้องมีหัวข้อ 1–10 ไม่ซ้ำ
- H2 High · ARCHITECTURE.md:47,49 (event delivery) — ให้ events ติดมากับ snapshot และบังคับผู้เรียกอ่านหลังทุก Step ทำให้ต้องสร้าง snapshot ทุก tick (ขัดกับเป้าลดการคัดลอก) และถ้า host ลืมอ่าน ผล Applied/Rejected หายเงียบ แก้: แยก event log ที่มี `EventSequence` แบบ global เพิ่มขึ้นตลอด + `ReadEvents(long afterSequence)` (หรือ `Step()` คืน `StepResult`) มีเพดานและรายงาน overflow/gap ชัดเจน snapshot สร้างเมื่อขอ (≤1 ครั้งต่อ frame) ทดสอบ: Step 50 tick แล้วอ่านครั้งเดียวต้องได้ events ครบเรียงตาม (Tick, order); log เต็มต้องแจ้ง gap ไม่หายเงียบ
- H3 High · ARCHITECTURE.md:45 (SubmitCommand) — "ระหว่าง tick" กำกวม ต้องระบุว่าเรียกได้เฉพาะระหว่าง Step (ห้าม re-entrant จาก Step/event handler) และนิยาม TargetTick = LastCompletedTick + 1 คิวคำสั่ง, next Sequence, next EntityId, next EventSequence ต้องอยู่ใน save (§9 ยังไม่ระบุ) ทดสอบ: submit 2 คำสั่งแย่ง stock เดียวกันใน tick เดียวกัน → อันแรก Applied อันที่สอง Rejected ทุกครั้ง; save ระหว่างมีคำสั่งค้าง → load → ผลเหมือนรันต่อเนื่อง; เรียก SubmitCommand ระหว่าง Step → throw/Rejected
- H4 High · ARCHITECTURE.md:25,48 (identity/death) — ยังไม่กำหนดว่า EntityId ห้าม reuse และ entity ที่ตายหายจาก snapshot เมื่อใด แก้: EntityId สาธารณะเป็นค่า 64-bit เพิ่มขึ้นตลอด ไม่ reuse (ภายในใช้ dense index/slot map ได้) tick ที่ตาย snapshot แสดง LifeState=Dead + Death event แล้ว tick ถัดไปลบออก ReadEntityDetails ของผู้ตายอ่านจาก history store ที่มีเพดาน ต้องตัดสินก่อน T002/T013 เพราะเปลี่ยนภายหลังทำให้ save พัง ทดสอบ: ฆ่า entity แล้ว spawn ใหม่ ID ไม่ซ้ำ; UI ที่เลือกผู้ตายยังอ่านประวัติได้; dead entity ไม่มี action ใน tick ถัดไป
- M1 Medium · ARCHITECTURE.md:47 (snapshot memory) — structural sharing ต่อ chunk ไม่ช่วยเมื่อ entity ส่วนใหญ่ขยับทุก tick (แทบทุก chunk dirty) 10k entities × ~32 B ≈ 320 KB ต่อ snapshot (ประมาณการ ไม่ได้วัด) ถ้าสร้างทุก tick ที่ 10 tps × 10× จะ allocate ~32 MB/s แก้: snapshot แบบ on-demand, struct arrays, chunk < 85 KB (เลี่ยง LOH), เก็บ snapshot ล่าสุด 2 ภาพสำหรับ interpolation ทดสอบ: benchmark allocations/GC ต่อ tick ที่ 1k/10k
- M2 Medium · ARCHITECTURE.md:92,102 + :51 — budget ต่อ tick (decision stagger, path queue) ต้องนับเป็นจำนวนงาน ไม่ใช่มิลลิวินาที มิฉะนั้นผลขึ้นกับความเร็วเครื่อง (ขัด determinism และ camera invariant) และ "ทุก tick ต้องประมวลผลครบ" ควรหมายถึงงานที่ถูก schedule ใน tick นั้นครบ ไม่ใช่ทุก NPC ตัดสินใจทุก tick ทดสอบ: รันเดิมบนเครื่องจำลองช้า (sleep ใน host) ต้องได้ state hash เท่ากัน
- M3 Medium · ARCHITECTURE.md:95 — RNG stream key ห้ามใช้ `string.GetHashCode()` (.NET สุ่มต่อ process) ใช้ stable hash เอง; ห้ามให้ผลเกมขึ้นกับลำดับวน `Dictionary/HashSet` หลังลบ/เพิ่ม ทดสอบ: รัน seed เดียวกันใน 2 process แยก เทียบ state hash
- M4 Medium · ARCHITECTURE.md:51,108 (threading path) — Step บน Godot main thread จะกิน frame budget เมื่อ entity ถึงหลักพัน เตรียมไว้ตอนนี้โดยไม่เปลี่ยน ISimulation: host ภายหลังรัน Step บน sim thread, ส่งคำสั่งผ่าน thread-safe inbox ของ host, publish snapshot ด้วยการสลับ reference (immutable) ไม่ต้องทำใน MVP แต่ห้าม contract ผูกกับ main thread
- M5 Medium · ARCHITECTURE.md:27,112 — relationships/memory ต้องมีเพดานต่อ entity ที่เป็นตัวเลข (เช่น top-K ตาม importance) ไม่งั้นโตแบบ O(n²) เมื่อประชากรมาก ระบุใน T010
- L1 Low — `ReadEntityDetails(worldId, …)` กับ 1 instance ต่อโลกซ้ำซ้อน ระบุว่า MVP 1 ISimulation = 1 WorldId และ reject WorldId อื่น; GameCommand ควรมี Issuer/Mode เพื่อตรวจสิทธิ์ God/Possession
- L2 Low — checkout F:/Code/game-dev/T001-review เป็น nested repo ที่ untracked ใน checkout หลัก ระวัง `git add .` ใน checkout หลักจะเพิ่มเป็น embedded repo ควรย้ายเป็น `git worktree` หรือ add ใน .git/info/exclude
- ต้องทดสอบก่อนอนุมัติ Architecture (หลังติดตั้ง SDK): `dotnet --list-sdks` และ Godot .NET `--version` จริง; build Simulation.csproj แยกและยืนยันไม่มี Godot reference; contract tests ตาม H2–H4; determinism 2-process hash; snapshot เก่ายังอ่านได้หลัง 100 tick
- ขั้นถัดไป: เจ้าของตัดสิน H1–H4 → Codex แก้ ARCHITECTURE.md §3/§9 และ A007 → Claude ตรวจรอบสอง (diff เฉพาะ §3/§9) → เจ้าของติดตั้ง .NET SDK + Godot .NET เพื่อปิด T001 acceptance

Known bugs
None reported because implementation has not started; this does not mean the game is bug-free. Record repro steps, expected/actual, severity, task/commit, and verification here or split BUGS.md when needed.

## Performance baseline
Not measured. Record device CPU, RAM, engine/SDK, build type, seed, entity count, simulation speed, tick percentiles, FPS/frame time, allocations, memory and path queue. Keep comparable scenarios. Never fill with invented numbers.
