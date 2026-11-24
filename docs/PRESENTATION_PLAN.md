# Presentation, UI, Asset, and Rendering Plan — v0.2 (Synced with A007 rev 2)

> **สถานะเอกสาร:** แผนฝั่ง Presentation (`Antigravity`) ที่ปรับแก้ตาม **N3** และ **A007 revision 2 (Accepted 2026-11-24)** เรียบร้อยแล้ว — ยังไม่สร้าง `Game/` Production code เนื่องจากรอ `Simulation/` และ `T003` จาก `Codex`
> **อ้างอิงสัญญา (Frozen):** [ARCHITECTURE.md §3–4, §8–9](file:///f:/Code/game-dev/ARCHITECTURE.md), [DECISIONS.md (A007 rev 2 · Accepted)](file:///f:/Code/game-dev/docs/DECISIONS.md)

---

## 1. สถานะปัจจุบันและ Dependency Check (ข้อมูลจริง ณ วันที่ 2026-11-24)

### 1.1 สถานะ Repository และงานที่เกี่ยวข้อง
- **Branch ปัจจุบัน (`prep/N3-presentation-contract-sync` จาก `81709a9`):** รับ commit ล่าสุดจาก `feat/T001-simulation-contract` (`ARCHITECTURE.md`, `TASKS.md`, `PROGRESS.md`, `docs/DECISIONS.md`, `docs/TESTING.md`) เรียบร้อยแล้ว โดยไม่แก้ไฟล์ใน `T001-review/` และไม่แตะ `main`
- **T001 (`Verify toolchain, solution layout, agree contracts`):** เอกสาร **Architecture APPROVED** (`A007 rev 2 Accepted`), เครื่องมือ `.NET SDK 10.0.404` และ `Godot 4.7.2 .NET` ติดตั้งแล้ว เหลือการยืนยัน build จริงเมื่อมี `Simulation/Simulation.csproj` (ย้าย/รอปิดใน `T002`)
- **T002 & T003:** สถานะ **`TODO`** — ยังไม่มี `Simulation/` project หรือ `ISimulation` implementation ใน repo
- **T007 (`Godot project, map renderer, camera, simulation bridge`):** สถานะ **`BLOCKED`** — งานฝั่งเอกสาร **N3** ของ Antigravity เสร็จแล้ว แต่ยังติด Dependency ของ `Simulation/Simulation.csproj` (`T001` build / `T003`)

### 1.2 ผลการตรวจสอบ Toolchain จริงบนเครื่อง (หลังเจ้าของ/Codex/Claude ติดตั้ง)
- **OS:** `Microsoft Windows NT 10.0.26300.0` (x64)
- **Git:** `C:\Program Files\Git\cmd\git.exe` (`git version 2.56.0.windows.3`)
- **.NET SDK (System-wide):** `C:\Program Files\dotnet\dotnet.exe` -> `dotnet --list-sdks` = **`10.0.404 [C:\Program Files\dotnet\sdk]`** (พร้อม Runtime `8.0.28`, `10.0.10`, `10.0.15`)
- **Godot 4 .NET:** `4.7.2.stable.mono.official.ed1daf0bf` (`GodotSharp.4.7.2` target framework **`net8.0`** พร้อม `rollForward: LatestMajor`) — รันทดสอบจริงผ่าน `C:\Users\mw064\scoop\shims\godot-mono.exe` (และตรวจพบ `Godot_v4.7.2-stable_mono_win64_console.exe` ที่ `%LOCALAPPDATA%\Programs\Godot\` ณ 09:21 ก่อนถูกย้าย/ลบเหลือ Scoop shim ณ 09:24)
- **การทดสอบ Environment ร่วมกัน:** คอมไพล์ `Sandbox/PresentationTest/PresentationTest.csproj` ด้วย System `dotnet 10.0.404` (`-c Release` และ `-c Debug`) และรันด้วย `godot-mono.exe --headless -- --run-sandbox-tests` ผ่านเรียบร้อย (`0 Warning(s), 0 Error(s)`)
- **GPU ที่ทดสอบ:** `Vulkan 1.4.357 - Forward+ - NVIDIA GeForce RTX 3060 Laptop GPU`

### 1.3 งานที่ทำสำเร็จในพื้นที่ทดสอบแยก (`Sandbox/PresentationTest/`)
- สร้างพื้นที่ทดสอบแยกจาก Production Code ใช้ **Mock Data ชั่วคราว** (`MockData/MockWorldData.cs`) และไม่เชื่อมต่อ `ISimulation` ใด ๆ
- **ชุด Placeholder Pixel-Art Atlases (`Assets/Placeholders/` + `Assets/CREDITS.md`):** ครบ 6 ชุด (Terrain, Modular Humanoid, Creatures & Buildings + True Dragon `64×64`, Plants & Flora, UI Icons, และ Combat/Magic/Weather VFX)
- **ผลทดสอบ `dotnet 10.0.404` + `Godot 4.7.2 .NET` (Release Build):**
  - Headless unit/invariant tests (`--run-sandbox-tests`): ผ่านครบทั้ง Asset dimensions, Camera zoom-to-cursor (`< 0.01 px` drift), Modular Character 7 layers (`Human` / `Demon` / `Dead`), และ Sprite optimization benchmark
  - **Sprite Batching Benchmark (`N=5,000`, `1280×720`, zoom `0.5×`):**
    - Headless (`Visible=4,021`): `CulledNodePool` `0.266 ms` (`300 nodes`), `MultiMeshBulkBuffer` `0.497 ms` (`2 nodes`), `NaiveNodes` `3.075 ms` (`5,000 nodes`)
    - Vulkan Forward+ (`Visible=4,204`): `MultiMeshBulkBuffer` **`DrawCalls=113`**, `ObjectsInFrame=1,146` vs `NaiveNodes` **`DrawCalls=1,099`**, `ObjectsInFrame=4,902`
  - **ข้อสรุปทางสถาปัตยกรรม:** สำหรับ `Game/` จริง ใช้ **AutoLOD = `CulledNodePool` เมื่อซูมใกล้/จำนวนในจอน้อย (`<= 250`) และ `MultiMeshBulkBuffer` เมื่อซูมไกล/ฝูงชนหนาแน่น**

### 1.4 ขอบเขตที่ Antigravity ปฏิบัติตามอย่างเคร่งครัด
- ไม่แก้ไฟล์ใน `T001-review/`, ไม่สร้าง Simulation Engine/Save-Load/AI Decision ซ้ำ, และไม่เชื่อม `ISimulation` จนกว่า `Codex` จะสร้าง `Simulation.csproj` (`T002`/`T003`) ตามสัญญาที่อนุมัติแล้ว

---

## 2. สถาปัตยกรรมฝั่ง `Game/` และการเชื่อมกับ `Simulation`

### 2.1 โครงสร้างโฟลเดอร์เป้าหมาย (สอดคล้องกับ `ARCHITECTURE.md §1`)
```text
Game/
  project.godot                 # Godot 4 .NET project root (สร้างหลังตรวจรุ่นจริง + T001 อนุมัติ)
  Game.csproj                   # ProjectReference -> ../Simulation/Simulation.csproj
  Integration/
    SimulationHost.cs           # Owner-thread fixed-tick loop, pause/speed/step host control, event/history drain
    EventGapRecovery.cs         # จัดการ Event Gap, กู้คืน milestone จาก ReadHistory, และอัปเดต Receipt Unknown
    CommandDispatcher.cs        # สร้างและส่ง GameCommand (IssuerId/Mode/Payload), จับคู่ CommandReceipt ด้วย CommandSequence
    SnapshotReadCache.cs        # เก็บ prev/curr WorldSnapshot (ไม่เกิน 2 ภาพ, on-demand <= 1/frame), interpolation & dead cache
  Presentation/
    World/
      WorldMapRenderer.cs       # Chunked TileMapLayer สำหรับ terrain, resources, fog/season tint
      SeasonAtmosphere.cs       # CanvasModulate + weather particles เฉพาะใน viewport
    Entities/
      EntityRenderManager.cs    # เลือก LOD ตาม zoom/frustum: Off-screen=0, Far=MultiMesh, Near=ActorPool
      EntityActorPool.cs        # Object pool ของ DetailedActorNode (จำกัดจำนวนบนจอ ไม่สร้างถาวรทุกตัว)
      ModularCharacterRig.cs    # ประกอบเลเยอร์ Body/Race/Hair/Outfit/HeldItem จาก AppearanceSeed + Shader Palette
      SpriteAnimationMap.cs     # แปลง ActionState + Direction -> animation frame (ไม่มี logic เกม)
    Buildings/
      SettlementRenderer.cs     # Chunked buildings, farms, storage, market status icons
    Camera/
      ObserverCamera2D.cs       # Pan, cursor-centered zoom, follow by EntityId (ห้ามส่งค่ากลับไปแก้ผลซิม)
    VFX/
      VfxPool.cs                # Object pool สำหรับ combat slash, magic projectiles, selection rings, particles
    Input/
      PlayerModeRouter.cs       # สลับโหมด Observer / God / Possession โดยแยกคำสั่งชัดเจน
    UI/
      MainHud.cs                # Top bar (Date, Tick, Speed, Backlog), Mode switcher, Minimap toggle
      EntityInspectorPanel.cs   # หน้าต่างข้อมูล NPC (Overview, Mind/Memory, Skills/Inventory, Social/History)
      EventFeedPanel.cs         # Event log แบบกรองหมวดได้ + คลิกเพื่อกระโดดกล้อง/เลือกตัวละคร
      GodToolsPanel.cs          # คำสั่ง God ชัดเจน (Spawn, Resource, Heal/Smite) ผ่าน SubmitCommand เท่านั้น
      PossessionHud.cs          # Hotbar ควบคุมตัวเอก (Stretch T020) + ปุ่ม Release Control
Assets/
  CREDITS.md                    # แหล่งที่มา สัญญาอนุญาต และเครดิต asset ทุกชุด
  Placeholders/                 # Sprite atlas ชั่วคราว (CC0 procedural placeholder)
  Atlases/                      # Texture atlas จริงในอนาคต
```

### 2.2 การไหลของข้อมูล Simulation ↔ Presentation (A007 Revision 2 · แก้ไข N3)

อ้างอิงสัญญาที่อนุมัติแล้วใน `ARCHITECTURE.md §3`: `ISimulation` มี 6 เมธอด (`Step()`, `SubmitCommand(GameCommand)`, `ReadSnapshot()`, `EventPage ReadEvents(long afterSequence, int limit)`, `HistoryPage ReadHistory(long afterSequence, int limit)`, `EntityDetails? ReadEntityDetails(WorldId, EntityId)`) โดย 1 `ISimulation` = 1 `WorldId` และเรียกได้เฉพาะบน owner thread ระหว่างการเรียกที่เสร็จสิ้นแล้ว (ห้าม re-entrant ระหว่าง `Step()` หรือจาก worker thread)

1. **Snapshot สำหรับการแสดงผล (แยกจาก Event Log โดยเด็ดขาด):**
   - `WorldSnapshot` **ไม่มี Event Collection** และ Simulation จะสร้างให้ **on demand** เท่านั้น
   - `SimulationHost` บริหาร `IsPaused`, `SpeedMultiplier` (`1×`, `2×`, `4×`, `10×`), และ `Single-Step (+1)` เองในฝั่ง Host โดยเรียก `ISimulation.Step()` สูงสุดไม่เกิน `MaxTicksPerFrame` เพื่อกันเฟรมค้างและแสดง `SimBacklogIndicator` บน HUD **โดยไม่ข้าม tick**
   - เรียก `ReadSnapshot()` **อย่างมาก 1 ครั้งต่อ rendered frame** (ไม่เรียกหลังทุก `Step()` เมื่อรันหลาย tick ในเฟรมเดียว) และเก็บใน `SnapshotReadCache` **ไม่เกิน 2 ภาพ** (`prevSnapshot`, `currSnapshot`) สำหรับทำ position interpolation; ชิ้นส่วน chunk (`< 85,000 B`) ที่ไม่เปลี่ยนจะถูกแชร์ตามสัญญา
   - **Identity & Death (H4):** `EntityId` (64-bit monotonic) ไม่ถูกใช้ซ้ำ; ใน tick ที่ตาย `D` หากขอ snapshot จะเห็น `LifeState = Dead` และตั้งแต่ `D + 1` จะหายจาก render chunks — Presentation แสดงผลศพ/หลุมศพชั่วคราวจาก `Death` event และอ่านรายละเอียดย้อนหลังผ่าน `ReadEntityDetails()` / `ReadHistory()` ได้ตลอด

2. **Event Drain Loop ด้วย `ReadEvents(cursor, limit)` (แทนการอ่านจาก Snapshot):**
   - `SimulationHost` เก็บ `long _eventCursor = 0` (exclusive cursor) และ `long _historyCursor = 0`
   - ในรอบ frame (หรือหลัง batch ของ `Step()` โดยแยกอิสระจากการวาดภาพ) ให้วนเรียก `ReadEvents(_eventCursor, limit: 256)` จนกว่า `Status` จะจับคู่ทัน `LatestSequence` หรือได้รายการครบตามเพดานต่อ frame เพื่อส่งเข้า `EventFeedPanel`, `VfxPool` และจับคู่ผล `Applied`/`Rejected` กับ `CommandDispatcher` ด้วย `CommandSequence`
   - Deduplicate ทุก event ด้วย `EventSequence`

3. **การจัดการ Event Gap และการกู้คืนสถานะ (`EventGapRecovery`):**
   - เมื่อ event log (bounded capacity default `4096`) ถูก evict ก่อนที่ Host จะอ่านทัน `ReadEvents(_eventCursor, limit)` จะคืนสถานะ **`Gap`** พร้อมช่วงที่หาย `[_eventCursor + 1, OldestAvailable - 1]` และ `NextCursor = OldestAvailable - 1`:
     1. **แจ้งผู้เล่นแบบไม่บล็อก:** แสดงข้อความ/แบนเนอร์ `[Event Gap: #<start>..#<end>]` บน `EventFeedPanel` ว่ามีเหตุการณ์ย่อยหลุดระหว่างเรนเดอร์
     2. **จัดการ Command Receipt ที่ค้างอยู่:** ตรวจ `CommandReceipt` ที่ยังไม่รู้ผลซึ่ง `TargetTick <= LatestCompletedTick` หากไม่พบใน retained suffix ให้เปลี่ยนสถานะเป็น **`OutcomeUnknown`** ทันที (**ห้ามอนุมานว่า `Applied` สำเร็จเด็ดขาด**)
     3. **บังคับอ่าน Snapshot ใหม่:** เรียก `ReadSnapshot()` ทันทีเพื่อ sync ตำแหน่งและตัวละครบนจอใหม่ และรีเซ็ต `prevSnapshot = currSnapshot` (งด interpolation ข้ามช่วงที่กระโดด)
     4. **รีเฟรชตัวละครที่กำลังติดตาม/เลือก:** เรียก `ReadEntityDetails(WorldId, EntityId)` สำหรับตัวที่ถูกเลือก, กล้องติดตาม และ Protagonist Bookmarks เพื่อยืนยันสถานะล่าสุด (รวมถึงกรณีเกิดและตายระหว่างเฟรม)
     5. **กู้คืน Milestones ด้วย `ReadHistory` และ Replay Suffix:** เรียก `ReadHistory(_historyCursor, limit)` และกรองเฉพาะ milestone ที่ `EventSequence` อยู่ในช่วงที่หาย `[gapStart, gapEnd]` เพื่อแสดงการตาย/การเกิด/เหตุการณ์สำคัญของเมือง จากนั้นตั้ง `_eventCursor = page.NextCursor` (`OldestAvailable - 1`) แล้วอ่าน suffix ที่เหลือใน `ReadEvents` ต่อโดย deduplicate ด้วย `EventSequence`
   - **เมื่อ Load Save / เปลี่ยนโลก:** ล้าง `SnapshotReadCache`, `CommandReceipt` ที่ค้างอยู่, `_eventCursor = 0`, และ `_historyCursor = 0` ทิ้งทั้งหมดทันที

4. **Write Path & Authority (`Observer` / `God` / `Possession`):**
   - **Observer Mode:** ใช้กล้อง `Pan` / `Zoom` / `Follow` / `Protagonist Bookmark` ภายใน `Game/Presentation` ล้วน ๆ **ห้ามส่งเข้า `SubmitCommand` และห้ามกระทบผลลัพธ์ของโลก**
   - **God Mode / Possession Mode:** ส่ง `GameCommand(WorldId, IssuerId, Mode, Kind, Payload)` ผ่าน `CommandDispatcher.Submit(command)` เท่านั้น; ตรวจสิทธิ์ที่ Core (`N4` trusted session grants); แสดงผล `Queued` -> `Applied` / `Rejected(reason)` / `OutcomeUnknown` บน UI ชัดเจน

5. **แนวทางรองรับ Simulation Thread ในอนาคต (`§8 M4`):**
   - MVP รัน `ISimulation` บน main thread ของ Godot; ออกแบบ `SimulationHost` ให้อยู่หลัง boundary ชัดเจนเพื่อย้าย `Step()` + `SubmitCommand()` ไปยัง dedicated simulation thread ผ่าน bounded inbox และสลับ immutable `WorldSnapshot` / `EventPage` reference กลับมาที่ main thread ได้ภายหลังโดยไม่เปลี่ยนสัญญา `ISimulation`

---

## 3. แผนระบบกราฟิก 2D Pixel Art, Asset และ Modular Character

### 3.1 ค่ามาตรฐานเบื้องต้น `[Proposed Defaults — รอเจ้าของยืนยันใน DECISIONS.md]`
- **Tile พื้นฐาน:** `16×16 px` (`PPU = 16`), 1 chunk = `16×16` หรือ `32×32` tiles (จับคู่ตรงกับ chunk size ของ Simulation)
- **Humanoid (Human / Demon):** `32×32 px` (pivot กึ่งกลางฐานเท้า `(16, 28)` เพื่อยืนพอดีบน tile `16×16` และเหลือพื้นที่ส่วนหัว/หมวก/เขา)
- **สัตว์ (Animals):**
  - ขนาดเล็ก (`16×16 px`): Chicken, Rabbit
  - ขนาดกลาง (`32×32 px`): Sheep, Pig, Boar, Wolf, Deer
  - ขนาดใหญ่ (`48×48 px`): Cow, Bear, Horse
- **พืชและทรัพยากร (Plants & Flora):** `32×32 px` (Oak Tree, Evergreen Pine, Ripe Berry Bush, Empty Bush, Crops Growing/Ripe, Mana Herb, Dead Tree/Stump)
- **มังกรจริง (True Dragons — ไม่ใช่ Dragonkin):** `64×64 px` ถึง `96×96 px` แยก shadow offset เมื่อบิน/ลงพื้น มี animation 4 ทิศหรือ flip ซ้าย-ขวา + ปีก/หาง/ลมหายใจ
- **อาคาร (Buildings):** `32×32 px` (House/Hut), `48×48 px` (Storage/Market/Workshop), `64×64 px` (Guild/Keep), และ `16×16 px` tileable crops/roads/walls
- **ไอคอน UI & Status:** `16×16 px` (HP, Hunger, Thirst, Energy, Shield, Coin, Book, Sword, Danger, Sleep, Mana, Protagonist Crown, Species/Dead icons)
- **Godot Pixel Crispness Settings:**
  - `textures/canvas_textures/default_texture_filter = Nearest`
  - `display/window/stretch/mode = canvas_items` (หรือ viewport integer scale option)
  - Snap sprite world positions เพื่อลด pixel shimmering ระหว่างกล้องเคลื่อนที่

### 3.2 กลยุทธ์ Texture Atlas (ลด Draw Calls และ State Changes)
จัดกลุ่มภาพลง Atlas กำลังสอง (Power-of-Two) เพื่อลด texture swap:
1. `atlas_terrain.png`: Terrain tiles (Grass, Dirt, Sand, ShallowWater, DeepWater, ForestFloor, Mountain, Snow, Farmland, StonePath) + tile variations
2. `atlas_props_buildings.png`: Trees, Bushes/Berries, Rocks/Ores, Crops stages, Houses, Storage, Market, Guild/GuardPost, Ruins
3. `atlas_modular_humanoid.png`: เลเยอร์สีกลาง (grayscale/white-mask) สำหรับ shader palette swap
4. `atlas_creatures.png`: Farm animals, Wild animals, Predators, True Dragons
5. `atlas_vfx_ui.png`: Status icons, Selection indicators, Pixel UI frames/icons
6. `atlas_vfx_effects.png`: Combat slash frames, Fireball/Dragon breath, Arcane bolts, Heal/Tame sparkles, Rain/Snow/Hit/Alert VFX (`vfx_effects_16x16_placeholder.png`)

### 3.3 ระบบ Modular Character (Humans, Demons, Palette Variants)
แยกชิ้นส่วนตามข้อมูลจาก `VisualKey` / `AppearanceSeed` ของ Simulation (deterministic 100%):
- **Layer 0 — Ground Shadow:** วงรีโปร่งแสง (ยกเว้นเมื่อตาย)
- **Layer 1 — Body Base + LifeStage:** Child / Adult / Elder scale หรือ sprite row; Human vs Demon (สีผิวใช้ Palette LUT shader 4-color ramp)
- **Layer 2 — Race Features:** Demon Horns / Tail (Human เว้นว่าง; สามารถขยายสู่ Elf Ears / Dwarf Beard ในอนาคต)
- **Layer 3 — Hair & Facial:** ทรงผม + สีผมจาก `AppearanceSeed`
- **Layer 4 — Outfit / Profession:** Villager, Farmer, Trader, Guard, Mage, Adventurer
- **Layer 5 — Held Item / Tool / Weapon:** ดาบ หอก ไม้เท้า ค้อน จอบ หนังสือ (แสดงเฉพาะตอนถือ/ใช้งาน)
- **Layer 6 — Status Indicator (LOD Near เท่านั้น):** ไอคอนเล็กเหนือหัวเมื่อ `Sleep`, `LowHealth`, `Fleeing`, หรือ `Possessed`

### 3.4 ระบบ Sprite และ Animation
- Mapping แบบ data-driven จาก Simulation state (`ActionKind` + `Facing`) สู่เฟรมใน atlas:
  - **Prototype (T007–T014):** ใช้ `2 frames` สำหรับ `Idle`/`Walk` และ `1–2 frames` สำหรับ `Eat`/`Sleep`/`Work`/`Attack`/`Dead` เพื่อความเร็วและตรวจพฤติกรรม AI ได้ชัด
  - **Polish (หลัง T018 ผ่าน):** ขยายเป็น `4–6 frames` + hit-flash shader + directional 4-way sprites สำหรับตัวเอก/มังกร/combat

---

## 4. แผน Rendering Optimization (รองรับ 100 → 1,000 → 10,000 Entities)

ปฏิบัติตามกฎข้อ 5–6 และ `ARCHITECTURE.md §7`: **ห้ามสร้างถาวร 1 Node ต่อ 1 สิ่งมีชีวิต**

| ระดับระยะ / Zoom | วิธีเรนเดอร์ | เป้าหมายทางเทคนิค |
|---|---|---|
| **Off-screen (นอก Frustum + 2-chunk margin)** | **ไม่เรนเดอร์ (Culled)** | 0 Godot Nodes, 0 Draw Calls; เก็บเพียงข้อมูลใน `SnapshotReadCache` สำหรับ Inspector/Minimap |
| **Far LOD / Strategic Zoom (`zoom < 0.75×` หรือมีตัวละครในจอมหาศาล)** | **`MultiMeshInstance2D` Batching** | วาดตัวละคร/สัตว์ที่อยู่ในจอทั้งหมดด้วย `1 Draw Call` ต่อ Atlas ผ่าน custom `INSTANCE_CUSTOM` (UV rect + faction/species color) |
| **Near / Medium LOD (`zoom >= 0.75×`, จำกัดโควต้า เช่น สูงสุด 250–300 Actors ในจอ)** | **`EntityActorPool` (Pooled Nodes)** | ดึง `DetailedActorNode` จาก Object Pool มาผูกกับตัวละครที่อยู่ใกล้กล้อง/ถูกเลือก/ถูกติดตาม แสดง Modular layers + Animation + Status icon และคืน Pool ทันทีเมื่อออกนอกจอ |
| **Terrain & Buildings** | **Chunked `TileMapLayer` / Dirty Chunk Rebuild** | อัปเดตเฉพาะ Chunk ที่ `ChunkVersion` เปลี่ยน (เช่น ทรัพยากรโต/ถูกเก็บ หรือสร้างอาคาร) ไม่เขียน TileMap ใหม่ทุกเฟรม |
| **VFX & Projectiles** | **`VfxPool` + `GPUParticles2D` / `MultiMeshInstance2D`** | นำ particle และ projectile node กลับมาใช้ซ้ำ ลด GC allocation ของ C# ระหว่างเล่น |
| **UI Throttling** | **Tick-driven / 10 Hz Refresh** | อัปเดตตัวเลข Inspector, Minimap และ Event Feed เมื่อ tick เปลี่ยนหรือตามช่วงเวลา ไม่สร้าง Control node ใหม่ทุก render frame |

---

## 5. แผน UI, HUD, กล้อง และโหมดการเล่น

### 5.1 โครงร่างหน้าจอ (Observer-First HUD Layout)
```text
+---------------------------------------------------------------------------------+
| [World: Seed #] [Day 12 · Spring · Y1] [|| 1x 2x 4x 10x | +1 Step] [Pop: 84]     |
| [Mode: Observer | God | Possess(locked)] [Overlays v] [Save/Load] [Settings]    |
+---------------------------------------------------------------------------------+
| +--------------------+                                  +---------------------+ |
| | Event Feed         |                                  | Entity / Settlement | |
| | [All][Death][Fight]|                                  | Inspector (T008)    | |
| | - T120 Boar attack |                                  | - Portrait & ID     | |
| | - T115 Mira eats   |                                  | - Needs / Vitals    | |
| | (Click to focus)   |         2D Pixel World           | - Action + Reason   | |
| +--------------------+         (Camera Viewport)        | - Mind / Memory     | |
|                                                         | - Skills / Social   | |
|                                                         | [Follow] [Bookmark] | |
|                                                         +---------------------+ |
| +--------------------+                                  +---------------------+ |
| | Protagonist Slots  |   [Context Toolbar: Observer /   | Minimap + Overlays  | |
| | (Bookmark Only)    |    God Brushes / Possess Hotbar] | [Terrain/Pop/Threat]| |
| +--------------------+                                  +---------------------+ |
+---------------------------------------------------------------------------------+
```

### 5.2 รายละเอียดแต่ละระบบ UI
1. **Camera & Zoom (`ObserverCamera2D`):**
   - `WASD` / `Arrow Keys` / `Middle-drag` เลื่อนกล้อง; `Scroll Wheel` ซูมเข้า-ออกรอบตำแหน่งเมาส์ (`0.25×` ถึง `4.0×`)
   - **Follow Mode:** กด `Follow` ที่ตัวละครใดก็ได้ กล้องจะล็อกตามตำแหน่ง interpolated; ถ้าผู้เล่นเลื่อนกล้องเองจะปลดล็อกหรือมีโหมด `Soft Follow` และมีปุ่ม `Return to Tracked`
   - **Protagonist Bookmarking (ไม่มี Plot Armor):** เลือกตัวเอกหลายคนไว้ในแถบ Bookmark ได้ คลิกเพื่อสลับติดตาม หากตัวเอกตายจะคงสถานะ `Dead` และประวัติชีวิตใน Inspector/Event Feed และเลือกตัวใหม่ได้
2. **NPC / Entity Inspector (`EntityInspectorPanel`):**
   - อ่านจาก `ISimulation.ReadEntityDetails(WorldId, EntityId)` (เรียกเมื่อเลือกและรีเฟรชตามรอบ tick ไม่ยิงทุกตัว)
   - แสดง: ชื่อ, `EntityId`, เผ่า/สายพันธุ์, อายุ/LifeStage, HP/Stamina/Mana(อนาคต), Needs bars (Hunger, Thirst, Energy, Safety), **Current Action + เหตุผลการตัดสินใจ**, Personality traits, Emotions, Bounded Memory, Skills, Inventory, ความสัมพันธ์แบบมีทิศทาง (`Trust`, `Affection`, `Fear`, `Rivalry`), และ **Dead History** (แสดงสาเหตุการตายและเหตุการณ์สำคัญย้อนหลังได้)
3. **Event Feed (`EventFeedPanel`):**
   - ดึง `WorldEvent` แบบแบ่งหน้าผ่าน `ReadEvents(cursor, limit)` (แยกจาก `ReadSnapshot()`) และกู้คืน milestones ช่วง `Gap` ผ่าน `ReadHistory(historyCursor, limit)` จัดเก็บแบบ ring buffer (เช่น แสดงล่าสุด 200 รายการใน UI), แสดงแถบแจ้งเตือนเมื่อเกิด `Gap`, กรองตามประเภท, คลิกเพื่อเลื่อนกล้องไปยังพิกัดเหตุการณ์หรือเปิด Inspector ของผู้เกี่ยวข้อง
4. **God Mode (`GodToolsPanel` — เชื่อม `SubmitCommand`):**
   - แยกจาก Observer อย่างชัดเจน ต้องสลับเข้าโหมด `God` ก่อนกดใช้ เพื่อป้องกันการแทรกแซงโลกโดยไม่ตั้งใจ
   - เครื่องมือ: Spawn (Human / Demon / Animal / Dragon ตามที่ Simulation รองรับ), เพิ่มทรัพยากร, รักษา/สร้างภัยพิบัติ (ในอนาคต), แสดง feedback เมื่อคำสั่ง `Applied` หรือ `Rejected`
5. **Possession Mode (`PossessionHud` — Stretch `T020`):**
   - ส่งคำสั่ง `Possess(EntityId)` -> เปลี่ยน Input เป็น `Move` / `Interact` / `Attack` / `UseSkill` สำหรับตัวละครนั้นโดยยังคง `EntityId` และสถานะเดิมทั้งหมด
   - แสดงแถบควบคุมตัวเอกและปุ่ม `Release Control` เพื่อให้ AI กลับมารับช่วงชีวิตต่อทันที โดยโลกและ NPC อื่นยังคงดำเนินตามปกติ
6. **Combat & Magic VFX (`T019`+):**
   - ตอบสนองเฉพาะ `WorldEvent` จาก Simulation: hit flash, floating damage/heal text (pooled), slash arcs, fire/ice/arcane projectiles, dragon breath cone/AOE, settlement alarm beacon

---

## 6. แผนการทำงานตามลำดับใน `TASKS.md`

| งาน | เงื่อนไขเริ่ม | ขอบเขตฝั่ง Presentation (`Antigravity`) | เกณฑ์ตรวจรับ |
|---|---|---|---|
| **Prep + N3 (ปัจจุบัน)** | ทำได้ทันที (ไม่แก้ Core/Contract) | แก้ N3 ในเอกสารนี้, `Assets/CREDITS.md`, 6 placeholder PNG atlases, `Sandbox/PresentationTest/` (Mock Data + กล้อง + HUD + MultiMesh Benchmark) | ไม่มีโค้ด Simulation ซ้ำ, ไม่แก้ `T001-review/`, Sandbox build & tests ผ่านบน Godot 4.7.2 + .NET 10.0.404 |
| **T007** | `Simulation.csproj` พร้อม + `T003` (`BLOCKED` รอ Codex) | สร้าง `Game/project.godot`, `Game/Game.csproj`, `SimulationHost` (ReadSnapshot <=1/frame + ReadEvents cursor + Gap recovery), chunk map renderer, `ObserverCamera2D`, AutoLOD renderer | แสดงผลจาก snapshot จริง, กู้คืน Gap ได้, กล้อง/input ไม่เปลี่ยน state |
| **T008** | `T005`, `T007` | Pause/Speed/Step HUD, Entity selection & follow, `EntityInspectorPanel`, `EventFeedPanel`, Dead entity history | เลือก/ตามตัวละครได้โดยไม่กระทบผลซิม, ดูประวัติคนตายได้ |
| **T012** | `T003`, `T009` | แสดงฤดูกาล/อุณหภูมิบน Top bar, season tint + resource visual density | ตรงกับผล Simulation, ทดสอบ headless เทียบ render |
| **T015–T016** | `T009`, `T010` | แสดง 2 ชุมชน Human/Demon, อาคาร (บ้าน/ฟาร์ม/คลัง/ตลาด), ยาม และ event การค้า | ข้อมูลคลังและเหตุการณ์ตรงกับ Simulation |
| **T018** | งาน Must ครบ | Playtest 15–30 นาที, จูน frame time/HUD usability, Windows export build, บันทึก baseline FPS/frame time | Build รันได้จริง, ไม่พบ bug ร้ายแรงฝั่ง presentation |
| **T019–T020** | `T008`, `T009`, `T014` | VFX การต่อสู้/เวท/มังกร, taming UI indicator, Possession controls (`Move`/`Interact`/Release) | ควบคุมและคืน AI ด้วย `EntityId` เดิมโดยโลกไม่หยุด |

---

## 7. คำถาม / การตัดสินใจที่ต้องยืนยันจากเจ้าของโปรเจกต์

1. **ขนาด Sprite & Tile Default:** ยืนยันค่าตั้งต้น `16×16` tiles และ `32×32` humanoids (หรือปรับเป็น `16×16` / `24×24` / `32×32`) ใน `docs/DECISIONS.md` ก่อนวาด asset ชุดใหญ่
2. **การเปลี่ยนผ่านสู่ `T007`:** เมื่อ `Codex` สร้าง `Simulation/Simulation.csproj` (`T002`/`T003`) และผ่านเงื่อนไขแล้ว `Antigravity` จะขึ้นโครง `Game/` บน branch `feat/T007-godot-presentation` ทันที (โดยใช้ `Godot 4.7.2 .NET` + `.NET SDK 10.0.404` / target `net8.0`)

---

## 8. ข้อความ Handoff สำหรับนำไปบันทึกใน `PROGRESS.md`

```text
Date/time: 2026-11-24
Agent: Google Antigravity
Task ID: Prep + N3 (pre-T007 contract sync)
Branch / commit: prep/N3-presentation-contract-sync (branched from 81709a9)
Status: BLOCKED (T007 blocked on Simulation.csproj / T003; N3 and presentation sandbox work DONE)
Summary of changes: Synced latest T001 commits (A007 rev 2 Accepted), updated docs/PRESENTATION_PLAN.md for N3 (ReadEvents cursor + Gap recovery + ReadHistory + snapshot separation <=1/frame), added Combat/Magic/Weather VFX placeholder atlas, updated Sandbox/PresentationTest with Mock Gap Banner / Receipt state / Animation pose tests, verified owner/Claude-installed Godot 4.7.2 .NET + system .NET SDK 10.0.404.
Files changed: docs/PRESENTATION_PLAN.md, Assets/CREDITS.md, Assets/Placeholders/*, Sandbox/PresentationTest/*, TASKS.md, PROGRESS.md
Commands run and results:
- C:\Program Files\dotnet\dotnet.exe --list-sdks -> 10.0.404 [C:\Program Files\dotnet\sdk]
- godot-mono.exe --version -> 4.7.2.stable.mono.official.ed1daf0bf
- C:\Program Files\dotnet\dotnet.exe build Sandbox/PresentationTest/PresentationTest.csproj -c Release / -c Debug -> Build succeeded (0 Warning(s), 0 Error(s))
- godot-mono.exe --headless --path Sandbox/PresentationTest -- --run-sandbox-tests -> [SANDBOX_TESTS_PASSED]
- godot-mono.exe --path Sandbox/PresentationTest -- --bench-vulkan -> N=5000 MultiMeshBulkBuffer: DrawCalls=113, UpdateMs=0.785 vs NaiveNodes: DrawCalls=1099, UpdateMs=3.165 (verified prior to adding 6th atlas; updated 6-atlas headless + screenshot verified at DrawCalls=118)
Known issues / assumptions: T007 cannot connect to ISimulation until Codex delivers Simulation/Simulation.csproj (T002/T003). Sandbox uses MockData only and stays isolated from Game/ and Simulation/.
Exact next step: Wait for Codex T002/T003 (Simulation.csproj + world map/clock/commands), then start T007 on feat/T007-godot-presentation.
Owner approval needed: Confirm default sprite/tile dimensions (16x16 / 32x32) when ready for final art.
```
