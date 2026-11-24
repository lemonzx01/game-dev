namespace FantasyWorld.Simulation;

/// <summary>Trade and guard tuning (units, coins, ticks). Defaults are provisional.</summary>
public sealed record TradeConfig(int StartingCoins = 100, int FoodPrice = 2, int WoodPrice = 3, int LoadUnits = 8,
    int SurplusFactor = 2, int MaxTradersOut = 1, int RouteSearchesPerTick = 2,
    int MembersPerGuard = 8, long GuardDamage = 350_000, int DefendScore = 4000)
{
    public static TradeConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (int v in new[] { FoodPrice, WoodPrice, LoadUnits, SurplusFactor, MaxTradersOut, RouteSearchesPerTick, MembersPerGuard, DefendScore })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(StartingCoins);
        ArgumentOutOfRangeException.ThrowIfLessThan(GuardDamage, 1);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (long v in new long[] { StartingCoins, FoodPrice, WoodPrice, LoadUnits, SurplusFactor, MaxTradersOut, RouteSearchesPerTick, MembersPerGuard, GuardDamage, DefendScore })
            w.Write(v);
    }

    internal int Price(ResourceKind kind) => kind == ResourceKind.Wood ? WoodPrice : FoodPrice;
}

/// <summary>
/// T016 trade and guards.
/// Trade: a settlement with a surplus (≥ SurplusFactor × target) sends one trader with up to LoadUnits of the good to the
/// nearest other settlement that needs it and can pay. On arrival the buyer's storage gains exactly the units sold and
/// pays price × units, which the seller's settlement receives; unsold units are carried home and deposited. Coins only
/// move between settlements (the founding grant is the God-given start). Events: TradeLoaded, TradeSold, TradePaid.
/// Guards: one per MembersPerGuard members (at least one). A guard sees threats (species its people fear) inside the town
/// radius, defends instead of fleeing, and kills with GuardDamage (DeathCause.Combat).
/// </summary>
public sealed partial class SimulationWorld
{
    private TradeConfig TradeCfg => options.Trade;
    private int routeSearchesLeft;

    // ---- Trade planning ------------------------------------------------------------------------

    /// <summary>The good and partner for a new trade trip from <paramref name="s"/>, or null. Food first, then wood.</summary>
    private (Settlement Partner, ResourceKind Kind)? TradeDeal(Settlement s)
    {
        if (s.TradersOut >= TradeCfg.MaxTradersOut) return null;
        foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Wood })
        {
            long stock = kind == ResourceKind.Food ? s.Food : s.Wood;
            int target = kind == ResourceKind.Food ? TownCfg.FoodTarget : TownCfg.WoodTarget;
            if (stock < (long)TradeCfg.SurplusFactor * Math.Max(1, target)) continue;
            Settlement? best = null;
            int bestDistance = int.MaxValue;
            foreach (var p in settlements)
            {
                if (p == s || (kind == ResourceKind.Food ? p.Food : p.Wood) >= target || p.Coins < TradeCfg.Price(kind)) continue;
                int d = Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
                if (d < bestDistance) { best = p; bestDistance = d; }
            }
            if (best is not null) return (best, kind);
        }
        return null;
    }

    private bool IsLongRoute(WorkGoal goal) => goal is WorkGoal.TradeTravel or WorkGoal.ReturnHome;

    // Loads trade goods at the home centre (the trader is standing there).
    private void LoadTrade(EntityState e, Settlement s, long now)
    {
        if (e.X != s.X || e.Y != s.Y || e.CarryAmount > 0 || TradeDeal(s) is not { } deal) return;
        var (partner, kind) = deal;
        long stock = kind == ResourceKind.Food ? s.Food : s.Wood;
        int target = kind == ResourceKind.Food ? TownCfg.FoodTarget : TownCfg.WoodTarget;
        long affordable = partner.Coins / TradeCfg.Price(kind);
        int units = (int)Math.Min(Math.Min(TradeCfg.LoadUnits, stock - target), affordable);
        if (units <= 0) return;
        if (kind == ResourceKind.Food) s.Food -= units; else s.Wood -= units;
        Load(e, kind, units);
        e.TradePartner = partner.Id;
        e.OnTrip = true;
        s.TradersOut++;
        Emit(new WorldEvent(0, now, EventKinds.TradeLoaded, 0, e.Id.Value, s.Id, s.X, s.Y, (int)kind, units));
    }

    // At the partner's centre: sell what the partner can pay for; the seller's settlement is paid.
    private void SellTrade(EntityState e, Settlement home, long now)
    {
        var partner = settlements[e.TradePartner - 1];
        if (e.X != partner.X || e.Y != partner.Y || e.CarryAmount <= 0) return;
        var kind = e.CarryKind;
        int price = TradeCfg.Price(kind);
        int units = (int)Math.Min(e.CarryAmount, partner.Coins / price);
        e.TradePartner = 0; // whatever happens, the trip now heads home
        if (units <= 0) return;
        long pay = (long)units * price;
        if (kind == ResourceKind.Food) partner.Food += units; else partner.Wood += units;
        partner.Coins -= pay;
        home.Coins += pay;
        e.CarryAmount -= units;
        if (e.CarryAmount == 0) e.CarryKind = 0;
        Emit(new WorldEvent(0, now, EventKinds.TradeSold, 0, e.Id.Value, partner.Id, partner.X, partner.Y, (int)kind, units));
        Emit(new WorldEvent(0, now, EventKinds.TradePaid, 0, partner.Id, home.Id, partner.X, partner.Y, price, pay));
    }

    // Back home: deposit leftovers and end the trip.
    private void EndTrip(EntityState e, Settlement home, long now)
    {
        if (e.X != home.X || e.Y != home.Y) return;
        if (e.CarryAmount > 0)
        {
            if (e.CarryKind == ResourceKind.Food) home.Food += e.CarryAmount; else home.Wood += e.CarryAmount;
            Emit(new WorldEvent(0, now, EventKinds.Deposited, 0, e.Id.Value, home.Id, home.X, home.Y, (int)e.CarryKind, e.CarryAmount));
            e.CarryAmount = 0;
            e.CarryKind = 0;
        }
        if (e.OnTrip) { e.OnTrip = false; home.TradersOut--; }
    }

    // ---- Guards --------------------------------------------------------------------------------

    private int GuardsWanted(Settlement s) => Math.Max(1, s.Members / TradeCfg.MembersPerGuard);

    // Called on think: an adult member volunteers while the town has fewer guards than it needs.
    private void MaybeBecomeGuard(EntityState e, long now)
    {
        if (SettlementOf(e) is not { } s || e.Job == Job.Guard || e.OnTrip || StageOf(e, now) == LifeStage.Juvenile) return;
        if (s.Guards >= GuardsWanted(s)) return;
        e.Job = Job.Guard;
        s.Guards++;
    }

    /// <summary>Nearest living creature the guard's people fear inside the town radius (Manhattan from the guard, then id).</summary>
    private EntityState? TownThreat(EntityState guard)
    {
        if (guard.Job != Job.Guard || SettlementOf(guard) is not { } s) return null;
        EntityState? best = null;
        int bestDistance = int.MaxValue, r = TownCfg.Radius;
        grid.Query(s.X, s.Y, r, id =>
        {
            var other = entities[FindLive(id)];
            if (other.LifeState != LifeStates.Alive || !Fears(guard, other.Species)) return;
            if (Math.Max(Math.Abs(other.X - s.X), Math.Abs(other.Y - s.Y)) > r) return;
            int d = Math.Abs(other.X - guard.X) + Math.Abs(other.Y - guard.Y);
            if (d < bestDistance || (d == bestDistance && other.Id.Value < best!.Id.Value)) { best = other; bestDistance = d; }
        });
        return best;
    }

    /// <summary>Damage dealt by an attacker: guards fight with GuardDamage, animals with their species' attack.</summary>
    private long AttackPower(EntityState attacker) =>
        attacker.Job == Job.Guard ? TradeCfg.GuardDamage : SpeciesOf(attacker)?.AttackDamage ?? 0;

    // Membership bookkeeping when a member dies.
    private void LeaveSettlement(EntityState e)
    {
        if (SettlementOf(e) is not { } s) return;
        s.Members--;
        if (e.Job == Job.Guard) s.Guards--;
        if (e.OnTrip) { e.OnTrip = false; s.TradersOut--; }
    }

    // ---- Test hooks ----------------------------------------------------------------------------
    internal void SetStorageForTest(int settlementId, long food, long wood, long coins)
    {
        var s = settlements[settlementId - 1];
        s.Food = food;
        s.Wood = wood;
        s.Coins = coins;
    }
}
