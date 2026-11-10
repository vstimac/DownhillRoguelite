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
    public static RunSave? Read(out string problem)
    {
        problem = "";
        string absolute = ProjectSettings.GlobalizePath(_path);
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
