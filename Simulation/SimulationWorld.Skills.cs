namespace FantasyWorld.Simulation;

/// <summary>Skills; values index <see cref="EntityState.Skills"/> (value − 1) and are part of the hash schema.</summary>
public enum Skill { Farming = 1, Foraging = 2, Woodcutting = 3, Combat = 4, Trading = 5, Scholarship = 6 }

/// <summary>Skill and reading tuning. Defaults are provisional.</summary>
public sealed record SkillConfig(int XpPerLevelSquared = 100, int MaxLevel = 10, int XpPerHarvestUnit = 10, int XpPerGatherUnit = 10,
    int XpPerHit = 20, int XpPerTradeUnit = 5, int ScholarBonusPercentPerLevel = 10, int BookPages = 300, int ReadSessionTicks = 100,
    int ReadScore = 100)
{
    public const int MaxScholarBonusPercentPerLevel = 1000;
    public const int MaxSupportedLevel = 1000;

    public static SkillConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (int v in new[] { XpPerLevelSquared, MaxLevel, BookPages, ReadSessionTicks, ReadScore })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        foreach (int v in new[] { XpPerHarvestUnit, XpPerGatherUnit, XpPerHit, XpPerTradeUnit, ScholarBonusPercentPerLevel })
            ArgumentOutOfRangeException.ThrowIfNegative(v);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxLevel, MaxSupportedLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ScholarBonusPercentPerLevel, MaxScholarBonusPercentPerLevel);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (int v in new[] { XpPerLevelSquared, MaxLevel, XpPerHarvestUnit, XpPerGatherUnit, XpPerHit, XpPerTradeUnit, ScholarBonusPercentPerLevel, BookPages, ReadSessionTicks, ReadScore })
            w.Write(v);
    }
}

/// <summary>Read model: XP and level per skill, plus reading progress (book key → pages read).</summary>
public sealed record SkillsView(EntityId Id, long Farming, long Foraging, long Woodcutting, long Combat, long Trading, long Scholarship,
    int FarmingLevel, int ForagingLevel, int WoodcuttingLevel, int CombatLevel, int TradingLevel, int ScholarshipLevel, int BooksStarted, int BooksFinished);

/// <summary>A reader's progress through one book (pages already read never award XP again).</summary>
internal struct ReadingProgress
{
    public long Book;   // settlementId × 1000 + book index
    public int Pages;
}

/// <summary>
/// T017 skills and reading. XP comes only from things that actually happened (units harvested/gathered/sold, hits landed,
/// pages read) and levels give small real effects (larger harvest/gather loads, harder guard hits). Scholarship speeds up
/// learning of the other skills. Reading happens at the town centre in sessions; when a session ends early (danger, hunger,
/// death) the reader is credited exactly the ticks actually read, and pages already read never count twice.
/// </summary>
public sealed partial class SimulationWorld
{
    private SkillConfig SkillCfg => options.Skills;

    private int Level(EntityState e, Skill skill) =>
        (int)Math.Min(SkillCfg.MaxLevel, Math.Sqrt(e.Skills[(int)skill - 1] / (double)SkillCfg.XpPerLevelSquared));

    private void GainXp(EntityState e, Skill skill, long baseXp, long now)
    {
        if (baseXp <= 0) return;
        int idx = (int)skill - 1;
        long current = e.Skills[idx];
        long remaining = long.MaxValue - current;
        if (remaining <= 0) return;
        Int128 scaled = skill == Skill.Scholarship
            ? baseXp
            : (Int128)baseXp * (100 + (Int128)SkillCfg.ScholarBonusPercentPerLevel * Level(e, Skill.Scholarship)) / 100;
        long xp = scaled >= remaining ? remaining : (long)scaled;
        if (xp <= 0) return;
        int before = Level(e, skill);
        e.Skills[idx] = current + xp;
        int after = Level(e, skill);
        if (after > before) Emit(new WorldEvent(0, now, EventKinds.SkillLevelUp, 0, e.Id.Value, 0, e.X, e.Y, (int)skill, after));
    }

    // ---- Books and reading ---------------------------------------------------------------------

    private static long BookKey(int settlementId, int index) => (long)settlementId * 1000 + index;

    private int PagesRead(EntityState e, long book)
    {
        foreach (var r in e.Reading) if (r.Book == book) return r.Pages;
        return 0;
    }

    /// <summary>The first unfinished book in the reader's town, or -1.</summary>
    private long NextBook(EntityState e)
    {
        if (SettlementOf(e) is not { } s) return -1;
        for (int i = 0; i < s.Books; i++)
        {
            long key = BookKey(s.Id, i);
            if (PagesRead(e, key) < SkillCfg.BookPages) return key;
        }
        return -1;
    }

    private bool WantsToRead(EntityState e, VitalsView v) =>
        e.SettlementId != 0 && v.Hunger < MindCfg.EatStartNeed && v.Thirst < MindCfg.DrinkStartNeed
        && PlanWork(e) == WorkGoal.None && NextBook(e) >= 0 && StageOf(e, tick + 1) != LifeStage.Juvenile;

    // At the town centre: open the next unfinished book and read until the session or the book ends.
    private void StartReading(EntityState e, long now)
    {
        if (SettlementOf(e) is not { } s || e.X != s.X || e.Y != s.Y || NextBook(e) is var book && book < 0)
        {
            FinishAction(e, now);
            return;
        }
        e.ReadingBook = book;
        e.ReadingSince = now;
        int left = SkillCfg.BookPages - PagesRead(e, book);
        ScheduleStep(e, now + Math.Min(SkillCfg.ReadSessionTicks, left));
    }

    /// <summary>Credits pages read since the session began (exactly the elapsed ticks, never past the book's end).</summary>
    private void FinishReading(EntityState e, long now)
    {
        if (e.ReadingSince <= 0) return;
        long book = e.ReadingBook;
        int already = PagesRead(e, book);
        int pages = (int)Math.Min(now - e.ReadingSince, SkillCfg.BookPages - already);
        e.ReadingSince = 0;
        e.ReadingBook = 0;
        if (pages <= 0) return;
        int at = e.Reading.FindIndex(r => r.Book == book);
        if (at < 0) e.Reading.Add(new ReadingProgress { Book = book, Pages = pages });
        else e.Reading[at] = new ReadingProgress { Book = book, Pages = already + pages };
        GainXp(e, Skill.Scholarship, pages, now);
        bool finished = already + pages >= SkillCfg.BookPages;
        Emit(new WorldEvent(0, now, EventKinds.ReadProgress, 0, e.Id.Value, book, e.X, e.Y, finished ? 1 : 0, pages));
    }

    private static void WriteSkills(BinaryWriter w, EntityState e)
    {
        foreach (long xp in e.Skills) w.Write(xp);
        w.Write((uint)e.Reading.Count);
        foreach (var r in e.Reading) { w.Write(r.Book); w.Write(r.Pages); }
        w.Write(e.ReadingBook);
        w.Write(e.ReadingSince);
    }

    private void ReadSkillsFrom(BinaryReader r, EntityState e)
    {
        for (int i = 0; i < e.Skills.Length; i++)
        {
            e.Skills[i] = r.ReadInt64();
            if (e.Skills[i] < 0) throw new InvalidDataException("Skill XP negative.");
        }
        int n = (int)r.ReadUInt32();
        if (n > 1000) throw new InvalidDataException("Reading list too long.");
        for (int i = 0; i < n; i++)
        {
            var p = new ReadingProgress { Book = r.ReadInt64(), Pages = r.ReadInt32() };
            if (p.Pages < 0 || p.Pages > SkillCfg.BookPages) throw new InvalidDataException("Reading progress invalid.");
            e.Reading.Add(p);
        }
        e.ReadingBook = r.ReadInt64();
        e.ReadingSince = r.ReadInt64();
    }

    public SkillsView? ReadSkills(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        if (worldId != this.worldId || FindLive(entityId.Value) is not (int i and >= 0)) return null;
        var e = entities[i];
        long[] s = e.Skills;
        return new SkillsView(entityId, s[0], s[1], s[2], s[3], s[4], s[5],
            Level(e, Skill.Farming), Level(e, Skill.Foraging), Level(e, Skill.Woodcutting), Level(e, Skill.Combat), Level(e, Skill.Trading), Level(e, Skill.Scholarship),
            e.Reading.Count, e.Reading.Count(r => r.Pages >= SkillCfg.BookPages));
    }

    internal void SetSkillForTest(long id, Skill skill, long xp) => entities[FindLive(id)].Skills[(int)skill - 1] = xp;
}
