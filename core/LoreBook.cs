using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Which found texts the carrier has ever read, across runs (user://lore.cfg), and which this run.
/// Reading is the only way in; dying never erases it. Placement prefers texts not read yet.
/// Not persistent (headless tests) keeps everything in memory, so tests never touch the player's.
/// </summary>
public sealed class LoreBook
{
    public const string DefaultPath = "user://lore.cfg";
    private const string Section = "read";

    public bool Persistent { get; }
    public int EverCount => _ever.Count;
    public int RunCount => _run.Count;

    private readonly string _path;
    private readonly HashSet<string> _ever = new();
    private readonly HashSet<string> _run = new();

    public LoreBook(bool persistent, string path = DefaultPath)
    {
        Persistent = persistent;
        _path = path;
        if (persistent)
        {
            Load();
        }
    }

    public bool HasEverRead(string id)
    {
        return _ever.Contains(id);
    }

    public bool ReadThisRun(string id)
    {
        return _run.Contains(id);
    }

    /// <summary>Records a reading; says whether it was the first this run and the first ever. Saves when new.</summary>
    public void Read(string id, out bool firstThisRun, out bool firstEver)
    {
        firstThisRun = _run.Add(id);
        firstEver = _ever.Add(id);
        if (firstEver)
        {
            Save();
        }
    }

    /// <summary>A new run: everything may be read "for the first time this run" again.</summary>
    public void BeginRun()
    {
        _run.Clear();
    }

    /// <summary>Forgets everything (tests, a fresh save).</summary>
    public void Reset()
    {
        _ever.Clear();
        _run.Clear();
        Save();
    }

    private void Load()
    {
        ConfigFile file = new();
        if (file.Load(_path) != Error.Ok || !file.HasSection(Section))
        {
            return;
        }
        foreach (string key in file.GetSectionKeys(Section))
        {
            _ever.Add(key);
        }
    }

    private void Save()
    {
        if (!Persistent)
        {
            return;
        }
        ConfigFile file = new();
        foreach (string id in _ever)
        {
            file.SetValue(Section, id, true);
        }
        file.Save(_path);
    }
}
