namespace FantasyWorld.Simulation;

/// <summary>Trade and guard tuning (units, coins, ticks). Defaults are provisional.</summary>
public sealed record TradeConfig(int StartingCoins = 100, int FoodPrice = 2, int WoodPrice = 3, int LoadUnits = 8,
    int SurplusFactor = 2, int MaxTradersOut = 1, int RouteSearchesPerTick = 2,
    int MembersPerGuard = 8, long GuardDamage = 350_000, int DefendScore = 4000,
    int RouteNodeBudget = 2000, int UnreachableCooldownTicks = 300)
{
    public static TradeConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (int v in new[] { FoodPrice, WoodPrice, LoadUnits, SurplusFactor, MaxTradersOut, RouteSearchesPerTick, MembersPerGuard, DefendScore, RouteNodeBudget, UnreachableCooldownTicks })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(StartingCoins);
        ArgumentOutOfRangeException.ThrowIfLessThan(GuardDamage, 1);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (long v in new long[] { StartingCoins, FoodPrice, WoodPrice, LoadUnits, SurplusFactor, MaxTradersOut, RouteSearchesPerTick, MembersPerGuard, GuardDamage, DefendScore, RouteNodeBudget, UnreachableCooldownTicks })
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
    private long marketRevision = 1;
    private long buyersCacheRevision = -1;
    private readonly List<Settlement> foodBuyers = new();
    private readonly List<Settlement> woodBuyers = new();

    // ---- Trade planning ------------------------------------------------------------------------

    internal long TradeDealScans { get; private set; }

    private (bool NeedFood, bool NeedWood, bool CanPayFood, bool CanPayWood) BuyerSignature(Settlement s) =>
        (s.Food < TownCfg.FoodTarget, s.Wood < TownCfg.WoodTarget, s.Coins >= TradeCfg.FoodPrice, s.Coins >= TradeCfg.WoodPrice);

    private void InvalidateSettlementEconomy(Settlement s, (bool, bool, bool, bool) before)
    {
        s.CachedDealRevision = -1;
        settlementViews = null;
        if (BuyerSignature(s) != before) marketRevision++;
    }

    /// <summary>The good and partner for a new trade trip from <paramref name="s"/>, or null. Food first, then wood.</summary>
    private (Settlement Partner, ResourceKind Kind)? TradeDeal(Settlement s)
    {
        if (s.TradersOut >= TradeCfg.MaxTradersOut) return null;
        if (s.CachedDealRevision == marketRevision)
            return s.CachedDealPartner is not null ? (s.CachedDealPartner, s.CachedDealKind) : null;
        var deal = FindTradeDeal(s);
        s.CachedDealRevision = marketRevision;
        s.CachedDealPartner = deal?.Partner;
        s.CachedDealKind = deal?.Kind ?? default;
        return deal;
    }

    private (Settlement Partner, ResourceKind Kind)? FindTradeDeal(Settlement s)
    {
        TradeDealScans++;
        long foodSurplus = (long)TradeCfg.SurplusFactor * Math.Max(1, TownCfg.FoodTarget);
        long woodSurplus = (long)TradeCfg.SurplusFactor * Math.Max(1, TownCfg.WoodTarget);
        if (s.Food < foodSurplus && s.Wood < woodSurplus) return null;

        if (buyersCacheRevision != marketRevision)
        {
            foodBuyers.Clear();
            woodBuyers.Clear();
            foreach (var p in settlements) // ascending Id preserves tie-break order
            {
                if (p.Food < TownCfg.FoodTarget && p.Coins >= TradeCfg.FoodPrice) foodBuyers.Add(p);
                if (p.Wood < TownCfg.WoodTarget && p.Coins >= TradeCfg.WoodPrice) woodBuyers.Add(p);
            }
            buyersCacheRevision = marketRevision;
        }

        foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Wood })
        {
            long stock = kind == ResourceKind.Food ? s.Food : s.Wood;
            long surplus = kind == ResourceKind.Food ? foodSurplus : woodSurplus;
            if (stock < surplus) continue;
            var buyers = kind == ResourceKind.Food ? foodBuyers : woodBuyers;
            Settlement? best = null;
            int bestDistance = int.MaxValue;
            foreach (var p in buyers)
            {
                if (p == s || s.BlockedBuyers.ContainsKey(p.Id)) continue;
                int d = Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
                if (d < bestDistance) { best = p; bestDistance = d; }
            }
            if (best is not null) return (best, kind);
        }
        return null;
    }

    private bool IsLongRoute(WorkGoal goal) => goal is WorkGoal.TradeTravel or WorkGoal.ReturnHome;

    // Terminal outbound trade failure: switch to returning the cargo, keep the trader slot until home/death,
    // and suppress immediate rebooking of the same unreachable buyer.
    private void FailTradeOutbound(EntityState e, long now)
    {
        if (SettlementOf(e) is { } home && e.TradePartner > 0)
        {
            long until = now + TradeCfg.UnreachableCooldownTicks;
            if (!home.BlockedBuyers.TryGetValue(e.TradePartner, out long existing) || until > existing)
            {
                home.BlockedBuyers[e.TradePartner] = until;
                home.CachedDealRevision = -1;
            }
        }
        e.TradePartner = 0;
    }

    private void PruneBlockedBuyers(long now)
    {
        foreach (var s in settlements)
        {
            if (s.BlockedBuyers.Count == 0) continue;
            List<int>? expired = null;
            foreach (var (partner, until) in s.BlockedBuyers)
                if (until <= now) (expired ??= new()).Add(partner);
            if (expired is not null)
            {
                foreach (int partner in expired) s.BlockedBuyers.Remove(partner);
                s.CachedDealRevision = -1;
            }
        }
    }

    // Loads trade goods at the home centre (the trader is standing there).
    private void LoadTrade(EntityState e, Settlement s, long now)
    {
        if (e.X != s.X || e.Y != s.Y || e.CarryAmount > 0 || s.TradersOut >= TradeCfg.MaxTradersOut || TradeDeal(s) is not { } deal) return;
        var (partner, kind) = deal;
        if (partner == s || s.BlockedBuyers.ContainsKey(partner.Id)) return;
        long stock = kind == ResourceKind.Food ? s.Food : s.Wood;
        int target = kind == ResourceKind.Food ? TownCfg.FoodTarget : TownCfg.WoodTarget;
        if (stock < (long)TradeCfg.SurplusFactor * Math.Max(1, target)) return;
        if ((kind == ResourceKind.Food ? partner.Food : partner.Wood) >= target) return;
        int price = TradeCfg.Price(kind);
        long affordable = partner.Coins / price;
        int units = (int)Math.Min(Math.Min(TradeCfg.LoadUnits, stock - target), affordable);
        if (units <= 0) return;
        var before = BuyerSignature(s);
        if (kind == ResourceKind.Food) s.Food -= units; else s.Wood -= units;
        Load(e, kind, units);
        e.TradePartner = partner.Id;
        e.OnTrip = true;
        s.TradersOut++;
        InvalidateSettlementEconomy(s, before);
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
        var beforePartner = BuyerSignature(partner);
        var beforeHome = BuyerSignature(home);
        if (kind == ResourceKind.Food) partner.Food += units; else partner.Wood += units;
        partner.Coins -= pay;
        home.Coins += pay;
        InvalidateSettlementEconomy(partner, beforePartner);
        InvalidateSettlementEconomy(home, beforeHome);
        e.CarryAmount -= units;
        if (e.CarryAmount == 0) e.CarryKind = 0;
        Emit(new WorldEvent(0, now, EventKinds.TradeSold, 0, e.Id.Value, partner.Id, partner.X, partner.Y, (int)kind, units));
        GainXp(e, Skill.Trading, (long)units * SkillCfg.XpPerTradeUnit, now);
        Emit(new WorldEvent(0, now, EventKinds.TradePaid, 0, partner.Id, home.Id, partner.X, partner.Y, price, pay));
    }

    // Back home: deposit leftovers and end the trip.
    private void EndTrip(EntityState e, Settlement home, long now)
    {
        if (e.X != home.X || e.Y != home.Y) return;
        if (e.CarryAmount > 0)
        {
            var before = BuyerSignature(home);
            if (e.CarryKind == ResourceKind.Food) home.Food += e.CarryAmount; else home.Wood += e.CarryAmount;
            InvalidateSettlementEconomy(home, before);
            Emit(new WorldEvent(0, now, EventKinds.Deposited, 0, e.Id.Value, home.Id, home.X, home.Y, (int)e.CarryKind, e.CarryAmount));
            e.CarryAmount = 0;
            e.CarryKind = 0;
        }
        if (e.OnTrip) { e.OnTrip = false; home.TradersOut--; home.CachedDealRevision = -1; settlementViews = null; }
    }

    // ---- Guards --------------------------------------------------------------------------------

    private int GuardsWanted(Settlement s) => Math.Max(1, s.Members / TradeCfg.MembersPerGuard);

    // Called on think: an adult member volunteers while the town has fewer guards than it needs,
    // deferring while carrying goods or executing an in-flight work/collection action.
    private void MaybeBecomeGuard(EntityState e, long now)
    {
        if (SettlementOf(e) is not { } s || e.Job == Job.Guard || e.OnTrip || e.CarryAmount > 0
            || e.Mind.Kind == ActionKind.Work || StageOf(e, now) == LifeStage.Juvenile) return;
        if (s.Guards >= GuardsWanted(s)) return;
        e.Job = Job.Guard;
        s.Guards++;
        perceptionVersion++;
    }

    /// <summary>Metered scan for nearest living feared intruder inside town radius (Manhattan from guard, then id).</summary>
    private bool TryScanTownThreat(EntityState guard, long now, out EntityState? threat)
    {
        threat = null;
        if (guard.Job != Job.Guard || SettlementOf(guard) is not { } s) return true;
        return RunCachedBestScan(
            guard, ScanPhase.TownThreat, s, ScanCacheKind.TownThreat, s.Id, guard.Species,
            guard.X, guard.Y, s.X, s.Y, TownCfg.Radius, long.MaxValue, now, shareInCache: true,
            out threat, out _, out _);
    }


    // Membership bookkeeping when a member dies.
    private void LeaveSettlement(EntityState e)
    {
        if (SettlementOf(e) is not { } s) return;
        s.Members--;
        if (e.Job == Job.Guard) s.Guards--;
        if (e.OnTrip) { e.OnTrip = false; s.TradersOut--; s.CachedDealRevision = -1; }
        e.TradePartner = 0;
        perceptionVersion++;
    }

    // ---- Test hooks ----------------------------------------------------------------------------
    internal void SetStorageForTest(int settlementId, long food, long wood, long coins)
    {
        var s = settlements[settlementId - 1];
        var before = BuyerSignature(s);
        s.Food = food;
        s.Wood = wood;
        s.Coins = coins;
        InvalidateSettlementEconomy(s, before);
    }
}
