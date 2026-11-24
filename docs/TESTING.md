# Testing, profiling and acceptance

## Correctness tests
- Fixed seed + identical command stream + supported build/config: compare canonical state/event hash at tick boundaries; test save/load continuation.
- Vital needs: starvation/dehydration and recovery follow rates, consumed resources decrease exactly once; no action for dead agents.
- Utility AI: different input scenarios pick feasible actions; blocked food or unavailable target cannot be selected; danger interrupt works; action commitment avoids endless switching.
- Ecology: a successful hunt consumes or transfers actual food; reproduction unique ID; check ecosystem under scarce food, winter, and predator growth. Extinction may be legitimate; investigate cause rather than forcing population survival.
- Social: directional trust; memory event linked to source; bounded low-value memory; no omniscient knowledge.
- Resource and economy: nonnegative inventories, conservation for trade, explicit source/sink events, no double spending of resources.
- Camera invariant: run same seed/commands with different camera movements or headless/rendered modes; simulation outcomes remain equal within documented determinism scope. Track/zoom commands should be presentation-only.
- Save: corrupted or incompatible save handled, existing save preserved, pending action and RNG restored.
- Performance optimizations: compare correctness baseline before/after. No skipping essential simulation when frame rate drops.

## Benchmark scenarios
1. Idle: 100/1,000/5,000/10,000 entities with basic needs.
2. Dense: many agents in small area, resource contention, path requests.
3. Ecology: mixed predator/prey and seasonal food; fixed seed.
4. Burst: many agents respond to one disaster event; bounded queue and timely emergency response.
5. Observer: render with zoom/pan/selection, compare with headless.
6. Long-run: simulate days/seasons, then years; target 100-year soak test in a later milestone. Track invariants, memory, population and causal events. Record actual simulated duration.

Start at 100, then 1,000 only if stable, larger populations after profiling; 100k is research. Run Release build when possible, record machine/spec, scenario, tick configuration, warmup, duration, seed, repeated runs. Measure p50/p95/p99 tick duration, ticks/sec, rendered frame time/FPS, RAM, allocations/GC, path queue and backlog. No claim of 60 FPS or population capacity without measurements. Establish regression threshold only after baseline (e.g. investigate >10% p95 degradation in comparable runs, not a universal pass rule).

## Human playtest
Let world run without input for 15–30 minutes; note what was interesting, why events occurred, whether NPC state makes sense, and whether UI helps inspect causes. Try hunger, danger, tracking an entity, save/reload and pause/speed. Note bugs and distinguish simulation flaws from intentional difficulty. Do not add feature solely because agent suggests it; owner prioritizes.

## Definition of done
Task acceptance in TASKS met with actual test/build evidence; no unrelated edits; documentation updated; asset provenance recorded; owner review where required. If verification cannot run, status remains REVIEW/BLOCKED with explanation.

## T001 Revision — acceptance cases

**รันได้ใน T001** ตรวจหัวข้อหลัก 1–10 เรียงลำดับและไม่ซ้ำ, interface เดียว, การแยก snapshot/events, ฟิลด์ save, สถานะ task, absence of project manifests, การ ignore checkout ซ้อน และผลของเครื่องมือ ดูผลรอบนี้ใน PROGRESS.md ตัวอย่างอ้างอิง RNG สำหรับ seed=42, systemTag=2, scopeId=17 ให้ค่าเริ่ม a95b939b9ebb349a และสอง draw แรก 5fb88fd6df7259e5, b8b82154685001e7 (hex 64-bit) คำนวณด้วย Python สอง process เพื่อให้ผู้เขียน C# มีค่าเทียบ

**รอ implementation — ทุกแถวด้านล่างเป็น NOT RUN** เริ่มรันเฉพาะ fixture ที่รองรับเมื่อมี T002 ส่วน test ที่ต้องใช้พฤติกรรมจริงให้รันซ้ำในงานเจ้าของระบบ

| Ref | Fixture และการกระทำ | ผลที่ต้องตรวจ | งานที่มี dependency |
|---|---|---|---|
| H1/L1 | build Simulation แยกจาก Game ตรวจ reference รวม transitive และ DLL metadata; ส่งคำสั่งผิด WorldId, ปลอม Mode หรือ issuer ไม่ได้ครอบครอง actor | build ผ่าน ไม่มี Godot dependency; Rejected, คิวและตัวนับเดิม; read ผิดโลกได้ null | T002 foundation, T003 commands |
| H2 | fixture ผลิต 1 event/tick เป็นเวลา 50 tick ไม่อ่าน snapshot แล้วอ่าน cursor 0 ทีละ 7; ทำซ้ำด้วย capacity=4 | ครั้งแรกได้ 1–50 ตามลำดับหนึ่งครั้งต่อ cursor; ครั้งหลัง Gap [1,46], อ่านจาก 46 ได้ 47–50; อ่านซ้ำได้ผลเดิม, 50 ได้หน้าว่าง, 51 invalid | T002 fixture / T003 event log |
| H3 | stock=1 ส่งคำสั่งใช้ 1 หน่วยสองคำสั่งก่อน Step เดียว; เรียก SubmitCommand ซ้อนระหว่าง Step; ลองรับเกิน capacity | ผลแรก Applied ผลสอง Rejected ตาม sequence และ stock=0; re-entry throw ก่อน mutation; คิวเต็ม reject โดยไม่ใช้เลขใหม่ | T003 |
| H3/save | save หลังรับสองคำสั่งก่อน Step พร้อม RNG ที่ draw แล้วและงานค้าง; เทียบรันต่อเนื่องกับ load แล้วเดิน 100 tick; ลองไฟล์เสีย | ทุก tick ได้ hash, event, receipt correlation, ID/sequence ถัดไปและ RNG ตรงกัน; โหลดเสียไม่เปลี่ยนโลกหรือ save เดิม | T002 baseline, T013 roundtrip |
| H4 | เลือก A แล้วข้ามการวาด tick ตาย D จนถึง D+3; ทดสอบทั้ง log ปกติและ log ล้น; ให้ B เกิดหลัง A ตาย | ภาพ D ที่ขอไว้มี A=Dead, D+1 ไม่มี A; A ไม่ทำ action หลังตาย, ID ของ B สูงกว่า A; UI เห็น Death หรือกู้จาก History แม้เกิดและตายระหว่างเฟรม; รายละเอียด A ยังอยู่หลัง load | T004, T007, T013 |
| M1 | เก็บ snapshot tick 0 ข้าม 100 tick ที่เปลี่ยนทุก chunk แล้วเทียบข้อมูลเดิม; วัด run ที่ไม่อ่านภาพและที่ 1,000/10,000 moving entities | ภาพเก่าคงเดิม, 0 ภาพเมื่อไม่มีผู้ขอ, เรียกซ้ำใน tick เดียวได้ภาพเดิม; รายงาน allocated bytes/tick, GC และ peak live bytes เมื่อ host ถือ 2 ภาพ | T002 fixture, T007/T014 |
| M2/M3 | รัน headless สอง process ด้วย seed/config/target ticks เดียวกัน ให้ host ฝั่งหนึ่ง sleep เปลี่ยนกล้องและสลับลำดับ insert/remove ใน hash collection; ให้ backlog เกิน budget | state/event hash ตรงกันทุก tick, RNG ตรง test vector, จำนวนงานไม่เกิน budget, งานที่ค้างเดินต่อในลำดับเดิม | T002 RNG, T003/T006 scheduler/path |
| M4 | replay admissions เดิมผ่าน main-thread host และ dedicated-owner prototype พร้อม reader ที่ช้ากว่า; โหลดโลกกลางคิว | ผลตรงตาม tick, อ่านภาพไม่ฉีก, ไม่มีข้อมูลจากโลกเก่าหลัง load หรือการแตะ SceneTree จาก worker | T007 หรือเมื่อ profiling รองรับ prototype |
| M5 | ให้หนึ่ง entity มี memory 65 และ relationship 33 รายการ, บังคับคะแนนเสมอ; สร้าง milestone เกิน 256 แล้วโหลดซ้ำ | เหลือ 64/32 ตาม tie-break; milestone แรกและ death summary ยังอ่านจาก archive ได้; NPC ไม่ได้รับความรู้เพิ่มเพราะ UI อ่าน | T010/T013 |
