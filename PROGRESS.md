# Progress & handoff

## Current state

T001–T014 DONE and merged to main (2026-11-24; T003–T014 merged by owner instruction without independent review). Main verified: simulation suite 68/68, Godot acceptance T007 + T008 pass, Game/Sandbox/Benchmarks build. Performance baseline: docs/PERFORMANCE.md (1,000 animals p99 ≈ 3–4 ms/tick). Playable: `Play.cmd` + docs/PLAYING.md (God tools, F5/F9 save/load). T015 settlements merged (human Ashford + demon Cinderhold in the opening scene). Remaining roadmap: T018 owner playtest, T016 trade/guards, T017 skills/reading, T018 playtest/build, T019–T020 stretch.

## Handoff log

**2026-11-25 | Claude Code | Integrated T018-env (Codex) | main | merged after review**

- Codex's environment art (independent local checkout, could not reach the shared repo) fetched, reviewed (renderer diff limited to environment parts, generator script read) and merged on top of T019. Merged main: 89/89 tests, T007/T008/GodTools/Settlement acceptances pass, in-game frame checked.

**2026-11-24 | Claude Code | T019 combat, magic, dragon, taming (simulation) | main | merged (self-reviewed, A009)**

- Validated attacks with fatigue cost; damage from skill, health, allies and toughness; villagers fight back when cornered or brave with friends. Firebolt/Heal for scholars with mana. Dragon species (armoured, retaliates, feared). Taming by feeding town food: refusal, personal trust, durable AnimalTamed milestone; tamed beasts stay home, are not threats, spare livestock. Schema 13. 89/89 tests, all Godot acceptances pass, 1,000-entity p99 2.6 ms. No Game/** changes (parallel T018-env). Handoff docs/handoffs/T019.md.

**2026-11-24 | Claude Code | T018 character art pass | main | merged (self-reviewed, A009)**

- Project-owned animated 24 px character sheet (8 human + 4 demon variants, sheep, deer, rabbit, wolf; idle/walk/work/attack/read/dead) from tools/art/make_characters.py; renderer glides creatures between cells, mirrors facing, depth-sorts and picks poses from actions. All Godot acceptances pass. Handoff docs/handoffs/T018-art.md.

**2026-11-24 | Claude Code | T017 skills and reading | main | merged (self-reviewed, A009)**

- Six skills with XP only from real activity (harvest, gather, hits, sales, pages); small level effects. Reading at the town centre in sessions; interrupted sessions credit exactly the pages read, finished books never re-award XP. Schema 12. 82/82 tests; all Godot acceptances pass; 1,000-entity p99 3.1 ms. Handoff docs/handoffs/T017.md.

**2026-11-24 | Claude Code | T016 trade and guards + visual polish | main | merged (self-reviewed, A009)**

- Visual polish: pixel-art terrain/sprites from placeholder atlases, readable panels, F3 debug, day/night (docs/handoffs/T018-visuals.md).
- T016: settlement coins, trade trips with conserved goods and coins, guards defending towns (Combat deaths). 78/78 tests; all Godot acceptances pass. Handoff docs/handoffs/T016.md.

**2026-11-24 | Claude Code | T015 settlements | main | merged (self-reviewed, A009)**

- Human/demon settlements with storage, seasonal farm plots and jobs (deliver, harvest, gather, woodcut); identical rules for both peoples; conservation proven over 3,000 ticks; Godot towns display and God founding tool. 74/74 tests; all Godot acceptances pass. Handoff docs/handoffs/T015.md.

**2026-11-24 | Claude Code (owner-requested) | T018 part 1 | main | merged**

- Playable build: God mode spawn tool and terrain brush via validated God commands with feed feedback, F5/F9 in-game save/load (drops old cursors, corrupt saves refused), terrain texture refresh, `Play.cmd`, Windows export preset, docs/PLAYING.md. Godot headless: GodTools, T007, T008 acceptances pass on main; simulation 68/68.
- Standalone export done after owner-approved template download (SHA-512 verified): `build/windows/FantasyWorldSimulator.exe` runs. Remaining: owner 15–30 min playtest.

**2026-11-24 | Claude Code (owner-requested) | T008 + T014 | main | DONE**

- T008 observer UI (pause/speed/step, select, follow, inspector with dead history, event feed, 120-animal starter herd): Godot headless `T008_ACCEPTANCE_PASS`; T007 acceptance still passes. Handoff docs/handoffs/T008.md.
- T014 profiling: benchmark harness and docs/PERFORMANCE.md; fixed stale decisions consuming the budget and a T013 load rejection of carried-over decisions (regression test); allocations −75 %. 68/68 tests. Handoff docs/handoffs/T014.md. Independent review still recommended.

**2026-11-24 | Claude Code (owner-requested integration) | merge T003–T013 + T007 | main | DONE**

- Merged `feat/T012-seasons-climate` (linear stack T003 → T004 → T005 → T006 → T009 → T011 → T010 → T013 → T012) and `feat/T007-godot-bridge` into main with no conflicts. The owner instructed the merge; no independent agent review was done, so a follow-up review is recommended.
- Verified on main: simulation suite 67/67 (exit 0), Game and Sandbox builds, Godot headless acceptance pass. Per-task evidence: docs/handoffs/T003.md … T013.md.
- Codex's earlier T003/T007 claims were taken over (Codex idle since 17:34; its fix branch was empty, its Game draft continued into T007). The `worktrees/T007-godot-bridge` clone and branch `wip/orch-drafts` are superseded.

**2026-11-24 | Claude Code (owner-requested) | Integrate into main | main | merge**

- Fast-forward `main` → `chore/multi-agent-workflow` (includes `prep/N3-presentation-contract-sync`), then merge `feat/T002-simulation-foundation` (T002 + A008) from the `T001-review` clone.
- Conflicts resolved: TASKS.md uses the A008 table (no owner column, no Active Claims table; claims = branch + docs/handoffs) with N3 DONE and T003 REVIEW; PROGRESS.md keeps every handoff entry from both sides. docs/MULTI_AGENT_WORKFLOW.md and scripts/agent-status.ps1 read claims from docs/handoffs instead of the removed table.
- Not pushed. Sandbox `.godot/` build cache was unstaged (ignored by Sandbox/.gitignore), not committed.

**2026-11-24 | Claude Code (owner-requested) | Process A008 | feat/T002-simulation-foundation | role removal**

- ยกเลิกการแบ่งบทบาท AI ตามคำสั่งเจ้าของ: แก้ AGENTS.md, CLAUDE.md, ANTIGRAVITY.md, START_HERE.md, README.md, TASKS.md (ลบคอลัมน์ Default owner), ROADMAP.md, ARCHITECTURE.md §3 heading และเพิ่ม A008 ใน docs/DECISIONS.md รายการประวัติเดิมใน PROGRESS/DECISIONS ไม่แก้
- ตรวจ agent-orchestrator: 4 session ใน `~/.local/share/agent-orchestrator/platform.sqlite3` อยู่ในสถานะ COMPLETED/STOPPED ไม่มี session ที่แจกงานอยู่ ลบ worktree/branch `orch/*` ที่ว่าง (ไม่มีไฟล์แก้และ commit อยู่ใน HEAD แล้ว) 7 ชุด: T002, T003, T004, T005, T006, T009, T010 เก็บ 4 ชุดที่มีงานยังไม่ commit ไว้: `orch/T007-claude-…` (Game/ project), `orch/SUB-T007-N3-…` (PRESENTATION_PLAN.md), `orch/SUB-T013-SCHEMA-…` (SAVE_SCHEMA_DRAFT.md), `orch/SUB-T006-MOCK-…` (MOCK_SCENARIOS.md)
- หมายเหตุ: orchestrator DB บันทึก T003 เป็น DONE แต่ worktree ไม่มีไฟล์เปลี่ยนเลย ถือว่า T003 ยังไม่ได้ทำ

**2026-11-24 | Google Antigravity | Multi-Agent Shared Workflow + Status Board | chore/multi-agent-workflow | REVIEW / READY TO MERGE**

- ออกแบบและติดตั้งระบบทำงานร่วมกันแบบยืดหยุ่น (ไม่แบ่งหน้าที่ตายตัวตามชื่อแอป) ใน [docs/MULTI_AGENT_WORKFLOW.md](docs/MULTI_AGENT_WORKFLOW.md):
  - ใช้ Git Repo เดียว + `git worktree` ใน `.worktrees/<TASK_ID>-<agent>` (`1 Task ID = 1 Branch = 1 Worktree = 1 Active Owner`)
  - เพิ่มตาราง `## Active Claims` บนสุดของ `TASKS.md` + โฟลเดอร์ `docs/handoffs/<TASK_ID>.md` (แยกบันทึกส่งต่อราย Task เพื่อลด Merge Conflict บน `PROGRESS.md`)
  - เพิ่มสคริปต์ read-only [scripts/agent-status.ps1](scripts/agent-status.ps1) สำหรับเช็คใน 1 วินาทีว่าแต่ละแอป/worktree กำลังทำ Task ใด, Branch ใด และมีไฟล์แก้ค้างกี่ไฟล์
- อัปเดต [docs/handoffs/T007-prep.md](docs/handoffs/T007-prep.md) และ `.gitignore` (`/.worktrees/`)
- **Exact next step:** เจ้าของตรวจและ merge `chore/multi-agent-workflow` + ช่วยตรวจรัน `T002` (`dotnet test Tests/Simulation.Tests -c Release -m:1` สำหรับ commit `48bb41f`) แล้วรวมเข้า `main` จากนั้นทุกแอปสามารถหยิบ `T003`, `T007` ฯลฯ ผ่าน `.worktrees/` ได้ทันที

**2026-11-24 | Google Antigravity | Prep + N3 + Prototype Presentation Sandbox | prep/N3-presentation-contract-sync | BLOCKED (T007 waiting on T002/T003)**

- รับ commit `9648887` (`T001 DONE`, `global.json` pinned SDK `10.0.404`) มายัง branch `prep/N3-presentation-contract-sync` โดยไม่แตะต้อง `T001-review/` และ `main`
- **N3 เสร็จสมบูรณ์:** ปรับปรุง [docs/PRESENTATION_PLAN.md](docs/PRESENTATION_PLAN.md) ให้ตรงกับ `A007 revision 2` (แยก `ReadSnapshot()` <= 1/frame ออกจาก `ReadEvents(afterSequence, limit)`, ออกแบบ `EventGapRecovery`, `OutcomeUnknown` receipts, `ReadHistory` catch-up, reset cursors on load)
- **เตรียม Asset ชั่วคราว (`CC0 1.0 / Project-owned`):** 6 placeholder PNG atlases ใน `Assets/Placeholders/` (Terrain, Modular Human/Demon, Animals + True Dragon `64×64` + Buildings, Plants/Flora, UI Icons, Combat/Magic/Weather VFX) + ทะเบียนแหล่งที่มาใน `Assets/CREDITS.md`
- **พื้นที่ทดสอบแยก (`Sandbox/PresentationTest/` — Mock Data เท่านั้น, ไม่เชื่อม `ISimulation`):**
  - เพิ่ม `ChunkedTerrainPreview` (`16×16` chunks = `64` chunks, frustum culling, dirty rebuild `+1`, `CanvasModulate` ฤดูกาล/กลางคืน), Minimap `128×128` คลิกเลื่อนกล้อง + viewport box, Overlays (`Population`/`Threat`), `+1 Step`, `Backlog` badge, `WorldSelectionRing`, 2-frame walk/breath animation และ `Test Gap UI`
  - คอมไพล์ `PresentationTest.csproj` (`net8.0`, `Godot.NET.Sdk/4.7.2`) ด้วย System `dotnet 10.0.404` (`global.json`) `-c Release` ผ่าน `0 Warning(s), 0 Error(s)` และรัน `godot-mono.exe --headless --path Sandbox/PresentationTest -- --run-sandbox-tests` → **`[SANDBOX_TESTS_PASSED]`**
- **งานที่ยัง BLOCKED:** `T007` (Production `Game/` bridge กับ `ISimulation`) ยัง `BLOCKED` จนกว่า `Codex` จะสร้าง `Simulation/Simulation.csproj` (`T002`/`T003`)
- **Exact next step:** รอ `Codex` ดำเนินการ `T002` และ `T003` จากนั้น merge/sync branch แล้วเริ่ม `T007` บน `feat/T007-godot-presentation`

**2026-11-24 | Antigravity (Coordinator) | T002 DONE | feat/T002-simulation-foundation | runtime verification**

- ตรวจรับ T002 ครบตามเกณฑ์ 9 ข้อใน TASKS.md และตรวจสอบ Source Code (`Simulation/Determinism.cs`, `Tests/Simulation.Tests/Program.cs`) ตรงกับ Release build ปัจจุบัน (`Simulation.dll`, `Headless.dll`, `Simulation.Tests.dll`)
- ผลรันจริงบน Windows Host โดยเจ้าของโปรเจกต์: `dotnet run --project ./Tests/Simulation.Tests -c Release --no-build` → Exit Code `0`, ผ่าน `7/7 T002 test groups passed` (`Pure simulation assembly`, `A007 RNG vector`, `Monotonic identity and checked exhaustion`, `Stable canonical hash and process replay`, `Paged events and explicit gap`, `Owner-thread and re-entry guards`, `Snapshot lifetime`)
- สถานะ: เปลี่ยน T002 เป็น `DONE`; งาน `T003` พร้อมเริ่ม และสามารถแตกงานอิสระคู่ขนานสำหรับ `T007` (N3 / UI prototype), `T013` (Save schema draft), และ `T006` (Mock/Benchmark specs) ได้โดยไม่ข้าม Mandatory Dependencies

**2026-11-24 | Codex | T002 | feat/T002-simulation-foundation | runtime verification**

- T001 commit 9648887 ต่อจาก 58c398e ใช้ Scoop Godot เท่านั้น ผู้ใช้ยืนยันใน Terminal ของเครื่องว่า Editor เปิด obj/toolchain-check/Game โดยไม่มี error หลังจาก Codex build C# ผ่าน การเปิด headless editor ใน Codex พบ Assemblies not found ทั้งจาก junction และ path 4.7.2; ตรวจ DLL/ไฟล์ใน Api/Debug และ Tools เทียบกับ Scoop cache 71 รายการตรงกัน
- Simulation/ เก็บ fixed tick, A007 interface, typed IDs, SplitMix64, canonical SHA-256, paged bounded event log, snapshot cache และ owner guard ส่วน Tests/Headless และ Tests/Simulation.Tests เป็น runner และ offline acceptance suite ตาม docs/TESTING.md
- เกณฑ์ 1 ผ่าน: SDK 10.0.404, Godot 4.7.2.stable.mono.official.ed1daf0bf เกณฑ์ 2 ผ่าน: dotnet build Simulation/Simulation.csproj -c Release และ dotnet build Tests/Simulation.Tests -c Release --no-restore -m:1 ผ่านทั้ง 3 โปรเจกต์ 0 warnings / 0 errors ตรวจ dotnet list package --include-transitive ไม่มี package และ PEReader ของ Release Simulation.dll พบเฉพาะ System.* ไม่มี GodotSharp
- รัน dotnet test Tests/Simulation.Tests -c Release --no-restore -m:1 จริงใน Codex: test runner เปิดได้, 0/7 กลุ่มผ่าน, 7/7 ล้มเหลวจาก Windows Application Control 0x800711C7 เมื่อโหลด Simulation.dll (MSB3073 exit 1) ยังยืนยันผล behavioral tests จากรอบนี้ไม่ได้ แยกจากผล build และการตรวจ metadata ข้างต้น
- ค่าอ้างอิงสำหรับข้อ 7 จาก Python struct.pack little-endian + SHA-256: seed=42, WorldId=1, streams=1,2,3, tick=1 ได้ 480C1B5668F5FF4B9A67A359E6AD9235F00C688E7CBC86E16A7FC94C09805D4B ไม่ใช้ผล Python แทน replay ของ C#
- ขั้นยืนยันใน Terminal ปกติ: dotnet test Tests/Simulation.Tests -c Release -m:1 ต้องเห็นผลจริงทุกกลุ่มก่อนส่ง Claude ตรวจเฉพาะ diff T002 T003 ยัง TODO ไม่มีการ push หรือ merge

**2026-11-24 | Codex | T001 DONE | feat/T001-simulation-contract | base 58c398e**

ตรวจ dotnet --list-sdks ได้ 10.0.404 (Windows x64), godot-mono --headless --version ได้ 4.7.2.stable.mono.official.ed1daf0bf ใช้ global.json pin SDK และใช้ Godot .NET SDK 4.7.2 ฝั่ง Game ตัวสร้างโปรเจกต์จริง GodotTools.ProjectEditor.ProjectGenerator.GenAndSaveGameProject ให้ desktop TargetFramework=net8.0 (android override net9.0) ไม่มีการติดตั้งเพิ่มหรือเปลี่ยน system PATH

ใน obj/toolchain-check ทดสอบ dotnet build Simulation/Simulation.csproj -c Release และ dotnet build Game/Game.csproj -c Debug ด้วย local NuGet cache, Game ใช้ ProjectReference ไปยัง Simulation ผ่านทั้งคู่ 0 warnings / 0 errors ตรวจ AssemblyRef ของ Simulation.dll ด้วย PEReader พบ System.Runtime เท่านั้น เป็น project ชั่วคราวสำหรับ gate ไม่ใช่ source ของ T002 การลองรันฉากเสริมก่อนหน้านี้ใน sandbox ล้มเหลวด้วย Assemblies not found จึงยังไม่อ้างผล Godot runtime integration

**2026-11-24 | Claude Code | T001 environment install | feat/T001-simulation-contract**

- ติดตั้งตามคำขอเจ้าของ ผ่าน winget จากแหล่งทางการ (winget ตรวจ installer hash ผ่านทั้งคู่):
  - `.NET SDK 10.0.404` (Microsoft.DotNet.SDK.10) → `dotnet --list-sdks` = `10.0.404 [C:\Program Files\dotnet\sdk]`
  - `Godot 4.7.2 .NET` — ใช้ชุดที่ติดตั้งผ่าน Scoop (`godot-mono`) ที่ `%USERPROFILE%\scoop\apps\godot-mono\current\` (shim `godot-mono` อยู่ใน PATH); `godot-mono.console.exe --version --headless` = `4.7.2.stable.mono.official.ed1daf0bf`; ทดสอบ headless `--editor --verbose` พบ `.NET: hostfxr initialized`, `GodotPlugins initialized`, `Found .NET Sdk version 10.0.404` (ยังไม่ได้เปิดหน้าต่าง editor จริง) ชุดที่เคยแตกจาก `winget download` ถูกลบแล้วตามคำขอเจ้าของ
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
## T018-env | Codex | Environment art verified locally; integration pending
Base 69e3a51. Pillow generator, two CC0 atlases and renderer integration. Debug build clean,
four Godot acceptance markers and 82/82 simulation groups passed. See docs/handoffs/T018-env.md.
