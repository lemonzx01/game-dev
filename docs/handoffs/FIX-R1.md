# FIX-R1 — Fix Codex review findings for T015–T017

| Field | Value |
|---|---|
| Task ID | FIX-R1 |
| Agent | Google Antigravity |
| Branch | `fix/R1-review-T015-T017` |
| Worktree | `F:\Code\game-dev\worktrees\FIX-R1-antigravity` |
| State | IN_PROGRESS |
| Source Review | `docs/reviews/2026-11-codex-review-T015-T017.md` |

## Files in Scope
- `Simulation/Systems/Navigation.cs`
- `Simulation/SimulationWorld.cs`
- `Simulation/SimulationWorld.Paths.cs`
- `Simulation/SimulationWorld.Settlements.cs`
- `Simulation/SimulationWorld.Trade.cs`
- `Simulation/SimulationWorld.Skills.cs`
- `Simulation/SimulationWorld.Save.cs`
- `Simulation/SimulationWorld.Entities.cs`
- `Simulation/SimulationWorld.Taming.cs`
- `Tests/Simulation.Tests/FixR1Tests.cs`
- `Tests/Simulation.Tests/T016Tests.cs`
- `Tests/Simulation.Tests/Program.cs`
- `docs/handoffs/FIX-R1.md`

## Findings Status
| # | Severity | Finding | Test | Status |
|---|---|---|---|---|
| 1 | High | Unreachable buyer holds trader slot indefinitely | `FixR1Tests` (pending) | IN_PROGRESS |
| 2 | High | Long trade routes bypass node budget and counter | `FixR1Tests` (pending) | IN_PROGRESS |
| 3 | Medium | Settlement edits can flood empty plots/centres | `FixR1Tests` (pending) | TODO |
| 4 | Medium | Guard promotion can strand worker load | `FixR1Tests` (pending) | TODO |
| 5 | Medium | Signed overflow in CarryCapacity | `FixR1Tests` (pending) | TODO |
| 6 | Medium | Scholarship bonus XP signed overflow | `FixR1Tests` (pending) | TODO |
| 7 | Medium | Crop scheduler references unvalidated on load | `FixR1Tests` (pending) | TODO |
| 8 | Medium | Settlement, trade, reading save invariants incomplete | `FixR1Tests` (pending) | TODO |
| 9 | Medium | T016 Trade test misses seller food/coin assertions | `FixR1Tests` + `T016Tests` | TODO |
| 10 | Medium | ReadSettlements O(towns * entities) per call | `FixR1Tests` (pending) | TODO |
| 11 | Medium | TradeDeal scans every settlement per worker | `FixR1Tests` (pending) | TODO |
| 12 | Low | Founding places farm plot beyond Manhattan 3 | `FixR1Tests` (pending) | TODO |

## Exact Next Step
Fix Findings 1 and 2 together, then proceed through 3–12 in order.
