using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The carrier's achievements, kept across runs in user://achievements.cfg: which are unlocked (and
/// when), and the running tallies they are measured by. A tally is either counted up (kills, torches
/// lit, across all runs) or a best (the deepest station or depth reached in one run). An achievement
/// unlocks when its tally reaches its goal, once, and never locks again. Not persistent (headless
/// tests) keeps everything in memory, so tests never touch the player's.
/// </summary>
public sealed class AchievementBook
{
    public const string DefaultPath = "user://achievements.cfg";
    private const string UnlockedSection = "unlocked";
    private const string TallySection = "tally";

    /// <summary>One achievement: what it is called, what it asks, and which tally reaches which goal.</summary>
    public sealed record Entry(string Id, string Title, string Description, string Tally, int Goal);

    /// <summary>Tallies the tracker keeps (progression/AchievementTracker).</summary>
    public static class Tallies
    {
        public const string BestStation = "best_station";
        public const string BestDepth = "best_depth";
        public const string BestCircle = "best_circle";
        public const string Kills = "kills";
        public const string Torches = "torches";
        public const string Sleeps = "sleeps";
        public const string Contracts = "contracts";
        public const string Relics = "relics";
        public const string LoreRead = "lore_read";
        public const string Boons = "boons";
        public const string Mends = "mends";
        public const string Deaths = "deaths";
        public const string Runs = "runs";
        public const string Webs = "webs";
    }

    /// <summary>Every achievement, in the order the page lists them.</summary>
    public static readonly Entry[] All =
    {
        new("first_fire", "First Fire", "Reach the first station.", Tallies.BestStation, 1),
        new("five_fires", "Five Fires", "Reach the fifth station in one run.", Tallies.BestStation, 5),
        new("ten_fires", "Ten Fires", "Reach the tenth station in one run.", Tallies.BestStation, 10),
        new("kilometre", "A Kilometre Down", "Descend a thousand metres in one run.", Tallies.BestDepth, 1000),
        new("past_limbo", "Past Limbo", "Go down into the second circle, Lust.", Tallies.BestCircle, 2),
        new("greed", "Deeper Still", "Reach the fourth circle, Greed.", Tallies.BestCircle, 4),
        new("tangled", "Tangled", "Walk into a giant spider's web.", Tallies.Webs, 1),
        new("first_blood", "First Blood", "Kill something that wanted to kill you.", Tallies.Kills, 1),
        new("hunter", "Hunter", "Kill 25 enemies or beasts, over all your runs.", Tallies.Kills, 25),
        new("torchbearer", "Torchbearer", "Light a torch against the dark.", Tallies.Torches, 1),
        new("rested", "Rested", "Sleep by a station's fire.", Tallies.Sleeps, 1),
        new("courier", "Courier", "Deliver a contract.", Tallies.Contracts, 1),
        new("relic_keeper", "Relic Keeper", "Bring a relic to where it belongs.", Tallies.Relics, 1),
        new("reader", "Reader", "Read five different found texts.", Tallies.LoreRead, 5),
        new("blessed", "Blessed", "Take a boon at a station.", Tallies.Boons, 1),
        new("needle", "Needle and Thread", "Mend your gear.", Tallies.Mends, 1),
        new("not_the_end", "Not the End", "Fall, and begin again.", Tallies.Deaths, 1),
        new("veteran", "Veteran", "Begin ten descents.", Tallies.Runs, 10),
    };

    public bool Persistent { get; }
    public int UnlockedCount => _unlocked.Count;
    public int Total => All.Length;

    private readonly string _path;
    private readonly Dictionary<string, long> _unlocked = new();
    private readonly Dictionary<string, int> _tallies = new();

    public AchievementBook(bool persistent, string path = DefaultPath)
    {
        Persistent = persistent;
        _path = path;
        if (persistent)
        {
            Load();
        }
    }

    public static Entry? Find(string id)
    {
        foreach (Entry entry in All)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }
        return null;
    }

    public bool IsUnlocked(string id)
    {
        return _unlocked.ContainsKey(id);
    }

    /// <summary>When it was unlocked (Unix seconds), or 0.</summary>
    public long UnlockedAt(string id)
    {
        if (_unlocked.TryGetValue(id, out long at))
        {
            return at;
        }
        return 0;
    }

    public int Tally(string tally)
    {
        if (_tallies.TryGetValue(tally, out int value))
        {
            return value;
        }
        return 0;
    }

    /// <summary>Adds to a counted tally; returns the achievements this unlocked (usually none).</summary>
    public List<Entry> Count(string tally, int amount = 1)
    {
        _tallies[tally] = Tally(tally) + amount;
        return Settle(tally);
    }

    /// <summary>Raises a best tally if this beats it; returns the achievements this unlocked.</summary>
    public List<Entry> Best(string tally, int value)
    {
        if (value <= Tally(tally))
        {
            return new List<Entry>();
        }
        _tallies[tally] = value;
        return Settle(tally);
    }

    /// <summary>Forgets everything (tests, a fresh save).</summary>
    public void Reset()
    {
        _unlocked.Clear();
        _tallies.Clear();
        Save();
    }

    private List<Entry> Settle(string tally)
    {
        List<Entry> unlocked = new();
        int value = Tally(tally);
        foreach (Entry entry in All)
        {
            if (entry.Tally == tally && value >= entry.Goal && !_unlocked.ContainsKey(entry.Id))
            {
                _unlocked[entry.Id] = (long)Time.GetUnixTimeFromSystem();
                unlocked.Add(entry);
            }
        }
        Save();
        return unlocked;
    }

    private void Load()
    {
        ConfigFile file = new();
        if (file.Load(_path) != Error.Ok)
        {
            return;
        }
        if (file.HasSection(UnlockedSection))
        {
            foreach (string key in file.GetSectionKeys(UnlockedSection))
            {
                _unlocked[key] = file.GetValue(UnlockedSection, key, 0L).AsInt64();
            }
        }
        if (file.HasSection(TallySection))
        {
            foreach (string key in file.GetSectionKeys(TallySection))
            {
                _tallies[key] = file.GetValue(TallySection, key, 0).AsInt32();
            }
        }
    }

    private void Save()
    {
        if (!Persistent)
        {
            return;
        }
        ConfigFile file = new();
        foreach (KeyValuePair<string, long> pair in _unlocked)
        {
            file.SetValue(UnlockedSection, pair.Key, pair.Value);
        }
        foreach (KeyValuePair<string, int> pair in _tallies)
        {
            file.SetValue(TallySection, pair.Key, pair.Value);
        }
        file.Save(_path);
    }
}
