using System.Collections.Immutable;
using System.Text;

namespace FantasyWorld.Simulation;

/// <summary>Faction of a sapient species. Values are part of the hash schema. No faction is good or evil by rule.</summary>
public enum Faction { None = 0, Human = 1, Demon = 2 }

/// <summary>A member's current occupation, chosen from what the settlement needs.</summary>
public enum Job { None = 0, Farmer = 1, Gatherer = 2, Woodcutter = 3, Guard = 4, Trader = 5 }

/// <summary>What a Work action is doing; values are part of the hash schema.</summary>
public enum WorkGoal { None = 0, Deliver = 1, Harvest = 2, GatherFood = 3, GatherWood = 4, TradeLoad = 5, TradeTravel = 6, ReturnHome = 7 }

/// <summary>Settlement tuning (ticks, units). Defaults are provisional.</summary>
public sealed record SettlementConfig(int Radius = 10, int FarmPlots = 6, int CropMax = 10, int CropGrowTicks = 150,
    int CarryCapacity = 4, int FoodTarget = 60, int WoodTarget = 30, int HarvestMinCrop = 3, int WorkScore = 150, int WorkCooldownTicks = 60)
{
    public static SettlementConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (int v in new[] { Radius, CropMax, CropGrowTicks, CarryCapacity, HarvestMinCrop, WorkCooldownTicks })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        foreach (int v in new[] { FarmPlots, FoodTarget, WoodTarget, WorkScore })
            ArgumentOutOfRangeException.ThrowIfNegative(v);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FarmPlots, 32);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(HarvestMinCrop, CropMax);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (int v in new[] { Radius, FarmPlots, CropMax, CropGrowTicks, CarryCapacity, FoodTarget, WoodTarget, HarvestMinCrop, WorkScore, WorkCooldownTicks })
            w.Write(v);
    }
}

/// <summary>Read model for UIs. Population and job counts are live members.</summary>
public sealed record SettlementView(int Id, string Name, Faction Faction, int X, int Y, int Radius, long Food, long Wood,
    int Population, int Farmers, int Gatherers, int Woodcutters, int CropsReady, long FoundedTick,
    long Coins = 0, int Guards = 0, int TradersOut = 0);

internal sealed class FarmPlot
{
    public int Cell;
    public int Crop;
    public bool GrowScheduled;
}

internal sealed class Settlement
{
    public int Id;
    public string Name = "";
    public Faction Faction;
    public int X, Y;          // storage / town centre
    public long Food, Wood;   // storage stock, never negative
    public long Coins;        // treasury (T016): founding grant, then only trade payments
    public int Members, Guards, TradersOut;
    public int Books = 1;     // T017: each town keeps one book (Almanac) at its centre
    public long FoundedTick;
    public readonly List<FarmPlot> Plots = new();
    public readonly SortedDictionary<int, long> BlockedBuyers = new(); // buyer settlement id -> cooldown until tick
    // R2 danger alarm state (persisted via WriteDanger/ReadDangerFrom; included in StateHash)
    public long AlarmUntil;
    public long AlarmThreatId;
    public int AlarmThreatX, AlarmThreatY;
    public long AlarmLastSeenTick;
    // Derived runtime cache (not saved or hashed): cleared whenever marketRevision or this town's trade state changes.
    internal long CachedDealRevision = -1;
    internal Settlement? CachedDealPartner;
    internal ResourceKind CachedDealKind;
}

/// <summary>God tool: found a settlement of a faction at a passable tile, at least two radii from any other settlement.</summary>
public sealed record FoundSettlementCommand(WorldId WorldId, long IssuerId, string Mode, int X, int Y, Faction Faction, string Name)
    : GameCommand(WorldId, IssuerId, Mode)
{
    public override CommandKind Kind => CommandKind.FoundSettlement;
}

/// <summary>
/// T015 settlements. Every unit of food and wood is traceable: wild nodes and crops → carried (Gathered/Harvested) →
/// storage (Deposited) → eaten from storage (EntityAte with Target = settlement id). A carrier that dies loses its load
/// through an explicit CarryLost event. Humans and demons follow exactly the same rules.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly List<Settlement> settlements = new(); // index = id − 1, never removed
    private IReadOnlyList<SettlementView>? settlementViews; // cached immutable read view for the last completed tick

    private SettlementConfig TownCfg => options.Settlements;

    private Settlement? SettlementOf(EntityState e) => e.SettlementId > 0 ? settlements[e.SettlementId - 1] : null;

    private Faction FactionOf(EntityState e) => SpeciesOf(e)?.Faction ?? Faction.None;

    // ---- Founding and membership ---------------------------------------------------------------

    private CommandReason Found(FoundSettlementCommand c, long now)
    {
        if (!TerrainRules.IsPassable(terrain[Cell(c.X, c.Y)])) return CommandReason.Impassable;
        foreach (var s in settlements)
            if (Math.Max(Math.Abs(s.X - c.X), Math.Abs(s.Y - c.Y)) < 2 * TownCfg.Radius) return CommandReason.TooClose;
        var town = new Settlement { Id = settlements.Count + 1, Name = c.Name, Faction = c.Faction, X = c.X, Y = c.Y, FoundedTick = now, Coins = TradeCfg.StartingCoins };
        // Farm plots: nearest grass tiles (Manhattan, then cell) within 3, excluding the centre and resource tiles.
        var candidates = new List<(int Distance, int Cell)>();
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int distance = Math.Abs(dx) + Math.Abs(dy);
                int x = c.X + dx, y = c.Y + dy;
                if (distance == 0 || distance > 3 || !InBounds(x, y) || terrain[Cell(x, y)] != Terrain.Grass) continue;
                int cell = Cell(x, y);
                if (resources.Find(cell, ResourceKind.Food) >= 0 || resources.Find(cell, ResourceKind.Wood) >= 0) continue;
                if (PlotAt(cell) is not null) continue;
                candidates.Add((distance, cell));
            }
        foreach (var (_, cell) in candidates.OrderBy(p => p.Distance).ThenBy(p => p.Cell).Take(TownCfg.FarmPlots))
            town.Plots.Add(new FarmPlot { Cell = cell, Crop = TownCfg.CropMax / 2 });
        settlements.Add(town);
        marketRevision++;
        settlementViews = null;
        for (int i = 0; i < town.Plots.Count; i++) ScheduleCrop(town, i, now);
        Emit(new WorldEvent(0, now, EventKinds.SettlementFounded, 0, 0, town.Id, c.X, c.Y, (int)c.Faction, town.Plots.Count));
        return CommandReason.None;
    }

    /// <summary>A sapient newcomer joins the nearest settlement of its faction within two radii (ties: lower id).</summary>
    private void JoinSettlement(EntityState e)
    {
        var faction = FactionOf(e);
        if (faction == Faction.None) return;
        Settlement? best = null;
        int bestDistance = int.MaxValue;
        foreach (var s in settlements)
        {
            if (s.Faction != faction) continue;
            int d = Math.Max(Math.Abs(s.X - e.X), Math.Abs(s.Y - e.Y));
            if (d <= 2 * TownCfg.Radius && d < bestDistance) { best = s; bestDistance = d; }
        }
        if (best is null) return;
        e.SettlementId = best.Id;
        best.Members++;
        e.HomeX = best.X;
        e.HomeY = best.Y;
    }

    // ---- Crops ---------------------------------------------------------------------------------

    private (Settlement Town, int Index)? PlotAt(int cell)
    {
        foreach (var s in settlements)
            for (int i = 0; i < s.Plots.Count; i++)
                if (s.Plots[i].Cell == cell) return (s, i);
        return null;
    }

    private void ScheduleCrop(Settlement s, int index, long now)
    {
        var plot = s.Plots[index];
        if (plot.GrowScheduled || plot.Crop >= TownCfg.CropMax) return;
        plot.GrowScheduled = true;
        var (interval, _) = CropGrowth(plot.Cell, now);
        scheduler.Schedule(now + interval, 10, 0, new WorkItem(WorkKind.CropGrow, s.Id, index), isRequired: true);
    }

    // Seasonal like wild plants (T012): base interval × 100 / growth %, dormant in winter or on cold tiles.
    private (long Interval, bool Grows) CropGrowth(int cell, long now)
    {
        int percent = options.Climate.GrowthPercent(WorldTime.From(now, options.Calendar).Season);
        if (percent == 0 || TemperatureAt(cell, now) < options.Climate.MinGrowthTemperature) return (TownCfg.CropGrowTicks, false);
        return (Math.Max(1, (long)TownCfg.CropGrowTicks * 100 / percent), true);
    }

    private void RunCropGrow(WorkItem item, long now)
    {
        var s = settlements[(int)item.Subject - 1];
        var plot = s.Plots[(int)item.Token];
        plot.GrowScheduled = false;
        if (CropGrowth(plot.Cell, now).Grows) plot.Crop = Math.Min(TownCfg.CropMax, plot.Crop + 1);
        ScheduleCrop(s, (int)item.Token, now);
    }

    // ---- Work ----------------------------------------------------------------------------------

    /// <summary>What the settlement needs from this member now (deterministic priority), or None.</summary>
    private WorkGoal PlanWork(EntityState e)
    {
        if (SettlementOf(e) is not { } s || StageOf(e, tick + 1) == LifeStage.Juvenile || e.Job == Job.Guard) return WorkGoal.None;
        int far = Math.Max(Math.Abs(e.X - s.X), Math.Abs(e.Y - s.Y));
        if (e.CarryAmount > 0)
            return e.TradePartner != 0 ? WorkGoal.TradeTravel : (e.OnTrip || far > MindCfg.PerceptionRadius) ? WorkGoal.ReturnHome : WorkGoal.Deliver;
        if (e.OnTrip || far > TownCfg.Radius) return WorkGoal.ReturnHome;
        foreach (var p in s.Plots)
            if (p.Crop >= TownCfg.HarvestMinCrop) return WorkGoal.Harvest;
        if (s.Food < TownCfg.FoodTarget) return WorkGoal.GatherFood;
        if (s.Wood < TownCfg.WoodTarget) return WorkGoal.GatherWood;
        if (TradeDeal(s) is not null) return WorkGoal.TradeLoad;
        return WorkGoal.None;
    }

    private bool IsWorkGoal(EntityState e, int cell)
    {
        var s = SettlementOf(e)!;
        switch (e.WorkGoal)
        {
            case WorkGoal.Deliver:
                return cell == Cell(s.X, s.Y);
            case WorkGoal.Harvest:
                foreach (var p in s.Plots)
                    if (p.Cell == cell && p.Crop >= TownCfg.HarvestMinCrop) return true;
                return false;
            case WorkGoal.GatherFood:
                return CanConsumeAt(cell, ResourceKind.Food) && PlotAt(cell) is null;
            case WorkGoal.GatherWood:
                int n = resources.Find(cell, ResourceKind.Wood);
                return n >= 0 && resources[n].Amount > 0;
            case WorkGoal.TradeLoad:
            case WorkGoal.ReturnHome:
                return cell == Cell(s.X, s.Y);
            case WorkGoal.TradeTravel:
                var partner = settlements[e.TradePartner - 1];
                return cell == Cell(partner.X, partner.Y);
            default:
                return false;
        }
    }

    /// <summary>Arrived at the work tile: deliver, harvest or gather. Stock only moves, never appears.</summary>
    private void DoWork(EntityState e, long now)
    {
        var s = SettlementOf(e);
        if (s is null) return;
        int cell = Cell(e.X, e.Y), capacity = TownCfg.CarryCapacity;
        switch (e.WorkGoal)
        {
            case WorkGoal.Deliver when cell == Cell(s.X, s.Y) && e.CarryAmount > 0:
                var beforeDeliver = BuyerSignature(s);
                if (e.CarryKind == ResourceKind.Food) s.Food += e.CarryAmount; else s.Wood += e.CarryAmount;
                InvalidateSettlementEconomy(s, beforeDeliver);
                Emit(new WorldEvent(0, now, EventKinds.Deposited, 0, e.Id.Value, s.Id, s.X, s.Y, (int)e.CarryKind, e.CarryAmount));
                e.CarryAmount = 0;
                e.CarryKind = 0;
                break;
            case WorkGoal.Harvest:
                if (PlotAt(cell) is not { } hit || hit.Town != s || s.Plots[hit.Index].Crop <= 0) break;
                var plot = s.Plots[hit.Index];
                int crop = (int)Math.Min(plot.Crop, (long)capacity + Level(e, Skill.Farming) / 2); // skilled farmers bring more
                if (crop <= 0) break;
                plot.Crop -= crop;
                Load(e, ResourceKind.Food, crop);
                ScheduleCrop(s, hit.Index, now);
                Emit(new WorldEvent(0, now, EventKinds.Harvested, 0, e.Id.Value, s.Id, e.X, e.Y, (int)ResourceKind.Food, crop));
                GainXp(e, Skill.Farming, (long)crop * SkillCfg.XpPerHarvestUnit, now);
                break;
            case WorkGoal.TradeLoad:
                LoadTrade(e, s, now);
                break;
            case WorkGoal.TradeTravel when e.TradePartner != 0:
                SellTrade(e, s, now);
                break;
            case WorkGoal.ReturnHome:
                EndTrip(e, s, now);
                break;
            case WorkGoal.GatherFood:
            case WorkGoal.GatherWood:
                var kind = e.WorkGoal == WorkGoal.GatherFood ? ResourceKind.Food : ResourceKind.Wood;
                int node = resources.Find(cell, kind);
                if (node < 0 || resources[node].Amount <= 0) break;
                ref var stock = ref resources[node];
                var skill = kind == ResourceKind.Food ? Skill.Foraging : Skill.Woodcutting;
                int take = (int)Math.Min(stock.Amount, (long)capacity + Level(e, skill) / 2);
                if (take <= 0) break;
                stock.Amount -= take;
                Load(e, kind, take);
                ScheduleRegrowth(node, now);
                Emit(new WorldEvent(0, now, EventKinds.Gathered, 0, e.Id.Value, s.Id, e.X, e.Y, (int)kind, take));
                GainXp(e, skill, (long)take * SkillCfg.XpPerGatherUnit, now);
                break;
        }
    }

    private static void Load(EntityState e, ResourceKind kind, int amount)
    {
        e.CarryKind = kind;
        e.CarryAmount = amount;
    }

    /// <summary>A member at the town centre eats from storage (Target = settlement id marks storage meals).</summary>
    private bool EatFromStorage(EntityState e, long now)
    {
        if (SettlementOf(e) is not { } s || e.X != s.X || e.Y != s.Y || s.Food <= 0) return false;
        var c = options.Vitals;
        var v = e.Vitals.At(now, c);
        if (v.Hunger == 0) return false;
        int units = (int)Math.Min(Math.Min((v.Hunger + c.FoodPerUnit - 1) / c.FoodPerUnit, c.MaxUnitsPerAct), s.Food);
        var beforeEat = BuyerSignature(s);
        s.Food -= units;
        InvalidateSettlementEconomy(s, beforeEat);
        e.Vitals = new VitalsAnchor(now, Math.Max(0, v.Hunger - units * c.FoodPerUnit), v.Thirst, v.Health);
        ScheduleDeath(e);
        Emit(new WorldEvent(0, now, EventKinds.EntityAte, 0, e.Id.Value, s.Id, s.X, s.Y, (int)ResourceKind.Food, units));
        return true;
    }

    /// <summary>Death while carrying: the load is lost through an explicit sink event.</summary>
    private void DropCarry(EntityState e, long now)
    {
        if (e.CarryAmount <= 0) return;
        Emit(new WorldEvent(0, now, EventKinds.CarryLost, 0, e.Id.Value, e.SettlementId, e.X, e.Y, (int)e.CarryKind, e.CarryAmount));
        e.CarryAmount = 0;
        e.CarryKind = 0;
    }

    private static Job JobFor(WorkGoal goal, Job current) => goal switch
    {
        WorkGoal.Harvest => Job.Farmer,
        WorkGoal.GatherFood => Job.Gatherer,
        WorkGoal.GatherWood => Job.Woodcutter,
        WorkGoal.TradeLoad or WorkGoal.TradeTravel => Job.Trader,
        _ => current, // delivering or returning keeps the job that produced the trip
    };

    // ---- Canonical encoding, reads -------------------------------------------------------------

    private void WriteSettlements(BinaryWriter w)
    {
        w.Write((uint)settlements.Count);
        foreach (var s in settlements)
        {
            w.Write(s.Id);
            byte[] name = Encoding.UTF8.GetBytes(s.Name);
            w.Write((uint)name.Length);
            w.Write(name);
            w.Write((int)s.Faction);
            w.Write(s.X); w.Write(s.Y);
            w.Write(s.Food); w.Write(s.Wood);
            w.Write(s.FoundedTick);
            w.Write(s.Coins); w.Write(s.Members); w.Write(s.Guards); w.Write(s.TradersOut); w.Write(s.Books);
            w.Write((uint)s.Plots.Count);
            foreach (var p in s.Plots) { w.Write(p.Cell); w.Write(p.Crop); w.Write(p.GrowScheduled); }
            w.Write((uint)s.BlockedBuyers.Count);
            foreach (var (partner, until) in s.BlockedBuyers) { w.Write(partner); w.Write(until); }
        }
    }

    private void ReadSettlements(BinaryReader r, Func<BinaryReader, int, int> count)
    {
        int n = count(r, 100_000);
        var seenPlotCells = new HashSet<int>();
        var centreCells = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            var s = new Settlement { Id = r.ReadInt32() };
            uint rawNameLength = r.ReadUInt32();
            if (rawNameLength is < 1u or > 128u) throw new InvalidDataException("Settlement name length invalid.");
            int nameLength = (int)rawNameLength;
            s.Name = Encoding.UTF8.GetString(r.ReadBytes(nameLength));
            s.Faction = (Faction)r.ReadInt32();
            s.X = r.ReadInt32(); s.Y = r.ReadInt32();
            s.Food = r.ReadInt64(); s.Wood = r.ReadInt64();
            s.FoundedTick = r.ReadInt64();
            s.Coins = r.ReadInt64(); s.Members = r.ReadInt32(); s.Guards = r.ReadInt32(); s.TradersOut = r.ReadInt32(); s.Books = r.ReadInt32();
            if (s.Coins < 0 || s.Members < 0 || s.Guards < 0 || s.TradersOut < 0 || s.Books is < 0 or > 1000)
                throw new InvalidDataException("Settlement counters invalid.");
            if (s.Id != i + 1 || s.Faction is not (Faction.Human or Faction.Demon) || !InBounds(s.X, s.Y)
                || !TerrainRules.IsPassable(terrain[Cell(s.X, s.Y)]) || s.Food < 0 || s.Wood < 0)
                throw new InvalidDataException("Settlement invalid.");
            int centreCell = Cell(s.X, s.Y);
            if (!centreCells.Add(centreCell) || seenPlotCells.Contains(centreCell))
                throw new InvalidDataException("Settlement centre overlaps another settlement or plot.");
            int plots = count(r, 32);
            for (int k = 0; k < plots; k++)
            {
                var p = new FarmPlot { Cell = r.ReadInt32(), Crop = r.ReadInt32(), GrowScheduled = r.ReadBoolean() };
                if (p.Cell < 0 || p.Cell >= terrain.Length || terrain[p.Cell] != Terrain.Grass
                    || p.Crop < 0 || p.Crop > TownCfg.CropMax || (p.GrowScheduled && p.Crop >= TownCfg.CropMax))
                    throw new InvalidDataException("Farm plot invalid.");
                if (centreCells.Contains(p.Cell) || !seenPlotCells.Add(p.Cell))
                    throw new InvalidDataException("Farm plot overlaps centre or another plot.");
                s.Plots.Add(p);
            }
            uint blockedCount = r.ReadUInt32();
            if (blockedCount > (uint)n) throw new InvalidDataException("Blocked buyers count invalid.");
            int lastPartner = 0;
            for (int k = 0; k < (int)blockedCount; k++)
            {
                int partner = r.ReadInt32();
                long until = r.ReadInt64();
                if (partner <= lastPartner || partner > n || partner == s.Id || until <= tick)
                    throw new InvalidDataException("Blocked buyer entry invalid.");
                s.BlockedBuyers.Add(partner, until);
                lastPartner = partner;
            }
            settlements.Add(s);
        }
    }

    internal int SettlementAggregationCount { get; private set; }
    internal long SettlementAggregationEntityScans { get; private set; }

    public IReadOnlyList<SettlementView> ReadSettlements()
    {
        CheckAccess();
        if (settlementViews is not null) return settlementViews;
        SettlementAggregationCount++;
        if (settlements.Count == 0) return settlementViews = ImmutableArray<SettlementView>.Empty;
        var pop = new int[settlements.Count];
        var farmers = new int[settlements.Count];
        var gatherers = new int[settlements.Count];
        var woodcutters = new int[settlements.Count];
        SettlementAggregationEntityScans += entities.Count;
        foreach (var e in entities)
        {
            if (e.LifeState != LifeStates.Alive || e.SettlementId <= 0) continue;
            int idx = e.SettlementId - 1;
            pop[idx]++;
            switch (e.Job)
            {
                case Job.Farmer: farmers[idx]++; break;
                case Job.Gatherer: gatherers[idx]++; break;
                case Job.Woodcutter: woodcutters[idx]++; break;
            }
        }
        var views = ImmutableArray.CreateBuilder<SettlementView>(settlements.Count);
        for (int i = 0; i < settlements.Count; i++)
        {
            var s = settlements[i];
            int ready = 0;
            foreach (var p in s.Plots) if (p.Crop >= TownCfg.HarvestMinCrop) ready++;
            views.Add(new SettlementView(s.Id, s.Name, s.Faction, s.X, s.Y, TownCfg.Radius, s.Food, s.Wood,
                pop[i], farmers[i], gatherers[i], woodcutters[i], ready, s.FoundedTick, s.Coins, s.Guards, s.TradersOut));
        }
        return settlementViews = views.MoveToImmutable();
    }

    /// <summary>Farm plot cells and crop levels of a settlement (for renderers and tests).</summary>
    public IReadOnlyList<(int X, int Y, int Crop)> ReadFarmPlots(int settlementId)
    {
        CheckAccess();
        if (settlementId < 1 || settlementId > settlements.Count) return [];
        return settlements[settlementId - 1].Plots.Select(p => (p.Cell % options.MapWidth, p.Cell / options.MapWidth, p.Crop)).ToList();
    }
}
