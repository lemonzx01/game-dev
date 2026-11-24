# Software Architecture — v0.1

## 1. Stack / boundaries
Godot 4 .NET for presentation, pure C#/.NET for simulation, Git for version control. Exact SDK/engine versions are pending environment verification. Do not add external dependencies or a database server unless an ADR justifies it. No required cloud service or LLM at runtime.

```text
Repository/
  AGENTS.md ... shared docs
  Simulation/         Pure C# class library, no Godot references
    Core/             IDs, clock, world state, RNG, commands, events
    Systems/          Needs, AI, movement, resources, ecology, society
    Queries/          Read models, spatial index
    Persistence/      Save DTOs, versioning, migration
  Game/               Godot .NET project
    Presentation/     Rendering, camera, sprites, UI, input
    Integration/      Simulation adapter, snapshots, command submission
  Data/               Species, resources, actions, items, buildings
  Assets/             Licensed source art and credits
  Tests/              Unit, integration, headless scenarios, benchmarks
  docs/               Additional design / ADR / testing records
```
This is a proposed layout, not an assertion that files already exist. Keep Simulation independent of Game. Ensure .NET target compatibility with actual Godot project before committing solution layout; avoid duplicate compilation of source folders.

## 2. Authoritative world model
`WorldState` owns simulation data; only a simulation step mutates it. Use stable WorldId + EntityId (never use scene node path as identity). Separate simulation position from render interpolation. Use compact collections and typed IDs. Start with simple component-oriented arrays/collections and migrate hot spots after measurement; no full custom ECS framework required in MVP.

Proposed components: Identity, Transform, Vitals, LifeStage, Species, Needs, Personality, Emotions, Mind/CurrentAction, Inventory, SkillProgress, SocialLinks, SettlementMembership. Store sparse memory and relationships separately. Species definitions data-driven; all IDs validated. World resources have nonnegative amounts and explicit producers/consumers.

## 3. Contract between Codex and Antigravity

A007 revision 2 (Accepted by owner 2026-11-24; frozen — changes require a new ADR). Simulation/Simulation.csproj owns the plain C# interface and DTOs. Game/Game.csproj, Tests/Headless and Tests/Simulation.Tests reference Simulation. Game/project.godot roots the presentation project. Each project compiles its own sources. One ISimulation owns one WorldId. The same API serves a headless host and Game/Integration.

```csharp
public interface ISimulation
{
    void Step();
    CommandReceipt SubmitCommand(GameCommand command);
    WorldSnapshot ReadSnapshot();
    EventPage ReadEvents(long afterSequence, int limit);
    HistoryPage ReadHistory(long afterSequence, int limit);
    EntityDetails? ReadEntityDetails(WorldId worldId, EntityId entityId);
}
```

### Commands and authority (H1, H3, L1)

Start at LastCompletedTick = 0. Call any API on the owner thread between completed calls. Nested calls from Step, callbacks or event handlers during Step, and calls from other threads throw InvalidOperationException before mutation. No synchronous event callbacks. Step processes one tick, publishing only its completed state. Pause, speed, single-step and SetTrackedEntity belong to the host. Camera and tracking never enter the command stream.

GameCommand carries WorldId, IssuerId, Mode, Kind and a typed payload. The core checks world, shape and authority against trusted session grants at submission and rechecks authority, living actor, target and current stock at execution. Observer cannot mutate the world. SpawnEntity needs a God grant. Future Possess, Move and Interact require the relevant grant and actor binding. A payload cannot grant itself authority. ReadEntityDetails returns null for another world or unknown ID.

Between steps, an admitted command receives TargetTick = LastCompletedTick + 1 and the next per-world CommandSequence (positive signed 64-bit, starts at 1). Queue capacity defaults to 1024. Invalid, unauthorized or full admissions return Rejected with reason, consume no sequence and enter no queue. Queued is a receipt, not completion. At TargetTick apply commands by ascending sequence before scheduled systems. Emit exactly one Applied or Rejected event with CommandSequence and reason for any rejection. Later commands see earlier writes. Counters must fail explicitly before wraparound.

### Events and recovery (H2, H4)

WorldSnapshot contains no event collection. Append WorldEvent to a per-world bounded log in commit order (ascending Tick, then emission order). EventSequence is contiguous, positive signed 64-bit, starts at 1 across all event kinds and ticks. An event carries Tick, Kind and relevant actor/target IDs, location, cause and CommandSequence. Default capacity 4096 entries. On full, evict the oldest before appending; readers never block simulation.

ReadEvents uses an exclusive cursor (0 starts reading), limit 1–256. An immutable page returns Status, OldestAvailable, LatestSequence, Records and NextCursor. Read in ascending sequence without consuming entries. Empty log has oldest 1, latest 0. On success NextCursor is the last returned sequence or the input cursor if empty. A cursor below OldestAvailable - 1 returns Gap with missing inclusive interval [afterSequence + 1, OldestAvailable - 1], no records, and NextCursor = OldestAvailable - 1. Negative/future cursors or invalid limits return InvalidRequest without advancing. Cursor belongs to this world and loaded timeline; discard host caches and cursors on world replacement/load.

Godot drains pages independently of drawing. On Gap, expose the lost range, mark unresolved receipts targeting already-completed ticks as outcome unknown until matched to a retained result (never infer success), rebuild visible state from the latest snapshot and query tracked IDs. Replay the retained suffix; deduplicate by EventSequence. ReadHistory pages permanent milestones by the same exclusive EventSequence, sorted and potentially sparse. It returns at most limit entries after the cursor, even if archived; valid old cursors do not gap. NextCursor is the last returned milestone sequence or the current log high-water mark when no milestones remain. Filter archive catch-up to the lost range locally. This recovers deaths even for entities born and lost between renders. A bookmark can query its individual history directly.

### Identity, death and snapshots (H4, M1)

EntityId is a positive signed 64-bit monotonic public ID per world, starting at 1. Never reuse a dead ID, even after compaction or load. Internal slots may be recycled. On death tick D invalidate actions immediately, record one Death event and durable death record together, and expose LifeState=Dead if snapshot D is requested. From D+1 omit that entity from render chunks. ReadEntityDetails continues to return its identity, death tick, cause and history summary from the indexed History Store.

Build WorldSnapshot on demand, at most once per requested tick; Godot requests at most once per rendered frame. It contains WorldId, Tick, time and immutable chunks of struct rows (ID, position, visual key, action, life state). Share unchanged chunks; preserve old views even when every chunk changes. Bound each backing array to less than 85,000 bytes including header. Host retains at most two snapshots for interpolation. Sim steps with no consumer must not materialize render snapshots. Performance and lifetime tests appear in §10.

## 4. Time and scheduling
- Fixed simulation step with integer tick/time; initial configurable target 10 simulation ticks per real second at 1× for prototype, tune by profiling. Render FPS independent. Speed multipliers schedule more ticks; if overloaded, slow effective speed or reduce noncritical presentation, show backlog, never skip gameplay time silently.
- Scheduler separates movement, decisions, needs, scheduled actions, environment and long-term systems. Stagger work using stable IDs; no all-agent decision spike at one tick.
- Actions: preconditions, target, duration, costs, effects, interrupt policy. Scheduled actions (e.g. sleep, reading) finish at due tick or wake on relevant event. Vital needs can be derived from elapsed time and updated at thresholds.
- Action commitment and cooldown prevent thrashing. Critical danger and active combat use bounded higher frequency.
- Stable ordering for conflicting writes and RNG; use seed + stable stream/event keys, not wall clock or thread scheduling. Same seed + commands + build/config should replay consistently; define supported determinism scope, do not promise cross-platform bitwise identity.

### Deterministic work and replay (M2, M3)

Each versioned configuration fixes integer decisions, path-node expansions and planning expansions per tick. Select due work by (due tick, priority, EntityId, enqueue ordinal) with explicit ascending keys, lower priority number first. Service up to each count, carry the ordered remainder and any partial frontier forward. Required due vital/death transitions run at their scheduled boundary before discretionary AI. Every tick completes its admitted work. Host sleep, frame time, viewport and worker completion time never determine eligibility. A slow host executes fewer whole ticks per wall-clock second.

RNG v1 uses independent SplitMix64 streams. Seed a stream with the low 64 bits (first 8 digest bytes decoded little-endian) of SHA-256 over ASCII FWS-RNG-v1 followed by seed:uint64, systemTag:uint32, scopeId:uint64, all little-endian. Reserve scope 0 for world-level streams; use stable numeric system tags from a versioned registry (1 = world generation, 2 = AI, 3 = ecology). Store each initialized stream's running state. For each draw add 0x9E3779B97F4A7C15 to state, then mix a copy z with xor-shift-right 30 and multiply by 0xBF58476D1CE4E5B9, xor-shift-right 27 and multiply by 0x94D049BB133111EB, then return z XOR (z >> 31). Arithmetic is unsigned modulo 2^64. Order draws within a stream by the scheduler's stable keys.

StateHash v1 is SHA-256 of canonical authoritative continuation fields in §9 schema order, including pending work and RNG. Use fixed-width little-endian integers, booleans 0/1, byte-count-prefixed UTF-8 (uint32), explicit null tags and collection lengths (uint32); sort keyed collections by numeric ID or ordinal UTF-8 bytes and preserve queue order. Use fixed-point integer fields for authoritative quantities in the MVP. Serialize versioned field order explicitly; no reflection, runtime GetHashCode or Dictionary/HashSet iteration order. Hash committed events separately in EventSequence order using the same encoding. Exclude render state, host timing and derived caches. Replay guarantee is scoped to identical seed, accepted inputs with target ticks/order, schema, build, data and configuration.

## 5. AI pipeline
Perceive local area and known places → derive needs/emotion → enumerate feasible actions → score utility (normalized, traits/risks) → choose action with deterministic tie break → execute/interrupt → emit event → update memories/beliefs. Track top candidates and reason in debug mode only; avoid logging every evaluation in production.
GOAP after baseline works: bounded depth/expansions, cache/retain viable plans, replan on invalidation, fall back to safe actions. FSM/behavior tree executes actions. Hierarchical organizations manage goals and orders; agents may accept/refuse. No omniscient NPC decisions; no per-agent LLM calls.

## 6. Spatial and pathfinding
Use chunk grid / spatial hash for local queries, update when entity changes cell. Query only bounded nearby cells and relevant entity type. Path requests queued with per-step budget; avoid synchronous pathfinding for every agent. MVP grid A* bounded, reuse paths when valid; region graph/hierarchical routes and flow fields for crowds later. Cache invalidation when terrain changes. Handle unreachable destinations and avoid infinite retries.

## 7. Scaling / simulation fidelity
Render LOD: cull off-screen, atlas/shared textures, pooled nodes or batched rendering where appropriate; zoom controls appearance only. Avoid creating permanent scene nodes for every living entity.
Simulation LOD: choose from activity, events, region load and importance independent of camera; distant idle individuals may use elapsed-time/scheduled action updates. Keep individual identity, resources, deaths, relationships and causal events. Coarse battle/economy must not be invented when camera returns. Define conservation/invariants and document approximation; if exact parity cannot be guaranteed, do not switch simulation rules based on camera. Important fights get high priority automatically regardless of observation. Camera tracking does not alter combat outcome.

## 8. Multithreading
Start deterministic single-threaded. Profile first. Add parallel jobs only for independent/read-only phases (e.g. score candidates from immutable state), then resolve commands in stable order on owner thread. No unsynchronized writes to world state; no Godot scene tree modifications by workers. Bound worker queue, avoid thread per agent. Keep tests comparing serial and parallel results. No C++ rewrite unless benchmark demonstrates justified bottleneck.

The interface is owner-thread-affine, independent of Godot's main thread. Start with one owner on the host main thread. If profiling warrants a dedicated simulation thread, the host posts ordered inputs with recorded tick admission to a bounded thread-safe inbox, forwards them between steps, and atomically publishes immutable snapshots/pages back to the main thread. Only the main thread touches SceneTree. Shutdown or load fences the inbox and invalidates old publications. Compare serial and threaded runs with identical admitted inputs before enabling this mode (M4).

## 9. Persistence and events

### Save/load boundary (H2–H4)

Capture on the owner between Steps, including commands accepted since the last step. The host fences input first; unacknowledged host messages must either be submitted before capture or remain explicitly unaccepted. Write a versioned checkpoint in the following logical order:

1. Schema and algorithm versions, WorldId, seed, data/config identity and deterministic settings, LastCompletedTick and world calendar.
2. NextEntityId, NextCommandSequence, NextEventSequence and scheduler enqueue counter; accepted command queue with full issuer/mode/payload, TargetTick, CommandSequence, in execution order; trusted gameplay grants/possession.
3. Entities/components, life state including deaths at the current tick, resources, environment, relationships, memories and history records/index keys. Preserve every value influencing a future decision.
4. Active actions, timers, pending events, ordered scheduler/path/AI backlogs, remaining budgets if carried, partial searches and deterministic tie-break state.
5. All initialized RNG stream keys, algorithm versions and running states; retained event log bounds and records, plus durable history or a verified immutable archive checkpoint. Define exact per-type field schemas before implementing T013.

Validate into a separate world before swapping: schema/migration, checkpoint references, IDs, monotonic counters above all allocated values, queue ticks/order, log sequence continuity, history links, supported RNG/config and nonnegative resources. Restore counters verbatim and rebuild derived indices. Save via temp + verified replace with backup; failed validation/write/load preserves the previous valid world and save. Restore accepted commands exactly once. Loading resumes that saved timeline; host drops pre-load pending receipts and cursors. Compare against uninterrupted execution from the same capture boundary.

### Working memory and History Store (M5)

Proposed T010 defaults per living entity: 64 episodic working memories and 32 directional relationship entries. On insertion above cap evict lowest (importance, lastRelevantTick, stable record ID), ascending. Update the reciprocal relationship only when separately justified. Keep active causal dependencies in durable action/history records before eviction. Reading history in the UI never grants knowledge to NPCs.

Persist birth/parentage, death/cause, founding or fall of settlements, war/peace and durable bonds such as taming or kinship once per milestone with WorldId, EventSequence, tick and participants. Index by entity and sequence. Keep at most 256 milestone records in the shared in-memory history page cache; archive older entries in indexed storage and page them on demand. Preserve minimal death summaries durably. Cap hot memory independently of archive size; verify archive persistence before releasing its only copy. Storage errors surface explicitly. No hard deletion of causal milestones based on age or camera.

## 10. Performance and acceptance
Headless runner shares simulation library. Test 100, 1,000, 5,000, 10,000 entities progressively; 50k/100k are future research targets, not guaranteed. Measure p50/p95/p99 step time, throughput, managed allocations/GC, memory, path queue, rendered frame time, hardware/seed/scenario/config. Establish real baseline before asserting FPS. For 60 FPS target frame budget ~16.7ms, not a guarantee of simulation capacity. Prioritize responsiveness, no runaway backlog, no growing memory beyond expected state, and correct behavior. See docs/TESTING.md.

Acceptance scenarios for H1–H4, M1–M5 and L1–L2 are in docs/TESTING.md under T001 Revision. The numeric limits are proposed configuration defaults pending measurement.
