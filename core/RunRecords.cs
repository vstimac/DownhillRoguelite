using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// What carries over between runs (the only thing that does): best depth and station, number of
/// runs and total metres descended. Saved to user://records.cfg after every run; unlocks read it.
/// Not persistent records (headless tests) live in memory only, so tests never touch the player's.
/// </summary>
public sealed class RunRecords
{
    public const string DefaultPath = "user://records.cfg";
    private const string Section = "records";

    public int Runs { get; private set; }
    public int BestDepth { get; private set; }
    public int BestStation { get; private set; }
    public long TotalDepth { get; private set; }

    public bool Persistent { get; }

    private readonly string _path;

    public RunRecords(bool persistent, string path = DefaultPath)
    {
        Persistent = persistent;
        _path = path;
        if (persistent)
        {
            Load();
        }
    }

    /// <summary>Adds a finished run; returns whether it set a new best depth. Saves.</summary>
    public bool Add(int depth, int station)
    {
        Runs++;
        TotalDepth += depth;
        bool record = depth > BestDepth;
        if (record)
        {
            BestDepth = depth;
        }
        BestStation = Mathf.Max(BestStation, station);
        Save();
        return record;
    }

    /// <summary>Forgets everything (tests, a fresh save).</summary>
    public void Reset()
    {
        Runs = 0;
        BestDepth = 0;
        BestStation = 0;
        TotalDepth = 0;
        Save();
    }

    private void Load()
    {
        ConfigFile file = new();
        if (file.Load(_path) != Error.Ok)
        {
            return;
        }
        Runs = (int)file.GetValue(Section, "runs", 0);
        BestDepth = (int)file.GetValue(Section, "best_depth", 0);
        BestStation = (int)file.GetValue(Section, "best_station", 0);
        TotalDepth = (long)file.GetValue(Section, "total_depth", 0L);
    }

    private void Save()
    {
        if (!Persistent)
        {
            return;
        }
        ConfigFile file = new();
        file.SetValue(Section, "runs", Runs);
        file.SetValue(Section, "best_depth", BestDepth);
        file.SetValue(Section, "best_station", BestStation);
        file.SetValue(Section, "total_depth", TotalDepth);
        file.Save(_path);
    }
}
