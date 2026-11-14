using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// A run in progress, saved so it can be continued after the game is closed (Save &amp; Continue;
/// death still ends the run and deletes the save). The pack and worn gear are saved beside it by
/// EquipmentPersistence (PackPath). Both files are written to a .tmp and then moved over the old
/// one, so a crash mid-write never leaves half a save; a file that doesn't read back whole is
/// ignored rather than half applied.
/// </summary>
public sealed class RunSave
{
    public const int CurrentVersion = 1;
    public const string DefaultPath = "user://run.json";
    public const string DefaultPackPath = "user://run_pack.json";

    public int Version { get; set; } = CurrentVersion;
    public string GameVersion { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public ulong Seed { get; set; }
    public int RunNumber { get; set; }
    public int Depth { get; set; }
    public int Stage { get; set; }
    public string Character { get; set; } = "";
    public List<string> Boons { get; set; } = new();
    public Dictionary<string, float> Modifiers { get; set; } = new();
    /// <summary>Resource paths of the conditional boons taken (Beta 3.3); older saves have none.</summary>
    public List<string> ConditionalBoons { get; set; } = new();
    public int Day { get; set; } = 1;
    public float Hour { get; set; }
    public float[] Position { get; set; } = new float[3];
    public float Yaw { get; set; }
    public float Health { get; set; }
    public float Hunger { get; set; }
    public float Thirst { get; set; }
    public List<string> TakenSupplies { get; set; } = new();

    private static string _path = DefaultPath;
    private static string _packPath = DefaultPackPath;

    public static string Path => _path;
    public static string PackPath => _packPath;

    /// <summary>Tests: save and read other files.</summary>
    public static void UseFiles(string path, string packPath)
    {
        _path = path;
        _packPath = packPath;
    }

    /// <summary>How many runs can be kept side by side (the title screen's "Saved runs").</summary>
    public const int SlotCount = 3;

    /// <summary>The slot being played, 1..SlotCount (slot 1 is the file older versions wrote).</summary>
    public static int Slot { get; private set; } = 1;

    public static string SlotPath(int slot)
    {
        if (slot <= 1)
        {
            return DefaultPath;
        }
        return "user://run_" + slot + ".json";
    }

    public static string SlotPackPath(int slot)
    {
        if (slot <= 1)
        {
            return DefaultPackPath;
        }
        return "user://run_" + slot + "_pack.json";
    }

    /// <summary>From now on the run is saved to and read from this slot.</summary>
    public static void UseSlot(int slot)
    {
        Slot = Math.Clamp(slot, 1, SlotCount);
        _path = SlotPath(Slot);
        _packPath = SlotPackPath(Slot);
    }

    /// <summary>The run kept in a slot without switching to it, or null (empty or unreadable).</summary>
    public static RunSave? Peek(int slot) => ReadFile(ProjectSettings.GlobalizePath(SlotPath(slot)), out _);

    /// <summary>The slot saved last, or 0 when none holds a run.</summary>
    public static int NewestSlot() => SlotByTime(true);

    /// <summary>Where a new run goes: the first empty slot, or the one saved longest ago.</summary>
    public static int SlotForNewRun()
    {
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            if (!File.Exists(ProjectSettings.GlobalizePath(SlotPath(slot))))
            {
                return slot;
            }
        }
        return SlotByTime(false);
    }

    private static int SlotByTime(bool newest)
    {
        int best = 0;
        DateTime bestTime = DateTime.MinValue;
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            string absolute = ProjectSettings.GlobalizePath(SlotPath(slot));
            if (!File.Exists(absolute))
            {
                continue;
            }
            DateTime written = File.GetLastWriteTimeUtc(absolute);
            bool better = best == 0;
            if (best != 0 && newest)
            {
                better = written > bestTime;
            }
            else if (best != 0)
            {
                better = written < bestTime;
            }
            if (better)
            {
                best = slot;
                bestTime = written;
            }
        }
        return best;
    }

    /// <summary>One line for the menu: "Run 20 · 340 m down · day 2 · mountain 12345".</summary>
    public string Describe()
    {
        return "Run " + RunNumber + " · " + Depth + " m down · day " + Day + " · mountain " + Seed;
    }

    public Vector3 Where => new(Position[0], Position[1], Position[2]);

    public static bool Exists => File.Exists(ProjectSettings.GlobalizePath(_path));

    public bool Write(out string problem)
    {
        problem = "";
        try
        {
            string absolute = ProjectSettings.GlobalizePath(_path);
            File.WriteAllText(absolute + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(absolute + ".tmp", absolute, true);
            return true;
        }
        catch (Exception error)
        {
            problem = error.Message;
            return false;
        }
    }

    /// <summary>The saved run, or null (none, unreadable, or from an incompatible save version).</summary>
    public static RunSave? Read(out string problem) => ReadFile(ProjectSettings.GlobalizePath(_path), out problem);

    private static RunSave? ReadFile(string absolute, out string problem)
    {
        problem = "";
        if (!File.Exists(absolute))
        {
            problem = "no saved run";
            return null;
        }
        try
        {
            RunSave? save = JsonSerializer.Deserialize<RunSave>(File.ReadAllText(absolute));
            if (save == null || save.Version != CurrentVersion || save.Position == null || save.Position.Length != 3 || save.Seed == 0)
            {
                problem = "the saved run is from another version or incomplete";
                return null;
            }
            foreach (float value in save.Position)
            {
                if (!float.IsFinite(value))
                {
                    problem = "the saved position is broken";
                    return null;
                }
            }
            return save;
        }
        catch (Exception error)
        {
            problem = error.Message;
            return null;
        }
    }

    /// <summary>Forgets the saved run (death, or a new run started from the menu).</summary>
    public static void Delete()
    {
        foreach (string path in new[] { _path, _packPath })
        {
            string absolute = ProjectSettings.GlobalizePath(path);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
    }
}
