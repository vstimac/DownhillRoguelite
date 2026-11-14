using System.Collections.Generic;
using DownhillRoguelite.Audio;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks that the art and sound packs are wired in: every item's model loads and fits
/// inside its cell block; every boulder and trunk model has a mesh; every sound set and recorded
/// footstep loads; every sound the code, scenes and resources name (res://audio/...) loads as audio;
/// every circle has an ambience bed that loops. A missing or renamed file fails here instead of
/// quietly falling back to silence.
/// Run: godot --headless --path . res://debug/tests/AssetTests.tscn
/// </summary>
public partial class AssetTests : Node3D
{
    [Export] public SoundEffects? Sounds { get; set; }
    [Export] public Footsteps? Steps { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        TestItems();
        TestProps();
        TestSounds();
        TestReferencedSounds();
        TestCircleBeds();
        if (_failures.Count == 0)
        {
            GD.Print("ASSET TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"ASSET TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestItems()
    {
        int withModel = 0;
        List<string> bad = new();
        foreach (string file in DirAccess.GetFilesAt("res://inventory/items"))
        {
            if (!file.EndsWith(".tres") || ResourceLoader.Load($"res://inventory/items/{file}") is not ItemDefinition item || item.Model == null)
            {
                continue;
            }
            withModel++;
            WorldItem world = new() { Definition = item };
            AddChild(world);
            Vector3 box = (Vector3)item.SizeCells * world.CellSize * world.VisualScale;
            Aabb bounds = new();
            if (world.ModelNode != null)
            {
                bounds = world.ModelNode.Transform * ModelFit.Bounds(world.ModelNode);
            }
            // The grid lines span the block exactly; the model must sit within it.
            if (world.ModelNode == null || bounds.Size.X > box.X * 1.05f || bounds.Size.Y > box.Y * 1.05f || bounds.Size.Z > box.Z * 1.05f)
            {
                bad.Add(item.ItemId);
            }
            world.QueueFree();
        }
        Check("item models load and fit inside their cell blocks", withModel >= 12 && bad.Count == 0,
            $"{withModel} items with models, oversized: [{string.Join(", ", bad)}]");
    }

    private void TestProps()
    {
        TerrainSettings settings = ResourceLoader.Load<TerrainSettings>("res://terrain/default_terrain.tres");
        List<string> missing = new();
        foreach (string path in settings.BoulderModels)
        {
            if (ModelFit.FirstMesh(path, out Transform3D _) == null)
            {
                missing.Add(path.GetFile());
            }
        }
        foreach (string path in settings.TrunkModels)
        {
            if (ModelFit.FirstMesh(path, out Transform3D _) == null)
            {
                missing.Add(path.GetFile());
            }
        }
        Check("every boulder and trunk model has a mesh", missing.Count == 0 && settings.BoulderModels.Length > 0,
            $"{settings.BoulderModels.Length} boulder and {settings.TrunkModels.Length} trunk models, missing [{string.Join(", ", missing)}]");
    }

    private void TestSounds()
    {
        if (Sounds == null)
        {
            Check("sound sets load", false, "no SoundEffects node");
            return;
        }
        string[] keys = SoundEffects.Keys;
        List<string> empty = new();
        foreach (string key in keys)
        {
            if (Sounds.SetSize(key) == 0)
            {
                empty.Add(key);
            }
        }
        int recorded = 0;
        if (Steps != null)
        {
            foreach (string prefix in Steps.RecordedPrefixes)
            {
                if (ResourceLoader.Exists($"{Steps.RecordedFolder}/{prefix}_000.ogg"))
                {
                    recorded++;
                }
            }
        }
        Check("every sound set and recorded footstep loads", empty.Count == 0 && recorded == 4,
            $"{keys.Length - empty.Count}/{keys.Length} sets, empty [{string.Join(", ", empty)}], {recorded}/4 recorded footstep surfaces");
    }

    /// <summary>Every res://audio/... sound file named in a script, scene or resource exists and loads as audio.</summary>
    private void TestReferencedSounds()
    {
        HashSet<string> paths = new();
        int files = 0;
        RegEx pattern = RegEx.CreateFromString("res://audio/[^\"{}]+?\\.(wav|ogg|mp3)");
        foreach (string file in SourceFiles("res://"))
        {
            files++;
            foreach (RegExMatch match in pattern.SearchAll(FileAccess.GetFileAsString(file)))
            {
                paths.Add(match.GetString());
            }
        }
        List<string> bad = new();
        foreach (string path in paths)
        {
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not AudioStream)
            {
                bad.Add(path);
            }
        }
        Check("every sound named in scripts, scenes and resources loads", paths.Count >= 90 && bad.Count == 0,
            $"{paths.Count} sounds named in {files} files, not loading [{string.Join(", ", bad)}]");
    }

    /// <summary>Scripts, scenes and resources under a folder (not the addons, not the import cache).</summary>
    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (string file in DirAccess.GetFilesAt(folder))
        {
            if (file.EndsWith(".cs") || file.EndsWith(".tscn") || file.EndsWith(".tres"))
            {
                yield return Join(folder, file);
            }
        }
        foreach (string sub in DirAccess.GetDirectoriesAt(folder))
        {
            if (sub.StartsWith(".") || sub == "addons" || sub == "export")
            {
                continue;
            }
            foreach (string file in SourceFiles(Join(folder, sub)))
            {
                yield return file;
            }
        }
    }

    private static string Join(string folder, string name)
    {
        if (folder.EndsWith("/"))
        {
            return folder + name;
        }
        return folder + "/" + name;
    }

    /// <summary>Each of the nine circles has its own ambience bed, and every bed loops.</summary>
    private void TestCircleBeds()
    {
        CircleAmbience beds = new();
        HashSet<string> distinct = new();
        List<string> bad = new();
        for (int circle = 0; circle < 9; circle++)
        {
            string path = beds.BedFor(circle);
            distinct.Add(path);
            if (CircleAmbience.LoadBed(path) is not AudioStreamOggVorbis ogg || !ogg.Loop)
            {
                bad.Add(path.GetFile());
            }
        }
        string beyond = beds.BedFor(12);
        beds.Free();
        Check("every circle has its own looping ambience bed", distinct.Count == 9 && bad.Count == 0 && beyond.Length > 0,
            $"{distinct.Count} beds for 9 circles, not looping or missing [{string.Join(", ", bad)}]");
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
